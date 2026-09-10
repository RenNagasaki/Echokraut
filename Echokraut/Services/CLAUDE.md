# Services

All business logic lives here as interface + implementation pairs, registered in `ServiceBuilder.cs` and injected via constructor.

## DI & Wiring
- `ServiceContainer` — lazy DI container. `ServiceBuilder.BuildServices()` registers all factories.
- `Plugin.cs` resolves services from the container; its constructor is pure orchestration.
- Cross-component communication uses events — no `SetXxx` post-construction wiring.

## Core Services

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| `ILogService` | `LogService` | Logging via Echotools. Event tracking with `Start`/`End`. |
| `IDatabaseService` | `DatabaseService` | SQLite data access (EF Core). Schema migrations v1–v13. |
| `IVolumeService` | `VolumeService` | Game volume integration, global volume, per-NPC volume. |
| `IBackendService` | `BackendService` | **Voice picking gotchas:** `PickVoice`'s name-match loop is guarded on a non-empty NPC name — `"anything".Contains("")` is true, so an unnamed speaker (bubble without a resolvable name, `???` before alias resolution) used to match `voices[0]` and never reach the race/gender filter. `IsVoiceFittingForNpc` carries the same guard; keep them in sync. The reachability probe uses a **static** `_probeClient`, not a per-call `new HttpClient` (socket exhaustion — same fix as the AllTalk streaming client). Routes voice generation to the **active engine** (AllTalk or EchokrauTTS, per `Configuration.BackendSelection`) or audio files. `CreateActiveBackend()` + `Active*()` helpers are the single place that knows the active engine. `GenerateVoice` uses the persisted `Speaker.voice` key as a fallback when the resolved `Voice` object is null (stale selectable list after a late connect). `RefreshBackend`/`SetBackendType` → `MapVoices` → `RefreshSelectables`. |
| `IAudioPlaybackService` | `AudioPlaybackService` | Playback loop, lip sync, 3D audio via BASS. Fires `CurrentMessageChanged`, `AutoAdvanceRequested`. |
| `IVoiceMessageProcessor` | `VoiceMessageProcessor` | Builds `VoiceMessage` from addon events, applies muting, language detection, NPC data lookup. Also logs voice clips to DB. |
| `ITextProcessingService` | `TextProcessingService` | Text cleanup: stutter removal, punctuation, SE tags. |
| `ICharacterDataService` | `CharacterDataService` | Race/gender/name resolution from game objects + Lumina. |
| `ICommandService` | `CommandService` | Chat commands (`/ek`, `/echokraut`). Fires `ToggleConfigRequested`, `CancelAllRequested`, etc. |
| `IVoiceTestService` | `VoiceTestService` | Shared play/stop voice testing for ImGui and Native UI. |
| `IVoiceClipManagerService` | `VoiceClipManagerService` | Business logic for Voice Clip Manager: play/generate/delete audio, bulk ops. |
| `ILiveGenerationLogger` | `LiveGenerationLogger` | Records a `voice_clip_generations` row for audio produced by the live playback path. Called from `AudioPlaybackService.OnSourceEnded` after `WriteStreamToFile` succeeds. No-ops when `VoiceMessage.VoiceClipId == 0` (e.g. VoiceTest). |
| `IAlltalkInstanceService` | `AlltalkInstanceService` | Local AllTalk install/uninstall, health checks. |
| `IEchokrauTtsInstanceService` | `EchokrauTtsInstanceService` | Local EchokrauTTS lifecycle. **Install ≈ Start**: the wrapper bootstrap installs + serves in one long-running process, so `Install()` polls for `Ready.EchokrauTTS.txt` instead of `WaitForExit`. Fires `OnInstanceReady`. Exposes `Installing`/`InstanceRunning`/`CurrentInstallStatus`/`CurrentInstallProgress` (same shape as `IAlltalkInstanceService`). Guarded against an empty installer URL. `InstallCustomData()` installs a user-supplied model/samples via the installer's `installcustomdataek` mode (see EchokrauTTS engine section). |
| `ITtsVoiceSyncService` | `TtsVoiceSyncService` | Engine switching. `CopyVoicesForSwitch` (DirectoryMerge old→new) + `SwitchEngine(backend)` = CancelAll → copy voices → save `BackendSelection` → `SetBackendType` (GAP-6: flush queue/playback BEFORE swapping `_backend`). |
| `IChangelogService` | `ChangelogService` | Reads embedded changelog resources, returns the entries newer than `Configuration.LastSeenChangelogVersion`. Drives `NativeChangelogWindow`. |
| `IRemoteUrlService` | `RemoteUrlService` | Download URLs from `Resources/RemoteUrls.json` with GitHub fallback. |
| `INpcAttributionRepairService` | `NpcAttributionRepairService` | One-shot DB cleanup. Walks every `character_instance`, asks Lumina who the canonical NPC is for that `NpcBaseId`, and reassigns mis-attributed instances + voice_clips to the right character. Dry-run produces a `NpcAttributionRepairReport`; Apply consumes it. UI lives in `NativeGameDataToolsWindow`. |
| `IJsonDataService` | `JsonDataService` | NPC data, voice names, model-gender map from JSON. Startup fetches the four data files (NpcRaces / NpcGenders / Emoticons / VoiceNames{LANG}) from the master branch on GitHub. `FetchUrl` **retries with backoff** (500/1500/3000 ms) on transient failures (HTTP 429/408/5xx, timeouts, network errors — see `IsTransient`) because the request burst gets rate-limited (429) on repeated launches, then returns null so the loader falls back to the **embedded copy** of that file (all four are `<EmbeddedResource>` in the csproj). Fetch/fallback failures log at Warning, not Error (recoverable). |
| `INpcDataService` | `NpcDataService` | Per-NPC voice config persistence (`NpcMapData`) + the in-memory NPC/player caches. **The caches are copy-on-write** — see below. |
| `IDialogHarvestService` | `DialogHarvestService` | Batch harvester: extracts quest/NPC dialog from game data, persists to SQLite with voice auto-assignment. |
| `IAudioPackageService` | `AudioPackageService` | Export/import of `*.ekpack` audio packages (zip + `manifest.json`). See "Audio packages" below. |
| `IVoicePackService` | `VoicePackService` | Downloads the curated, freely-licensed voice pack (zip from `RemoteUrlsData.VoicePackUrl`) and unpacks it into the target voice folder. **Legal replacement for the removed in-game sample extraction — no game asset is ever read.** |

