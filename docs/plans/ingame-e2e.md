# In-game E2E run

Status: **steps 1-6 done**, step 7 (docs) done 2026-11-22, stage 8 `cleanup` built and run in the game 2026-11-22;
plan 2026-11-21, revised after review
`docs/reviews/ingame-e2e-review-2026-11-21.md` (finding ids in brackets). Results per step in "Build step N — result"
below; last game run `20261122-014414` (18/2/1, with `cleanup`). Open: `remote:game-voice` fails
`ServerUnsupported` until the private remote runs a wrapper with `inline_reference`. The engine loop ran on an
unreleased wrapper >= 0.0.2.0 via `testWrapper` (6/0/0, see "Unreleased wrapper"); last full run `20261123-141448`
13/0/0 without crash. The last unattached `TextButtonNode` at unload (`_vsAdvancedToggle`) was removed in 8bca7fc. Builds on the in-game self-test (`docs/plans/ingame-self-test.md`,
code `Echokraut/Services/SelfTest/`, last change e1bcb0e).

## Goal
One run that the user starts over ssh and then leaves alone: it resets the plugin to a brand-new user,
walks the first-time wizard, installs EchokrauTTS on `E:\EkE2E`, cycles through several EchokrauTTS
sub-engines, sends the existing checks and new real-pipeline scenarios through them, switches to the
user's private remote instance and ends with a JSON report plus restore instructions. Progress is
visible only through log lines that a watcher on the Mac follows. **No step may need the user** (no
interactive `talk`), and **no step steers the character or clicks game UI** (no `InteractWithObject`,
no walking, no auto-advance click — botting).

Not part of this plan: starting the game / auto-login, lip sync (`.claude/next-prompt-lipsync.md`),
judging the sentence continuation (`.claude/next-session-prompt.md`), AllTalk, speech bubbles.

