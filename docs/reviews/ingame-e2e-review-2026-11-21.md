# Plan Review — 2026-11-21

**Reviewed documents:**
- `docs/plans/ingame-e2e.md` (plan, 196 lines)
- `.claude/next-prompt-e2e.md` (user brief, 46 lines)
- `docs/plans/ingame-self-test.md` (§ Automation), `CLAUDE.md` (hard constraint), `Echokraut/Services/CLAUDE.md`, `Echokraut/Windows/CLAUDE.md`, `Echokraut/DataClasses/CLAUDE.md`

**Code checked against the "Facts" table:** `Plugin.cs`, `Configuration.cs`, `EchokrauTtsData.cs`,
`EchokrauTtsInstanceService.cs`, `VoiceMessageProcessor.cs`, `DialogContinuationService.cs`,
`ChatTalkHelper.cs`, `Queue/VoiceMessageQueue.cs`, `SelfTest/Checks/{Pipeline,Backend,Feature}Checks.cs`,
`NativeFirstTimeWindow.cs`, `NativeAlltalkBuilder.ValidateInstallPath`, `TtsInstallDetection.cs`,
`DatabaseService.Dispose`, `LineSubmissionService`, `Resources/Engines.json`.
Dalamud source (goatcorp/Dalamud master): `DalamudPluginInterface.cs`, `LocalDevPlugin.cs`.

**Reviewer:** plan-reviewer agent (opus)
**Scope note:** full pre-implementation audit of the E2E plan, focus on reset/reload order, data loss, brief coverage, threading, unattended operation.

## Summary

- **Verdict:** not-ready. The approach is sound, but five points can damage real data or produce false greens.
- **Blocking issues:** 5
- **High:** 7 · **Medium:** 10 · **Low:** 10
- **Contradictions (plan vs. brief / code):** 4 (H1 fact row 29, H6 Dispose save, L1 request path, B5 "non-destructive")
- **Top 3:**
  1. The dependency rule lets `remote` (needs only `0`) and `cleanup` run against the **real** config when backup, reload or reset fails (B1).
  2. `StopInstance` does nothing when this plugin instance does not own the wrapper process, and the E2E install uses the same port 8765. A real engine that is still running makes every E2E check pass against `C:\LocalTTS` (B2).
  3. `StopInstance` POSTs `/shutdown` to the **shared** `EchokrauTts.BaseUrl`. Once stage 6 points it at the private remote, any stop of a tracked local process shuts down the remote instance (B3).

### Answers to the caller's specific questions
- **Does anything read the config or open the DB before phase B can run in the ctor?** No. `Plugin.cs:91-103` runs only `NativeLibraryLoader.Initialize` and `Loc.Init` before `GetPluginConfig()`. The DB is opened inside `ServiceBuilder.BuildServices` (line 107) and later. Phase B fits between lines 101 and 103.
- **Does Dalamud hand out the config before the ctor?** No. In the Dalamud source, the `DalamudPluginInterface` ctor does not touch the config file. `GetPluginConfig` → `PluginConfigurations.LoadForType` reads it on demand. `ConfigDirectory` may create the folder, nothing more. A missing `Echokraut.json` → `null` → `new Configuration()` (`Plugin.cs:103`). Fresh defaults are confirmed: `FirstTime=true` (:136), `TtsInstallRoot=C:\LocalTTS` (:35), `LocalSaveLocation=C:\LocalTTS\LocalSaves` (:195).
- **Dispose vs. new ctor order:** Dalamud unloads (runs `Dispose`) before it loads the new instance. `Dispose` saves the config (`Plugin.cs:430`), then `_services.Dispose()` disposes `DatabaseService`, which calls `SqliteConnection.ClearAllPools()` (`DatabaseService.cs:2270`) and so releases the file lock. `EchokrauTtsInstanceService.Dispose` → `StopInstance` → `Kill(true)`. The plan's claim "wipe after the Dispose save" holds. See H6 for what is left over.
- **Q3 (startup probe of `C:\LocalTTS`):** can be closed. `TtsInstallDetection` has **no callers** in `Echokraut/`, and auto-start is gated on `!FirstTime && LocalInstall` (`Plugin.cs:200-205`, `330-336`). No pre-seeding is needed.

