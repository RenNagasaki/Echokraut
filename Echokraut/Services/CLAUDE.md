# Services

All business logic lives here as interface + implementation pairs, registered in `ServiceBuilder.cs` and injected via constructor.

## Further documentation — read the file before working in that area
Split by area on 2026-11-13 (verbatim move, the file was ~97k characters). `Queue/CLAUDE.md` loads by
itself when a file in `Queue/` is read; the files under `docs/claude/services/` NEVER load by themselves.

| Area | File |
|---|---|
| Queue system: cancellation, epochs, pruning, in-flight abort | `Echokraut/Services/Queue/CLAUDE.md` |
| EchokrauTTS backend client: streaming, prebuffer, underrun/crackle, diagnostics | `docs/claude/services/echokrautts-streaming.md` |
| EchokrauTTS local install, installer, wrapper update, test install, custom data, voice pack per language, engine catalog, deploy coupling | `docs/claude/services/echokrautts-install.md` |
| Dialog lifecycle, addon event helpers, player-name placeholders, orphan WAV adoption, live generation logging, mapped-NPC caches, dialogue state | `docs/claude/services/dialog-pipeline.md` |
| Sentence continuation across Talk boxes (rows the game splits over boxes, row pairs, ellipsis rule) | `docs/claude/services/sentence-continuation.md` |
| Dialog Harvest: quest dialogs, NPC aliases, live alias resolution, voice-name suggestions | `docs/claude/services/dialog-harvest.md` |
| Dialog Harvest: cut_scene + scripted sheets, placeholder filter, `LuabParser`, `LgbParser` | `docs/claude/services/dialog-harvest-sources.md` |
| Database service, changelog on update, Voice Clip Manager service, Echolines client | `docs/claude/services/database.md` |
| Audio packages (`*.ekpack`), game-voice reference | `docs/claude/services/game-voice-and-packages.md` |
| Voice Pack: service, narrator policy, pack v1.0.0 / v1.1.0 | `docs/claude/services/voice-pack.md` |
| In-game E2E run: stages, state, reload, telemetry, engine options, installer log | `docs/claude/services/e2e-run.md` |
| E2E mixed wrapper state (B8), wrapper CLI cases without the game (B10, `tools/e2e/wrapper-cli.sh`) | `docs/claude/services/e2e-run-wrapper.md` |

## DI & Wiring
- `ServiceContainer` — lazy DI container. `ServiceBuilder.BuildServices()` registers all factories.
- `Plugin.cs` resolves services from the container; its constructor is pure orchestration.
- Cross-component communication uses events — no `SetXxx` post-construction wiring.