## Facts this plan relies on (checked 2026-11-21)
| Fact | Source |
|---|---|
| Dalamud loads the dev build from `<repo>\Echokraut\bin\x64\Debug\Echokraut.dll` with `AutomaticReloading: true`; the watcher reacts to `LastWrite` on the DLL (500 ms debounce), reloads only in state Loaded/LoadError/UnloadError | `dalamudConfig.json`, Dalamud `LocalDevPlugin` |
| Reload order: old `Dispose` (saves config `Plugin.cs:430`, DB `ClearAllPools`, wrapper `Kill`) runs before the new ctor | Dalamud, `DatabaseService.cs:2270` |
| Nothing before `GetPluginConfig()` (`Plugin.cs:103`) reads the config or opens the DB (only `NativeLibraryLoader.Initialize`, `Loc.Init`); Dalamud reads `Echokraut.json` only on that call; missing file → `new Configuration()` | `Plugin.cs:91-107` |
| `E:` has ~93 GiB free, `E:\EkE2E` does not exist yet | `Get-PSDrive E` |
| `pluginConfigs\Echokraut\` holds `echokraut.db` (+ `-wal`/`-shm`), `addonconfig.json`, `echolines-sent.txt`, `game_voice_aliases.json`, `selftest\` | listing |
| Dalamud log: `%APPDATA%\XIVLauncher\dalamud.log` (~12 MB, rotates to `dalamud.old.log`) | listing |
| Fresh `Configuration`: `FirstTime = true`, `TtsInstallRoot = C:\LocalTTS`, **`LocalSaveLocation = C:\LocalTTS\LocalSaves`**, **`ShareLinesWithCommunityDb = true`**, `JoinSplitSentences = false`, `VoiceBattleDialogue` on | `Configuration.cs:35,65,136,140,153,195` |
| No startup probe finds `C:\LocalTTS` on a fresh config: `TtsInstallDetection` has no callers, auto-start needs `!FirstTime && LocalInstall` | `Plugin.cs:200-205,330-336` |
| The install itself sets `FirstTime = false` (`MarkInstalled`) — not a proof the wizard ran | `EchokrauTtsInstanceService.cs:580` |
| `StopInstance` returns at once unless **this** plugin instance started the process, and POSTs `/shutdown` to `EchokrauTts.BaseUrl` — the field Local and Remote share | `EchokrauTtsInstanceService.cs:743,752` |
| Default local port 8765 (`BaseUrl http://127.0.0.1:8765`) — the real engine's port too | `EchokrauTtsData.cs:19` |
| Wizard logic sits in button lambdas of `NativeFirstTimeWindow` (step 0 engine + mode, step 1 settings, step 2 finish) and `NativeEchokrauTtsBuilder.BuildLocalInstance` (path input, engine dropdown, Install/Start/Stop). The Local step has **no** `LocalSaveLocation` field | the two files |
| Install API: `Install()`, `Installing`, `CurrentInstallStatus`/`Progress`, `OnInstanceReady` (not raised on failure or on a repeat switch), `SwitchTtsBackend(id)`, `AvailableEngines` (runtime catalog from the wrapper's `engines.json`, embedded fallback) | `IEchokrauTtsInstanceService` |
| `StopInstance` and `RefreshBackend` block for seconds (HTTP + kill) | `EchokrauTtsInstanceService`, `BackendService` |
| Queue cancel = per-source epoch (`GetEpoch`, bumped on cancel) | `VoiceMessageQueue.cs:209-244` |
| Sentence continuation runs **only for `TextSource.AddonTalk`**; the self-test path uses `VoiceTest` | `DialogContinuationService.cs:66` |
| Chat: `ChatTalkHelper` on `IChatGui.ChatMessage`, FC needs `VoiceChatFreeCompany`, own sender matched to `LocalPlayer`; it goes through `ProcessSpeechAsync` (`selfTest: false`) → DB character, clip rows, WAV, Drive upload, Lodestone lookup | `ChatTalkHelper.cs:59-181`, `AudioPlaybackService.cs:164` |
| Wrapper install opens a console window (`UseShellExecute = true`) | `EchokrauTtsInstanceService.cs:374` |
| Private EchokrauTTS: reachable only over the tailnet, no API key; its URL lives in `~/.config/echokraut-e2e.json` on the Mac (in no repo) | infra repo |

## Design

### Pieces
| Piece | Where | Job |
|---|---|---|
| `IE2eRunService` / `E2eRunService` | `Services/SelfTest/E2e/` | polls the request file, owns the run state machine, writes state + report, logs `EKTEST` lines |
| `E2eStage` (abstract) + one class per stage | `Services/SelfTest/E2e/Stages/` | `Id`, `Needs`, `Timeout`, `RunAsync(ctx, ct)` → `SelfTestResult` (existing Pass/Fail/Skip type) |
| `E2eStartupReset` | `Services/SelfTest/E2e/` | phase B, runs in the `Plugin` ctor between `Loc.Init` and `GetPluginConfig`. Static on purpose: it runs before DI exists [L7]; file ops behind a small `IFileSystem`-style seam so it is tested against a temp folder; logs through `IPluginLog` (no `ILogService` yet) [M8] |
| `IFirstTimeWizardController` / `FirstTimeWizardController` | `Services/` | the wizard's flow, pulled out of the window (below) |
| Mac scripts | `tools/e2e/` (`start.sh`, `watch.sh`, `fetch-report.sh`) | preconditions, upload the request, follow the log, fetch the report |
| PowerShell side | `tools/e2e/*.ps1` (scp'd, never inlined) | preconditions, port check, log tail |

Existing self-test checks (`BackendCheck`, `VoicesCheck`, `GenerationCheck`, `PipelineCheck`,
`ContinuationCheck`, `GameVoiceCheck`, …) are reused inside stages, not copied.

### Trigger and gate
- Request file `pluginConfigs\Echokraut-e2e\request.json` (deviates from the brief's
  `pluginConfigs/Echokraut/selftest/` because the reset wipes that folder [L1]), checked every 2 s from
  the framework tick (one `File.Exists`), read and **deleted** at once (a crash cannot re-trigger it).
- Content:
  `{ "id": "20261122-1", "installRoot": "E:\\EkE2E", "engines": ["qwen-1.7b","qwen-0.6b","xtts","f5","pocket","moss"], "remoteUrl": "https://…", "stages": null }`
  (`stages: null` = all; `engines` default = all six). `remoteUrl` comes from a local file on the Mac (`~/.config/echokraut-e2e.json`,
  in no repo) — the private host name never lands in the public plugin repo [L1].
- **Only on a dev load** (`IDalamudPluginInterface.IsDev`). An installed plugin ignores `request.json`
  **and `state.json`** and logs one warning [M8]. Phase B checks `IsDev` and that `state.id` matches
  before touching a file.
- One run at a time; `/ektest` refuses while an E2E run is going and vice versa.

### Run state across reloads
`Echokraut-e2e\state.json` = `{ id, request, phase, stageResults[], backupDir, createdIds[], touchedOptions{} }`,
written **atomically** (temp file + `File.Move` overwrite) after every stage and sub-step [BLK-4].
Phases: `requested` → `reset-pending` → `backup-done` → `wipe-started` → `reset-done` → `stages…` → `finished|aborted`.
- On every load the service reads it: an unfinished run resumes at `phase`. A stage that was running
  when the plugin unloaded (crash, scp deploy, reload) is **restarted**, never counted [M6].
- A run whose state was last written more than 6 h ago is closed as `aborted` [L4].
- `createdIds` (character ids, clip row ids, WAV paths) and `touchedOptions` (old value per config
  option a stage changed) let `cleanup` undo exactly what the run did [M5].

### Log format (what the watcher greps)
`EKTEST <id> <stage> start|pass|fail|skip <detail>` (fail → `Warning`), `EKTEST <id> <stage> progress <text>`
for long stages (at most one line per 10 s or per status change). Final line
`EKTEST <id> done <passed>/<failed>/<skipped> <reportPath>`. English (log text). Details pass a scrubber
that replaces the remote host with `<remote>` — exception messages carry host names [M9]. Chat gets a
localized start line, a summary line and the restore warning (DE/FR/JP in `Loc`) [L8].

### Abort and dependency rules [BLK-1]
- Any outcome other than `pass` of `preconditions`, `backup` or `reset` (including timeout and skip)
  **ends the run at once**. Up to then the real config is untouched or restorable.
- Every stage from `first-time` on needs `reset`. Other dependencies as in the table; a failed or skipped
  dependency → `skip` with `"needs <stage> (<outcome>)"`. Independent stages still run.
- `cleanup` touches config and DB only when `phase >= reset-done`; otherwise it writes the report only.
- Waiting is always a **polled predicate** (every 2 s) with a ready and a failed condition, fail fast;
  the timeout is the last resort. Ready = `InstanceRunning` and `/health` answers; failed =
  `!Installing && !InstanceStarting && !InstanceRunning` after the start, or a status starting with
  `Failed`. `OnInstanceReady` alone is never awaited [H3].

### Threading [H4]
Stage bodies run on the thread pool. Only node/ATK work, object-table reads and the controller's step
changes hop through `IFramework.RunOnFrameworkThread`. `StopInstance`, `Install`, `SwitchTtsBackend`,
`RefreshBackend` always run off the framework thread. Never `.Wait()`/`.Result` inside a
`RunOnFrameworkThread` lambda.

## Stages
| # | Id | Needs | What | Pass when |
|---|---|---|---|---|
| 0 | `preconditions` | — | **ssh** (`start.sh`): game running; `E:` ≥ 60 GB free; `C:` free ≥ 2 × DB size [L3]; `remoteUrl` answers from the gaming PC (Tailscale); no `request.json` pending. **Plugin**: logged in; `IsDev`; no Talk/BattleTalk addon open, not in a cutscene or duty (ideally in an inn room) [M2]; root guard (below); current config is **not** itself an E2E config (`TtsInstallRoot` = request root, or an earlier `state.json` at `reset-done` without restore) [BLK-4] | all true |
| 1 | `backup` | 0 | **Phase A** (running plugin, real config): stop the real local engine (off-thread), then require port 8765 to be free (`Get-NetTCPConnection` empty, `/health` fails) — a wrapper this plugin did not start stays up otherwise [BLK-2]; set `SuppressSaveOnDispose` [H6]; `phase = reset-pending`; touch own DLL → reload (60 s timeout → fail, real config untouched) [M6]. **Phase B** (next ctor, from `reset-pending` without `backupDir` only): copy `pluginConfigs\Echokraut.json` + `pluginConfigs\Echokraut\` (without `selftest\`) to `Echokraut-e2e\backup-<UTC>\`, write `manifest.json` (SHA-256 per file), re-read and compare, **write `restore.ps1` now** [BLK-4], `phase = backup-done` | every hash matches |
| 2 | `reset` | 1 | still phase B: `phase = wipe-started`; delete `Echokraut.json` and everything in `pluginConfigs\Echokraut\` except `selftest\` (a `wipe-started` load resumes the wipe and never backs up again); check `Echokraut.json` is absent right before `GetPluginConfig`; `phase = reset-done`. Then the normal ctor runs → fresh `Configuration`, new DB | `FirstTime`, DB has 0 characters / 0 clips |
| 3 | `first-time` | 2 | wait for the wizard (`IsFirstTimeOpen`, 30 s, toggle it if `HandleStartup` missed the login [L6]); drive `FirstTimeWizardController`: EchokrauTTS → Local → install root = `installRoot` (an existing folder with marker is emptied first — every run is a fresh install, D4; marker `EkE2E.marker` written **before** `Install` [M7]) → `LocalSaveLocation = <installRoot>\LocalSaves` (E2E-only, the wizard has no such field — deviation from a real newcomer) → community-DB checkbox = value from the backed-up JSON [M3] → Install → wait ready (progress lines; a console window opens over the game [L5]) → the process on port 8765 lives under `installRoot` [BLK-2] → `RefreshBackend`, wait ≥ 1 enabled voice [H5] → Next → Finish. ~1 s pause between steps so the window can be followed | `!IsFirstTimeOpen`, `LastSeenChangelogVersion == PluginVersion`, `BackendSelection == EchokrauTTS`, `InstanceType == Local`, `TtsInstallRoot == installRoot`, `LocalSaveLocation` under it, `LocalInstall`, `InstanceRunning`, `BackendCheck` Pass [H2] |
| 4 | `checks:local` | 3 | all non-interactive checks of `SelfTestCatalog` on the fresh install [M1] | no Fail |
| 5 | `engine:<id>` (one per engine) | 3 | engines from `AvailableEngines` at run time, `minVersion` vs `InstalledWrapperVersion`; a requested id missing from the catalog or too new → skip with that reason [M10]. `SwitchTtsBackend(id)`, `StartInstance()` if not running, wait ready (model download), `RefreshBackend` + ≥ 1 voice, then `GenerationCheck` + `PipelineCheck` | generation + pipeline Pass |
| 6 | `scenarios` | `checks:local` + last passed `engine:*` with that engine active again (none → skip) [M4]; without the `engine` stage in the run only `checks:local` (+ refuses unless `ConfigIsE2e`) | `continuation`: `JoinSplitSentences = true`, await `Prepare(lang)`, pick two consecutive sheet boxes without a player placeholder from the index, send both as self-test speech **with an `AddonTalk` event id** (new overload of `ProcessSelfTestSpeechAsync` that keeps the source) → box 1 has the head of box 2 appended, box 2 lost it (assert on `SelfTestSpeech.Queued.Text`) [H1]. `queue-order`: 3 self-test messages play in send order, matched by their own message references, not global order [M2]. `epoch-cancel`: cancel the source while one is queued → dropped, nothing plays. `mute`: mute a self-test text → not queued; unmute. `chat-fc` (below). Options restored from `touchedOptions`, created ids recorded | each sub-scenario reported on its own |
| 7 | `remote` | 2 | **fixed order** [BLK-3]: `StopInstance` (off-thread), wait until no local process is tracked and port 8765 is closed, only then `InstanceType = Remote` + `BaseUrl = remoteUrl` through one shared switch method (also used by the Backend tab) + `RefreshBackend` + ≥ 1 voice [H5]; `BackendCheck`, `GenerationCheck`, `PipelineCheck`, `checks:remote` (all automatic checks again) [M1]. Game voice: set `GameVoiceReferenceEnabled` + current consent version (starting the run is the user's opt-in, decision D2), `GameVoiceCheck`, then **one generation for a voiced NPC through `IGameVoiceReferenceService`**, assert `ReferenceSource == GameVoice`, row deleted by id [H7] | each Pass |
| 8 | `cleanup` | — | **built** (result below). always (rules above): delete `createdIds`, no `Echokraut Selftest` rows left, options back from `touchedOptions` (game-voice opt-in off again), write `e2e-<id>.json`, `EKTEST … done`, chat warning "plugin runs on the E2E config until restore.ps1" | DB clean |

`E:\EkE2E` stays after the run.

### Root guard (hard stop in `preconditions`) [M7]
`installRoot`: rooted path on drive `E:`, no UNC, no `\\?\`; passes `NativeAlltalkBuilder.ValidateInstallPath`
(no spaces or dashes); after resolving junctions/symlinks (`Directory.ResolveLinkTarget`) and
case-insensitively it neither equals nor overlaps the backed-up config's `TtsInstallRoot`,
`LocalSaveLocation` or `Alltalk.LocalInstallPath`; an existing folder only with `EkE2E.marker` in it.

### Reload (stage 1)
The plugin sets its own DLL's `LastWriteTimeUtc` and then does nothing more. Fallback if the watcher
does not fire (spike in build step 1): `start.sh` waits for `EKTEST <id> backup reload-requested` and
touches the DLL over ssh. An scp deploy during a run also reloads — covered by "resume at phase, restart
the running stage".

### `StopInstance` URL fix (part of build step 1) [BLK-3]
`StopInstance` posts `/shutdown` to `http://127.0.0.1:{Port()}`, never to `BaseUrl`. Today a user who
switches Local → Remote while a local wrapper is tracked shuts down the remote server (no key). Real
bug fix with a unit test and a `BUG FIXES` bullet in v0.19.3.3 EN/DE [L10].

### First-time wizard controller (stage 3)
`NativeFirstTimeWindow` (672 lines) and `NativeEchokrauTtsBuilder.BuildLocalInstance` hold the flow in
lambdas. Pulled out into `FirstTimeWizardController`:
- state: `Step` (0/1/2), `RemoteTestSucceeded` + URL snapshot;
- actions: `ChooseEngine(TTSBackends)`, `ChooseMode(AlltalkInstanceType)`, `SetInstallRoot(path)`,
  `SetLocalSaveLocation(path)`, `SetShareLines(bool)`, `StartInstall()` (stop if running **and**
  `Install()`, both inside `Task.Run`), `TestConnectionAsync()`, `Next()`, `Back()`, `Finish()`
  (`FirstTime = false`, save, raises `Completed`);
- the window binds its buttons to these and reads `Step` in `OnUpdate`; the finish callback becomes the
  `Completed` event (no setter wiring).
The E2E stage calls the **same** actions. The window does not know about the run. The Backend tab keeps
its direct service calls.

### Chat scenario (`chat-fc`)
`IChatGui.Print(new XivChatEntry { Type = XivChatType.FreeCompany, Name = <own name>, Message = "<test text>" })`
— printed into the local chat log only, never sent to the server. For the scenario only: `VoiceChat`,
`VoiceChatPlayer`, `VoiceChatFreeCompany` on (in `touchedOptions`). It goes through the **real**
pipeline (not self-test): the own player's DB character, clip rows and the WAV (under
`<installRoot>\LocalSaves`) go into `createdIds` and are deleted in cleanup; Drive upload is off on a
fresh config. Pass = a `Chat` message with the test text reaches the queue and plays (real FC chat may
arrive at the same time — matched by text). Build step 1 checks first whether a printed entry passes the
`ChatMessage` event at all; if not, D3.

### Auto-advance [M2]
Auto-advance clicks an open Talk window. The run switches it off for its duration (`touchedOptions`),
and `preconditions` requires no Talk addon to be open.

### Report and restore
- `Echokraut-e2e\e2e-<id>.json`: version, language, start/end, request (without `remoteUrl`), per stage
  outcome/detail/duration, backup folder. A test searches the whole serialized report for the host [M9].
- `restore.ps1` (written in phase B, before the wipe): refuses while `ffxiv_dx11`, `XIVLauncher` or
  `Dalamud.Injector` run; deletes `pluginConfigs\Echokraut.json` and `pluginConfigs\Echokraut\` **except
  `selftest\`**, including `-wal`/`-shm`; copies the backup back; checks the manifest hashes [L2]. The
  user runs it by hand with the game closed. The plugin never deletes backups.
- **Live restore (added 2026-11-21, no game restart):** opt-in stage `restore` (`RestoreStage`, only when
  listed; also runs after an aborting stage). Phase A shares `E2eSelfReload` with `backup`: check every
  backup file against the manifest (bad → fail, nothing touched), stop the E2E engine, port free,
  `SuppressSaveOnDispose`, `phase = restore-pending` + `restore-requested-id`, touch the DLL. The next ctor
  (`E2eStartupReset`) checks the hashes again, wipes the E2E config (except `selftest\`), copies the backup
  back, checks every copy → `restore-done`, `ConfigIsE2e = false`; a failure after the wipe stays at
  `restore-started` and resumes on the next load. A new request carries `BackupDir` over while the config is
  an E2E one, so a restore can run as its own request: `tools/e2e/restore.sh --live`; or as the last stage:
  `start.sh --stages … --restore`. `restore.ps1` with the game closed stays the fallback.

### Watching from the Mac
`tools/e2e/watch.sh`: `ssh gaming-pc` + uploaded `tail-ektest.ps1`
(`Get-Content dalamud.log -Wait -Tail 0 | Select-String 'EKTEST <id>'`), re-opens on rotation (file
shrinks), one line per event → Monitor tool, ends on `EKTEST <id> done`.

## Build steps (each ends with a run in the game)
1. **Skeleton + spikes + URL fix.** `E2eRunService` (request, gate, state, log format, report),
   `preconditions` + a `checks` stage, Mac scripts, `StopInstance` fix. Spikes: DLL self-touch reload,
   `IChatGui.Print` through the chat hook (watch the hook's log line only; the line is not voiced since
   FC voicing stays off). Run: non-destructive (only existing self-test checks).
2. **Backup + reset + reload** (stages 1, 2, `restore.ps1`, phase states). Run: reset, then restore by hand —
   the first destructive run; restore proven before anything builds on it.
3. **Wizard controller + first-time + checks:local** (refactor + tests). Run: up to stage 4.
4. **Scenarios** (stage 6) — after the reset, never on the real config [BLK-5]. Run: up to 6 (with one engine).
5. **Engine loop** (stage 5). Run: up to 6.
6. **Remote + game voice** (stage 7). Run: complete.
7. Docs: this plan → status, `docs/plans/ingame-self-test.md` › Automation, `Services/CLAUDE.md` row,
   `Testing/checklist.json` item for the E2E run.

### Build step 1 — result (2026-11-21)
- Code: `Services/SelfTest/E2e/` (`E2eRunService`, `E2eRequest`, `E2eState`/`E2eStateStore`, `E2eLogFormat`,
  `E2eRootGuard`, `E2eGameProbe`, `E2eCatalog`, stages `preconditions`, `checks`, `chat-hook`, `reload`);
  `ISelfTestRunGate` shared with `/ektest`; scripts `tools/e2e/` (`start.sh`, `watch.sh`, `fetch-report.sh`,
  `lib.sh`, `preconditions.ps1`, `submit-request.ps1`, `tail-ektest.ps1`). Reports land in `.claude/e2e-reports/`.
- `chat-hook` and `reload` are build-step-1 spikes; `chat-hook` probes with `Echo` when FC voicing is on
  (never voiced), else with `FreeCompany`.
- Runs `20261121-s1a` (3/0/1) and `20261121-s1b` (2/0/0): checks 9/0/1 on the real config.
- **Spike reload: works.** Setting the DLL's `LastWriteTimeUtc` reloads within ~1.3 s (`Reason = Reload`), and
  the new instance resumed the run from `state.json` (restart of the running stage proven). No ssh
  fallback needed. A deploy by **rename** (`Move-Item` over the DLL) does **not** reload; copy over it or touch it.
- **Spike chat: works.** An entry printed with `IChatGui.Print` passes `IChatGui.ChatMessage` (sender = own
  name) — D3's fallback is not needed. Verified with `Echo`; `FreeCompany` itself follows in step 4 (FC
  voicing is off on the reset config).

### Build step 2 — result (2026-11-21)
- Code: `E2eStartupReset` (phase B, static, called in `Plugin` ctor between `Loc.Init` and `GetPluginConfig`,
  logs through `IPluginLog`), `E2eRestoreScript` (restore.ps1, ASCII/CRLF, finds every path from
  `$PSScriptRoot`), stages `BackupStage` (phase A) + `ResetStage`; `ReloadSpikeStage` removed (its touch
  logic lives in `BackupStage`). `IE2eRunService.SuppressSaveOnDispose` gates `_configuration.Save()` in
  `Plugin.Dispose`. Catalog order: preconditions, backup, reset, checks, chat-hook.
- Phase handshake: phase A sets `reset-pending` + mark `reset-requested-id = <id>`; phase B acts only on
  that mark, on a dev load and on a state younger than 6 h. Errors land in marks (`backup-error` → phase
  back to `requested`, nothing wiped; `reset-error` → stays `wipe-started`, resumes on the next load) and
  the restarted stages report them. The run service no longer bumps the phase to `stages` at run start
  (it overwrote `reset-pending`); `ResetStage` moves it there after its pass.
- Port check: TCP connect to `127.0.0.1:8765` (+ the configured loopback port), polled up to 20 s after
  `StopInstance` (EchokrauTTS + AllTalk). `ssh`-side `Get-NetTCPConnection` not needed.
- Run `20261121-s2a` (`--stages preconditions,backup,reset`): **3/0/0**. Reload ~1 s after the touch,
  5 files backed up (`Echokraut.json`, `addonconfig.json`, `echokraut.db`, `echolines-sent.txt`,
  `game_voice_aliases.json`), wiped, fresh config + empty DB confirmed. `-wal`/`-shm` are not in the
  backup because the old Dispose closes the DB, SQLite checkpoints and deletes them.
- Restore entry points: Start-menu shortcut "Echokraut E2E Restore" (`tools/e2e/install-restore-shortcut.ps1`,
  runs `~\eke2e-tools\restore-latest.ps1`, waits for Enter) and `tools/e2e/restore.sh` from the Mac (skill
  `/e2e-restore`). `restore-latest.ps1` restores only while `state.json` says `ConfigIsE2e`, and then runs the
  `restore.ps1` in `BackupDir`. Once state.json is gone it never restores an older backup.
- Restore proven 2026-11-21 via `/e2e-restore`: 5 files back, every SHA-256 matches, state.json deleted.

### Build step 3 — result (2026-11-21, game run 5/0/0)
- `IFirstTimeWizardController` / `FirstTimeWizardController` (Services/, DI). The window reports open/closed
  (`ReportWindowOpen` from `OnSetup`/`OnFinalize`) because `NativeWindowManager` is created after the run
  service and is not in the container. `Completed` is handled by `NativeWindowManager` (MarkAllSeen, close,
  config window). `Next`/`Finish` check their own condition (a dimmed node still gets its click).
  Builders take optional `setInstallRoot`/`startInstall`; the Backend tab passes none.
- Stage `first-time` (`FirstTimeStage`) + `IE2eInstallHost`/`E2eInstallHost` (empty root with marker only,
  `ShareLinesWithCommunityDb` from the backed-up JSON, listener exe via `GetExtendedTcpTable`). Install
  predicate: one inactive poll is tolerated (all flags are false between `MarkInstalled` and
  `InstanceRunning = true`), two in a row = failed; never active within 30 s = failed.
- Stage `checks:local` = `ChecksStage` with `needs first-time`. Catalog order now: preconditions, checks,
  chat-hook, backup, reset, first-time, checks:local (the non-destructive ones before the backup).
- Game run `20261121-180037` (3/1/1): backup, reset pass; install to `E:\EkE2E` done in 396 s, but
  `first-time` failed on the listener check: on Windows the venv `python.exe` is only a launcher, the
  process on 8765 is uv's base interpreter under `%APPDATA%\uv\python\…` (same command line, parent =
  `E:\EkE2E\echokrautts\.venv\Scripts\python.exe`). Fix: `ListenerExecutable` returns the parent's path
  when it has the listener's file name (`LauncherOrSelf`; parent PID via `CreateToolhelp32Snapshot`).
- Rerun `20261121-193522`: **5/0/0**. first-time 431 s (install 406 s), listener
  `E:\EkE2E\echokrautts\.venv\Scripts\python.exe`, 260 voices, wrapper 0.0.1.9; checks:local 8 pass /
  2 skip (sentence-continuation and game-voice are off on a fresh config), generation ~18 s for the first
  bytes on the fresh install.
- Found on the side: a voiced chat line with 3D audio crashed the game (`GameObject.Position` read on the
  BASS poller's timer thread → Dalamud "Not on main thread!"). Fixed with `SpeakerPositionCache`
  (filled on the framework thread), see `Services/CLAUDE.md` › `IAudioPlaybackService`.

### Build step 4 — result (2026-11-21)
- Stage `scenarios` (`ScenariosStage`) with sub-scenarios in `Services/SelfTest/E2e/Scenarios/`: `continuation`,
  `queue-order`, `epoch-cancel`, `mute`, `chat-fc`. Each is reported on its own as `scenarios:<id>` through
  `E2eContext.ReportPart` (own `EKTEST` line, own entry in state/report; a restarted stage skips parts already
  reported). The stage itself passes when no part failed (all skipped → skip).
- **Dependency until step 5:** `Needs = checks:local` (and through it first-time/reset), plus a guard
  `ConfigIsE2e` → skip "never on the real one" [BLK-5]. It runs on the engine the first-time install left
  running. Step 5 moves the need to the last passed `engine:*` [M4].
- **No new overload:** `ProcessSelfTestSpeechAsync` already takes the source from the `EKEventId`; the
  scenarios pass `AddonTalk` where the Talk-only rules matter (continuation, mute). Shared sender
  `E2eSelfTestSpeaker` (framework thread, throwaway speaker `Echokraut Selftest`, `SelfTestTraces` cleanup —
  pulled out of `PipelineCheck`).
- `continuation`: pair from `SentenceContinuationIndex.Sample` (`IDialogContinuationService.SamplePair`): first
  unambiguous pair without player/gender placeholder, box 2 goes on after the head, both ≤ 400 chars. Lines
  are cancelled after the assert (AddonTalk outside a dialog is not played anyway).
- `mute` = the **NPC mute** (`NpcMapData.IsEnabled`, saved like the UI does), Talk source; the per-instance
  dialogue mute needs a game object's base id and is not covered.
- `epoch-cancel`: cancel through `IAudioPlaybackService.ClearQueue(VoiceTest)` (the advance path), then
  `IsObsolete` + 15 s silence.
- `chat-fc`: `VoiceChat`/`VoiceChatPlayer`/`VoiceChatFreeCompany` on, matched by text + `Chat` source; own
  character (only if new), clip row and WAV go into `createdIds` first and the scenario removes them itself
  right away — an id stays listed only if its removal did not happen (crash), for stage 8. No cleanup stage yet.
- Options: `E2eOptions` (`Set` records the old value once as JSON in `touchedOptions`, `Restore`). The stage
  turns auto-advance off for its duration [M2], restores leftovers at start, everything else after each
  scenario and all at the end.
- `continuation` judges the **spoken** text: the same two boxes first go through the pipeline with the option
  off (baseline), then on; joined box 1 = baseline 1 plus more, joined box 2 = a shorter end of baseline 2. A
  raw-head compare fails, because cleaning rewrites the head ("Mit Ezel II." is spoken "Mit Ezel zweiter").
- `queue-order` orders by each held entry's `StartedPlayingAt`, not by polling `GetCurrentlyPlaying` (a short
  line can start and end between two polls; the unit test flaked under load).
- **Game runs (gaming PC, German client, remote reachable):** `20261121-235532` 10/2/0 — `continuation` failed
  on the raw-head compare above, the other four passed. After the fix `20261122-000855` **12/0/0**: scenarios
  5/0/0 in 70 s (continuation pair "Mit Ezel II.", queue 1,2,3, epoch 0 → 1 while PendingGeneration, mute,
  FC line played + WAV saved and removed); `restore` put back all 5 files with matching SHA-256,
  `state.json` `ConfigIsE2e: false`. `echokraut.db` can not be re-hashed from outside while the plugin holds it.

### Build step 5 — result (2026-11-22)
- One stage `engine` (`EngineStage`), each engine reported as `engine:<id>` through `ReportPart` (like
  `scenarios`): the catalog is built before any request, the engine list comes from the request and
  `AvailableEngines` at run time. Not in the catalog / `minVersion` above `InstalledWrapperVersion` → skip with
  the reason [M10]; no enabled voice → skip; failed start, ready wait (30 min) or a failed generation/pipeline
  check → fail. Refuses on the real config (`ConfigIsE2e`), it would change the user's engine choice.
- Waits shared with `first-time`: `E2eEngineWait` (ready predicate `Judge`, voices) — pulled out of `FirstTimeStage`.
- After the loop the last passed engine is switched back on when the loop ended on another one; the mark
  `engine-active` is set only once an engine is ready. `scenarios` overrides the new `E2eStage.UnmetNeed`: with
  `engine` in the run it needs the last passed `engine:*` **and** that id in `engine-active`; without it
  `checks:local` as before.
- **System drive (Risks › model caches):** models go under the install root (bootstrap sets `HF_HOME`,
  `HF_HUB_CACHE`, `TTS_HOME` to `models/`), but **uv's package cache (`%LOCALAPPDATA%\uv\cache`, 24 GB on the
  gaming PC) and uv's Python (`%APPDATA%\uv\python`) stay on `C:`** — the bootstrap sets no `UV_CACHE_DIR`.
  Three venv groups (classic: xtts/f5/pocket, qwen, moss); a first qwen or moss venv can add several GB of
  wheels there. `C:` had 14 GB free. Guard in the plugin: below 5 GB free on the system drive the loop skips
  every further engine; each switch logs the free space. The real fix (cache under the install root) belongs to
  the wrapper repo, not done here.
- **Game run `20261122-002606`** (`--stages preconditions,backup,reset,first-time,checks:local,engine,scenarios
  --restore`, all six engines requested): **16/0/3**. first-time 401 s (wrapper 0.0.1.9, 260 voices), checks:local
  8/0/2. Engine catalog of the installed wrapper 0.0.1.9 lists only `xtts`, `f5`, `moss` → `qwen-1.7b`,
  `qwen-0.6b`, `pocket` skipped "not in the engine catalog" [M10] (they need a newer wrapper release). xtts 18 s
  (first bytes 0.4 s), f5 72 s (17 s, restart + model load), moss 52 s (1.0 s); engine stage 142 s, active at the
  end: moss. Before wrapper 0.0.2.0 the bootstrap installs every engine up front, so no switch downloaded
  anything: `C:` stayed at 14.0 GB free, uv cache 24.2 GB. The `C:` risk remains for a wrapper ≥ 0.0.2.0 (per-engine
  install on the first switch). scenarios 5/0/0 on moss; restore put back all 5 files (SHA-256 ok),
  `state.json` `ConfigIsE2e: false`.

### Build step 6 — result (2026-11-22)
- Shared switch `IBackendService.SwitchInstanceTypeAsync(type, baseUrl, eventId)`: leaving Local stops the local
  instance (thread pool), then mode + `BaseUrl` are written and saved. The Backend tab's Local/Remote/None buttons
  use it too — a user-visible change (the local engine no longer keeps running after "Remote Server"): IMPROVEMENTS
  bullet in v0.19.3.3 EN/DE, checklist item `be-mode-stops-local`.
- Stage `remote` (`RemoteStage`, needs `reset`, refuses on the real config, skips without `remoteUrl`), parts
  `remote:switch` (`E2eSelfReload.StopEnginesAsync` = stop + port free, nothing tracked, then the switch, then
  ≥ 1 voice), `remote:backend|generation|pipeline` (a failed switch skips all, a failed backend the game voice),
  `remote:game-voice` (`GameVoiceCheck` after the D2 opt-in: `GameVoiceReferenceEnabled`, `GameVoiceConsentVersion`,
  `GameVoiceConsentUtc` via `E2eOptions`), `remote:game-voice-generation` (`E2eGameVoiceGeneration`). The opt-in is
  switched off again at the end of the stage (+ `ClearMemory`), not only in the future cleanup stage.
- Game-voice generation: the NPC comes from `IGameVoiceReferenceService.SampleVoicedNpcNameAsync` (text index only,
  no audio: owned token with the most lines, exactly one voice-actor epoch, name → token round trip checked), then
  `RankAsync`, then a self-test line under that NPC's own name (never saved, never on Drive) through the real
  pipeline; pass = `ReferenceSource == GameVoice`; its clip row and (when new) the character are deleted by id
  (`createdIds` while it runs). Game audio stays inside the service (hard constraint unchanged).
- `checks:remote` is its own `ChecksStage` after `remote` with `needs remote:switch` (a part id): a remote without
  game-voice support must not hide the other checks. Catalog order: … engine, scenarios, remote, checks:remote, restore.
- **Game run `20261122-010433`** (full chain, `--restore`): **20/2/5**. first-time pass (wrapper 0.0.1.9, 260 voices),
  checks:local 8/0/2, engine xtts/f5/moss pass (qwen-1.7b, qwen-0.6b, pocket not in the 0.0.1.9 catalog), scenarios
  5/0/0, `remote:switch` (332 voices), backend, generation (first bytes 5.1 s), pipeline pass; **`remote:game-voice`
  fail `ServerUnsupported`**: the private remote's `/health` has no `inline_reference` (older wrapper, xtts on cuda) —
  the plugin judges correctly, the remote needs a wrapper with inline references. `checks:remote` was skipped (it
  still needed the whole stage) → fixed to `remote:switch`.
- **Rerun `20261122-011737`** (`preconditions,backup,reset,remote,checks:remote --restore`, no install): 9/2/1, same
  game-voice result, `checks:remote` 8/0/2. Restore put back all 5 files both times, `state.json` `ConfigIsE2e: false`.
  Neither report nor any `EKTEST` line contains the remote host.
- ~~Open: the game-voice generation part has not run against a real server yet (needs the remote wrapper update).~~
  Done 2026-11-24: the private remote now runs the release image 0.0.1.9 (`/health`: `inline_reference: true`, xtts fp16
  on cuda) instead of the stale `main` pin from 2026-08-08. **Game run `20261124-171508`**
  (`preconditions,backup,reset,remote,checks:remote --restore`): **13/0/0**. `remote` 6/0/0: switch (332 voices),
  backend 125 ms, generation (first bytes 2.2 s), pipeline, `remote:game-voice` pass (673 speaker tokens, 25361 voiced
  lines, one clip decodes in memory), `remote:game-voice-generation` pass (Alphinaud: 3 clips ranked, line spoken with
  the game voice). `checks:remote` 8/0/2, cleanup removed nothing (stages had cleaned up themselves), restore put back
  all 5 files (SHA-256 ok). Report and `EKTEST` lines contain no remote host.

### Stage 8 `cleanup` — result (2026-11-22, game run 18/2/1)
- `CleanupStage` (`Stages/CleanupStage.cs`), in `E2eCatalog` after `checks:remote`, **before** `restore` (restore puts
  the real DB back; deleting there would hit it). New `E2eStage.AlwaysRuns` + `IsIn(request)`: the run service takes
  the stage even when the request's stage list leaves it out; `RunsAfterAbort` too. A resumed run after the restore
  reload sees its result and does not run it again.
- Only with `ConfigIsE2e` **and** `phase >= reset-done`; otherwise it touches nothing: pass "report only", or fail
  when state still lists created ids / touched options (they cannot come from an E2E config then).
- Order: every `touchedOptions` entry back (`E2eOptions.Restore`), game-voice opt-in forced off if still on, save,
  `ClearMemory`; then `createdIds` — `wav:` (deleted only under `installRoot`, otherwise kept), `clip:` rows,
  `character:<name>` (every row with that name, its clip rows, the cache entry through `INpcDataService.RemoveCharacter`);
  then every `Echokraut Selftest` character. Pass = nothing still listed and no `Echokraut Selftest` row left.
  Scenarios and remote keep removing their own traces right away; the stage only catches what a crash or unload left.
- Report `e2e-<id>.json` and the `done` line stay in the run service's `Finish` (they must also cover the restore
  that runs after cleanup). Deviation from the table: the warning comes from `Finish`, not the stage — only when the
  run ends with `ConfigIsE2e` (no restore, or a failed one): `EKTEST <id> run warn …` before the `done` line (the
  watcher stops there) and a localized chat line after the summary.
- **Game run `20261122-014414`** (`preconditions,backup,reset,first-time,checks:local,scenarios,remote,checks:remote
  --restore`): **18/2/1**. The run service planned `…,checks:remote,cleanup,restore` although the request did not list
  `cleanup` (`AlwaysRuns` works). `cleanup pass` right before `restore start`: 0 created ids, 0 `Echokraut Selftest`
  characters, no options to restore, game-voice opt-in off — scenarios and remote had already removed their own traces.
  Restore put back all 5 files (hashes match), `state.json` `ConfigIsE2e: false`, **no `run warn` line**. The two fails
  are the known `remote:game-voice` `ServerUnsupported` (+ the stage `remote` it belongs to); the skip is
  `remote:game-voice-generation`. Neither report nor any `EKTEST` line contains the remote host.
- The first try `20261122-013349` (17/2/1) ran without `cleanup`: the dev-load DLL on the PC was a build from before
  stage 8 (no `CleanupStage` type in it). After a deploy of 4671222 the rerun above. Before an E2E run, check that the
  dev-load DLL contains the newest stage (`Select-String -Encoding ascii -Pattern <StageClass>` on the DLL).

### Unreleased wrapper — `testWrapper` (2026-11-22)
- Why: the engine loop needs wrapper ≥ 0.0.2.0 for qwen-1.7b/qwen-0.6b/pocket, and 0.0.2.0 is to be released only
  after this run. `releases/latest` (0.0.1.9) and the tag's `engines.json` cannot deliver it.
- `tools/e2e/push-test-wrapper.sh` builds the zip from the local `echokrautts` working tree (`build-release-zip.py`,
  uncommitted changes included; prints HEAD + number of dirty paths + SHA-256), appends the checkout's `engines.json`
  and places it via `place-test-wrapper.ps1` in the wrapper checkout next to the plugin's dev load. Request field
  `testWrapper` (`start.sh --test-wrapper`): `first-time` installs it through the wizard controller
  (`StartInstall(testWrapper: true)` → `InstallTestWrapper`), fails up front when the zip is missing. Recorded version
  `test (local zip)`; the catalog is read from the installed build and pinned. Details:
  `docs/claude/services/echokrautts-install.md` › Test install.
- Run: `push-test-wrapper.sh`, then `start.sh --test-wrapper --stages preconditions,backup,reset,first-time,engine --restore`.
- **Game run `20261123-130938`** (that command; zip from echokrautts `3a6d456`, 0 dirty paths, SHA-256 `54184ac7…`
  equal on the Mac and the PC): **engine loop 6/0/0** — all six engines pass for the first time. first-time 164 s
  (`test (local zip)`, 260 voices). Stage time incl. switch, per-engine install and ready wait / first bytes:
  qwen-1.7b 213 s / 0.7 s, qwen-0.6b 95 s / 0.5 s, xtts 69 s / 18.0 s, f5 136 s / 17.1 s, pocket 100 s / 2.2 s,
  moss 124 s / 1.1 s; engine stage 738 s, active at the end: moss. Each pipeline check played a voice-pack voice
  after ≤ 0.2 s. **`C:` stayed at 32.7 GB free on every switch** (uv cache already warm at 24.2 GB, uv Python
  0.1 GB); `E:` 74.7 → 68.7 GB, install root 24.2 GB. `cleanup` pass (nothing left over).
- **But the run did not finish:** `restore` put back all 5 files (hashes match, `state.json` phase
  `restore-done`), then the game **crashed ~3 s after the restore reload** — native `C0000005` in
  `AtkEventManager.ClearEvents` ← `AtkUnitManager.Update` → Dalamud `OnAddonFinalize` →
  `KamiToolKit NativeAddon.Finalizer` → `ComponentNode.OnDeinitialize`. The unload just before had logged
  "2 node(s) not disposed safely, forcing disposal of: TextButtonNode" ×2. So no `run done` line and no
  `e2e-<id>.json`; the numbers above come from `state.json` and the `EKTEST` lines. The earlier restore runs
  (without `first-time`+`engine` as the last stages) did not crash.
- **Cause (2026-11-23, from the crash log + dalamud.log):** the addon alive at unload was the **config window**:
  `OnWizardCompleted` closes the wizard and opens it, and with `first-time`+`engine` as the last stages nothing
  closes it before the restore reload. `Close()` only starts the closing animation; `NativeWindowManager.Dispose`
  waited ≤ 2 ticks, the game finalized the window ~3 s later through KamiToolKit code of the unloaded plugin and
  hit freed memory. One of the two "Forcing disposal" TextButtonNodes was the never-disposed measuring node in
  `NativeConfigWindow` (mode buttons), not attached, so noise rather than the cause. Why earlier restore runs
  survived is not provable (logs rotated; a use-after-free only crashes when the memory was reused).
- **Fix:** `E2eSelfReload.CloseWindowsAsync` (backup + restore, after the engine stop, before the touch) closes every
  own window through `IOwnedWindowsService` and waits until none has a native addon (10 s, else fail without
  reload); `NativeWindowManager.Dispose` waits off the framework thread until the windows are finalized (≤ 1 s);
  the measuring node is disposed. **Resume after the game restart worked:** `run resume restore` → `restore pass` →
  `done 13/0/0`, report `e2e-20261123-130938.json` written.
- **Rerun `20261123-141448`** (same command, fix deployed): **13/0/0**, engine loop 6/0/0 again; `restore progress closing
  the plugin windows` (the config window from the wizard was open), unload 1.5 s later, reload, `restore pass`, `done` —
  **no crash**, game kept running. The unload still logged one "Forcing disposal of: TextButtonNode" (was two); it is
  not attached anywhere, so harmless, source not found yet.

### Real pre-release 0.0.2.0 (2026-11-24)
- Why no `testWrapper`: the run should take the path users get once `RemoteUrls.json` on `main` names 0.0.2.0
  (release download, `engines.json` at the tag, recorded version `0.0.2.0`).
- ⚠ **A dev build ignores the local `RemoteUrls.json`**: the copy on GitHub `main` wins over the embedded one, so
  until that push the plugin installs what `main` names. For this run the deployed DLL was built with
  `RemoteJsonUrl` pointing at a missing file (404 → embedded copy wins); the source was reverted right after the
  build and a normal build was deployed again after the run. Never commit that switch.
- Run `20261124-184749` (`start.sh --no-remote --stages preconditions,backup,reset,first-time,engine --restore`):
  **13/0/0**. first-time 182 s (installed `0.0.2.0`, 260 voices). Engines incl. switch + ready wait / first bytes:
  qwen-1.7b 236 s / 1.3 s, qwen-0.6b 107 s / 0.5 s, xtts 67 s / 17.9 s, f5 139 s / 17.6 s, pocket 100 s / 2.1 s,
  moss 122 s / 1.1 s; engine stage 771 s. Restore went through a reload (`run resume restore`), all hashes match, the game
  kept running (same process) and no "Forcing disposal" line was logged.

## Tests (xUnit, `Echokraut.Tests/`)
Request parsing + validation (engines default all six); root guard (C:, equal/inside/overlapping real
paths, junction, UNC, `\\?\`, spaces, folder without marker); `E2eStartupReset` against a temp folder
(backup, manifest, hash mismatch → no wipe, wipe keeps `selftest\`, `wipe-started` resumes without a
second backup, not `IsDev` / id mismatch → no file touched); state machine (atomic write, resume +
restart of the running stage, stale state → aborted, abort rule for stages 0-2, dependency skip,
cleanup without reset → report only); log line format + host scrubber; `FirstTimeWizardController`
(every action, step transitions, remote URL snapshot, `StartInstall` does not stop synchronously,
`Finish` raises `Completed`); `StopInstance` posts to `127.0.0.1:{port}`; each stage with mocked services
(pass/fail/skip paths); report serialization without the host anywhere.

## Risks
- **Install time**: wrapper + Python env + models: stage 3 timeout 60 min, engine stages 30 min each,
  with progress lines so a stall shows.
- **VRAM**: game + one engine. The real engine is stopped in stage 1; stage 7 stops the E2E engine.
  `qwen-1.7b` needs ~5 GB next to the game.
- **Model caches outside `E:`**: if a sub-engine downloads models into the user profile (HF cache on `C:`),
  the run fills `C:`. Check in build step 5 (wrapper side, `echokrautts`).
- **Game crash mid-run**: state shows where; backup and `restore.ps1` exist from phase B on.
- **Remote `/shutdown`**: whether the remote wrapper accepts it without a key is not checked (echokrautts
  repo); the URL fix makes the question moot for the plugin.

## Decisions (user, 2026-11-21)
- **D1 Engines:** all six — `qwen-1.7b`, `qwen-0.6b`, `xtts`, `f5`, `pocket`, `moss` (still filtered at run
  time by catalog and wrapper version; `pocket` has no Japanese model).
- **D2 Game-voice opt-in:** always on in the E2E run; starting the run is the user's explicit opt-in.
  Only the user's own instance, the same NPC, in memory, through `IGameVoiceReferenceService` (hard
  constraint unchanged). Switched off again in cleanup.
- **D3 Chat fallback:** if `IChatGui.Print` does not pass the chat hook, the scenario hands the entry
  straight to `ChatTalkHelper`'s processing (everything after the hook is still covered; the detail says so).
- **D4 Second run:** `E:\EkE2E` is emptied first (only with the marker) — every run is a fresh install.
- **D5 Character:** the main character (whichever is logged in); `chat-fc` uses its name, only locally.
- **D6 Reset scope:** everything in `pluginConfigs\Echokraut\` except `selftest\` (incl.
  `echolines-sent.txt`, `game_voice_aliases.json`, `addonconfig.json`).
- **D7 Changelog:** E2E code and wizard refactor `(no changelog: dev-only E2E run)`; the `StopInstance`
  fix gets a `BUG FIXES` bullet in v0.19.3.3 EN/DE.

Closed by the review: startup probe of `C:\LocalTTS` (none), Dispose save (wipe before load **and**
`SuppressSaveOnDispose`, as the brief asks), reload trigger (self-touch, ssh fallback).