## Blocking issues

### BLK-1: Stages 6 and 7 can run against the real config
- **Where:** plan § Stages (row 6 `remote` needs `0`, row 7 `cleanup` needs `—`), § Dependency rule.
- **Finding:** Only "a failed **reset** ends the run". Three failures do not end it: a failed `backup` (hash mismatch, reload never fires, phase B throws), a backup that is skipped, or a reload timeout. In each case `reset` is skipped, not failed, so `remote` runs anyway. It stops the local engine, switches the **real** config to Remote with `remoteUrl` and turns on `GameVoiceReferenceEnabled`. `cleanup` then "restores chat/FC options" and writes a restore script for a reset that never happened.
- **Impact:** The user's real config is changed without a backup.
- **Suggested fix:** "Any outcome other than `pass` for `backup` or `reset` (including timeout or skip) ends the run at once. Every stage from `first-time` on lists `reset` in `Needs`; `remote` needs `reset` (it stops the local engine only if `first-time` passed). `cleanup` touches config and DB only when `state.phase >= reset-done`; otherwise it writes the report only."

### BLK-2: A real engine still running on port 8765 gives false greens
- **Where:** stage 1 phase A ("`StopInstance`, wait for `!InstanceRunning`"), stage 3.
- **Finding:** `StopInstance` returns at once when `_instanceThread == null && _instanceProcess == null` (`EchokrauTtsInstanceService.cs:743`). It only stops a process **this** plugin instance started. A wrapper started by hand, or left orphaned by an earlier load, stays up while `InstanceRunning` is already `false`, so the wait passes immediately. The E2E install uses `Port()` = the port from `BaseUrl`, which is `http://127.0.0.1:8765` by default (`EchokrauTtsData.cs:19`) and the same port as the real engine.
- **Impact:** The E2E wrapper fails to bind, or never gets that far. `BackendCheck`, `GenerationCheck` and `PipelineCheck` in stages 3 and 4 then pass against the real `C:\LocalTTS` install. The run reports green and has tested nothing.
- **Suggested fix:** Add to `preconditions` (ssh) and to phase B (plugin): "Port 8765 must be free: `Get-NetTCPConnection -LocalPort 8765` is empty, and `GET http://127.0.0.1:8765/health` fails. Otherwise stop." After stage 3 is ready: "the process listening on 8765 has its image or working directory under `installRoot`" (ssh `Get-Process -Id (Get-NetTCPConnection -LocalPort 8765).OwningProcess`), or `/health` reports a path under `installRoot`.

### BLK-3: `/shutdown` can reach the private remote instance
- **Where:** stage 6 ("stop the local engine, switch to Remote with `remoteUrl`"), stage 7, every reload during or after stage 6.
- **Finding:** Local and Remote share **one** field, `EchokrauTts.BaseUrl`. `StopInstance` POSTs to `BaseUrl + /shutdown` (`EchokrauTtsInstanceService.cs:752`), and the remote has no API key. If the URL is switched before the local process is stopped, or a later stop finds a tracked process (cleanup, `Dispose` on a dev reload or scp deploy, `RestartLocalIfRunning`), the plugin shuts down the user's remote instance.
- **Impact:** The private service goes down, and later runs fail in confusing ways.
- **Suggested fix:**
  1. Plan text: "Stage 6 order: `StopInstance`, then wait until `_instanceProcess == null` and port 8765 is closed, then change `InstanceType` and `BaseUrl`. Assert that no local process is tracked before writing the URL."
  2. Code fix in the same build step: `StopInstance` posts to `http://127.0.0.1:{Port()}` (the local instance), never to `BaseUrl`, plus a unit test. Without this the hazard remains for real users who switch from Local to Remote.

