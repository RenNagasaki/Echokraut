# In-game self-test

Status: implemented 2026-11-18 (v0.19.3.3). Code: `Echokraut/Services/SelfTest/`.

## Goal
Check the plugin inside the running game without quest progress — also on a Free Trial
character — so a release can be tested quickly, by hand or automated.

## What Dalamud offers
`ISelfTestRegistry` (plugin service, injected into `Plugin`'s ctor) takes `ISelfTestStep`s
(`Name`, `RunStep()` once per frame → `Waiting`/`Pass`/`Fail`, `CleanUp()`). They show up in
Dalamud's self-test window next to Dalamud's own steps. `RunStep` runs inside that ImGui window,
so drawing there is allowed. No third state: "skipped" shows as Pass in the window.

## Design
- `SelfTestCheck` (abstract, plain async, no Dalamud UI) — one per check. Same instance in two runners:
  - `DalamudSelfTestStep` (adapter for the window, draws the instruction of interactive checks),
  - `SelfTestService.RunAsync` behind `/ektest` (`/ektest all` = with the interactive ones):
    sequential, chat summary, JSON report `pluginConfigs/Echokraut/selftest/selftest-latest.json`
    plus one file per run (newest 20 kept).
- `SelfTestRunner.RunCheckAsync`: per-check timeout, exception → Fail, outer cancel → "Cancelled."
- `SelfTestCatalog.Build`: the list in run order. New checks go there.
- Details are English diagnostics (log + report); names and instructions are localized.

## Checks
| Id | What | Skipped when |
|---|---|---|
| `player` | logged in, player name + content id cached (framework thread) | — |
| `game-data` | 20 dialogue sheets + ENpcResident names in the client language | — |
| `database` | SQLite answers (clip count, muted dialogues) | — |
| `story-progress` | `ReachedPatchKey` known within 10 s | — |
| `backend` | fresh reachability probe + `CheckReady` | None mode |
| `voices` | enabled voices, default voice | None mode |
| `generation` | `IBackendService.GenerateVoice` directly, first 16 KB of audio, `NoCache` | None mode |
| `pipeline` | `IVoiceMessageProcessor.ProcessSelfTestSpeechAsync` → queue → BASS playback → DB log | None mode, plugin off |
| `sentence-continuation` | index builds and is not empty (`JoinableBoxCount`) | option off |
| `game-voice` | availability `Available` + `SelfCheckAsync` decodes one clip | opt-in off, AllTalk |
| `talk` / `bubble` / `chat` (interactive) | a real line of that source plays (queue polled) | that voicing off |

## Side effects — why the test leaves nothing behind
- `ProcessSelfTestSpeechAsync` is the same path as `ProcessSpeechAsync` (shared `ProcessCoreAsync`),
  with source `VoiceTest` and `VoiceMessage.IsSelfTest = true`:
  - never offered to Echolines (explicit `!selfTest` guard; `VoiceTest` is not reportable anyway),
  - `AudioPlaybackService.OnSourceEnded` does not save it → no WAV, no Drive upload, no generation row.
- The pipeline check deletes the throwaway character `Echokraut Selftest` and its voice-clip rows
  afterwards (cascade covers contexts). It never deletes a character with another name.
- **Threading**: the check starts the pipeline via `IFramework.RunOnFrameworkThread`, like the addon
  helpers. `ProcessCoreAsync` reads `ObjectTable.LocalPlayer`, which throws "Not on main thread!" from
  the thread pool (first in-game run 2026-11-21, `pipeline` was red for that reason only). An exception
  in the pipeline comes back as `SelfTestSpeech.Error` and is shown in the result, not guessed at.
- Game audio: `SelfCheckAsync` lives in `GameVoiceReferenceService` (still the only reader), runs only
  after the opt-in, decodes in memory and drops the PCM at once — only its length leaves the method.

## Not covered (and why)
- **BattleTalk / cutscene choices**: need a duty, FATE or story cutscene — not reliably reachable
  without progress. Stays in the manual checklist.
- **Lip sync, 3D position, auto-advance, cancel on advance**: need a real Talk addon; the interactive
  `talk` step plays a line but does not judge these. Manual checklist.
- **The addon hooks themselves** are only proven by the interactive steps.
- **Driving the character** (walking, opening quests): that is botting (ToS, ban risk) — deliberately not done.

## Automation: the in-game E2E run (built 2026-11-22)
The checks above also run unattended from the Mac as part of the E2E run (`IE2eRunService`, code
`Echokraut/Services/SelfTest/E2e/`, plan and per-step results `docs/plans/ingame-e2e.md`). **Dev loads only**
(`IsDev`); the game must already be running and logged in — logging in and sending `/ektest` stay manual.

- Scripts (`tools/e2e/`, run from the repo root on the Mac, ssh to the gaming PC):
  - `push-test-wrapper.sh [echokrautts-checkout]` — builds a wrapper zip from the local `echokrautts` working tree
    (+ its `engines.json`) and places it where a dev load's test install looks; then `start.sh --test-wrapper` lets
    `first-time` install it instead of the release (testing a wrapper before its release).
  - `start.sh [--stages a,b] [--engines x,y] [--root 'E:\EkE2E'] [--no-remote] [--restore] [--test-wrapper]` — PC-side
    preconditions (`preconditions.ps1`), then `request.json` into `pluginConfigs/Echokraut-e2e/`; prints the run id.
    The remote URL comes from `~/.config/echokraut-e2e.json` (never from the repo); logs and reports show it as `<remote>`.
  - `watch.sh <id>` — follows the `EKTEST <id> …` lines in `dalamud.log` (one line per event, suits the Monitor
    tool), ends on `EKTEST <id> done`. `fetch-report.sh <id>` copies `e2e-<id>.json` to `.claude/e2e-reports/`.
  - `restore.sh` — real config back: `--live` (game keeps running, request with only the stage `restore`), without
    a flag `restore.ps1` with the game closed (fallback; also the Start-menu entry "Echokraut E2E Restore").
- Stages in run order (`E2eCatalog`): `preconditions`, `checks` (the automatic checks of `SelfTestCatalog` on the
  real config), `chat-hook`, **destructive from here** `backup` (reload), `reset` (wipe, fresh config + DB),
  `first-time` (wizard via `FirstTimeWizardController`, fresh EchokrauTTS install under the root), `checks:local`,
  `engine` (`engine:<id>` per sub-engine), `scenarios` (`scenarios:<id>`: continuation, queue-order, epoch-cancel,
  mute, chat-fc), `remote` (`remote:switch|backend|generation|pipeline|game-voice|game-voice-generation`),
  `checks:remote` (all automatic checks again on the remote), `cleanup` (**always**, also when `--stages` leaves it
  out and after an abort: leftover `createdIds` — WAV only under the root, clip rows, characters — and every
  `Echokraut Selftest` character removed, `touchedOptions` restored, game-voice opt-in off; on the real config it
  only reports) and the opt-in `restore` (only when listed, e.g. `start.sh --stages … --restore`; also runs after an
  aborting stage).
- After `reset` the plugin runs on the E2E config until a restore — always end a destructive run with `--restore`
  or `restore.sh`. A run that ends on the E2E config says so: `EKTEST <id> run warn …` right before the `done`
  line, plus a chat line.
- A VPN does not reduce the ban risk of running Dalamud (bans are per account; same PC and hardware); a
  separate account is the actual separation.