## Core Services

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| `ILogService` | `LogService` | Logging via Echotools. Event tracking with `Start`/`End`. |
| `IDatabaseService` | `DatabaseService` | SQLite data access (EF Core). Schema migrations v1–v8 (`CurrentSchemaVersion`). |
| `IVolumeService` | `VolumeService` | Game volume integration, global volume, per-NPC volume. |
| `IBackendService` | `BackendService` | **Voice picking gotchas:** `PickVoice`'s name-match loop is guarded on a non-empty NPC name — `"anything".Contains("")` is true, so an unnamed speaker (bubble without a resolvable name, `???` before alias resolution) used to match `voices[0]` and never reach the race/gender filter. `IsVoiceFittingForNpc` carries the same guard; keep them in sync. The reachability probe uses a **static** `_probeClient`, not a per-call `new HttpClient` (socket exhaustion — same fix as the AllTalk streaming client). Routes voice generation to the **active engine** (AllTalk or EchokrauTTS, per `Configuration.BackendSelection`) or audio files. `CreateActiveBackend()` + `Active*()` helpers are the single place that knows the active engine. `GenerateVoice` uses the persisted `Speaker.voice` key as a fallback when the resolved `Voice` object is null (stale selectable list after a late connect). `RefreshBackend`/`SetBackendType` → `MapVoices` → `RefreshSelectables`. |
| `IAudioPlaybackService` | `AudioPlaybackService` | Playback loop, lip sync, 3D audio via BASS. Fires `CurrentMessageChanged`, `AutoAdvanceRequested`. ⚠ The BASS position pollers run on timer threads: they read `SpeakerPositionCache` (filled in `UpdateListenerState`, framework thread), never `IGameObject.Position` — Dalamud throws "Not on main thread!" there and the unhandled timer exception crashed the game (2026-11-21). |
| `IVoiceMessageProcessor` | `VoiceMessageProcessor` | Builds `VoiceMessage` from addon events, applies muting, language detection, NPC data lookup. Also logs voice clips to DB. |
| `ITextProcessingService` | `TextProcessingService` | Text cleanup: stutter removal, punctuation, SE tags. |
| `ICharacterDataService` | `CharacterDataService` | Race/gender/name resolution from game objects + Lumina. |
| `ICommandService` | `CommandService` | Chat commands (`/ek` = Voice Clip Manager, `/ekconfig`, `/ekfirst`, `/ekdata`, `/ekt*` toggles, `/ektest`; **no `/echokraut`** — E2E `ui:alias` fails on it, WORKLOG). Fires `ToggleConfigRequested`, `CancelAllRequested`, etc. |
| `IVoiceTestService` | `VoiceTestService` | Shared play/stop voice testing for ImGui and Native UI. |
| `IVoiceClipManagerService` | `VoiceClipManagerService` | Business logic for Voice Clip Manager: play/generate/delete audio, bulk ops. |
| `ILiveGenerationLogger` | `LiveGenerationLogger` | Records a `voice_clip_generations` row for audio produced by the live playback path. Called from `AudioPlaybackService.OnSourceEnded` after `WriteStreamToFile` succeeds. No-ops when `VoiceMessage.VoiceClipId == 0` (e.g. VoiceTest). |
| `IAlltalkInstanceService` | `AlltalkInstanceService` | Local AllTalk install/uninstall, health checks. |
| `IEchokrauTtsInstanceService` | `EchokrauTtsInstanceService` | Local EchokrauTTS lifecycle. **Install ≈ Start**: the wrapper bootstrap installs + serves in one long-running process, so `Install()` polls for `Ready.EchokrauTTS.txt` instead of `WaitForExit`. Fires `OnInstanceReady`. Exposes `Installing`/`InstanceRunning`/`CurrentInstallStatus`/`CurrentInstallProgress` (same shape as `IAlltalkInstanceService`). Guarded against an empty installer URL. `InstallCustomData()` installs a user-supplied model/samples via the installer's `installcustomdataek` mode (see EchokrauTTS engine section). `StopInstance` posts `/shutdown` to `127.0.0.1:<port the process was started with>` (`LocalShutdownUrl`), **never to `BaseUrl`** — after a switch to Remote that names the remote server. |
| `ITtsVoiceSyncService` | `TtsVoiceSyncService` | Engine switching. `CopyVoicesForSwitch` (DirectoryMerge old→new) + `SwitchEngine(backend)` = CancelAll → copy voices → **stop the OLD engine's local instance** → save `BackendSelection` → `SetBackendType` (GAP-6: flush queue/playback BEFORE swapping `_backend`). The stop was missing until 2026-09-26: both engines serve on their own port and each holds a model in VRAM, so the abandoned process kept that memory and kept answering health probes — the Backend tab showed an engine as running that the plugin no longer talked to. `StopInstance` is a no-op when nothing local is running, so it needs no condition; a throwing stop is caught and logged at Warning, because failing there must not leave the user on neither engine. |
| `IChangelogService` | `ChangelogService` | Reads embedded changelog resources, returns the entries newer than `Configuration.LastSeenChangelogVersion`. Drives `NativeChangelogWindow`. |
| `IRemoteUrlService` | `RemoteUrlService` | Download URLs from `Resources/RemoteUrls.json` with GitHub fallback. |
| `INpcAttributionRepairService` | `NpcAttributionRepairService` | One-shot DB cleanup. Walks every `character_instance`, asks Lumina who the canonical NPC is for that `NpcBaseId`, and reassigns mis-attributed instances + voice_clips to the right character. Dry-run produces a `NpcAttributionRepairReport`; Apply consumes it. UI lives in `NativeGameDataToolsWindow`. |
| `IJsonDataService` | `JsonDataService` | NPC data, voice names, model-gender map from JSON. Startup fetches the four data files (NpcRaces / NpcGenders / Emoticons / VoiceNames{LANG}) from the master branch on GitHub. `FetchUrl` **retries with backoff** (500/1500/3000 ms) on transient failures (HTTP 429/408/5xx, timeouts, network errors — see `IsTransient`) because the request burst gets rate-limited (429) on repeated launches, then returns null so the loader falls back to the **embedded copy** of that file (all four are `<EmbeddedResource>` in the csproj). Fetch/fallback failures log at Warning, not Error (recoverable). |
| `INpcDataService` | `NpcDataService` | Per-NPC voice config persistence (`NpcMapData`) + the in-memory NPC/player caches. **The caches are copy-on-write** — see `docs/claude/services/dialog-pipeline.md`. |
| `IDialogHarvestService` | `DialogHarvestService` | Batch harvester: extracts quest/NPC dialog from game data, persists to SQLite with voice auto-assignment. |
| `IAudioPackageService` | `AudioPackageService` | Export/import of `*.ekpack` audio packages (zip + `manifest.json`). See "Audio packages" in `docs/claude/services/game-voice-and-packages.md`. |
| `IGameVoiceMappingService` | `GameVoiceMappingService` | Game-voice alias + split files, embedded < remote < local, `Changed` event, repo export (§11.5). |
| `IStoryProgressService` | `StoryProgressService` | Story patch of a line (`ContextFor`): accepted quest whose text it is → accepted MSQ → completed progress (`ScenarioTree` + `IUnlockState` + `QuestManager.NormalQuests`, framework-thread snapshots, nothing stored; §11.4). |
| `IDialogContinuationService` | `DialogContinuationService` | Option `JoinSplitSentences`: Talk box ending mid-sentence gets the sentence end from the same sheet row (the game split it over boxes, followed page by page) or the next row, next box is spoken without it. Sheets via `IDialogSheetSource`/`LuminaDialogSheetSource`. See `docs/claude/services/sentence-continuation.md`. |
| `ISelfTestService` | `SelfTest/SelfTestService` | In-game self-test: `/ektest` (`all` = with interactive steps) + Dalamud's self-test window (`ISelfTestRegistry`, `DalamudSelfTestStep`). Checks in `SelfTest/Checks/`, list in `SelfTestCatalog`. Pipeline check uses `IVoiceMessageProcessor.ProcessSelfTestSpeechAsync` (`VoiceMessage.IsSelfTest`: never saved/uploaded/reported) and deletes its throwaway character. Report `pluginConfigs/Echokraut/selftest/selftest-latest.json`. Plan + limits: `docs/plans/ingame-self-test.md`. |
| `IFirstTimeWizardController` | `FirstTimeWizardController` | First-time wizard flow without UI: `Step` 0/1/2, `ChooseEngine`/`ChooseMode`/`SetInstallRoot`/`SetLocalSaveLocation`/`SetShareLines`, `StartInstall` (stop + install in `Task.Run`), `TestConnectionAsync` (URL snapshot), `Next`/`Back`/`Finish` (guards in the action, not only in the dimming), `Completed` event (handled by `NativeWindowManager`: `MarkAllSeen`, close, open config). `NativeFirstTimeWindow` only binds to it and reports open/closed (`ReportWindowOpen`); the E2E stage `first-time` drives the same actions. |
| `IE2eRunService` | `SelfTest/E2e/E2eRunService` | Unattended in-game E2E run, **dev loads only** (`IsDev`); stages, state, telemetry (B2), engine options + install files (B3), wrapper response cache / cancel refcount (B4, stage `wrapper-http`), own windows (B5, stage `ui`), game voice (B6, stages `game-voice` + opt-in `game-voice-index`), B7 scenarios (narrator, phonetics, voices, chat channels, local save, volume, bubble, harvest, AllTalk switch-off), mixed wrapper state (B8, stage `wrapper-mixed`, request `wrapperVersion`) → `docs/claude/services/e2e-run.md` (B8 in `e2e-run-wrapper.md`). |
| `IOwnedWindowsService` | `OwnedWindowsService` | Lifetime of the own native windows for non-UI code: `AnyAlive` (reported every frame by `NativeWindowManager`: any window still has a native addon, also while closing), `RequestCloseAll` → `CloseAllRequested` (framework thread). Used by `E2eSelfReload.CloseWindowsAsync` before the DLL touch (backup + restore): a window alive at unload crashes the game when finalized. `Probe(OwnedWindowsProbe)` → `ProbeRequested` (framework thread, answered synchronously by `NativeWindowManager.OnProbe`): snapshot (state, screen rect, visible texts), open/close/move, sweep steps — the E2E stage `ui`'s only way into the UI. |
| `IVoicePackService` | `VoicePackService` | Downloads the curated, freely-licensed voice pack (zip from `RemoteUrlsData.VoicePackUrl`) and unpacks it into the target voice folder. **Legal replacement for the removed in-game sample extraction — no game asset is ever read.** |

## Key Patterns
- New features → create interface + implementation here
- Register in `ServiceBuilder.cs`
- Use events for cross-component communication
- `Helper/Functional/` is for pure stateless utilities only (no DI needed)