### BLK-4: Phase B is not crash-safe or idempotent; `restore.ps1` comes too late
- **Where:** § Run state, stage 1/2, § Report and restore, § Risks ("restore.ps1 works without the plugin").
- **Finding:**
  - The phase B sub-states are not defined. If the game crashes during the wipe, the next load still sees `phase = reset-pending`. It then backs up the **partly wiped** folder as a new backup and wipes again.
  - `restore.ps1` and the report are written only in stage 7. After a crash in stages 3-6 there is no restore script, although the risk section relies on one.
  - A second run without a restore in between backs up the **E2E** config as "the real one". The root guard then fails, because `installRoot == TtsInstallRoot` of that config, which stops the run but for the wrong reason.
- **Impact:** The recovery path is broken exactly in the crash case it is meant for, and later backups can silently be of the wrong data.
- **Suggested fix:**
  - "Phase B states are written atomically (temp file + `File.Move` overwrite): `reset-pending` → `backup-done {backupDir}` → `wipe-started` → `reset-done`. A backup is taken only from `reset-pending` with no `backupDir` set. `wipe-started` resumes the wipe and never re-backs up."
  - "`manifest.json` and `restore.ps1` are written right after the hash check, before the wipe."
  - "`preconditions` fails when the current config is itself an E2E config (`TtsInstallRoot` equals the request's root, or an earlier `state.json` with `reset-done` and no restore). Backups are never deleted by the plugin."

### BLK-5: Build step 2 is not non-destructive on the real config
- **Where:** § Build steps 2 ("on the current real config … Run: non-destructive").
- **Finding:**
  - `chat-fc` goes through `ChatTalkHelper` → `ProcessSpeechAsync` (`selfTest: false`, `ChatTalkHelper.cs:181`). That creates a DB character for the user's **own** player plus `voice_clips` and generation rows, saves a WAV into the real `LocalSaveLocation` (`AudioPlaybackService.cs:164` skips this only for `IsSelfTest`), uploads to Google Drive if the real config uploads, and starts a Lodestone lookup.
  - `continuation` and `mute` change the real `JoinSplitSentences` and mute state.
  - `cleanup` only removes `Echokraut Selftest` rows.
- **Impact:** Leftovers in the real DB and on disk. Possibly a test clip uploaded to a shared Drive folder.
- **Suggested fix:** Move step 2 after step 3 (backup/reset/restore proven), so the scenarios run first on a reset config. Alternatively, limit step 2 to `queue-order`, `epoch-cancel` and `mute` with self-test messages, plus "record every id created, delete by id, restore every touched option".

## High

### H1: The continuation scenario cannot work as written (fact row 29 is misleading)
- `VoiceMessageProcessor.cs:140` calls `_continuation.Apply`, but `Apply` returns the text unchanged unless the source is `TextSource.AddonTalk` (`DialogContinuationService.cs:66`). The self-test pipeline uses `TextSource.VoiceTest` (`BackendChecks.cs:31` `NewEventId`).
- `JoinSplitSentences` is `false` by default (`Configuration.cs:153`). The index is built lazily: the first `Apply` only starts `Prepare` and returns unchanged (:96-99).
- `_pendingHead` is global, so a real Talk box in between consumes it.
- **Fix (add to § Stages / scenario):** "`continuation`: set `JoinSplitSentences = true`, await `Prepare(lang)` (or run `ContinuationCheck`), send box 1 and box 2 as self-test speech with an **`AddonTalk`** event id. This needs an overload `ProcessSelfTestSpeechAsync(eventId, …)` that keeps the source. Assert on `SelfTestSpeech.Queued.Text`. Pick a pair without a player placeholder. Restore the option afterwards." Also correct fact row 29 to "runs only for `AddonTalk`".