## EchokrauTTS engine (2nd TTS backend)
- **Backend clients** live in `Backends/`: `ITTSBackend` implemented by `AlltalkBackend` and **`EchokrauTtsBackend`**. `BackendService.CreateActiveBackend()` picks by `BackendSelection`. EchokrauTTS specifics: raw PCM passthrough (NO WAV wrap), `language` omitted from `/tts` (server synthesizes in its loaded model — GAP-4), `X-Job-Id` header, Bearer auth. **Streaming**: both backends honor the shared `Configuration.StreamingGeneration` toggle — off = wait for the full clip (`EchokrauTtsBackend.MaterializeAudioStream` buffers the whole response into a `MemoryStream`; AllTalk writes to disk first then rewinds). EchokrauTTS used to always stream regardless of the setting.
  - **Streaming underrun / crackle (slow-GPU fix):** the wrapper's `/tts` is a progressive `StreamingResponse` — XTTS emits PCM *as it generates*. On a modest GPU that runs slower than real-time, the BASS push stream in `Live3DAudioEngine` starved and clicked (the saved file stayed clean because it's re-read from the fully-buffered stream after playback). A **fixed** prebuffer cushion only postpones this: the deficit grows for as long as the clip lasts, so playback eats the cushion and then glitches through the rest of the line. Current fix — `Helper/Functional/StreamBufferPolicy` (pure rules, unit-tested) + a rebuffer watchdog in `Live3DAudioEngine.Source`:
    - Start on a 1000 ms cushion (`InitialCushionMs`), or immediately when `_readerDone` (whole clip already arrived) so short lines stay low-latency AND fully buffered. `PrebufferTimeoutMs` caps a stalled source.
    - While the reader is still delivering, `RebufferLoop` watches the fill level; below `LowWaterMs` (200 ms) it pauses the channel, **doubles** the cushion (`NextCushionMs`, capped at `MaxCushionMs` = 6000 ms), refills and resumes. After one or two rebuffers the cushion covers the source's shortfall and the rest plays in one piece — a couple of clean pauses instead of continuous garble.
    - The BASS channel buffer is sized `MaxCushionMs + 500` so the grown cushion can actually fit.
    - ⚠ **The watchdog measures the fill with `BufferedOutBytes()`, never with `BASS_DATA_AVAILABLE`** — the same trap as the prebuffer, one step later. BASS refills the playback buffer only gradually after a resume, so a watchdog reading it there reacts to *its own resume*: pause → refill → resume → "underrun" → … thousands of iterations per second, each one an Info line (observed: the identical `cushion now 6000ms` line repeated for a whole millisecond block in `dalamud.log`). Two further guards keep that shape impossible: a rebuffer is only counted (cushion doubled, line logged) when `Bass.ChannelPause` actually **succeeded** — the old code logged first and `continue`d without sleeping when the pause failed, which is a hot loop — and `StreamBufferPolicy.MayRebufferAgain` caps rebuffers at one per `MinRebufferIntervalMs` (500 ms, deliberately below `InitialCushionMs` so a genuine rebuffer can never be suppressed).
    - The watchdog only resumes what **it** paused (`_rebuffering` + `State == Playing`), so a user pause/stop always wins. Draining while `_readerDone` is the end of the clip, not an underrun.
    - Guaranteed-glitch-free path remains Streaming off (full clip buffered before playback).
  - **⚠ Never measure the buffer fill with `BASS_ChannelGetData(BASS_DATA_AVAILABLE)` before playback has started.** That reports the **playback buffer**, which BASS fills from a push stream's queue only while the channel is actually *playing*. Before the first `ChannelPlay` it sits at a small constant however much you push, so a prebuffer cushion measured that way is unreachable and `WaitForCushion` falls through to its only other release condition — `_readerDone`, i.e. *the whole clip has been generated*. That is a chicken-and-egg: playback waits for the buffer, the buffer only fills through playback. It made streaming look completely broken while every layer beneath was healthy, which is why both the wrapper analysis and the plugin code review kept clearing themselves.
    - Fix: count it ourselves. `_pushedOutBytes` (`Interlocked.Add` in `PushSilence`/`PushAll`) minus `Bass.ChannelGetPosition(Bytes)` → `Source.BufferedOutBytes()`, with the pure arithmetic in `StreamBufferPolicy.BufferedAhead` (clamped at 0 — the two counters are sampled independently, so the position can briefly read past the pushed total, and a negative fill would look like a permanent underrun to the rebuffer watchdog). Valid while stopped, playing AND paused, so the watchdog — which pauses the channel to refill and would have hit the same trap — uses it too.
    - Measured before/after (2026-08-01, 8.17 s line): bytes arrived progressively at 2.42x real-time from 0.5 s after the request, yet playback started 4 ms after the backend reported `done`. The `WaitForCushion` line logs the BASS value alongside our own (`BASS reported …`) — the gap between the two numbers is the signature of this trap.
  - **Streaming diagnostics live at Debug level**, not Info: `EchokrauTtsBackend` logs the header wait + `streaming`/`transferEncodingChunked`/`contentLength` (a `Content-Length` on `/tts` would mean someone buffered the whole body — a FastAPI `StreamingResponse` must be chunked), and `Live3DAudioEngine` logs the input-format wait (= time to the source's first bytes), a per-stream arrival profile (block count, first-block latency, first 8 arrivals) and the playback-start offset. Turn on Debug for the source to re-run the measurement instead of rebuilding the probes.
- **Local install** (`Helper/Functional/`): `LocalInstallerProvisioner` (DRY provisioning + BLK-5 version handshake against `Configuration.InstalledInstallerVersion` / remote `InstallerVersion`) — used by BOTH `AlltalkInstanceService` and `EchokrauTtsInstanceService`. `TtsPaths` (per-engine subfolders under `TtsInstallRoot`), `TtsInstallDetection` (is-installed probes), `DirectoryMerge` (voice copy on engine switch).
- **Standalone installer** `EchokrautLocalInstaller/` (separate project, released as `ELI-*` GitHub tags — never influences plugin versioning): mode `echokrautts <root> <url> <isWin> <port> <lang> <parentPid> [ttsBackend] [xttsFp16]` downloads-if-url → runs bootstrap → parses NDJSON stdout → writes `Ready.EchokrauTTS.txt` on `ready`.
- **Wrapper update handshake (user-triggered, data-preserving).** `EchokrauTtsData.InstalledWrapperVersion` is what's on disk. The newest release is **looked up on demand** via `RemoteUrlsData.EchokrauTtsReleasesUrl` (GitHub `releases/latest`) when the user presses the button — **never on a timer or at startup**: unauthenticated GitHub API calls are capped at **60/h per user IP**, and the plugin already fetches several files from GitHub at launch (`JsonDataService` has retry+backoff precisely because those bursts hit 429). `RemoteUrlsData.EchokrauTtsVersion`/`EchokrauTtsUrl` remain the shipped baseline for fresh installs (`RemoteUrlsJsonTests` asserts the tag appears inside the URL).
  - `GitHubReleaseParser` (pure, tested) pulls **tag AND zip asset URL out of the same release** — taking only the tag and keeping the baseline URL would install the old zip under the new version's name. `EchokrauTtsInstanceService._foundRelease` then overrides both (`LatestWrapperVersion`, `WrapperDownloadUrl`), and `MarkInstalled`/`MarkUpdated` record `LatestWrapperVersion`, i.e. the tag of the zip actually downloaded.
  - Asset choice: first `.zip` whose name mentions EchokrauTTS, else the first `.zip`. A release with no zip is a **failure**, not "no update" — it cannot be installed, and "up to date" would hide it.
  - `WrapperUpdateState` (`Enums/`) drives one button with two jobs: `NotChecked`/`CheckFailed` → "Check for updates" (clickable), `Checking` → disabled, `UpdateAvailable` → "Update" (clickable), `UpToDate` → "Update" disabled. A failed check deliberately returns to "Check for updates" — showing a disabled "Update" would read as "you are current", which a failed lookup does not establish. HTTP 403 is reported with a rate-limit hint.
  - **The state must be reset after an install/update, not only after a check.** `MarkInstalled`/`MarkUpdated` write the new `InstalledWrapperVersion`; without `RefreshUpdateStateAfterInstall()` (→ `WrapperUpdatePolicy.StateAfterInstall`) the state stayed `UpdateAvailable` and the button kept offering an update that had already run. It goes back to `NotChecked` ("Check for updates") rather than `UpToDate`, because `UpToDate` is a dead state and **dimming a KamiToolKit node does not block its clicks** (`Windows/CLAUDE.md` → "Dim() is not a click guard") — the first attempt at this fix greyed the button out and it stayed clickable. `_foundRelease` is kept so a later reinstall still pulls that release's zip.
  - `RemoteUrlsReachabilityTests` calls the real API and asserts the parser accepts the live release; 403/429 (rate limit) is tolerated because it says nothing about the URL being correct.  - `Helper/Functional/WrapperUpdatePolicy` is the pure comparison (`IsUpdateAvailable` + label building; ordinal string compare, never version parsing — a rollback published upstream is also "update available"). Empty remote tag ⇒ never offered. An empty *installed* tag on a local install never reaches the policy: `Configuration.MigrateWrapperVersionForExistingInstalls` backfills it once with `WrapperUpdatePolicy.AssumedLegacyVersion` (pre-handshake installs can only run the one published release, so "?" + a pointless re-download would be wrong). Should it reach the policy anyway (Remote/None, or a hand-edited config), it counts as a mismatch.
  - `IEchokrauTtsInstanceService.UpdateWrapper()` reuses `Launch`/`RunInstance`: same download-then-serve shape as `Install()`, but installer mode `updateechokrautts` instead of `echokrautts`, **`DownloadVoicePack` is skipped** (it wipes `samples/` first and would take the user's voices with it) and `MarkUpdated()` records the tag without touching `LocalInstall`/`FirstTime`.
  - **`Launch`/`RunInstance` take a `WrapperLaunchMode`** (`Enums/`), not the former `(bool download, bool update)` pair. The pair could express four combinations of which only three meant anything, and it could not say "install, but without downloading" at all — which is exactly what the test install needs. Three questions are derived from the mode: `NeedsDownload` (is the installer handed a URL), `IsInstallPhase` (progress bar + post-ready bookkeeping) and the installer's argv mode.
- **Test install of an untagged wrapper (developer-only, `WrapperLaunchMode.TestInstall`).**
  `Helper/Functional/TestWrapperInstall` gates a second install button on exactly one condition: a
  locally built zip lies at the hard-coded `ZipPath` (`…/Echokrautts/wrapper/EchokrauTTS.zip`).
  `EchokrauTtsInstanceService.ExtractTestZip` unpacks it
  over `echokrautts/` (wholesale, `overwriteFiles: true`, no preserve list — the same unpack a real
  Install does), the installer is then handed an **empty URL** so it skips its download and bootstraps
  what is on disk, and the post-ready phase runs the Install branch (voice pack + `MarkInstalled`).
  - **Why it exists:** before this, the install path could not be exercised until release day. The
    download URL comes from `RemoteUrls.json` fetched from the plugin's `main` branch, and the remote
    value **beats** the embedded copy (`MergeWithFallback`/`StringOrFallback` only fall back on
    *empty* fields) — so editing the local file changes nothing at runtime. And the update check asks
    GitHub for `/releases/latest`, which **ignores drafts and pre-releases**, so a pre-release is not
    a workaround either. Copying the wrapper into place and pressing Start tests the *wrapper*, but
    never the *install*.
  - `MarkInstalled` takes the tag to record; the test install passes `TestWrapperInstall.VersionMarker`
    (`"test (local zip)"`) instead of `LatestWrapperVersion`. Because `WrapperUpdatePolicy` compares
    tags as opaque strings, that marker keeps the Backend tab honest **and** leaves every real release
    reading as "update available", so a test build is always replaceable.
  - **Hard-coded on purpose, no config value:** the button unpacks and executes an unreviewed archive,
    so the path must not be steerable — a user-settable path would turn a developer convenience into
    a way to talk someone into running a foreign zip. Being an absolute path inside a source checkout
    is what makes it invisible on an installed copy.
  - **No player identity in the gate.** An earlier draft also required a specific character name. It
    was removed: an identifying value committed to a public repo ties the author to a game account,
    and it bought nothing — the path already restricts this to one machine, and a freely chosen name
    was never a boundary. Consequence: `TestInstallAvailable` is now a bare disk probe, asked every
    frame by the UI, so `EchokrauTtsInstanceService` caches it behind a 5 s re-probe interval (a
    failing lookup per frame for every user otherwise; without the re-probe the developer would have
    to restart the game after building a zip).
  - The button is built into `NativeEchokrauTtsBuilder.BuildLocalInstance`'s install row, so it appears
    in **both** the Backend tab and the first-time wizard without either window knowing about it.
    `Launch` re-checks `TestInstallAvailable` because dimming a KamiToolKit node does not block clicks.
  - Installer side: `updateechokrautts` takes the SAME args as `echokrautts` and only changes unpacking — `ExtractPreservingUserData` walks the zip entry-by-entry and skips anything whose **first** path segment is in `Constants.PRESERVEDECHOKRAUTTSFOLDERS` (`samples`, `models`). First segment only, so a python package named `models` deeper in the tree still updates. Zip-slip guarded, nothing is ever deleted (a file dropped by the new release stays behind — the safe direction for a mode that promises data survival).
  - `LatestWrapperVersion` is exposed on `IEchokrauTtsInstanceService` rather than injecting `IRemoteUrlService` into the windows — that service already owns the wrapper lifecycle and holds the URL config.
  - ⚠ **Needs the installer re-release**: an installer that predates the mode falls through the arg switch and exits immediately, which the plugin reports as "Installer exited before EchokrauTTS became ready". Publish `ELI-1.3.0.0` (csproj is already at `1.3.0.0`), then bump `installerUrl` + `installerVersion` in `RemoteUrls.json` — `RemoteUrlsReachabilityTests` fails on a tag that isn't published yet, which is why the repo still points at `ELI-1.2.0.0`.
- **Custom data (own model + samples)** — analog to AllTalk's "Install only custom data". `EchokrauTtsData.CustomModelUrl`/`CustomVoicesUrl` (direct or Google Drive zip URLs) → `IEchokrauTtsInstanceService.InstallCustomData(eventId, installProcess:false)` runs the installer's **`installcustomdataek`** mode off-thread: model zip → `echokrautts/models/echokraut_custom` (replaces prior custom model; installer flattens a single wrapping folder), voice samples zip → `echokrautts/samples` (**additive** — existing voices preserved). Then restarts the wrapper (when running or auto-start on) so the model reloads. **Voice-pack skip:** when `CustomVoicesUrl` is set, the fresh-install voice pack download is skipped in BOTH `AlltalkInstanceService.Install` and `EchokrauTtsInstanceService.RunInstance` — the pack service wipes the voices/samples folder before unpacking, so running it would clobber the user's custom voices (AllTalk installs them during the install step; EchokrauTTS gets them via a separate custom-data install). The **wrapper auto-detects** `models/echokraut_custom` for the ACTIVE engine: F5 picks the first `*.safetensors`/`*.pt`/`*.ckpt` (+ optional `vocab.txt`/`arch.txt`) via `models.resolve_custom_model`; XTTS uses the dir when it holds `config.json` + a weight file via `xtts_backend._resolve_custom_model_dir`. No config passing — presence of the folder is the signal. UI = 2 URL inputs + button in the shared `NativeEchokrauTtsBuilder`. **Needs the installer re-release** (new mode lives in the installer binary).
- **Voice pack per client language.** `VoicePackService` no longer takes one fixed URL: it resolves
  `RemoteUrlsData.VoicePackUrls` (language key → URL, same keys as `VoiceNameUrls`) against the
  client language via `Helper/Functional/VoicePackUrlResolver` — the language's own pack, else
  English, else the legacy single `VoicePackUrl` so an older `RemoteUrls.json` keeps working.
  Publishing a pack for a new language is one line in `RemoteUrls.json`, no plugin release.
  - The language is injected as `Func<ClientLanguage>`, not captured once: a manual re-download from
    the Game Data Tools window may happen long after startup, and the user can switch client language
    in between.
  - The service logs which case applied. In a non-English client without its own pack the voices
    *will* sound English, and that is worth saying rather than leaving the user to guess.
  - `ClientLanguageKeys` (`Helper/Functional/`) holds the one spelling of those keys, shared with
    `JsonDataService` — a key spelled differently in one place does not fail loudly, it silently
    falls back to English.
- **Engine catalog (`ITtsEngineCatalogService` / `TtsEngineCatalogService`).** The sub-engine list is read from the
  wrapper repo's `engines.json`, not hard-coded in the plugin — a wrapper release that adds an engine shows up without a
  plugin release. URL template `RemoteUrlsData.EchokrauTtsEnginesUrl`, `{version}` → `EchokrauTtsVersion`; on failure the
  wrapper's default branch, then the embedded `Resources/Engines.json`. **The tag is tried first on purpose:** reading the
  branch would advertise engines no published wrapper ships yet; the branch exists only for the case where a release
  predates the file (`0.0.0.2` does).
  - **The fetch runs off the constructor** (`Task.Run`) and the embedded list is live immediately: the catalog is
    UI-only, and blocking startup — let alone a UI frame — on two HTTP round trips to fill a dropdown is not worth it. A
    window built in the seconds before the answer arrives shows the shipped list, one restart behind at worst.
  - Exposed to the UI as `IEchokrauTtsInstanceService.AvailableEngines` rather than injecting the catalog into both
    windows: that service already owns the installed wrapper version the list is judged against (same reasoning as
    `LatestWrapperVersion` above). `SwitchTtsBackend(string engineId)` persists the id and restarts a running instance.
  - A dropdown cannot grey out a single entry, so an engine the installed wrapper is too old for is refused **in the
    click handler** (`OnOptionSelected` puts the label back); the `"(from wrapper version X)"` suffix is only the
    explanation. Same rule as the Update button: the look never guards, the code does.
- **⚠ Deploy coupling:** Local EchokrauTTS only works once the user publishes the installer (`ELI-1.1.0.0`) + wrapper releases AND points `RemoteUrls.json` `installerUrl`/`installerVersion`/`echokrauTtsUrl` at them. Remote EchokrauTTS works today. `RemoteUrlsData` must copy `InstallerVersion` + `EchokrauTtsUrl` in `MergeWithFallback` (else BLK-5 is off).

## Dialog lifecycle & `DialogState.CurrentVoiceMessage`
- **Two close signals for the Talk addon.** `AddonTalkHelper` handles the close in `HandleAddonClosed` (clears `InDialog`, flushes the AddonTalk queue via `IAddonCancelService.Cancel(..., dialogClosed: true)`, drops the current speaker, resets alias tracking). It is reached from `AddonEvent.PostUpdate` (addon hidden but still updating) **and** `AddonEvent.PreFinalize` (addon torn down outright — cutscene end, zoning, ESC), guarded idempotently by `wasTalking`. PostUpdate alone missed the teardown case, leaving `InDialog` true so later lines still played.
- **`DialogState.CurrentVoiceMessage` is published early** — in `VoiceMessageProcessor.ProcessSpeechAsync` at step 3.5, right after the NPC data is resolved and **before** the skip checks (muted NPC, no voice assigned, volume 0). The in-dialog toolbar reads it to know who is talking; publishing only for lines that actually play was a catch-22 (an NPC with no usable voice offered no speaker → the voice dropdown stayed empty → the user could never assign the voice that would have made it play). `Volume` is filled in at step 6; `LogVoiceClip` still runs after the checks, so the DB side is unchanged.

## Addon Event Helpers
Hook into game addon lifecycle to capture dialog events:
- `IAddonTalkHelper` — main Talk addon (NPC dialog)
- `IAddonBattleTalkHelper` — battle dialog
- `IAddonSelectStringHelper` / `IAddonCutSceneSelectStringHelper` — player choices
- `IAddonBubbleHelper` — NPC speech bubbles
- `IChatTalkHelper` — chat-based dialog
- `ISoundHelper` — hooks game sound to mute original VO. Fires `TalkVoiceLine`/`BattleBubbleVoiceLine`.

## Queue System (`Queue/`)
- `IVoiceMessageQueue` / `VoiceMessageQueue` — thread-safe `ConcurrentQueue<QueueEntry>`.
- `CancelBySource`/`CancelAll` mark entries as `Cancelled` in state (can't remove from ConcurrentQueue).
- Both `PlaybackLoopAsync` and `GenerationLoopAsync` must check `entry.State == Cancelled` after dequeuing.
- **Terminal states are final.** `VoiceMessageEntry.TransitionTo` returns `false` and changes nothing once the entry is `Completed`/`Cancelled`/`Failed` (`IsTerminal`); the `MarkAs*` methods honour that return value and skip their side effects (enqueue for playback, `_currentlyPlaying`, statistics). Without this, a line cancelled *while generating* got flipped back to `ReadyToPlay` when the backend finished and played after the dialog had closed — the "audio plays with the dialog box long gone" bug.
- **Cancellation must walk `_allEntries`, not just the queues.** Between `TryDequeuePendingGeneration` and `MarkAsGenerating` an entry is in no collection at all; `CancelAll` iterating only the queues + `_generatingEntries` left exactly those alive. Both `CancelAll` and `CancelBySource` now cancel every non-terminal entry in `_allEntries`.
- `BackendService.ProcessGenerationAsync` re-checks `entry.State` **after** the (slow) generation and discards + disposes the stream if it was cancelled meanwhile.
- **Cancellation epochs close the fast-skip race.** Cancelling entries is not enough: `ProcessSpeechAsync` is async, so the message for a line the player just skipped can still be under construction and reach the queue *after* the flush. `VoiceMessageQueue` keeps a per-`TextSource` epoch, bumped **first** in `CancelBySource`/`CancelAll`; `VoiceMessageProcessor` stamps `VoiceMessage.CancelEpoch` at the very top of `ProcessSpeechAsync` (before any await), and `IsObsolete(message)` = stamp older than the source's current epoch. Checked in `GenerationLoopAsync` (before spending a backend round-trip), `ProcessGenerationAsync` (after generation) and `AudioPlaybackService.PlayAudioAsync` (last line of defence). Epochs are per-source so a dialogue advance never invalidates queued bubbles/chat.
- **Advancing cancels the queue, not just the audible stream.** `AddonCancelService.Cancel` calls `ClearQueue` for *every* cancellation (advance and close), keyed on the message's own source. Previously only `dialogClosed: true` flushed, so skipping within an open dialog stopped the sound but left the generation running — the skipped line then spoke up whenever it finished.
- **A stream that is still starting up must be stoppable.** `Live3DAudioEngine.PlayStream` **blocks**: the WAV header sniff reads from the (possibly slow, network-backed) source and `WaitForCushion` waits out the prebuffer — measured together at ~7 s on a sub-realtime EchokrauTTS run. During that window the caller had no `StreamId` yet (`Guid.Empty`), so `AudioPlaybackService.StopPlaying` stopped nothing and the skipped line played out in full *after* the dialog had closed. Three parts to the fix:
  - `PlayStream(..., Action<Guid>? onStreamCreated)` publishes the id right after the source is registered and **before** `Start()`; `PlayAudioAsync` assigns `message.StreamId` + the `_currentlyPlayingDictionary` entry from that callback.
  - `Source` tracks `_starting`. `StopAndDispose` used to early-out on `State == Stopped` — which is exactly the state during `Start()` — so it now only skips when `Stopped && !_starting`. `StartCore` bails before `Bass.CreateStream` and before `ChannelPlay` (under `_stateLock`) when `_cts` is cancelled, and `Start` swallows teardown fallout while cancelled. `StopPlaying` calls `Stop` unconditionally for the same reason (state-gated stops missed starting sources).
  - `PlayAudioAsync` re-checks `IsObsolete` / `entry.State == Cancelled` **after** `PlayStream` returns and discards without lip sync, toolbar update or completion. It deliberately does *not* key on the engine state: a very short clip may legitimately have finished already and must keep its normal completion path (local save runs off `OnSourceEnded`).
- **Finished entries are pruned; they are not kept for the session.** `_allEntries` used to only
  ever grow — every line ever spoken stayed resident, so `CancelAll`/`CancelBySource`/
  `GetStatistics` walked the whole play history on each dialogue advance, and each entry pinned an
  `IGameObject` through `VoiceMessage.SpeakerFollowObj`. Now: reaching a terminal state nulls the
  two game-object handles (`ReleaseGameReferences` — `Speaker` and `Stream` stay, `OnSourceEnded`
  still saves the audio off the message), and `Enqueue` runs an amortised `PruneTerminal()` that
  drops terminal entries older than `TerminalRetention` (30 s, `internal` so tests can zero it).
  The retention exists because the playback loop and the UI still look an entry up right after it
  went terminal — do not prune on the transition itself.
- **`AudioPlaybackService._currentlyPlayingDictionary` is concurrent and self-clearing.** It is
  written from the playback loop (`PlayStream`'s `onStreamCreated`) and read from `OnSourceEnded`,
  which BASS raises on a thread-pool thread — a plain `Dictionary` can corrupt under that overlap.
  Removal happens in three places because a stream can end three ways: `OnSourceEnded` (`TryRemove`
  instead of `TryGetValue` — natural end), `StopPlaying` (a stopped source never raises
  `SourceEnded`, so nothing else would clean up after a skip) and the startup-skip branch in
  `PlayAudioAsync`.
- **In-flight generation is really aborted — and the abort is tied to the right job.** `IVoiceMessageQueue.SourceCancelled` fires after the epoch moved; `BackendService` subscribes and, when the request it is currently generating belongs to that source, calls `ITTSBackend.StopGenerating` fire-and-forget (EchokrauTTS `POST /cancel/{jobId}`, AllTalk stop endpoint). Discarding the result alone keeps it inaudible but still burns GPU time that the line the player is actually on needs.
  - The in-flight request is one `GenerationInFlight(Entry, JobId)` record, **not** two fields: `OnSourceCancelled` must read entry and job id as one consistent snapshot, or it pairs one line's entry with the next line's job id.
  - The job id is owned by `BackendService`, **not** the backend. `EchokrauTtsBackend` used to keep a `_lastJobId` field, which broke twice over: `RefreshBackend` builds a fresh backend instance and dropped it, and "last job seen" pointed at the *next* line as soon as generation moved on — so a skip aborted the line the player had just skipped *to*. The backend now hands the id out through `GenerateAudioStreamFromVoice(..., onJobStarted)` and takes it back as a parameter on `StopGenerating(eventId, jobId)`. **No `_lastJobId` fallback** — no id means no cancel, which is the safe direction. AllTalk ignores the parameter (its stop endpoint is global).

## Dialog Harvest (`DialogHarvestService` + `LuabParser` + `LgbParser`)
- Extracts NPC dialog from Lumina sheets (DefaultTalk, Balloon) and quest dialog sheets. Persists linked dialogs directly to SQLite with voice auto-assignment.
- `RunAsync(ClientLanguage, CancellationToken, int? questTypeFilter = null)` — harvests for a single selected language. `questTypeFilter` semantics: `null` = everything (default), `0` = only non-quest dialog (DefaultTalk/Balloon/etc., quest scan is skipped entirely), `1..6` = only quests whose `ClassifyQuest` matches that `QuestType` enum value (DefaultTalk/etc. persist is skipped). The `Game Data Tools` window exposes this via a quest-type dropdown next to the harvest button.
- `PersistLinkedDialogs()` — creates characters, contexts, instances, assigns voices, logs voice clips. Caches by character identity (not NpcId) to avoid redundant voice assignment. Suppresses DB events during batch, notifies once at end.
- Race resolution: uses English Race sheet for playable races, falls back to `ModelsToRaceMap` (via `ModelChara`) for beast tribes.
- German/French gender tags (`[a]`, `[p]`) in NPC names are resolved based on NPC gender before storage. Unknown tags stripped via regex fallback.
- Unmatched dialogs (no NPC link) still exported to JSON files.
- **Quest dialog match priority** (in `HarvestQuestDialogs`): (1) user-supplied `quest_npc_aliases.json` per-quest override, (2) Lua bytecode 5-priority resolution, (3) silent-actor paren-prefix heuristic, (4) global `CutsceneNpcAliases`, (5) name-based multi-stage matching.
- **Silent-actor paren-prefix heuristic**: when a quest dialog text starts with `(-???-)` or `(-Name-)` and the Lua resolution didn't pin down an ACTOR, scan the same Lua function for ACTORs that hadn't spoken before this call but do speak afterwards. 1 candidate → auto-attribute (`MatchSource.SilentActorHeuristic`). 0 or ≥2 candidates → defer; if the entry stays unmatched after all priorities, emit it to `<localSaveLocation>/harvest/quest_alias_candidates.json` with full multilingual `Texts`, the heuristic-narrowed `Candidates` (may be empty), `AllActors` (the full `QuestParams` cutscene cast — fallback pick list when the heuristic didn't help), and a `Context` window (1 preceding line + up to 3 following lines, each with their resolved speaker — helps the user identify who's speaking by surrounding dialog). When Lua data is available, ordering is by Lua scene/function (strict bytecode-linear, scene-bounded). When Lua isn't available, falls back to dialog-sheet row order (less precise — may mix scenes — but still useful). Entries that get rescued by name-fallback are NOT emitted. Per-scene only — cross-scene reveals don't count, to avoid false positives.
- **Quest NPC aliases** are loaded from three layered sources, later sources override earlier ones for the same `(QuestId, NpcNameKey)`:
  1. **Embedded** `Resources/QuestNpcAliases.json` (always, ships with the plugin)
  2. **Remote** `RemoteUrlsData.QuestNpcAliasesUrl` (community-curated, fetched at harvest start; failure is non-fatal — falls back silently)
  3. **Local user** `<localSaveLocation>/harvest/quest_npc_aliases.json` (per-user, wins everything)
- Each entry resolves by `npcId` (when set and >0) or `npcName` (English-only, case-insensitive, with normalization: strip spaces/apostrophes/hyphens, uppercase — matches the convention in `npcNameLookup`). English-only is intentional: alias files are author-curated, so a single canonical language prevents subtle locale mismatches. When a name resolves to multiple NPCs (multiple ENpc instances of the same character — e.g. several "Ultros" spawns), the FIRST match is used and a Debug log lists the alternatives. Reason: all matches share the same `(name, gender, race, language)` and funnel into one DB character row anyway, so per-spawn `npc_base_id` is the only thing that differs. Set an explicit `NpcId` only when you specifically want a particular spawn instance. Unknown names → warning + skip. Required fields: `QuestId`, `NpcNameKey`, and either `NpcId` (>0) or `NpcName`. `QuestName` / `Comment` are optional for human readability.
- **Global aliases** use `QuestId: 0` — applies to all quests where the same `NpcNameKey` appears. Use this when the script-name → NPC mapping is a fact about the game (e.g. `ORTHRUS` always = Ultros), not a per-quest override. Lookup tries per-quest first, then global, so a per-quest entry can still override the global if needed.
- Workflow: harvester emits unmatched paren-prefix entries (with full multilingual `Texts`, heuristic `Candidates`, full `AllActors` cast) to `quest_alias_candidates.json`; user picks the right NPC and adds an entry to `quest_npc_aliases.json` (or contributes to the remote file via PR); re-run harvest.
- Performance: clears EF Core change tracker every 500 items.
- **Raw column indices go through `ProbeIntermediateSheet` + `LuminaColumnProbe.MergeInto`.** The shop/warp/TripleTriad/PreHandler sheets are read by hard-coded column index (`RawRow.ReadUInt32Column`), which Square shifts between patches — a shifted column throws. Skipping the value is correct, but it used to happen in ~30 separate silent `catch { }` blocks, so an outdated index looked like "the harvest found nothing" instead of "the indices moved". The reads are now counted and `DoHarvest` emits **one** warning when any failed. `MergeInto` always unions (the first block used to assign outright — harmless only because it ran first against an empty dictionary).
- **Speaker-alias capture during persist** (`PersistLinkedDialogs`): when a linked-dialog text starts with `(-Fakename-)` AND the fakename differs from the resolved NPC name, the harvester upserts a row into `character_speaker_aliases` (character_id, language, alias). **Includes anonymous markers like `???`** — multiple character rows share the same `???` alias, and the live runtime disambiguates via physical-presence + already-spoken tracking (see `VoiceMessageProcessor.ResolveCharacterByAlias`). Cached as `(language, normalized-alias) → List<characterId>` in `DatabaseService._cachedAliasMap`, refreshed on `RefreshCaches()` and cleared on `WipeAll()`.
- **Live alias resolution** (`VoiceMessageProcessor.ResolveCharacterByAlias`): runs after `IJsonDataService.GetNpcName` misses (VoiceNames JSON has priority). Picks in this order:
  0. **BaseId fast path** (`TryResolveByBaseId`): if `speaker.BaseId != 0` AND `_db.FindCharacterIdByNpcBaseId(baseId, language)` hits, return that character row immediately — beating every name-based step below, including unambiguous single-candidate alias matches. This is the most reliable identifier because cutscene actors keep their real ENpcBase even when the dialog box hides the name as "???" or a fakename.
  1. Single alias-map match → return it.
  2. Multiple matches → prefer the character whose `CharacterInstance.NpcBaseId` equals the live `speaker.BaseId`. (Mostly subsumed by step 0; survives for edge cases where the direct lookup's `OrderBy(LastSeen)` chose a different row.)
  3. No BaseId match → narrow to characters whose instances appear in `IGameObjectService.GetSpawnedNpcBaseIds()` (physically present in the current scene).
  4. Still ambiguous → prefer one not yet resolved this dialog session via `DialogState.SpeakersResolvedThisDialog`. Tracker is cleared by `AddonTalkHelper.OnPostUpdate` when AddonTalk closes.
- **Voice-name suggestion emit** (`EmitVoiceNameSuggestions`): after persist, scans every linked dialog (quest + non-quest) for `(-Fakename-)` speaker hints at the start of a line, groups per `(language, NPC)`, and writes TWO output files per locale (`en`/`de`/`fr`/`ja`) into `<localSaveLocation>/harvest/`:
  - `voice_name_suggestions_<lang>.json` — entries safe to merge into `VoiceNames{LANG}.json` (`{voiceName, speakers}` schema, same as `VoiceMap`).
  - `voice_name_collisions_<lang>.json` — entries that look suspicious because the fakename string contains the name of another known NPC (`{fakename, resolvedAs, likelyMeantFor}`). FFXIV references the same DefaultTalk row from multiple ENpcBase rows often, so a `(-Kriles Stimme-)` line gets attributed to both Krile (correct) and Alisaie (wrong). The Alisaie entry lands in collisions for manual review; the Krile entry is clean and lands in suggestions.
  The DB alias capture above is the runtime path and keeps ALL aliases including suspicious ones (the live resolver disambiguates via BaseId). The JSON split is purely about cleaning the community-PR target.
  
  Filter pipeline: skips when the fakename equals the NPC name (no aliasing), the NPC is unresolved (`NpcId=0`), the per-language name is missing, or the fakename is an anonymous `???` marker (those go DB-only). Capture-regex: `^\(-([^-]+)-\)`. Collision-detect helper `FindCollidingNames` does case-insensitive substring match against the per-language name index, skipping names <4 chars and the current NPC's own name. Pure accumulator + collision detector are `internal static` for testability.
- **Cut_scene unvoiced harvest** (`HarvestCutsceneDialogs`): scans every `cut_scene/*` Excel sheet via `IDataManager.Excel.SheetNames`, parses TEXT keys with `VoiceExtractKey.TryParse`, resolves shortname → NPC via the existing English name index, and persists each line as a `LinkedDialog` with `QuestType.None` + `TextSource.AddonTalk`. **Voiced lines are filtered out** via `VoiceScdPaths.Exists(IDataManager, audioBase, langCode)` — if FFXIV ships an SCD audio file for the harvest language, the line is skipped (the harvest only captures the silent residue). ⚠ **Existence alone is not "voiced", and the scan must not stop at the first file found**: SE ships stub SCDs for cut lines (1 byte, or a 416/448-byte header with no payload), and a line can have a stub `_m_` variant next to a real `_f_` one. `Exists` keeps scanning until a candidate clears `MinVoicedScdBytes` (1024 — empirical: 80 de / 76 en cutscene lines sit in the gap between the stubs and the smallest real clip). Gated on `harvestNonQuest` because we don't parse `.cutb` timeline files and can't tell which quest a cutscene belongs to. Race/Gender resolved per-NPC via `npcBaseSheet.GetRow` + `GetRaceString` + `DetermineGender`.
- **Scripted sheet families** (`HarvestScriptedSheetDialogs`): harvests `custom/*`, `raid/*`, `content/*`, `warp/*`, `transport/*`, `opening/*`, `guild_order/*`, `leve/*` — ordinary Talk-addon NPC conversations (beast tribes, seasonal events, side content, inn keepers, aetheryte attendants). **Measured 2026-08-09: ~14,700 lines, by far the largest gap that existed** (the cutscene pass contributes 132). Key convention is `TEXT_<SHEET LEAF UPPER>_<SPEAKER>_<line numbering>`, parsed by the pure `Helper/Functional/ScriptedSheetKey`. Attribution order: (1) speaker token resolves against the NPC name index (11,617 lines), (2) token unknown but the sheet has exactly ONE owner via `CustomTalk` col 2 → `ENpcBase` (`BuildCustomTalkOwnerMap`, 2,905 lines). Anything else goes to `<localSaveLocation>/harvest/scripted_alias_candidates.json` (`ScriptedAliasCandidate`) rather than being guessed. Shares the `harvestNonQuest` gate with the cutscene pass (`QuestType.None`).
  - **No voiced-filter is applied and none is needed.** The entire voiced corpus under `cut/*/sound/` is the cutscene family — all 25,410 resolvable SCDs share the single prefix `vo_voiceman_*` — and `SoundHelper` only recognises `^cut/.*/(vo_|voice)` as a Talk voice line. These families ship no voice acting (probed: 0 hits across all of them).
  - **`dungeon/*` is deliberately excluded**: the only family with no `game_script/` counterpart (0 of 31 sheets) and no speaker segment in its keys.
  - `ScriptedSheetKey.IsNonSpeechToken` drops `SYSTEM`, menu/button captions, flow markers (`ENTER`/`LEAVE`/`LCUT`/`TALK`), and the player's own choice prompts (`Qn`) and answers (`An`) — 11,420 lines. **Uncertain tokens are deliberately NOT on that list**: odd names like `PLATOONINSTRUCTOR` or `MEMBERC01548` become alias candidates for review instead, because deciding to omit a line is a judgement call.
- **Development-placeholder filter** (`Helper/Functional/HarvestTextFilter`, pure + tested): Square Enix keeps rows for cut lines and fills them with `（★未使用／削除予定★）` instead of deleting them. Applied in the quest, cutscene and scripted-sheet passes.
  - ⚠ **Do not quote "12,673 junk lines" — that number was wrong** and is kept here as a warning. Counting rows that *contain* the marker (de 12,673 / fr 11,191 / en 970) is NOT the number of rows that reach the DB, because the pre-existing `nonEmpty.Count == 1` filter ("all four locales identical") already dropped most of them: the placeholder text is identical in de/fr/ja, so only rows where one locale still had real text survived. **Measured properly** by running the whole quest stage old-vs-new over all 5,532 sheets: **232,221 → 231,862 kept, i.e. ~359 lines**. The `allSame` counter falls 10,866 → 917 in the same run, which is where the rest always went. DefaultTalk/`FlattenTexts` is unaffected (13,621 → 13,621).
  - The filter still earns its place: those ~359 quest lines plus ~1,032 in `custom/*` (a source that was never harvested before, so they are prevented rather than removed) are Japanese developer text that TTS would read out loud. It is also what makes the stub-SCD fix safe — most of the 69 cutscene lines that fix recovers are themselves `（★未使用／削除予定★）`.
  - **Lesson for any future "how much does this fix buy" claim: measure the pipeline end to end, not the input.** Every filter downstream can already be catching what you think you are catching.
  - ⚠ **A bare substring test on `未使用` is wrong** — it also matches a real Easter-event line ("また、未使用のを…", unused *eggs*). The rule requires the `★` decoration AND a marker; measured cost of that extra condition is one line game-wide. The inverse is equally wrong: `★` alone appears in legitimate stylized dialogue (Gigi, `quest/006/ClsGld500_00658`).
  - The check is **scoped to the harvest language**. The old `PlaceholderTexts` set tested every locale and dropped the entry if any was a placeholder — with the broader rule that would delete lines that are fine in the user's language but were cut in another (de 10,660 vs fr 9,435, overlapping but not identical).
  - `PlaceholderTexts` used to live in `DialogHarvestService` as an **exact-match** set used only by `FlattenTexts` — which is exactly why the decorated form slipped through. It now lives in `HarvestTextFilter` and both call sites share it.
- `LuabParser` parses Lua 5.1 bytecode from FFXIV quest scripts to map text keys → ACTOR NPC IDs.
- Key bytecode patterns: SELF (op=11) for method calls, GETTABLE (op=6) for text key loading, CLOSURE+SETGLOBAL for scene function registration, EQ blocks in dispatch functions for ACTOR→scene routing.
- `ParseWithDispatch()` returns both Talk calls and dispatch-based scene→ACTOR mappings.
- Debug info is stripped from FFXIV scripts — no local variable names available.
- `LgbParser` parses FFXIV LGB (Level Group Binary) territory files to extract ENpc entries.
- LGB structure: File header (LGB1) → Chunk (LGP1) → Layers → Instance objects (typed entries).
- ENpc entries (type=8) contain BaseId (ENpcBase row ID) at +0x30 and Behavior at +0x4C from entry start.
- Balloon IDs are NOT in LGB — resolved via ENpcBase→Balloon (col 105) and LGB Behavior→Behavior sheet→Balloon chain.
- Scans planevent.lgb, planmap.lgb, planlive.lgb, planner.lgb across all TerritoryType rows.

## Database Service (`IDatabaseService` / `DatabaseService`)
- EF Core with SQLite (`echokraut.db` in plugin config dir).
- Schema: `characters` → `character_contexts` → `character_instances` → `voice_clips` → `voice_clip_generations`, plus `voices`, `voice_allowed_races`, `voice_allowed_genders`, `phonetic_corrections`, `lodestone_lookups`.
- 13 schema migration versions in `RunSchemaMigrations()`. Fresh installs skip v2–v4 (those reference old table names). v5+ use try-catch guards since `EnsureCreated()` already creates the final schema on fresh installs.
- `LogOrUpdateVoiceClip()` upserts by CharacterId + NpcBaseId + OriginalText (composite index for performance). **Orphan-resolve fallback**: if the primary text-based lookup misses AND the incoming clip has a non-empty `WavFileName`, a secondary lookup by `(CharacterId, WavFileName)` runs; on hit, the existing row is promoted in place (text/base id/source/language fields filled from the live encounter, no duplicate row inserted). This rescues the wav-only rows produced by `BackfillAudioFiles` so legacy on-disk audio gets stitched onto the live runtime path the first time the same NPC says the same line again.
- `MigrateFromConfig` and `BackfillAudioFiles` are **deferred to post-login**, not run from `InitializeDatabase`. `Plugin.cs::RunDataMigrationsIfLoggedIn()` is invoked from `HandleStartup` (when already logged in) and from `OnLogin`. Reason: placeholder detection and player-content-id resolution need `LocalPlayer` to be present, which isn't true at plugin construction time. `Configuration.AudioFilesBackfillPending` gates the audio scan so it runs once and never repeats. The wav-filename hashing rule mirrors the runtime path: `IAudioFileService.VoiceMessageToFileName(IAudioFileService.RemovePlayerNameInText(originalText))` — `VoiceMessageProcessor` now depends on `IAudioFileService` so it computes and stores `WavFileName` on every new clip, which is what makes the backfill's orphan rows match.
- Characters unique on `(name, gender, race, language)` — NPCs stored per game language.
- `FindCharacter(name, gender, race, language)` — language is **required** (no default). Misleading default of `1`=English caused silent miss-then-duplicate bugs on non-EN clients; removed in current schema.
- Per-player + alias generations: `LogVoiceClipGeneration / GetVoiceClipGeneration / DeleteVoiceClipGeneration` take an optional `aliasGender` param (0=real player, 1=male alias, 2=female alias). Unique index `(voice_clip_id, player_content_id, alias_gender)` lets a clip carry the player's own audio plus shareable alias variants in parallel rows. Alias rows always pass `playerContentId=0`.
- `SuppressEvents` flag: set during batch operations (harvest) to prevent UI threads from querying DB concurrently. Call `NotifyVoiceClipLogged()` once after batch completes.
- `ClearChangeTracker()`: clears EF Core change tracker during bulk inserts to prevent progressive slowdown.
- `FindCharacter()` uses `_writeLock` for thread safety with concurrent harvest writes.
- `Dispose()` explicitly closes connection + calls `SqliteConnection.ClearAllPools()` to release file lock.
- Constructor wraps `InitializeDatabase` in try-catch; calls `Dispose()` on failure so DB is released.
- **`GetAllCharacters()` is the one uncached read — never call it on the live path.** It pulls the
  whole `characters` table (`AsNoTracking().ToList()`) under `_writeLock`; everything else
  (`GetNpcs`/`GetPlayers`/`GetVoices`/`GetPhoneticCorrections`/`GetMutedBaseIds`) serves from a
  cache. Use **`FindCharacterById(int)`** for a single row — the alias resolver in
  `VoiceMessageProcessor` used to call `GetAllCharacters().FirstOrDefault(c => c.Id == …)` per
  spoken line, which on a harvested DB is a five-figure row scan per line.
- **DB events must be raised OUTSIDE `_writeLock`.** `VoiceClipLogged` has `NpcDataService.LoadFromDatabase` attached; firing it under the write lock runs a foreign handler inside our lock. Any handler that takes a lock of its own then forms a lock-order cycle with the dialogue path, which constantly holds that lock and calls into `IDatabaseService` — a full game freeze. `LogVoiceClip`/`LogOrUpdateVoiceClip` used to do this; both now collect a `notify` flag and raise after the lock (`WipeAll` was already correct). **This is what makes it possible to ever put a lock around `NpcDataService`'s mapped-NPC lists** (audit doc B5) — don't reintroduce in-lock event raises.
- **`DatabaseWiped` event** fires from `WipeAll()` after the row-level cleanup completes. `BackendService` subscribes to it in its constructor and calls `RefreshBackend()` so the voices table is repopulated from the running TTS backend instead of staying empty until the next plugin reload.
- **BaseId-first runtime resolver** (`VoiceMessageProcessor.TryResolveByBaseId`): when the live speaker has a non-zero `IGameObject.BaseId` AND `_db.FindCharacterIdByNpcBaseId(baseId, language)` returns a hit, that character row is returned immediately — beating any name-based alias lookup, including unambiguous single-candidate matches. FFXIV cutscene actors keep their real ENpcBase even when the dialog box hides their name as "???" or a fakename, so this is the most reliable speaker identifier. Without it, anonymous speakers fell through to the alias map; if the real speaker hadn't been harvested with `(-???-)`, the resolver picked a wrong-but-present alias candidate and silently attributed the line to that NPC. The historical mis-attribution in existing DBs is what `INpcAttributionRepairService` cleans up.
- **Attribution repair helpers** on `IDatabaseService`:
  - `GetAllInstancesForRepair()` — flat-projected join of every `character_instance` with its parent character's (name, gender, race, language). No EF tracking.
  - `ReassignAttribution(oldCharId, newCharId, npcBaseId)` — moves the instance row (delete+reinsert because PK is composite) AND every `voice_clip` with the same `(CharacterId, NpcBaseId)` pair from old to new. Unique-index collision on `voice_clips (CharacterId, NpcBaseId, OriginalText)` → canonical wins, orphan + its generations dropped via cascade. Returns `(moved, mergedAndDeleted)`. Refreshes muted cache.
  - `DeleteCharacterIfEmpty(characterId)` — returns true if the character row had no instances and no voice_clips and was deleted; idempotent on already-deleted / still-populated rows.

## Changelog on update (`IChangelogService` + `NativeChangelogWindow`)
- Embedded changelog files live at `Resources/Changelogs/v{MAJOR.MINOR.BUILD.REVISION}_{EN|DE}.txt` and are referenced by `Echokraut.csproj` as `<EmbeddedResource>`. Add **two files per release** (EN + DE); FR/JA fall back to EN at runtime via `ChangelogService.PickLanguage`.
- Filename **must** start with `v` and match the regex `^Echokraut\.Resources\.Changelogs\.v(\d+\.\d+\.\d+\.\d+)_(EN|DE)\.txt$`. The `v` prefix anchors the version regex against the surrounding dotted resource path (otherwise the four version dots blend with the namespace dots).
- `Configuration.LastSeenChangelogVersion` (default `"v0.18.0.6"`) holds the last version whose changelog the user dismissed. `ChangelogService.GetUnseenChangelogs()` returns every embedded entry with version > LastSeen and ≤ `Plugin.PluginVersion`, ordered ascending.
- **Brand-new install handling**: the FirstTime wizard's "I Understand" callback (wired in `NativeWindowManager`) calls `IChangelogService.MarkAllSeen()` before opening the config window — without this, the changelog popup would immediately follow the wizard with notes about features the user already starts with.
- **Show gate** lives in `Plugin.HandleStartup` and `Plugin.OnLogin`. Order: `FirstTime` first → `Changelog` second. The changelog only opens when `!FirstTime && IsLoggedIn && !IsChangelogOpen && HasUnseenChangelogs()`.
- Defensive: if `LastSeenChangelogVersion` is corrupt/unparseable, the service returns an empty list (don't show the entire history) and warns. Same for unparseable plugin version.
- Source of truth: the embedded `Resources/Changelogs/v{TO}_{LANG}.txt` files. For external use (GitHub releases, Discord, etc.) reformat from the same content — there's no separate public-facing copy in the repo to keep in sync.

## Player-name placeholders (`-PlayerName-` / `-PlayerFirstName-` / `-PlayerLastName-`)
The harvest stores lines that address the player with these tokens
(`DialogHarvestService.ExtractTextWithPlayerName`); the live runtime gets the same line from the
game with the name already substituted. Four rules keep the two halves in sync — breaking any one
of them reproduces a bug that was live in 0.19.3.0:

- **`IGameObjectService.LocalPlayerName` must be ticked, not stumbled upon.** It (plus content id
  and gender) used to be filled ONLY as a side effect of the `LocalPlayer` getter, which nothing
  calls until the first dialog line is processed — so a freshly loaded plugin reported an empty
  player name while the user was logged in. Everything downstream degrades *silently* in that
  window: `TalkTextHelper.SubstitutePlaceholders` returns the text unchanged, `BackfillAudioFiles`
  loses its placeholder detection, and `GetEffectivePlayerContentId` answers 0.
  `RefreshLocalPlayer()` is now called from `Plugin.OnFrameworkUpdate` (every 60 s; every 2 s while
  the name is still empty) and once from `RunDataMigrationsIfLoggedIn`. It **clears** the cache
  when no player is present, because a stale name from the previous character is worse than none.
- **`voice_clips` only ever stores tokens.** `VoiceMessageProcessor.LogVoiceClip` runs the text
  through `Helper/Functional/PlayerNameTokenizer` before persisting (the spoken `message.Text` /
  `OriginalText` stay untouched). Without it the live row's `OriginalText` differed from the
  harvested row's, the `(CharacterId, NpcBaseId, OriginalText)` upsert missed, and the same dialog
  line ended up as two rows — both pointing at the same WAV (the filename hash collapses
  `-PlayerFirstName-` and `<PLAYERFIRSTNAME>` to the same token) but tracked under different
  `player_content_id` buckets via `HasPlayerPlaceholder`.
- **A placeholder must never reach the backend.** `VoiceClipManagerService.HasUsableText` refuses
  the generation when `ContainsPlayerPlaceholder` is still true after substitution. The failure is
  sticky otherwise: the WAV in which the TTS reads "-PlayerFirstName-" out loud lands on exactly
  the path `TryLoadCachedAudio`'s disk fallback adopts, so one bad generation is replayed forever.
- **Two placeholder vocabularies exist and are NOT interchangeable.** Text/DB side uses
  `-PlayerName-` (`TalkTextHelper.Placeholder*`); the file-name hash uses `<PLAYERNAME>`
  (`AudioFileService.RemovePlayerNameInText`). They deliberately normalise to the same on-disk
  name — that is what lets the harvested row, the live row and a restored backup share one file.

`IDatabaseService.RetokenizePlayerNames(playerName, dryRun, ct)` is the one-shot cleanup for
databases written before this (UI: Game Data Tools → "Player Name Cleanup", dry run + apply). It
rewrites literal-name rows back to tokens and, when that makes a row identical to its harvested
twin, moves the generation rows over and deletes the duplicate. Generation rows that would collide
on `(voice_clip_id, player_content_id, alias_gender)` are dropped — they described the same file.
Nothing on disk is touched. Covered by `RetokenizePlayerNamesTests` + `PlayerNameTokenizerTests`.

## Audio packages (`IAudioPackageService`, `*.ekpack`)
Hand a big batch of generated audio to another user so their install attributes every file to the
right character, language and voice. UI: Game Data Tools → "Import / Export".

- **A package is a plain zip**: `manifest.json` at the root, the WAVs under `audio/<speaker>/<file>.wav`.
  Nothing is written *into* the audio files — no RIFF `LIST`/`INFO` tags, no custom chunk. That was
  the deliberate choice over embedding metadata (evaluated 2026-08-09): the live generation path and
  `RawPcmToWav` stay untouched (its size patching uses hard-coded offsets 4/40 that a chunk before
  `data` would invalidate), and a package can be built from audio generated years ago. **If you ever
  do embed a chunk, it is an addition to this, not a replacement** — the manifest stays the source of
  truth because a receiver must be able to index 20 000 clips without opening 20 000 files.
- **The manifest is versioned** (`AudioPackageFormat.CurrentVersion` / `MaxSupportedVersion`). A
  package from a newer plugin is refused with a readable message rather than half-read.
- **What never leaves the sender** (`Helper/Functional/AudioPackageRules`, pure + unit-tested):
  - `IsShareable` — a placeholder clip's `alias_gender == 0` generation speaks the *exporter's own
    character name*. Those are skipped and counted; their male/female alias variants
    (`alias_gender` 1/2, "Adventurer"/"Abenteurer") are exactly the shareable form and go in.
  - `RelativeEntryPath` keeps only `<speaker folder>/<file name>` — the sender's absolute
    `LocalSaveLocation` (which contains a Windows user name) never reaches the manifest.
  - No `player_content_id` and no player name are written; imports always store under
    `playerContentId = 0` (`ImportPlayerContentId`).
- **Import creates what's missing** — character → context → `voice_clips` → `voice_clip_generations`,
  so a receiver who never harvested still gets working audio. `UpsertCharacter` is called with an
  empty `VoiceKey` on purpose: which voice an NPC uses is the receiver's setting; the *generation*
  row records what the audio was actually made with.
- **Existing local audio is never overwritten.** The file is kept and counted under
  `SkippedExistingFile`, but the DB row is still written so the clip counts as generated.
- **Zip-slip guarded** via `ResolveImportTarget`; entries resolving outside `LocalSaveLocation` are
  counted as failures.
- **Idempotent**: a second import of the same package creates no characters, no clips and no
  duplicate generation rows (`LogVoiceClipGeneration` upserts on the unique key).
- **Bulk plumbing**: import sets `BulkMode` + `SuppressEvents` and restores both in `finally`,
  followed by `FlushChanges` / `RefreshCaches` / `NotifyVoiceClipLogged`. A per-run `ImportCache`
  memoizes character ids, ensured contexts and each character's existing clip keys — without it every
  entry re-queried that character's whole clip list. New clips need a `FlushChanges` before their
  generation row, because batch mode defers `SaveChanges` and the id would still be 0.
- Covered by `AudioPackageRulesTests` (rules) and `AudioPackageServiceTests` (two in-memory installs
  round-tripping a real zip through the temp folder).

## Live-path orphan WAV adoption
- `VoiceMessageProcessor.TryLoadCachedAudio` (Step 8.5 in `ProcessSpeechAsync`) does a **two-stage cache lookup** before handing the message to the backend:
  1. DB `voice_clip_generations` row — fast path for clips this install previously generated/adopted.
  2. **Disk fallback** via `IAudioFileService.TryFindExistingLocalAudio` — probes the deterministic `{LocalSaveLocation}/{Speaker.Name}/{VoiceMessageToFileName(RemovePlayerNameInText(OriginalText))}.wav` path. A hit means the WAV exists on disk without a DB row (e.g. backup copied from a friend's install, manual restore). The file is adopted by writing a `voice_clip_generations` row with the speaker's current `voice` key and the effective player id, then loaded into the message stream the same way as a DB-cache hit.
- **Gating**: `Configuration.LoadFromLocalFirst` only. `SaveToLocal` is the write side of the cache and has no effect on playback or adoption. (Earlier code gated on either flag — tightened in feature/none-mode-ui-polish to reflect the user's intent: a user with `LoadFromLocalFirst=false` explicitly opted out of disk reuse.)
- **Manager paths are intentionally not adopting orphans.** `VoiceClipManagerService.GenerateForVoiceClip` and `GenerateAllUnsaved` go straight to the backend (or short-circuit on None-mode) — adoption happens lazily, dialog-by-dialog, the first time the line plays in-game. Rationale: no need to bulk-scan disk on demand when the natural live-path adoption covers everything as it gets used.
- Adoption uses `LogVoiceClipGeneration` (upsert-keyed on `(voice_clip_id, player_content_id, alias_gender)`), so re-firing the same dialog never duplicates rows even if the disk file existed before the DB row did.
- The bulk one-shot scan (`DatabaseService.BackfillAudioFiles`) still exists for the migration story, but is no longer the only way orphan files reach the DB.

## Live generation logging (`ILiveGenerationLogger`)
- Mirrors the bulk path's `_db.LogVoiceClipGeneration(...)` call so that voice clips produced through chat / addon talk / bubble events become visible in the Voice Clip Manager (status: generated, save path stored).
- Wiring: `VoiceMessageProcessor.LogVoiceClip` captures the `Id` returned by `IDatabaseService.LogOrUpdateVoiceClip` into `VoiceMessage.VoiceClipId`. After `AudioPlaybackService.OnSourceEnded` writes the audio file, a `ContinueWith` continuation calls `_generationLogger.LogIfApplicable(voiceClipId, hasPlayerPlaceholder, savePath, eventId)` on `TaskScheduler.Default`.
- `LogOrUpdateVoiceClip` returns the persisted `VoiceClipEntity` (with `Id` populated in non-batch mode) — needed because the live-path code needs to know which DB row to attribute the generation to. In batch mode (`SuppressEvents=true` during harvest), the returned `Id` may still be 0 and is intentionally ignored by the harvest caller.
- **`HasPlayerPlaceholder` must travel with the message.** `VoiceClipManagerService.GetEffectivePlayerId` keys generation rows on this flag (placeholder clips → local content id, non-placeholder → 0), so the live path must store under the same id. `VoiceMessageProcessor.LogVoiceClip` computes it via `TalkTextHelper.ContainsPlayerPlaceholder(originalText)` when persisting the clip, mirrors it onto `VoiceMessage.HasPlayerPlaceholder`, and the logger replicates the same effective-player-id rule. Storing under the wrong id silently breaks the manager's "is generated" lookup — symptom seen in practice: rows are written but the UI keeps showing clips as "not generated".
- Auto-alias generation stays bulk-only. Generating male+female alias variants would mean three backend calls per chat line — too expensive for the live path. Users who want alias variants opt in via the Voice Clip Manager UI.

## Voice Pack (`IVoicePackService` / `VoicePackService`)
- **Why it exists:** the previous "voice starter set" feature decoded FFXIV's own `.scd` voice
  files (MS-ADPCM → PCM → resample) and wrote them out as cloning references. That was removed
  wholesale for legal reasons — no code in this repo may read, decode or redistribute game audio.
  If you are tempted to re-add an SCD decoder for *audio*, don't.
  (`VoiceScdPaths.Exists` / `LanguageCodeForScd` survive, but only to answer "does FFXIV voice this
  line?" for the harvest filter — they never read sample data.)
- **What it does instead:** downloads one curated zip from `RemoteUrlsData.VoicePackUrl` and unpacks
  it into the target folder. `DownloadAsync(ct, outputRootOverride, outputSubfolder)`.
  - `outputRootOverride == null` → `<LocalSaveLocation>/<outputSubfolder>` (default `Voices`),
    **existing files are kept**.
  - `outputRootOverride != null` → `<root>/<subfolder>`, **target is wiped first** (fresh-install
    semantics, same as the old extractor). Used by both install flows:
    `AlltalkInstanceService.Install` → `alltalk_tts/voices`, `EchokrauTtsInstanceService.DownloadVoicePack`
    → `echokrautts/samples`. Both skip the download when the engine's `CustomVoicesUrl` is set.
  - Empty `VoicePackUrl` = pack not published yet → warn + no-op (never fails an install).
  - Zip-slip guarded: entries resolving outside the target folder are skipped with a warning.
- **UI**: `NativeGameDataToolsWindow` "Voice Pack" section (button + shared progress bar).
  `ResolveVoicePackTarget()` picks the ACTIVE engine's local voice folder, else `Voices/`.
- **Batch gating**: `BatchOperation.VoicePackDownload` (was `VoiceExtract`) via `IBatchModeService`.
- **On-disk contract the pack must satisfy** (unchanged from what the engines expect): 16-bit PCM
  mono WAV + a same-basename `.txt` transcript per sample. 22050 Hz (AllTalk) / 24000 Hz (XTTS
  native) both work. The `.txt` is for the engine (F5 needs a reference transcript); the plugin
  never reads it.
- **Filename grammar is load-bearing — get this wrong and voices install but stay inert.**
  `BackendService.MapVoices` stores the filename *with extension* as the voice identity
  (`BackendVoice`) and derives everything else from the basename exactly once, at first sight:
  `Gender_RacePool[-BodyType]_NPCnnn.wav`
  - `ReSetVoiceGenders` reads **segment[0] only** → must be `Male`/`Female`.
  - `ReSetVoiceRaces` reads **segment[1] only**, split on `-` → race token (`All` = every race in
    `Constants.RACELIST`, or an exact `NpcRaces` token: `Miqote`, `AuRa`, `Hyur` — no apostrophes,
    no spaces, not "Hyuran") plus optional `Child`/`Elder`/`Adult` body token. Adult = omit.
  - `UseAsRandom = basename.Contains("NPC")` — **case-sensitive substring**. `FitsNpcData` requires
    it, so a pack voice without `NPC` in the name is never auto-assigned, only manually pickable.
  - `EchokrautVoice.VoiceName` (display) = first `_`-segment that isn't gender/race/body-type.
  - Empty `AllowedRaces` is a hard reject in `FitsNpcData`; empty `AllowedGenders` is a wildcard.
  - **Numeric segments are a trap**: `Enum.TryParse` accepts `"01"` as the enum value 1, so
    `Male_01_Foo.wav` is silently filed as race Hyur. Never use bare numbers as a segment.
  - Optional `Narrator.wav` (exact spelling, top level) replaces the default narrator fallback —
    the `IsDefault` compare uses the full backend string, so it must not sit in a subfolder.
  - Renaming a sample after release = old voice deleted + new voice added + NPC migration. Fix the
    names before publishing the pack.
  - `VoicePackService.WarnAboutUnparsableNames` logs a warning for both failure modes after
    unpacking (malformed grammar / missing `NPC` marker). Good: `Male_All_NPC001.wav`,
    `Female_All-Child_NPC091.wav`. Bad: `voice_m_07.wav`, `Male_01.wav`, `Male_All_M01.wav`.
  - Subfolders inside the voice folder are **unverified** for both engines — keep the pack flat.
- **Pack content + licensing (must hold for anything published at `VoicePackUrl`)**:
  - Never include CC BY-NC / -ND / -SA material (EARS, Expresso, Emilia, MyST, People's Speech SA
    subset are all disqualified).
  - The zip **must** contain `LICENSE.txt` (full legalcode) and `ATTRIBUTION.csv` with one row per
    clip: source dataset, original speaker id, authors, license, source URL, and whether the clip
    was modified (CC BY 4.0 §3(a) requires the modification notice — every clip in the pack is
    modified, since utterances are concatenated and normalised).
  - Keep the pack versioned and the attribution keyed by speaker id so a GDPR erasure request can
    actually be honoured; don't surface original speaker ids in the UI.
  - Upstream datasets are 1–100 GB and cannot be fetched at runtime — the pack is always a
    pre-curated release asset hosted by us.

### Pack v1.1.0 — child voices rebuilt from English adults (2026-08-02)
Same 260 voices and the same 200 adults as v1.0.0; **only the 60 child voices were replaced**,
~112 MB zipped. The Samrómur (Icelandic) children of v1.0.0 did not hold up in practice — the
cloned output was not good enough — so the documented fallback was taken: child voices are now
**pitch- and formant-shifted English LibriTTS-R adults**, i.e. child-*sounding* adult speakers,
not recordings of children. The pack is therefore single-source (LibriTTS-R) and single-language.

- **The shift is Praat `Change gender...`** (`praat-parselmouth`): resample — which raises pitch
  AND formants together — then restore duration and retarget the pitch median. Moving the
  **formants** is the whole point; raising pitch alone reads as helium, raising formants with it
  reads as a physically smaller speaker.
- Chosen by ear from an A/B set (`demo_child/`), setting "B medium":
  girls `formant 1.20 / 262 Hz / range 1.15`, boys `formant 1.14 / 240 Hz / range 1.00`.
  Result 237–270 Hz, which is the real 7–10-year-old band.
- **Both child genders come from adult FEMALE sources.** Boys and girls are acoustically
  near-identical at that age, and a male source would need a formant ratio near 1.4 and audibly
  falls apart. Sources are paired lowest-pitch-first with the lower (boy) target so each clip gets
  the least manipulation that reaches its target. Source selection is by **measured** F0 ≥ 172 Hz,
  not by corpus gender metadata.
- **Sourcing pitfall — the clean splits are nearly exhausted.** `test-clean` + `dev-clean` +
  `train-clean-100` hold ~330 speakers and 200 already ship as adults; only 58 were left and the
  F0 filter passes ~45 % of those. `build_children.py` therefore has three source paths in order:
  the indexed splits → `stream_sources` over `train-clean-360` → reusing the pack's own adult
  female clips. The middle one walks the split **without a prior index**: rows are ordered by
  speaker, so one `/rows` window every 300 rows *is* a speaker sample and every throttled request
  produces a candidate instead of only mapping one. It filled 39 of the 60; the reuse path was not
  needed, so no speaker appears twice in the pack.
- CC BY 4.0 permits adaptations, but §3(a) requires declaring them: every child row in
  `ATTRIBUTION.csv` carries the exact formant ratio, target pitch and range factor, and `README.txt`
  states plainly that these are not recordings of children.
- `build_children_samromur_v1.py` is kept — it built the published v1.0.0 and is the record of it.

### Pack v1.0.0 — what was actually built (2026-08-01)
260 voices, 1:1 gender ratio in both age groups, ~105 MB zipped:
100 `Male_All_NPCM###` + 100 `Female_All_NPCF###` (adults),
30 `Male_All-Child_NPCCM##` + 30 `Female_All-Child_NPCCF##` (children).
One reference clip per voice, 7.5–14 s, 24 kHz / 16-bit / mono, plus its `.txt` transcript.
Build scripts + `ATTRIBUTION.csv` live in `F:/Git-Repositories/Dalamud/VoicePack/` (outside any repo).

- **Adults: LibriTTS-R** (CC BY 4.0, Google restoration), splits `test-clean` + `dev-clean` +
  `train-clean-100`. It is already 24 kHz mono — the pack's target format — so adults are never
  resampled. Speaker gender comes from the corpus's own `speakers.tsv` (`doc.tar.gz` on
  <https://www.openslr.org/141/>), never from pitch estimation.
- **Children: Samrómur Children** (CC BY 4.0, Icelandic), dev+test tarballs (~97 MB each).
  Real child speech, ages 6–14, ranked by an SNR/clipping score because the corpus is
  crowdsourced and per-speaker quality varies far more than content does. 16 kHz → 24 kHz.
  The language mismatch was a deliberate bet — cloning references carry timbre, not language —
  and it **lost**: see v1.1.0 above, which replaced these with the formant-shift fallback.
- **VCTK 0.92 was evaluated and rejected**: datashare.ed.ac.uk serves it as one 11.7 GB zip whose
  single inner member is one deflate stream, so HTTP range requests can't pull individual files —
  it's all-or-nothing. LibriTTS-R is reachable per clip through the HF `datasets-server` `/rows`
  endpoint (throttles hard: ~3 workers, long backoff, cache every page).
- **Never truncate a clip to hit the length cap.** Concatenate whole utterances only and skip any
  that would overflow. Cutting mid-sentence desyncs the audio from its `.txt`, which F5-style
  backends pair together — and it is invisible in every install-time check.

## Voice Clip Manager Service (`IVoiceClipManagerService` / `VoiceClipManagerService`)
- Encapsulates voice clip actions so both ImGui and Native windows stay thin.
- `PlayEncounter()` uses `TextSource.VoiceTest` to bypass dialog-closed check.
- `StopPlayback()` stops current message + clears VoiceTest queue.
- `DeleteAudioForEncounter()` calls `StopPlayback()` + `Thread.Sleep(100)` before delete (BASS holds file lock).
- `GetAudioPath()` uses `SavePath` from DB if available, falls back to computed path.
- `GenerateForEncounter()` saves path to DB via `UpdateVoiceClipSaved()`.
- `BuildVoiceMessage()` constructs `EKEventId` from base via `new EKEventId(baseId.Id, baseId.TextSource)`.
- **Auto alias generation**: when `Configuration.AutoGenerateShareableAliases` is on, every successful `GenerateForVoiceClip` for a clip with `HasPlayerPlaceholder` automatically calls `GenerateAliasVariant(clip, isMale: true)` and `GenerateAliasVariant(clip, isMale: false)` afterward. Each runs as its own backend call; alias-variant failure is logged at warning level and does not fail the main generation. The alias text is `TalkTextHelper.GetPlayerAlias(clip.Language, isMale)` — uses the **clip's** language, not the current client language. Audio file paths diverge naturally because the substituted text differs (alias name vs. real player name).

## Mapped-NPC caches (`NpcDataService`) — copy-on-write
`_mappedNpcs` / `_mappedPlayers` are read from the UI (frame thread), the dialogue pipeline (thread pool) and the `VoiceClipLogged` handler. They used to be shared `List<NpcMapData>` handed straight out of `MappedNpcs`/`MappedPlayers`, which raced (`Collection was modified` while a reader iterated) and let callers mutate them from anywhere.

- **Readers** take the `volatile` field once and get a complete, never-mutated snapshot. Safe to iterate without copying.
- **Writers** build a new list under `_mapLock` and publish it by assignment (`AddToCache*` / `RemoveFromCache*` / `ClearMappedCaches`).
- **`_mapLock` must never wrap an `IDatabaseService` call.** That is the whole reason for copy-on-write instead of a plain lock: this service calls into the DB constantly, and the DB raises events that land back here (`LoadFromDatabase`). A lock spanning both directions is a lock-order cycle → game freeze. `GetAddCharacterMapData` therefore decides *inside* the lock and does its DB work *after* it.
- **`LoadFromDatabase` builds both lists fully, then publishes.** The old in-place `Clear()` + `Add()` loop left a window where a reader saw an empty cache.
- **The properties are `IReadOnlyList` on purpose.** Handing back a `List<>` would let a caller mutate the snapshot, which silently affects nothing — the type makes that a compile error instead. Eight call sites used to do `RemoveCharacter(x)` **and** `MappedXxx.Remove(x)`; `RemoveCharacter` now maintains the cache itself, and `ClearMappedCaches()` covers the wipe path.
- **Find-or-add in `GetAddCharacterMapData` is atomic.** Two dialogue lines for the same unseen NPC could otherwise both miss and both insert.
- What this does *not* guard: the `NpcMapData` objects inside the lists. Those stay shared mutable state by design (`SaveCharacter` updates the canonical entry in place).
- Covered by `NpcDataServiceCacheTests`, including a reader/writer concurrency test.

## Dialogue state (`IDialogStateService` / `DialogStateService`)
Cross-cutting dialogue state: `CurrentVoiceMessage`, `IsVoiced`, `IsInsideOwnedWindow`, plus speaker tracking (`MarkSpeakerResolved` / `WasSpeakerResolved` / `ClearResolvedSpeakers`).

- **Injected, not static.** This was a static `DialogState` class (SonarQube **S2696**); it is now a normal service consumed by `VoiceMessageProcessor`, all four addon helpers, `VoiceTestService`, `DialogTalkController` and `Plugin.WireEvents`.
- **It only works because container factories are lazy singletons.** `ServiceContainer.GetService` caches what a factory returns, so every consumer shares one instance. A per-resolve instance would split the state between the addon hooks, the pipeline and the toolbar without any visible error — that caching is pinned by `ServiceContainerTests.GetService_FactoryRegistration_RunsOnceAndCachesTheInstance`.
- **Speaker tracking is a `ConcurrentDictionary` behind methods.** It used to be a public `HashSet<int>`, written from `ProcessSpeechAsync` (thread pool) and cleared from the addon lifecycle (framework thread) — an overlap a plain `HashSet` does not survive. `CurrentVoiceMessage`/`IsVoiced` are `volatile` for the same reason.
- Covered by `DialogStateTests` (per-test instances now — no shared process state, unlike the static version).
- `IsInsideOwnedWindow` — shared hit-test function set by `DialogTalkController.SetWindowHitTest()`, consumed by `AddonTalkHelper.OnPreReceiveEvent()` to prevent speech cancellation on window clicks.

## Key Patterns
- New features → create interface + implementation here
- Register in `ServiceBuilder.cs`
- Use events for cross-component communication
- `Helper/Functional/` is for pure stateless utilities only (no DI needed)

## Echolines community line database (client, phases 3 / 3b / 7)

`ILineSubmissionService` / `LineSubmissionService` reports dialogue lines to the Echolines service so
lines missing from the plugin's own data can be found. Server side lives in its own repo; the wire
format, limits and consensus rules are quoted in `docs/` of that repo, not restated here.

- **Base address is `RemoteUrlsData.EcholinesUrl`** (`RemoteUrls.json` → `echolinesUrl`), not a
  constant: the service can move without a plugin release, and **an empty value switches reporting
  off entirely** — that is the kill switch if the service has to go down.
- **Call site is step 3.5 in `VoiceMessageProcessor`**, *not* `LogVoiceClip`. `LogVoiceClip` sits
  behind four early returns, two of which fire in None mode — precisely the installations whose
  reports matter most, because they never generate audio and so never learn which lines exist.
- **`EcholinesRules` is the pure, tested half**: source whitelist, language code, suppression key,
  chunk maths, and the privacy guard. Anything decidable without a network or a game belongs there.
  - **Whitelist, never a blacklist.** The call site is passed by *every* source, so a source added to
    `TextSource` later must stay silent until someone adds it on purpose. `Chat_IsNeverReportable` is
    the test that matters — chat is player-authored text.
  - Reported sources: `AddonTalk`, `AddonBattleTalk`, `AddonBubble`. `AddonSelectString` /
    `AddonCutsceneSelectString` are deliberately out (user, 2026-09-07): candidates, but still buggy,
    and in a *shared* database a wrong line that reaches three votes becomes everyone's.
  - The privacy guard rejects text still carrying the player's name **including the first name alone**
    — FFXIV addresses the player by first name far more often than by full name. Name parts of two
    characters or fewer are skipped, or a character called "Yu Li" would silence half the dialogue.
- **Suppression set = "this installation already sent it"**, not "we know this line". Whether the line
  is in the local DB or has audio is irrelevant. Persisted to `echolines-sent.txt` in the plugin
  config directory, appended only after a successful send.
- **Live path**: buffered, flushed every 60 s or at 50 lines, ≤200 lines per request, on a timer
  thread — never the frame thread. A failed send is **dropped, not queued**: a retry queue grows
  without bound during an outage, and one lost report costs nothing when the database is fed by many
  installations.
- **Bulk import (3b)**: `SubmitHarvestAsync` walks the language's stored lines in pages of 1000, one
  request roughly every 11 s (the server's import budget is a few per minute — a harvest is meant to
  take hours). Resume position per language in `Configuration.EcholinesImportProgress`.
  - `GetVoiceClipsForLanguage` orders **by id, not by timestamp**: the run is resumable, so the order
    has to be stable across calls; a timestamp order reshuffles as new lines arrive mid-run and would
    silently skip or repeat pages.
  - `EcholinesRules.ResumeChunk` restarts from zero when the saved position no longer fits the current
    harvest. Skipping costs data and nothing downstream could detect it; restarting only costs time.
  - **Only an unfiltered harvest submits.** A quest-type-filtered run is a partial picture, and
    shipping it as if it were complete is worse than shipping nothing.
- **`installId` is created on first use**, not in the config initialiser — an existing config must not
  silently gain one before the user has seen the switch.
- **One switch, two places, one value**: `Configuration.ShareLinesWithCommunityDb`, default on, in the
  first-time wizard and Settings → General. Existing users never see the wizard again, so **the
  changelog entry is their disclosure**, not documentation.

### Phase 7 — what was actually dead
Only `UploadVoiceLine` (the per-line JSON upload to Drive) had no caller and was removed from
`IGoogleDriveSyncService`, `GoogleDriveSyncService` and `DriveUploadService`. ⚠ **`DriveUploadService`
itself stays** — the plan said to delete it, but it also hosts `UploadFile`, which `AudioFileService`
uses for the WAV upload. `DriveLinkHelper` and the whole download side stay too.
