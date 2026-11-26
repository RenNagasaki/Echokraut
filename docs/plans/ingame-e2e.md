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

## Coverage of the release test plan (2026-11-25)
Goal: the user tests by hand only what really needs a human. Basis: block "🎯 Release-Testplan: Plugin 0.19.3.3 ·
Installer ELI-1.3.2.0 · EchokrauTTS 0.0.2.0" plus sections 0, ⚡ and 1-10 of `docs/pre-release-ingame-checklist.md`
(96 open boxes counted 2026-11-25; R1 update and R3.1-3 are done). Classes: **a** already covered (evidence),
**b** new SelfTest check / E2E stage (in Dalamud, no character control), **c** from the Mac without the game
(ssh, logs, `nvidia-smi`, files, `gh`/`curl`), **d** stays manual (reason). "+" = split item. **B<n>** = build step below.

### Section 0, ⚡ Quick smoke, R0
| Item | Class | How / why |
|---|---|---|
| 0 build, test | a | `node .claude/verify.js` (also the Stop hook) |
| 0 SonarQube | c · B1 | scan sequence as a script (exclusions on `begin`, see CLAUDE.md › SonarQube) |
| 0 GitLab pipeline + mirror | c · B1 | `glab ci status` on `main` after the user's push |
| 0 changelog + version | a | `.claude/hooks/echokraut-commit-gate.js` |
| 0 installer released + `RemoteUrls.json` | c · B1 | `gh api` release assets vs `installerUrl`/`installerVersion` |
| ⚡ loads without crash, logged in | a | every E2E `backup`/`restore` is a reload while logged in (13/0/0 runs) |
| ⚡ loads via the login screen | c · B9 | needs a game start; else d |
| ⚡ `/ek`, `/ekfirst` open | b · B5 | `ICommandManager.ProcessCommand`, assert through `IOwnedWindowsService` |
| ⚡ talk to an NPC → voiced | d (+a) | needs an NPC interaction (botting); the pipeline with source `AddonTalk` is covered by `scenarios:continuation`/`mute` |
| ⚡ dialog-toolbar voice dropdown 2-3× | d | the toolbar exists only with an open Talk addon |
| ⚡ Backend section after close + reopen | b · B5 | open config → Backend tab → node visibility → close → reopen, both engines |
| ⚡ unload → `echokraut.db` deletable | a | `E2eStartupReset` deletes the DB (+`-wal`/`-shm`) right after the old `Dispose`, no game restart |
| R0 verify wrapper + plugin on the Mac | c · B1 | both `verify.js` in one preflight script |
| R0 GitHub mirror current | c · B1 | `curl` raw `RemoteUrls.json` on GitHub `main` vs GitLab |
| R0 `engines.json` == embedded, `minVersion`, order | c · B1 | script compares both files (JSON) |
| R0 `build-release-zip.py --list` | c · B1 | expected paths present, no `CLAUDE.md` |
| R0 Docker GPU + CPU images | c + d | trigger + watch with `gh` (outward: only after the user's OK); CPU image smoke on the docker host (`xtts` ready + `/tts`, `pocket` message); GPU image at runtime: no GPU on the docker host → d |

### R1 wrapper alone, R2 plugin + installer + wrapper
| Item | Class | How / why |
|---|---|---|
| R1 xtts f5 moss fresh, lazy install | a + b · B2 | engine loop passes all six (`20261124-184749`); new: install-root size after each engine, `f5_tts` absent from `.venv` before `f5` |
| R1 qwen: `.venv-qwen`, `/health` cuda, first word ≈ 0.3 s | a + b · B2 | engine loop (first bytes 0.5 s); new: `/health` device + venv folder recorded per engine |
| R1 pocket + pocket-6; `--language fr` fallback | b · B3 + c · B10 | layers 24/6 via the plugin option; the fr fallback with `windows-test.sh pocket -Extra "--pocket-layers 6"` + `--language fr`, assert `pocket.layers_fallback` in NDJSON |
| R1 response cache (miss/hit/bypass/shared, one `tts request start`, `/health response_cache`, web UI bypass) | b · B4 | stage `wrapper-http` against the running E2E engine; web UI: request body the page sends |
| R1 cancel refcount (one of two, both → GPU drops, then `miss`) | b · B4 | same stage, `nvidia-smi` sampled from the stage (dev only, read-only query) |
| R1 NDJSON language + `key` field | b · B3 + c · B10 | de: install log of `first-time`; fr/ja: `windows-test.sh --language` |
| R2 engine list: rating in client language, best first, DE/EN/JA not truncated | a + b · B5 | text/order: `TtsEngineRatingTests`, `TtsEngineCatalogServiceTests`; live: dropdown option texts vs catalog, text width vs node width; EN/JA only if the plugin language can be switched at runtime, else d |
| R2 XTTS → Qwen 0.6B: German console, no mojibake, ready, installer log line | b · B3 (+d) | engine loop switches already; new: parse `EchokrautLocalInstaller.log` (`ttsBackend=… | xttsFp16=… | qwenTemperature=…`, no `qwenXvecOnly`), scan the log for mojibake (`Ã`, U+FFFD); the console window itself: one look, d |
| R2 Qwen "calmer emphasis" → restart, `--qwen-temperature 0.7` | b · B3 + d | option set through the same path as the UI, restart + log arg; "sounds different" = hearing, d; no xvec checkbox: code (a) |
| R2 Qwen 1.7B ready, VRAM ≈ 5 GB | a + b · B2 | engine loop + VRAM per engine from `nvidia-smi` |
| R2 ⚠ FPS drop with Qwen | b · B2 | frame times from `IFramework.Update`: idle and during generation, per engine (xtts/qwen-0.6b/qwen-1.7b), with GPU load; not the Limsa walk, but the same render loop next to the same generation |
| R2 Pocket: CPU, steps, layers 24 ↔ 6, own steps kept | a + b · B3 | `/health` device cpu + GPU load; layer switch → restart + first bytes; `--pocket-decode-steps 8` in the log; step options for 6 layers; back to 24 keeps its steps (`EngineOptionScopeTests` for the logic) |
| R2 Japanese client + Pocket → clear message, no half download | c · B10 + d | wrapper side via `windows-test.sh pocket --language ja`; the JA text in the plugin needs a JA client, d |
| R2 VCM "regenerate" twice → fresh; dialog line twice → cache hit | a + b · B4 | `EchokrauTtsBackendTests` (`no_cache` only from the VCM); live: `IVoiceClipManagerService` regenerate twice + wrapper log (2 starts, bypass); a non-self-test line twice → second first-byte ≈ 0 |
| R2 fast clicking (cancel on advance) | a + b · B4 + d | `scenarios:epoch-cancel`; new: cancel during generation → wrapper job cancelled, GPU drops; the real click: d |
| R2 game voice phase 6 index (with/without Whisper, abort/resume, hint, button active after reload) | b · B6 (opt-in) | stage `game-voice-index`: start through the service, progress + ETA + frame times, cancel after N tokens, start again → "current" count rises; full runs only when requested (15-30 min / 1-2 h) |
| R2 after the batch: unseen voiced NPC speaks at once; `game_voice_unmatched_de.json` | b · B6 | `RankAsync` time with index ≈ quick stage only; file exists + schema |
| R2 epoch by story progress (Y'shtola, NG+) | a + d | `story-progress` check (log line); NG+ needs a character state and a cutscene, d |
| R2 speaker-change detection → suggested splits | b · B6 (opt-in) | part of `game-voice-index`: file contains Y'shtola `06100`, Hydaelyn `06000`, no Alphinaud/Thancred border |
| R2 "assign game voices" + export for the repo | b · B6 | service calls (not the window): assign a token → next line `ReferenceSource == GameVoice`, no re-rank in the log; export file matches the resource format |
| R2 "edit character" game-voice field `-` / empty | b · B6 | set the field, generate, assert reference source both ways |
| R2 changelog popup once, version in the title | b · B5 | E2E config with `LastSeenChangelogVersion` = previous version, reload → window open once, reload → not again; window title |

### R3-R6
| Item | Class | How / why |
|---|---|---|
| R3 plugin 0.19.3.3 + wrapper 0.0.1.9: Qwen/Pocket greyed, update offered | b · B8 | request field `wrapperVersion` (install an older release in `first-time`), then `AvailableEngines` flags + reason text, update check offers latest, after the update selectable; reusable for the mixed states of every release |
| R4 Linux/XIVLauncher | d | no Linux game machine reachable from here |
| R4 AMD (the friend) | d | foreign hardware |
| R4 no matching GPU backend + Qwen → stops before download | c · B10 | `windows-test.sh qwen-0.6b` with a forced `gpu_backend=cpu`, assert the message and no model download in NDJSON |
| R5 game voice: no WAV on disk; Qwen/Pocket `inline_reference`; MOSS "not supported" | b · B6 | file snapshot (install root, `pluginConfigs`, `%TEMP%`) before/after every game-voice part; game-voice generation also on local qwen/pocket; moss → availability `ServerUnsupported` |
| R5 first line of a new NPC without index, quick-stage ms, two NPCs in a row | b · B6 | index cleared, two sampled NPCs back to back, `RankAsync` timings into the report |
| R5 narrator voice choose / restart / unchoose / disable | b · B7 | config + reload (`E2eSelfReload`), `NarratorVoicePolicy` result after each step |
| R5 click into an Echokraut window doesn't advance (HUD 100/150 %) | d | needs a Talk dialog and a real mouse click |
| R5 dialog voice dropdown after add/remove voice / VCM change | d (+b) | the dropdown lives in the Talk toolbar; its data refresh events can be checked in B7 |
| R5 AllTalk ↔ EchokrauTTS switch stops the old | b · B7 (half) | switch to AllTalk (not installed): E2E process gone, port closed, VRAM back; with a real AllTalk install d (AllTalk is not in the E2E run) |
| R5 slow source → plays in one piece | b · B2 + c | underrun counter per played line in every pipeline check; the artificial GPU load from the Mac (second generator loop via ssh) |
| R6 `RemoteUrls.json` game-voice URLs → no "Remote … unavailable" | c · B1 | `curl` both URLs + grep `dalamud.log` after the next load |
| R6 `engines.json` at tag 0.0.2.0, Docker release workflow | c · B1 | `curl` + `gh run list` |
| R6 ELI zip name == `installerUrl`, plugin loads the new installer | c · B1 | `gh api` assets; log line on the next load |
| R6 plugin from the repository (no dev build) | d + c | E2E is dev-only (fixed); `/ektest` by hand, report fetched over ssh (`fetch-report.sh` for `selftest-latest.json`) |

### Sections 1-10 (general regression)
| Item | Class | How / why |
|---|---|---|
| 1 fresh config, logged in → FTU opens | a + b · B5 | `first-time` waits for the wizard; new: fail if it had to toggle it (today it toggles silently) |
| 1 fresh config at the login screen → FTU after login | c · B9 | needs a game start, else d |
| 1 reload logged in | a | E2E reloads |
| 1 changelog popup once after a bump | b · B5 | as R2 |
| 1 commands `/ek`, toggle, `/ekfirst`, alias | b · B5 | `ProcessCommand` each, assert window/state |
| 1 unload → DB deletable | a | as ⚡ |
| 2 engine selector, default highlight | a + b · B5 | `FirstTimeWizardControllerTests`; highlight = node state |
| 2 mode buttons, Next gate | a | `FirstTimeWizardControllerTests` |
| 2 step 1 shows only the chosen engine's sections | b · B5 | node visibility per engine |
| 2 step 2 summary, "Test connection" | b · B5 | `TestConnectionAsync` against the remote in Remote mode |
| 2 finish → wizard not again, changelog not stacked | a + b · B5 | `first-time` pass conditions; new: reload after finish → neither window opens |
| 3 engine dropdown AllTalk ↔ EK | b · B5 | UI sweep |
| 3 mode section survives close/reopen | b · B5 | as ⚡ |
| 3 EK engine dropdown + engine-only options + restart per toggle | b · B3/B5 | options in B3, visibility in B5 |
| 3 install/start/stop → no game freeze | b · B2 | frame-time watchdog: longest frame during `Install`, `StartInstance`, `StopInstance`, switches; fail above a limit |
| 3 remote URL test → label | b · B5 | node text after the test |
| 3 backend status display | b · B5 | VCM status node vs reachability (local up, remote, none) |
| 4 AllTalk local install | d | AllTalk not in the E2E run (plan › Goal) |
| 4 EK local install, console, `samples/` with `.txt` | a + b · B3 | `first-time` installs; new: `.txt` next to every `.wav`, install-log mojibake scan |
| 4 engine switch copies voices / restarts | a + d | XTTS ↔ F5 etc.: engine loop; AllTalk ↔ EK: d |
| 5 dialogue (AddonTalk) | d (+a) | real addon hook; pipeline covered |
| 5 battle talk, choices, cutscene choices, retainer | d | duty/FATE/cutscene/summoning bell |
| 5 bubbles (3D) | b · B7 | passive stage: stands where it is, waits ≤ 2 min for a bubble with `VoiceBubble` on (skip when none, e.g. inn) |
| 5 chat per enabled channel | b · B7 + d | `IChatGui.Print` per channel type (Say, Shout, Party, LS, FC …); "only nearby players audible" needs real players at a distance, d |
| 5 auto-advance, cancel on advance | d | Talk addon |
| 5 in-game voice volume slider | b · B7 | set the game's voice volume through `IGameConfig` for the stage (restored), read the BASS channel volume (float read, safe) |
| 6 voice selection search, reassign, persists | b · B7 | service + reload |
| 6 VCM big tree scroll, detail, generate/delete/edit | b · B5/B7 | window sweep: scroll through every page, open detail; actions through `VoiceClipManagerService` |
| 6 phonetics add/change/delete | b · B7 | correction → test line → queued text has the replacement |
| 6 "reload voices" | b · B7 | voice count stable |
| 7 local save → second request from disk | b · B7 | non-self-test line twice (like `chat-fc`), second loaded from the WAV |
| 7 Google Drive | d | needs the OAuth account |
| 8 dialog harvest | b · B7 | small harvest (limit), output file non-empty |
| 8 voice-pack download files | a + b · B3 | `first-time` downloads; new: `LICENSE.txt`, `ATTRIBUTION.csv`, `.txt` per `.wav` |
| 9 every dropdown, slider, tab, pagination | b · B5 | UI sweep over every own window (only own nodes, never game UI) with frame-time watchdog |
| 9 light + dark theme | d | visual judgement |
| 9 window clamp at the screen edge | b · B5 | move a window off-screen, assert ≥ 50 % visible |
| 9 no freeze at any button | b · B2/B5 | watchdog during the sweep (safe buttons only; install/delete via B2/B3) |
| 10 DE renders, no missing keys | a + b · B5 | `LocAssert` unit tests; sweep collects visible texts and flags English fallbacks in a DE client |

**Result:** of 96 open boxes ≈ 14 already covered, ≈ 58 automatable in the game (b), ≈ 14 from the Mac (c),
≈ 16 stay manual fully or in part (hearing, Talk addon/clicks, duties/cutscenes/NG+, Linux, AMD, theme, Drive,
installed build).

### Build plan (order by value / effort)
| Step | What | Covers | Effort |
|---|---|---|---|
| B1 | `tools/release/preflight.sh` (Mac): both verifies, mirror, `engines.json` compare, zip list, release assets vs `RemoteUrls.json`, raw URLs at the tags, pipeline status; read-only, no push | 0, R0, R6 (≈ 12) | S |
| B2 | E2E telemetry: frame-time watchdog (idle / generation / install / switch), `nvidia-smi` VRAM + GPU load per engine, install-root size, `/health` device, underrun counter; into the report | R1, R2 FPS + VRAM, 3 freeze, 9 freeze (≈ 9) | M |
| B3 | engine options in the `engine` stage: Qwen temperature, Pocket 24/6 + steps, installer-log args, NDJSON `key`/language, mojibake scan, voice-pack files | R1, R2, 3, 4, 8 (≈ 9) | M |
| B4 | stage `wrapper-http`: cache miss/hit/bypass/shared, cancel refcount + GPU drop, VCM regenerate, repeat hit | R1, R2 (≈ 5) | M |
| B5 | stage `ui`: commands, config/Backend close-reopen, wizard sections + test connection, engine dropdown texts, changelog popup, window sweep (dropdowns, tabs, pagination, VCM tree), clamp, loc fallback | ⚡, R2, 1, 2, 3, 6, 9, 10 (≈ 20) | L |
| B6 | game voice extended: local qwen/pocket/moss, first line without index (2 NPCs), no-files snapshot, character field, alias assign/export; opt-in `game-voice-index` (abort/resume, Whisper, splits) | R2, R5 (≈ 9) | M-L |
| B7 | scenarios: narrator, phonetics, voice reassign, reload voices, local-save hit, volume, chat channels, passive bubble, harvest, AllTalk switch-off | R5, 5, 6, 7, 8 (≈ 12) | M |
| B8 | request field `wrapperVersion` (older release in `first-time`) + mixed-state checks | R3 (1, recurring) | M |
| B9 | game start + login from the Mac (scheduled task in the user session, XIVLauncher auto-login); character select needs a decision (below) | ⚡, 1, WORKLOG "Login + /ektest" (≈ 3) | M |
| B10 | `tools/e2e/wrapper-cli.sh`: the install path of `../echokrautts/tools/windows-test.sh` (own PC script: that one takes no `--language`/free args) with `--language fr/ja`, `pocket-6`, forced CPU backend, judges the NDJSON (no wrapper code change); done, see B10 — result | R1, R2, R4 (≈ 4) | S |

`/ektest` itself needs no trigger work: the request file already runs the same checks (`checks` stage); B9 only
removes the manual game start and login.

Decisions (user, 2026-11-25): **order B1 → B2 → B3 first**, then the rest by the table; **B9 dropped** (game start and
login stay manual); the `game-voice-index` part of B6 runs **only when listed** (`--stages … game-voice-index`).
B5's sweep presses only buttons without side effects.

### B1 — result (2026-11-25)
- `tools/release/preflight.sh [--no-verify] [--wrapper <checkout>]` (Mac, read-only, ~30 s with both verifies): versions
  (`<Version>` > GitHub tag; ELI and wrapper tags shown), changelog EN/DE exist + embedded, `Testing/checklist.json`
  version; `RemoteUrls.json` local vs GitLab `origin/main` vs GitHub `main` (mirror stale = FAIL, local not live = WARN
  with the diff); per file: version fields inside their URLs, game-voice URL keys (R6), every URL answers (live = FAIL,
  local-only = WARN; `echolinesUrl` is an API base, any answer < 500 counts; `KNOWN_BAD` turns tracked defects into WARN);
  wrapper `engines.json` == embedded, order + `minVersion` vs the released wrapper, `engines.json` at the tag; zip list
  has every tracked `src/*.py` and no dev files; GitLab pipeline of `main` for both repos (`glab api`, host from the
  git remote) + unpushed commits; both `verify.js`. Exit 1 on any FAIL.
- No game part, so no E2E run for this step. First run: 0 FAIL, 9 WARN — all expected before the push (local
  `RemoteUrls.json` names ELI-1.3.2.0/0.0.2.0, game-voice URLs missing, `ba4e7b2` unpushed) plus the known AllTalk
  release asset 404 (out of scope here, in `KNOWN_BAD`).
- Not in it: SonarQube (own scan sequence), Docker images (triggering a workflow is outward, needs the user's OK).

### B2 — result (2026-11-25)
- Code: `E2eFrameMonitor` (fed by `E2eRunService.Tick` every frame; windows via `Begin`/`MeasureAsync`), `NvidiaSmiGpuProbe`
  (`--query-gpu=memory.used,memory.total,utilization.gpu`, `CreateNoWindow`, sampled every 1 s), `E2eTelemetry` in
  `state.json` › `telemetry` and `e2e-<id>.json` › `Telemetry`. Ops: `install` (first-time), `stop` (the engine stage stops
  the first-time engine once to read the VRAM of the game alone = baseline; `nvidia-smi` has no per-process VRAM under
  WDDM), `switch:/start:/ready:<engine>` (`ready` covers lazy install + model download). Per engine: `/health` device,
  5 s idle vs generation+pipeline frames and GPU, VRAM peak over baseline, install-root size, `.venv*`, `f5_tts` in
  `.venv` before the engine, underruns (`IAudioPlaybackService.UnderrunCount` delta around the pipeline check).
- Limits: **freeze = a frame > 1000 ms** during an op → that part fails (`engine:<id>`, or `first-time` for `install`);
  hitch > 250 ms only counted. `f5_tts` in `.venv` before `f5` fails `engine:f5` unless the run started on f5.
- Game run `20261125-133923` (`start.sh --no-remote --test-wrapper --stages preconditions,backup,reset,first-time,engine
  --restore`, wrapper zip echokrautts `fd6a205`, 0 dirty): **13/0/0**, engine loop 6/0/0, restore through a reload, no crash.
  Baseline 5493 MiB (of 32607). Longest frame of any op 98 ms (`ready:xtts`); install 160 s / 82 ms.

  | engine | device | VRAM over baseline | GPU mean/max | fps idle → generation | max frame gen | root after |
  |---|---|---|---|---|---|---|
  | qwen-1.7b | cuda | 6164 MiB | 42 / 97 % | 17.0 → 16.8 | 319 ms | 18.4 GB |
  | qwen-0.6b | cuda | 4054 MiB | 48 / 97 % | 17.1 → 17.0 | 69 ms | 20.8 GB |
  | xtts | cuda | 4824 MiB | 25 / 53 % | 17.0 → 17.0 | 70 ms | 20.8 GB |
  | f5 | cuda | 1492 MiB | 23 / 86 % | 17.0 → 17.0 | 69 ms | 22.5 GB |
  | pocket | cpu | 69 MiB | 17 / 22 % | 17.0 → 16.6 | 71 ms | 23.2 GB |
  | moss | cpu | 69 MiB | 15 / 20 % | 17.0 → 16.2 | 86 ms | 24.2 GB |

  `f5_tts` absent from `.venv` until f5 (lazy install holds); venvs `.venv` + `.venv-qwen`; 0 underruns on every line.
- ⚠ **The game ran capped at ~17 fps** (window in the background → the game's own inactive frame limit), so the
  FPS comparison only says "no drop below that cap". For R2 the run must be repeated with the game in the foreground
  or the background limit off; the telemetry itself needs no change.
- Deployed DLL was built before a small refactor (`FirstTimeStage` uses `E2eFrameMonitor.MeasureAsync`, same
  measurement); verify green after it.

### B3 — result (2026-11-25)
- Code: `EngineStage.Options.cs` (partial of `EngineStage`), `E2eInstallerLog` (Mode lines, NDJSON, mojibake of
  `<installRoot>/EchokrautLocalInstaller/EchokrautLocalInstaller.log`), `E2eVoicePackFiles`. New parts of the `engine` stage:
  - `files:voice-pack` (before the loop): `LICENSE.txt`, `ATTRIBUTION.csv`, a `.txt` next to every `.wav` under `echokrautts/samples`.
  - `options:qwen-temperature` (after the first qwen engine passed): `SetQwenTemperature(0.7)` — the checkbox's call — →
    restart, ready, the new installer Mode line must say `qwenTemperature=0.7`, generation; then back to the original.
  - `options:pocket-layers` (after pocket passed): steps 8 on 24 layers → layers 6 (own steps, generation) → layers 24
    (steps 8 kept, generation) → original steps; each step checks `pocketLayers`/`pocketDecodeSteps` in the Mode line.
  - `files:installer-log` (after the loop): no mojibake (`Ã`, `â€`, U+FFFD), no `qwenXvecOnly`, a Mode line per passed
    engine, NDJSON with `key`, for `language=de` at least one keyed message with a German letter.
- Evidence is the installer's `Mode:` line (`EchokrauTtsServeArgs.Describe`), not the bootstrap command line: the installer does
  not log the latter on Windows, and `BootstrapArguments` maps the fields 1:1 (unit-tested). A step that fails puts the original
  values back and restarts once (first run: later engines ran on 0.7 / steps 8). Originals sit in marks (`E2eOptions` only
  addresses top-level `Configuration` properties); a reload mid-part restores them at the next stage start and skips the part.
  File parts can only fail the stage; it passes only through `engine:*` parts.
- Run `20261125-152750` (normal dev build): 14/3/0. Both option parts **failed correctly**: GitHub `main` still names
  **ELI-1.3.1.0**, whose Mode line has no `qwenTemperature`/`pocket*` although the plugin passes them (`[9]=0.7 [10]=8 [11]=24`):
  the old installer drops the options. Mixed state "plugin 0.19.3.3 + ELI-1.3.1.0" = options silently without effect.
- Run `20261125-154801` (same command, DLL built with `RemoteJsonUrl` pointing at a missing file → embedded `RemoteUrls.json`,
  ELI-1.3.2.0; source reverted right after the build, normal build deployed after the run): **17/0/0**, engine loop 6/0/0.
  Qwen 0.7: restart + ready 36 s, first bytes 0.53 s; back to 0.9 36 s. Pocket: steps 8 40 s; layers 6 44 s, first bytes
  1.28 s (24 layers: 2.2 s); layers 24 38 s, steps 8 kept; steps back to 4 36 s. Voice pack 260 `.wav`, all with `.txt`.
  Installer log 1749 lines, 14 Mode lines, NDJSON 397 events / 150 with key / 29 German, mojibake 0.
- Not in it: the console window (d), "sounds calmer" (hearing, d), the dropdown's step options (B5), `fr`/`ja` (B10).

### B4 — result (2026-11-25)
- Code: `Stages/WrapperHttpStage.cs` + `WrapperHttpStage.CancelAndVcm.cs` (stage `wrapper-http`, after `engine`; dependency like
  `scenarios`, now shared as `EngineStage.ActiveEngineUnmet`), `E2eWrapperHttp.cs` (`IE2eWrapperHttp` / `EchokrauTtsWrapperHttp`: plain
  HTTP against the running engine with the plugin's own `/tts` body; `IE2eWrapperLog` over the installer log), `E2eInstallerLog`
  `TtsStarts` / `GenerationOf` / `TtsDone`. Parts:
  - `cache`: miss → hit (same bytes) → `no_cache` bypass → hit (the bypass run's bytes), two at once → miss + shared (same bytes);
    `tts request start` counted after each step; `/health` counters must move by exactly hits 2 / shared 1 / misses 2 / bypassed 1;
    `GET /` must contain `no_cache`.
  - `cancel`: pair one (3 sentences) — cancel the first client, the second must get the clip to the end (generation `done`,
    `tts request done`); pair two (10 sentences) — cancel both → `cancelled`, no `tts request done`, `/health` running 0, GPU mean
    lower after than during (judged only on cuda with ≥ 20 % load); the same text again → miss (cancelled at once).
  - `vcm-regenerate`: throwaway `Echokraut Selftest` character + clip (in `CreatedIds` first), `GenerateForVoiceClip` twice →
    2 starts, bypassed +2, no hit; clip, WAV and character removed.
  - `repeat-hit`: `IBackendService.GenerateVoice` without `NoCache` (the path of a dialogue line) twice → 1 start, hits +1, second
    first bytes ≤ 300 ms.
  Texts carry the run id. Skips on a wrapper without `response_cache` in `/health`.
- Runs (`start.sh --no-remote --test-wrapper --engines xtts --stages preconditions,backup,reset,first-time,engine,wrapper-http --restore`,
  wrapper zip echokrautts `fd6a205`, 0 dirty; `--engines xtts` so the active engine is a CUDA one and no `options:*` part needs
  ELI-1.3.2.0, so the normal dev build was enough):
  - `20261125-170203` **15/0/0** — first version of `cancel` (only "still running after one cancel"): GPU 47 → 19 %.
  - `20261125-171044` 13/2/0 — `cancel` with "runs to the end" on 10 sentences: the remaining client had 7.8 MB after 60 s and was
    still receiving. One test sentence is ~23 s of XTTS audio; the stage waited too short, the wrapper was right.
  - `20261125-171901` **15/0/0** — pair one on 3 sentences, own `RunToEndWait` (3 min): first client 42 kB, second to the end
    2.7 MB, generation `done`; both cancelled → `cancelled`, GPU 41 → 19 %; again miss. `repeat-hit` 359 ms → 0 ms. Stage 101 s
    (cache 43 s, cancel 30 s, vcm 19 s, repeat 9 s). Restore through a reload, cleanup found nothing left.
- Test plan: R1 response cache and R1 cancel refcount ticked; R2 VCM regenerate got a sub-item (the dialogue line itself still needs
  a Talk dialog, d). Not in it: R2 fast clicking (cancel on advance through the plugin) — the plugin side is `scenarios:epoch-cancel`,
  the GPU drop after it is not measured yet.


### B5 — result (2026-11-25)
- Code: stage `ui` (`Stages/UiStage.cs` + `UiStage.Sweep.cs`, after `scenarios`, needs `first-time`, E2E config only), `E2eUiDriver`
  (probes on the framework thread), `E2eUiChecks` (judgements, unit-tested). The UI side answers through `IOwnedWindowsService.Probe`
  → `NativeWindowManager.OnProbe`: snapshot (open/alive, screen rect, visible texts read from the ATK tree by `Windows/E2eUi/AtkTextCollector`),
  open/close/move, and the steps each window declares itself (`IE2eSweepable`: tabs, dropdown open/close without a pick, next/prev page,
  first VCM row → detail, wizard Back + "Test connection", changelog "I've read it", the Backend tab's collapsible EK local section).
  Helpers: `IChangelogService.PreviousVersion()`, `TtsEngineAvailability.ForConfig` (the dropdown's entries, shared with the builder),
  `Loc.Keys`. `first-time` reports `first-time:auto-open` (the wizard opened by itself after the reset reload).
- Parts: `commands`, `alias`, `backend-reopen`, `engine-dropdown`, `wizard`, `sweep` (frame watchdog op `ui-sweep`), `clamp`, `loc`,
  `changelog` (two self-reloads, last part; marks `ui-changelog*`, the engine is waited for after each reload). Details:
  `docs/claude/services/e2e-run.md` › Own windows (B5).
- Runs (`start.sh --no-remote --test-wrapper --engines xtts --stages preconditions,backup,reset,first-time,engine,ui --restore`, wrapper zip
  echokrautts `fd6a205`, DE client):
  - `20261125-175241` 14/7/0 — the collector read nothing inside a `ScrollingListNode`: KamiToolKit attaches a component's children as
    **siblings** of the component's root node (`NodeBase.Linking`: `AsLastChild` → `AfterAllSiblings` of `UldManager.RootNode`), the walk
    only went below the root. Also the plugin version carries its "v" (`v0.19.3.3`), the stage compared "vv0.19.3.3".
  - `20261125-180113` 15/6/0 — texts read; the Backend tab's sections are **collapsible** (EK local closed by default, its content really
    invisible), and the wizard's "EchokrauTTS base URL" is only the input's placeholder (hidden while a URL is set). `changelog` passed.
  - `20261125-180742` **18/3/0** — `ui` 7/2: commands, backend-reopen (25 labels identical, still expanded), engine-dropdown (6 entries in
    catalog order, none cut off), wizard, sweep (56 steps, 421 frames, max 116 ms, 0 hitches), clamp (4 windows), changelog pass.
    The two failures are real findings in the plugin (WORKLOG): **`/echokraut` is not registered** (tester checklist `inst-cmd` and
    `Services/CLAUDE.md` name it) and **4 English texts in the DE client**: the log tabs "General"/"Dialogue"/"Bubbles" (`LogTabs` labels
    without `Loc.S`) and "Done" (raw `CurrentInstallStatus` in the wizard's install bar). Restore through a reload, cleanup found nothing.
- Test plan: R2 changelog, 1 fresh config → wizard, 1 changelog after a bump, 2 step 1 sections, 2 summary + test connection, 2 finish,
  9 clamp ticked; sub-items with evidence for ⚡ `/ek` (the item names the wrong command: `/ek` opens the VCM), ⚡ Backend close + reopen
  (EK only), R2 engine list (DE only), 1 commands (alias missing), 2 engine selector (highlight not judged), 3 mode/engine dropdown/options,
  9 dropdowns (no pick), 9 tabs/pagination (no slider, no 100+ list), 9 no freeze, 10 DE (4 findings).
- Not in it: AllTalk sections (switching the engine has side effects, B7), picking dropdown entries, sliders, lists with 100+ rows (fresh
  E2E DB), the VCM tree scroll, EN/JA client texts (needs a client language switch), the dialogue toolbar dropdown (needs a Talk addon, d).

### B6 — result (2026-11-25)
- Code: stages `game-voice` (after `wrapper-http`) and opt-in `game-voice-index`, per-engine part `game-voice:engine-<id>` in the
  engine loop, `E2eAudioFileWatch` (no audio file on disk: snapshot + `FileSystemWatcher` over install root, `pluginConfigs`,
  `%TEMP%`), `E2eGameVoiceChecks` (availability from `/health`, alias export, matching report, split suggestions), service
  additions `SampleVoicedNpcsAsync(lang, count, unindexedOnly)` + event `PairRanked` (rank timings),
  `GameVoiceReferenceService.HarvestDirectoryOf`. Details: `docs/claude/services/e2e-run.md` › Game voice (B6).
- Decision: an engine is judged by its `/health inline_reference`, not by a fixed "MOSS = not supported": the part fails when
  availability and `/health` disagree or a line carries the game voice on an unsupported server.
- Runs (KamiToolKit `ed99707` / Echotools `5a7e66b` from `7434cd9`, wrapper zip echokrautts `fd6a205`, DLL with the embedded
  `RemoteUrls.json` → ELI-1.3.2.0, DE client):
  - `20261125-194150` **24/0/0** (`--engines qwen-0.6b,pocket,moss,xtts --stages …,engine,game-voice --restore`): every engine
    reports `inline_reference` true → Available, Alphinaud speaks with the game voice on all four (first audio qwen 481, pocket 508,
    moss 844, xtts 558 ms; ranked once on qwen, 48 s with Whisper). **MOSS is the ONNX runtime here and supports inline
    references** — the test plan's "MOSS: not supported" only holds for MOSS on PyTorch (not covered). `first-line`: Alisaie 987 ms
    (quick 860 ms, total 8.3 s), Wuk Lamat 1346 ms (quick 1225 ms, total 6.8 s), both GameVoice; `character-field` `-` → VoicePack,
    token / empty → GameVoice; `alias` disabled → VoicePack, assigned → GameVoice, no re-rank, export 27 entries. No audio file in
    any part (314 audio files before, none new). Options B3 pass again with ELI-1.3.2.0.
  - `20261125-203323` **22/0/0** (`--engines xtts --stages …,engine,game-voice,game-voice-index --restore`): game-voice parts as
    above; `abort-resume` (5 + 5 pairs, current 3 → 8), `whisper` (3 pairs with the Whisper flag; two had no usable clip, so only one
    was really checked), `full` (673 tokens, 664 pairs, 550 with a reference, 655 s incl. detection, max frame 70 ms, report DE 354
    unmatched / 3 ambiguous), `splits` (Hydaelyn 06000, Y'shtola 06100, none for Alphinaud/Thancred; new: Hythlodaeus 06004,
    "Krieger der Dunkelheit" 05000 — that name arrives with a `[p]` placeholder), `unseen-npc` (Thancred, never met, 420 ms, no rank).
- Findings (WORKLOG): `GameVoiceAliases.json` has `pippin` several times (36 entries, 26 tokens); split suggestion names keep a `[p]`
  placeholder; after a fast batch the Backend-tab count still says 667 outdated (fast index counts as outdated while Whisper is
  available — intended per the docs, the hint text may need it); xtts first generation after a fresh start 17-18 s in both runs.
- Deploy: after the KTK reorg the dev folder needs the whole dependency set (`KamiToolKit`, `Echotools.UI`, `Echotools.Logging`,
  `EchoScd`, `Echokraut` last) — copying only `Echokraut.dll` ended in a `TypeLoadException` and the request was never read.
- Batch ETA lines now restart per phase (ranking, then detection; one clock in the service) — unit-tested, not yet in a game run.
- Test plan: R5 first line of a new NPC ticked; sub-items with evidence for R2 phase 6, after the batch, voice-change detection,
  assign + export, character field, R5 game voice / no WAV / Qwen-Pocket / MOSS.
- Not in it: a full Whisper batch (1-2 h), the Backend-tab button + hint after a reload, the windows themselves (assign walk, NPC edit
  field: service/helper calls), MOSS on PyTorch, the epoch by story progress (needs NG+).

### B7 — result (2026-11-25)
- Code: ten new sub-scenarios in `E2e/Scenarios/` (phonetics, voice-reassign, reload-voices, narrator, chat-channels, local-save,
  volume, bubble, harvest, alltalk-off), `E2eChatLines` (shared by chat-fc / chat-channels / local-save), `IE2eGameSound` +
  `gamecfg:SoundVoice=<n>` in `CreatedIds` (cleanup restores the game slider), `IAudioPlaybackService.ChannelVolume` (BASS float
  readback). Details: `docs/claude/services/e2e-run.md` › Scenarios (B7).
- Runs (`--engines xtts --stages preconditions,backup,reset,first-time,checks:local,engine,scenarios --restore`, DE client; the
  stage list needs `checks:local`, `scenarios` skips without it):
  - `20261125-220607` 22/5/1, `20261125-221740` 24/3/1 — scenario bugs found and fixed in between: voice-reassign must pick from
    `npc.Voices` and send as Talk (VoiceTest has no character cache: every line a new speaker with a random voice), volume must wait
    for the BASS stream (the queue marks the entry playing before `PlayStream`/`SetVolume`), harvest filter `None` (event quests: 0
    dialogs in DE).
  - `20261125-223048` **25/2/1**: phonetics ('Stadt' → 'Zebrafant' → 'Kolibrimaus' → itself), voice-reassign (survives "reload
    voices"), reload-voices (260 = 260, same keys), chat-fc, chat-channels (10 channels on queued / off not), local-save (second line
    from the WAV), volume (channel 1 at 100, 0.5 at 50), harvest (non-quest, 27 247 rows in 143 s), alltalk-off (EK stopped, port
    closed, VRAM 9430 → 5705 MiB, back and ready); bubble skipped (no bubble at the standing spot); **narrator fails on a real
    finding**: pick + self-reload pass, but the voice pack (260 WAVs) has no `Narrator.wav`, so no `IsDefault` narrator exists and
    unpick / switched off have nothing to fall back to (WORKLOG).
- Not in it: bubble with a bubble actually spoken (needs a spot with NPC bubbles), chat "only nearby players audible", AllTalk as a
  real second engine, the voice-selection search and the windows themselves (service calls), hearing.

### B8 — result (2026-11-26)
- Code: request field `wrapperVersion` (`start.sh --wrapper-version <tag>`, a release tag, excludes `testWrapper`) →
  `first-time` resolves it through `IEchokrauTtsInstanceService.SelectWrapperReleaseAsync` (GitHub `releases/tags/<tag>`,
  `GitHubReleaseParser.ReleaseByTagUrl`; tag and zip stay paired in `_foundRelease`) before anything is touched, installs through the
  normal `Install` and fails when another tag got recorded. Stage `wrapper-mixed` (`Stages/MixedWrapperStage.cs`, right after
  `first-time`, skipped without the field): `installed`, `greyed`, `update-offered`, `update`, `unlocked`; judgements in
  `E2eMixedWrapperChecks` (blocked = catalog `minVersion` above the tag, own `Version` compare). The clicks are the window's own
  handlers through `OwnedWindowActions` (`action:wrapper-update`, `action:engine-pick:<id>`), kept out of the B5 sweep
  (`E2eSweepSteps.Action`). Backend-tab navigation shared with `ui` (`E2eBackendTab`). Details: `docs/claude/services/e2e-run-wrapper.md` › Mixed wrapper state (B8).
- Decision: expectations come from the catalog, never from release names — the stage serves the mixed state of every future release
  (a release that adds no engine skips `greyed`/`unlocked` and still checks the update).
- Found on the way: `Util.GetHostPlatform()` in the `EchokrauTtsInstanceService` ctor crashes the test host outside Dalamud; the
  platform is now injectable (`isWindows`).
- Checked beforehand: wrapper 0.0.1.9 accepts the newer installer's start options (`parse_known_args`), so ELI-1.3.2.0 + 0.0.1.9 starts.
- Run `20261126-093222` (`start.sh --no-remote --wrapper-version 0.0.1.9 --engines pocket,qwen-0.6b,xtts --stages preconditions,backup,
  reset,first-time,wrapper-mixed,checks:local,engine --restore`, DE client, DLL built against the embedded `RemoteUrls.json` (ELI-1.3.2.0 /
  0.0.2.0; GitHub `main` still names 0.0.1.9, whose `engines.json` has no Qwen/Pocket) — source reverted right after the build, normal
  build deployed after the run): **22/0/0**. first-time installed the real 0.0.1.9 release (402 s incl. all three engines of the old
  bootstrap, longest frame 157 ms); `greyed`: qwen-1.7b, qwen-0.6b, pocket blocked with "(ab Wrapper-Version 0.0.2.0)", rendered
  dropdown = `ForConfig`, every pick refused; `update-offered`: "Wrapper: 0.0.1.9 (neueste: 0.0.2.0)"; `update`: 52 s, longest frame
  100 ms, 260 voices kept, installer `updateechokrautts` fetched `/0.0.2.0/`; `unlocked`: pocket picked → ready 48 s, then xtts again.
  Engine loop on the updated install 7/0/0 (pocket 2.1 s / qwen-0.6b 0.6 s / xtts 16.8 s first bytes, both option parts pass),
  restore through a reload.
- Test plan: R3 "Plugin 0.19.3.3 + Wrapper 0.0.1.9" ticked.
- Not in it: the mixed state with GitHub `main` as it is today (plugin 0.19.3.3 + `RemoteUrls` 0.0.1.9 + ELI-1.3.1.0) — that is the
  pre-push state, covered by the test plan's own R3 note (both values in one push).


### B10 — result (2026-11-26)
- Code: `tools/e2e/wrapper-cli.sh [--fresh] [qwen-cpu|pocket-ja|pocket6-fr …]` (Mac) → `wrapper-cli.ps1` (PC, one case per call) →
  `wrapper_cli_judge.py` (Mac). Same install path as `../echokrautts/tools/windows-test.sh` (working tree copied by tar, then
  `bootstrap\install_win.ps1 --start`), but its own script: `windows-test.sh` takes no `--language` and no free bootstrap args, and
  `windows-test.ps1` posts `/tts` with `language=de` fixed. Own test folder on E: (never the E2E install root), uv cache and uv Python
  inside it (C: stays untouched), port 8799. The PC side only records (NDJSON, stderr, `result.json`: ready, exit code, `/health`, `/tts`,
  model files new or changed, `.venv*` sizes before/after); the judge matches every keyed text against the wrapper's own
  `src/i18n.py` templates in the expected language (catalog imported from the sibling checkout, nothing duplicated).
- Cases: `qwen-cpu` (`--tts-backend qwen-0.6b --gpu-backend cpu --language de`), `pocket-ja` (`--tts-backend pocket --language ja`),
  `pocket6-fr` (`--tts-backend pocket --pocket-layers 6 --language fr`, then `/tts` in French).
- Run `wrapper-cli-20261126-101029` (wrapper `fd6a205`, reused install): **19 PASS / 1 FAIL** (+ INFO lines):
  - `qwen-cpu`: stops after 12 s with the `boot.unsupported_device` text ("… erkannt wurde 'cpu'. Wähle XTTS, F5 oder MOSS."), exit 1, no
    `deps`/`model` step started, no model file, no Qwen venv. **FAIL:** the fatal error has **no `key`** — bootstrap's
    `FatalError(i18n.t(...))` goes out as `str(exc)`, which drops the catalog key; holds for every `FatalError` text (also the JA case's
    second fatal line `boot.model_failed`).
  - `pocket-ja`: 12 keyed texts, all Japanese; stops with `model.download_failed` in Japanese, exit 1, no model file written. Findings
    (INFO): the reason inside the Japanese frame is **German** (`pocket_backend.language_folder` raises a fixed German `ValueError`),
    and the language is refused only in step `model` — **step `deps` installed the 6.3 GB `.venv` first**.
  - `pocket6-fr`: `pocket.layers_fallback` in French ("… pas de modèle à 6 couches pour 'fr', utilisation de 24 couches"), ready after
    36 s, `/health` pocket on cpu, `/tts` fr 3.5 s audio in 5.1 s; 12 keyed texts, all French. Download progress lines
    (`Pocket french_24l: …`) carry no key (dev-style progress, not judged). The bootstrap exits 2 after a clean `shutdown` event (not judged).
  - No mojibake, every stdout line JSON, in all three.
- Test plan: R1 pocket-6 + `--language fr` ticked; R4 "no matching GPU backend + Qwen" ticked. Not ticked: R1 NDJSON language (fr/ja
  texts pass, but fatal lines have no `key`), R2 Japanese client + Pocket (reason German, venv downloaded before the refusal); both got a
  sub-item with the evidence. Wrapper findings → WORKLOG (no wrapper code changed here).
- Follow-up (2026-11-26, wrapper fix in echokrautts): fatal texts keep their `key` (`FatalError.message`), Pocket refuses an unknown
  language in `step_deps` with `boot.unsupported_language` (before any install), and `/shutdown` stops uvicorn through its own SIGINT
  handler (`signal.raise_signal`; `os.kill` on Windows was `TerminateProcess` → exit 2). Judge: `pocket_ja` expects
  `boot.unsupported_language` + no deps/model step + no venv change; `pocket6_fr` expects exit 0 after `shutdown`.
  Run `wrapper-cli-20261126-102448`: **20 PASS / 0 FAIL**. R1 NDJSON language ticked; R2 JA+Pocket stays open for the in-game JA client.