### H2: Stage 3 pass criteria do not prove the wizard ran
- `MarkInstalled` sets `FirstTime = false` and saves as soon as the wrapper is ready (`EchokrauTtsInstanceService.cs:580`), before Next or Finish. `FirstTime == false` is therefore met by the install alone.
- The Local wizard step has **no** `LocalSaveLocation` field (that input exists only for None mode, `NativeFirstTimeWindow.cs:284`). `SetLocalSaveLocation` is therefore an E2E-only step and must happen before `Install`.
- **Fix:** Pass when all of these hold: `!IsFirstTimeOpen`, `LastSeenChangelogVersion == Plugin.PluginVersion` (set by `_onComplete`), `BackendSelection == EchokrauTTS`, `EchokrauTts.InstanceType == Local`, `TtsInstallRoot == installRoot`, `LocalSaveLocation` under `installRoot`, `LocalInstall`, `InstanceRunning`, and `BackendCheck` passes. Document that `SetLocalSaveLocation` deviates from a real newcomer.

### H3: Waiting on events or timeouts hides immediate failures
- `Launch` returns silently, with only a warning, on an invalid path or a missing wrapper URL (`:333-354`).
- `RunInstance` failure sets `Installing = false` and `CurrentInstallStatus = "Failed…"` (`:711-715`, `:732-733`).
- `SwitchTtsBackend` is a no-op for the current engine and restarts **only if running** (`:232`, `:301`). `OnInstanceReady` therefore does not fire again for an engine that was already active.
- **Fix:** "Stages wait on a predicate polled every 2 s: ready = `InstanceRunning && /health ok`; failed = `!Installing && !InstanceStarting && !InstanceRunning` after the start, or a status starting with `Failed`. Fail fast; the timeout is only the last resort. After `SwitchTtsBackend`, call `StartInstance()` if not running."

### H4: Threading — blocking calls on the framework thread
- `StopInstance` blocks: an HTTP POST with a 3 s timeout, then `Kill`. It is reached from the controller's `StartInstall` ("stop if running"), from `SwitchTtsBackend` → `RestartLocalIfRunning`, and from stage 6.
- `RefreshBackend` blocks for up to 5 s (`Windows/CLAUDE.md` › Backend calls).
- The plan says stages call controller actions "on the framework thread".
- **Fix:** "Stage bodies run on the thread pool. Only node/ATK work, object-table reads and controller step changes (`Next`/`Back`/`Finish`/`Choose*`) hop through `IFramework.RunOnFrameworkThread`. `StartInstall` runs both the stop **and** the `Install()` inside `Task.Run`. Never `.Wait()` or `.Result` inside a `RunOnFrameworkThread` lambda." Add a test that `StartInstall` returns without calling `StopInstance` synchronously.

### H5: Voice lists after a backend switch, and stage 6's switching API, are unspecified
- After the reset the DB has no voices. `GenerationCheck` picks from `npcData.GetEchokrautVoices()`. After the install, each engine switch and the remote switch, `RefreshBackend` must run (off-thread) and the stage must wait for `VoicesMapped`.
- Stage 6 does not say **how** it switches: `InstanceType = Remote` + `BaseUrl` + `RefreshBackend`, via the wizard's `TestConnection` path, or via the Backend tab path.
- **Fix:** Name the service call (preferably a small `IBackendService`/controller method shared with the Backend tab). Add "wait until ≥1 enabled voice" to stages 3, 4 and 6.

### H6: Brief says "turn off the Dispose save"; the plan says "not needed" (contradiction)
- Brief point 2: "Reset-Stufe muss das Speichern beim Dispose abschalten". Plan § Reload: "no suppression switch needed".
- The plan's reasoning holds for the main path (see the answers above). Two gaps remain:
  - Stray work from the old instance can outlive `Dispose`: `Task.Run(… CancellationToken.None)` for enrichment (`Plugin.cs:301`) and backend refresh (`:293`). DB writes are guarded by `_disposed`, but a late `Configuration.Save()` through the old interface is not.
  - The user explicitly asked for the switch.
- **Fix:** Keep phase B as planned, and also add a one-line `SuppressSaveOnDispose` flag that phase A sets. After the wipe, verify that `Echokraut.json` is absent right before `GetPluginConfig`. Confirm the deviation with the user in the open questions.

### H7: The game-voice opt-in is implicit, and the remote reference path is never exercised
- Q4 turns "the request file exists" into consent. The hard constraint requires the user's **explicit** opt-in.
- `GameVoiceCheck` only checks server capability and decodes one clip in memory (`FeatureChecks.cs:68-82`). It never sends a reference to the remote, which is what brief point 6 asks for ("Spielstimmen-Referenz").
- **Fix:**
  - The request gets an explicit `"gameVoiceOptIn": true`, default absent = off. The plugin sets `GameVoiceConsentVersion = GameVoiceConsent.CurrentVersion` only then, and cleanup reverts it.
  - Add a sub-check that generates one line for a voiced NPC name through `IGameVoiceReferenceService` and asserts `ReferenceSource == GameVoice`. The row is deleted by id in cleanup and is excluded from `.ekpack` and Drive (already enforced by code).

## Medium

- **M1 — Existing automatic checks are missing from the stage table.** Brief point 5 says "Bestehende automatische Checks", but the `checks` stage exists only in build step 1. Add stage `checks` (all non-interactive checks of `SelfTestCatalog`) after `first-time`, and once more after `remote`.
- **M2 — Interference from the live game.** `VoiceBattleDialogue` is on by default (`Configuration.cs:140`). During `chat-fc`, real FC members' chat is voiced too. `queue-order` must match its **own** message references (as `PipelineCheck` does), not the global queue order. Add plugin-side preconditions: no Talk addon open, not in a cutscene or duty, ideally in an inn room. `AutoAdvanceRequested` → `AddonTalkHelper.Click` fires when `_inDialog` (`AudioPlaybackService.cs:212`), and a click into a game dialog is input automation, which the brief rules out ("kein Charakter-Steuern"). Also state the no-steering rule in the plan.
- **M3 — The fresh config changes the community-DB consent.** `ShareLinesWithCommunityDb` defaults to `true` (`Configuration.cs:65`). The reset also creates a new `EcholinesInstallId`, and Q7 would wipe `echolines-sent.txt`. If the user had opted out, every real line heard during the run, **and after it until the restore**, gets reported. Fix: stage 3 sets the wizard's checkbox to the value from the backed-up JSON (or `false`).
- **M4 — Stage 5 depends on the wrong stage.** It needs `3` but runs "on the last local engine". If that engine failed, every scenario fails misleadingly. Use "on the last `engine:*` stage that passed; none → skip". `queue-order` is listed in both stage 4 and stage 5; keep it in one place.
- **M5 — Cleanup misses non-self-test leftovers.** `chat-fc` leaves the own player's character, clip rows, a WAV and a Lodestone record (see BLK-5). Cleanup only matches `Echokraut Selftest`, so the brief's "keine DB-Reste" fails. Record created ids per scenario in `state.json` and delete them by id.
- **M6 — Reload trigger.** Dalamud's `LocalDevPlugin` watches `NotifyFilters.LastWrite` → `Changed` on the DLL, with a 500 ms debounce, and reloads only in state `Loaded`/`LoadError`/`UnloadError`. `File.SetLastWriteTimeUtc` should therefore fire it; keep the spike. Specify: after the touch, phase A does nothing more, and a 60 s reload timeout fails `backup` (real config untouched). **An scp deploy during a run also reloads**: every phase must resume safely, for example an install killed by `Dispose` is restarted, not reported as passed or failed.
- **M7 — The root guard is incomplete.** Also reject `installRoot` that equals or overlaps the real `LocalSaveLocation` and `Alltalk.LocalInstallPath`. Resolve junctions and symlinks (`Directory.ResolveLinkTarget`), compare case-insensitively, reject `\\?\` and UNC forms, and reuse `NativeAlltalkBuilder.ValidateInstallPath` (no spaces or dashes). Write the marker **before** `Install`.
- **M8 — Phase B must check its own gate.** In the ctor, before touching files, check `pluginInterface.IsDev` and that `state.id` matches. An installed plugin ignores `state.json` exactly as it ignores `request.json`. `ILogService` does not exist yet at that point, so log through the injected `IPluginLog`.
- **M9 — The remote host can leak through error texts.** Exception messages such as `HttpRequestException` contain the host name, so stage details can carry it into `e2e-<id>.json` and chat. The test "report without `remoteUrl`" should search the whole serialized report for the host string, and details should be scrubbed.
- **M10 — The engine list must come from runtime.** Fact row 27 cites the embedded fallback `Resources/Engines.json`; the wrapper publishes its own catalog. Stage 4 must read `AvailableEngines` at run time and compare `minVersion` with `InstalledWrapperVersion`. An id in the request that is missing from the catalog → skip with that reason.

## Low

- **L1 — Request path deviates from the brief.** Brief: `pluginConfigs/Echokraut/selftest/request.json`. Plan: `pluginConfigs\Echokraut-e2e\request.json`. The deviation is justified (the folder survives the wipe), but confirm it with the user. Same for `remoteUrl` from `~/.config` instead of the infrastructure repo: better, but say so.
- **L2 — `restore.ps1` details.** It deletes `selftest\`, which was never backed up, so keep it out of the delete. It must remove `-wal`/`-shm` before copying back. Refuse also while `XIVLauncher`/`Dalamud.Injector` run, not only `ffxiv_dx11`.
- **L3 — Free space on `C:`.** Check it for the backup (DB size × 2) in preconditions.
- **L4 — Stale-state rule.** Define the 6 h limit from the **last** state write, not from the run start: stage 3 at 60 min + 3 × 30 min engines + remote comes close on a slow day.
- **L5 — Install window.** `UseShellExecute = true, CreateNoWindow = false` (`:374-375`) opens a console window over the game. That is fine unattended, but say it in the plan so the screen watcher is not surprised.
- **L6 — Wizard opening race.** `HandleStartup` opens the wizard only if `IsLoggedIn` at that moment. Stage 3 should wait for `IsFirstTimeOpen` (timeout 30 s) and toggle it itself if needed.
- **L7 — Pre-DI class with file IO.** `E2eStartupReset` is a static class with file IO in `Services/`. That departs from the interface + impl rule; justify it in the plan (it runs before DI) and test it against a temp folder (already planned).
- **L8 — Chat line translations.** The localized start and summary lines need DE/FR/JP entries in `Loc` (CLAUDE.md › Localization).
- **L9 — Character choice.** The self-test plan (§ Automation) mentions a Free Trial or separate account; this plan is silent on which character. Say it explicitly, since `chat-fc` uses the own name.
- **L10 — Changelog (Q8).** The commit gate needs either a staged changelog or the `(no changelog: …)` note. A wizard refactor without visible change qualifies for the note; the `StopInstance` URL fix (BLK-3) is user-visible as a bug fix and needs a `BUG FIXES` bullet in v0.19.3.3 EN/DE.

## Brief coverage matrix (`.claude/next-prompt-e2e.md`)

| Brief point | Plan | Status |
|---|---|---|
| 0 Preconditions (game, logged in, remote host, E: free) | stage 0 | ok (+BLK-2 port check) |
| 1 Backup + checksum, restore by hand with game closed, instructions in report | stages 1, 7, `restore.ps1` | gap: written too late (BLK-4) |
| 2 Stop real engine (port 8765), full reset, reload, Dispose save off | stages 1/2, § Reload | BLK-2, H6 (deviation) |
| 3 Wizard via controller, `E:\EkE2E` hard stop, install progress in log | stage 3, § Controller, root guard | H2, M7 |
| 4 Sub-engine switching, generation + pipeline each, not AllTalk | stage 4 | H3, M10 |
| 5 Existing checks + continuation, queue order, epoch, mute, no DB leftovers, FC chat via Print | stage 5 | M1 (checks missing), H1, M5 |
| 6 Remote: reachability, generation, pipeline, game-voice reference | stage 6 | H5, H7, BLK-3 |
| 7 Report + restore instructions, `E:\EkE2E` stays | stage 7 | ok |
| Trigger file, log format, Mac watcher, skip dependents | § Trigger, § Log, § Watching, § Dependency | L1; BLK-1 |
| Framework thread for the object table | not explicit | H4 |
| No character steering | not stated | M2 |
| PowerShell as uploaded `.ps1` | § Pieces | ok |
| Deploy path | fact row 1 | ok |
| Commit gate / changelog | Q8 | L10 |
| VRAM: stop real engine first | § Risks | ok |

## Untested assumptions

| Claim | Where | Evidence to get |
|---|---|---|
| `IChatGui.Print` passes the `ChatMessage` event | § Chat, Q5 | spike in build step 1 (planned) |
| DLL self-touch triggers the reload | § Reload, Q2 | spike in step 1 (planned); Dalamud source makes it likely (LastWrite watcher) |
| "The self-test pipeline path runs the sentence continuation too" | fact row 29 | **false for `VoiceTest`**, see H1 |
| "`restore.ps1` works without the plugin" | § Risks | only true once BLK-4 moves its creation before the wipe; test it in build step 3 |
| Install ≤ 60 min, engine switch ≤ 30 min | § Risks | measure in steps 4/5, log the durations into the report |
| Model caches stay on `E:` | § Risks | check the wrapper's `HF_HOME`/`TORCH_HOME` before step 5 |
| "Running the plugin on the E2E config afterwards … never into the real install" | § Risks | true for audio (`LocalSaveLocation` on E:). Not true for community-DB reports (M3) and for `/shutdown` to the remote (BLK-3) |
| `queue-order` sees only its own messages | stage 5 | real BattleTalk/chat can interleave (M2) |

## Fact table check

| Row | Result |
|---|---|
| `Plugin.Dispose` saves (`Plugin.cs:430`) | confirmed |
| Fresh defaults (`Configuration.cs:35,136,195`) | confirmed |
| Install API on `IEchokrauTtsInstanceService` | confirmed. Note: `SwitchTtsBackend` restarts **only when running** and is a no-op for the current engine (H3) |
| Engine catalog | matches the embedded fallback only (M10) |
| Queue epoch (`VoiceMessageQueue.cs:209-244`) | confirmed (`CancelBySource` bumps first) |
| Continuation in the self-test path (`VoiceMessageProcessor.cs:140`) | misleading: `Apply` is called but is a no-op for `VoiceTest` (H1) |
| `ChatTalkHelper` (59-130) | confirmed. FC needs `VoiceChat` + `VoiceChatFreeCompany`; own name needs `VoiceChatPlayer` (`:179`); lines are ignored during cutscenes (`:65`) |

## Scope of this review
- Documents read: 6 docs + the code files listed above.
- Skipped: `NativeEchokrauTtsBuilder.cs` (only its call site in the wizard was checked); `tools/e2e/*` (does not exist yet).
- Web: Dalamud `DalamudPluginInterface.cs` and `LocalDevPlugin.cs` on GitHub (config-load timing, auto-reload watcher).
- Confidence: high for ordering, DB and config (checked in code); medium for the Dalamud reload details (source read, not run); medium for the remote `/shutdown` exposure (depends on whether the wrapper accepts it without a key, not checked in `echokrautts`).
