# EchokrauTTS as an alternative TTS backend

Add **EchokrauTTS** (a self-contained F5-TTS wrapper) as a second TTS engine alongside AllTalk:
installable + runnable locally, usable remotely, selectable in the First-Time wizard and the
Backend settings window, with automatic voice-folder copying when the user switches engines.

**Version target:** bump `0.19.0.1` → **`0.19.1.0`** (significant user-visible feature).
**Branch:** `feature/echokrautts-backend` (touches many files).

---

## Decisions already made (user)

1. **Local install runs via the extended `EchokrautLocalInstaller`** (consistent install path with
   AllTalk), NOT driven directly from the plugin.
2. **Plan doc + plan-reviewer first**, then phased implementation (build+test+commit per phase).
3. **Voice-copy on engine switch**: copy the *previous* engine's voices into the *newly selected*
   engine's folder, **overwriting same-named files, keeping extras** in the target. Both directions.
4. **Shared install root**: the install folder stays the same; under it live `alltalk_tts\` AND
   `echokrautts\`.

---

## Goals & Non-Goals

### Goals
- Add `TTSBackends.EchokrauTTS`; let the user pick the engine in First-Time + Backend UI.
- Per-engine config (base URL, Local/Remote/None mode, install state) for EchokrauTTS.
- Local install/start/stop of the EchokrauTTS wrapper via an extended `EchokrautLocalInstaller`.
- Remote EchokrauTTS support (point at a remote wrapper URL).
- Detect which engine(s) are installed under the shared root.
- On engine switch, copy voices between `{root}\alltalk_tts\voices` and `{root}\echokrautts\samples`
  (overwrite same-named, keep extras), only when the source folder exists.
- `EchokrauTtsBackend : ITTSBackend` implementing all five interface methods.

### Non-Goals
- Changing AllTalk's install/run flow (only additive).
- Streaming-as-you-generate for EchokrauTTS in v1 (buffer-then-play is acceptable; see Risks).
- Multi-language concurrent generation on a single EchokrauTTS server (server is single-language
  per run — see "Language constraint"). v1 pins EchokrauTTS to the client language.
- Auto-emitting `.txt` reference-transcript sidecars for F5-TTS (quality enhancement; future).
- Modifying the `Echokrautts` wrapper repo itself (it is a separate repo; we consume its releases).

---

## Research findings

### EchokrauTTS wrapper (`../Echokrautts/wrapper/`)
- **Python / F5-TTS**, self-contained via `uv` (no system Python, no conda). Entry:
  `bootstrap/install_win.ps1` → `bootstrap/bootstrap.py` (6 idempotent steps; markers in `.state/`).
- Runs a **FastAPI server**, default `127.0.0.1:8765`.
- **Endpoints**: `POST /tts` (streaming **raw PCM s16le, 24000 Hz, mono**; metadata in headers
  `X-Job-Id`, `X-Sample-Rate`, `X-Channels`, `X-Sample-Format`), `GET /samples`
  (`{samples:[names]}`), `POST /cancel/{job_id}`, `GET /jobs/{job_id}`, `GET /health`
  (`{status,backend,device,workers,queue,language}`), `POST /shutdown`. Optional Bearer `api_key`.
- **Voices** live in `samples/` (flat; `.wav`/`.flac`/`.mp3` + optional `<basename>.txt` reference
  transcript). NOT `voices/`.
- **stdout NDJSON** protocol: `starting` / `progress{index,total,step,message,percent,done}` /
  `ready{host,port,backend,workers}` / `log` / `error{fatal}` / `shutdown`.
- **Parent-PID watchdog** + Windows job object → server self-terminates if the host dies.
- **CLI** (bootstrap): `--start`, `--host`, `--port`, `--language`, `--api-key`, `--parent-pid`.

### Language constraint (CRITICAL)
`src/server.py:152-158`: the server **loads exactly one language model at startup**; a `/tts`
request whose `language` differs from `config.language` is **rejected** (HTTP error). `config.py:45`
default `language = "de"`. Switching the active language = **restart the process with `--language`**
(bootstrap step note + SPEC §14.3). All four language checkpoints are pre-downloaded (bootstrap
step 5), so a restart is model-load-only (no re-download), but still a process restart.

→ **v1 behavior**: the local EchokrauTTS server is pinned to the **client language**
(`IClientState.ClientLanguage`). Lines whose resolved language differs are handled per "Language
handling" below. (AllTalk has no such constraint — it accepts a per-request language.)

### AllTalk plugin stack (for parity)
- `Enums/AlltalkInstanceType.cs`: `Local | Remote | None`.
- `DataClasses/AlltalkData.cs`: `BaseUrl` (default `http://127.0.0.1:7851`), endpoint path
  properties, `LocalInstall`, `AutoStartLocalInstance`, `LocalInstallPath` (default
  `C:\alltalk_tts`), `InstanceType`, `HasLiveGeneration => InstanceType != None`. Legacy bool
  fields + idempotent `MigrateLegacyInstanceTypeFields()`.
- `Services/IAlltalkInstanceService` + `AlltalkInstanceService`: `Install()`, `StartInstance()`,
  `StopInstance(eventId)`, `InstallCustomData(...)`. Launches `EchokrautLocalInstaller.exe` with a
  mode arg + params; polls `{root}\EchokrautLocalInstaller\Ready.txt` (written by the installer when
  AllTalk prints "Server Ready"); fires `OnInstanceReady`. Exposes `Installing`,
  `CurrentInstallStatus`, `CurrentInstallProgress`, `InstanceRunning`, `InstanceStarting`,
  `InstanceStopping`.
- `EchokrautLocalInstaller/` (standalone project in this repo, **downloaded at runtime** from
  `RemoteUrls.InstallerUrl`, currently release tag `ELI-1.0.0.1`): `Program.cs` arg modes
  `install` / `start` / `installcustomdata`; `Constants.ALLTALKFOLDERNAME = "alltalk_tts"`; creates
  `{root}\alltalk_tts\...\voices\`. `GoogleDriveHelper` for custom downloads.
- `Backend/ITTSBackend.cs` (5 methods): `GetAvailableVoices`, `GenerateAudioStreamFromVoice`,
  `CheckReady`, `ReloadService`, `StopGenerating`. `AlltalkBackend` builds absolute URIs from
  `Alltalk.BaseUrl`, uses two static long-lived `HttpClient`s (general 5 s; streaming 2 s keep-alive).
- `Services/BackendService.RefreshBackend()`: returns unless `BackendSelection == Alltalk`; for
  Local connects only when `InstanceRunning`, for Remote when reachable; constructs `AlltalkBackend`
  then `MapVoices`. Subscribes to `OnInstanceReady` and `DatabaseWiped`.
- `Enums/TTSBackends.cs`: currently only `Alltalk`; `Configuration.BackendSelection` persists it.
- UI: `NativeFirstTimeWindow` (Step 0 mode buttons Local/Remote/None → set `Alltalk.InstanceType`;
  Step 1 builds Local/Remote/None sections via `NativeAlltalkBuilder`; install progress bar). 
  `NativeConfigWindow.BuildBackendPanel()` (`_backendDropDown` currently `["Alltalk"]` with deferred
  selection; mode switcher buttons; collapsible Local/Advanced/Remote/None sections via
  `NativeAlltalkBuilder` + `CreateTrackedCollapsibleSection`). `NativeAlltalkBuilder` exposes
  `BuildLocalInstance` / `BuildRemoteInstance` + `ValidateInstallPath`.
- Auto-start gate (`Plugin.cs`): `!FirstTime && IsLoggedIn && Alltalk.LocalInstall &&
  InstanceType==Local && AutoStartLocalInstance` → `StartInstance()` + `RefreshBackend()`.
- Localization: `Loc.S("English")` + `Localization/Loc.cs` dictionary with DE/FR/JP.

---

## Architecture

### Shared install root  *(DECIDED: dedicated field)*
Introduce a dedicated **`Configuration.TtsInstallRoot`** (engine-agnostic; default `C:\alltalk_tts`)
as the single canonical install root. Both engines derive subfolders from it via `TtsPaths`.
- **Migration** (idempotent, in `Configuration.Initialize()`, mirroring
  `MigrateLegacyInstanceTypeFields`): if `TtsInstallRoot` is empty/default and
  `Alltalk.LocalInstallPath` holds a non-default value, copy it into `TtsInstallRoot`. Keep
  `Alltalk.LocalInstallPath` `[Obsolete]`-style for one cycle so old configs still deserialize.
- **Re-route every reader/writer** of `Alltalk.LocalInstallPath` to `TtsInstallRoot`:
  `AlltalkInstanceService` (install/start/customdata path composition), `NativeGameDataToolsWindow.cs:516`,
  `NativeAlltalkBuilder` install-path input (+ `ValidateInstallPath` now validates `TtsInstallRoot`),
  the First-Time wizard. The UI install-path field binds to `TtsInstallRoot`.
- This lands in **Phase 1** (config + migration + re-route) so later phases build on the clean field.

Resulting layout:
```
{root}\
├── alltalk_tts\            (AllTalk engine)        voices\  ← AllTalk voices
├── echokrautts\            (EchokrauTTS wrapper)   samples\ ← EchokrauTTS voices
│   ├── .venv\ .state\ src\ bootstrap\ models\ config.json ...
│   └── samples\
└── EchokrautLocalInstaller\
    ├── EchokrautLocalInstaller.exe
    ├── Ready.txt               (AllTalk ready signal — unchanged)
    └── Ready.EchokrauTTS.txt   (EchokrauTTS ready signal — new)
```
Helper for path composition (new, pure): `Helper/Functional/TtsPaths.cs`
- `AllTalkRoot(root) => {root}\alltalk_tts`
- `AllTalkVoices(root) => {root}\alltalk_tts\voices`
- `EchokrauTtsRoot(root) => {root}\echokrautts`
- `EchokrauTtsSamples(root) => {root}\echokrautts\samples`
Single-sourced so installer, voice-copy, and detection all agree.

### Engine selection
- `Enums/TTSBackends.cs`: add `EchokrauTTS`.
- `Configuration.BackendSelection` (existing) is the engine switch. Changing it:
  1. runs the voice-copy (previous → new engine, see below),
  2. calls `IBackendService.SetBackendType(...)` + `RefreshBackend()`.

### Config model — `DataClasses/EchokrauTtsData.cs` (new)
Parallel to `AlltalkData`, but **reuses the shared root** (does not store its own install path):
```csharp
public class EchokrauTtsData
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8765"; // property, not field (avoids SonarQube S1104)
    public string TtsPath { get; } = "/tts";
    public string SamplesPath { get; } = "/samples";
    public string HealthPath { get; } = "/health";
    public string ShutdownPath { get; } = "/shutdown";
    public string CancelPath { get; } = "/cancel/";      // + {jobId}
    public string? ApiKey { get; set; }                  // optional Bearer
    public bool LocalInstall { get; set; }               // bootstrap completed
    public bool AutoStartLocalInstance { get; set; } = true;
    public AlltalkInstanceType InstanceType { get; set; } = AlltalkInstanceType.None;
    public bool CpuMode { get; set; }                    // forwarded to bootstrap GPU detect override
    // HasLiveGeneration handled centrally (see below)
}
```
- Add `public EchokrauTtsData EchokrauTts { get; set; } = new();` to `Configuration`.
- **`HasLiveGeneration`**: today `AlltalkData.HasLiveGeneration => InstanceType != None`. Generalize:
  a `Configuration`-level helper `bool HasLiveGeneration` that, based on `BackendSelection`, returns
  the active engine's `InstanceType != None`. Audit every existing `Alltalk.HasLiveGeneration`
  caller and route through the engine-aware helper. (Decision-propagation hotspot — see Risks.)
- `AlltalkInstanceType` enum is reused as-is (Local/Remote/None) for EchokrauTTS too.

### Backend client — `Backends/EchokrauTtsBackend.cs` (new, `: ITTSBackend`)
- `GetAvailableVoices`: `GET {BaseUrl}/samples` → parse `{samples:[...]}`. **Preserve the source
  extension** (e.g. `Female_Hyur_Iceheart.wav`) so `BackendVoice` keys are identical to AllTalk's
  `/api/voices` output — `MapVoices` stores `BackendVoice` *with* extension and derives `VoiceName`
  via `Path.GetFileNameWithoutExtension`. Strip the extension ONLY when building the `/tts` `sample`
  field. Return **null on HTTP failure**, empty list only on a genuinely empty server (so `MapVoices`
  never wipes the voice DB on a transient outage). [resolves BLK-4]
- `GenerateAudioStreamFromVoice`: `POST {BaseUrl}/tts` JSON
  `{sample, text, language, speed?, nfe_step?}` with optional `Authorization: Bearer`. Response is
  **raw PCM s16le 24000 Hz mono**. **v1: buffer the full body, prepend a 44-byte WAV header
  (24000/16/1), return a `MemoryStream`** so the existing BASS playback path consumes it unchanged
  (a streaming header variant is a later optimization). Capture `X-Job-Id` into a field for stop.
  Read sample rate from `X-Sample-Rate` (fallback 24000) rather than hard-coding.
  - **Language**: pass the **server's** language; if the requested `language` differs from the
    pinned language, see "Language handling" (the call must not 400 the server).
- `CheckReady`: `GET {BaseUrl}/health` → success when `status=="ok"`; return `"Ready"` on success so
  the existing First-Time `TestConnection` ("Ready"-only success token) works uniformly.
- `StopGenerating`: `POST {BaseUrl}/cancel/{lastJobId}` (best-effort; no global stop endpoint).
- `ReloadService`: no-op returning `true` for v1 (model reload = process restart with `--language`,
  handled by the instance service, not the HTTP client).
- Reuse the long-lived static `HttpClient` pattern (no per-request client).

### Local install/run — extend `EchokrautLocalInstaller`
New arg modes (mirror existing `install`/`start`):
- `installechokrautts <installRoot> <echokrauTtsUrl> <isWindows> <cpuMode> [apiKey]`
  1. download wrapper zip from `<echokrauTtsUrl>` → extract to `{installRoot}\echokrautts\` (contents
     directly under it, so `samples\` resolves to `{installRoot}\echokrautts\samples`).
  2. run `bootstrap\install_win.ps1` (hidden) — fetches uv, builds venv, installs torch+f5-tts,
     preloads models. Forward bootstrap **NDJSON** stdout: map `progress` → installer log/console,
     map `ready` → write `{installRoot}\EchokrautLocalInstaller\Ready.EchokrauTTS.txt` (parity with
     AllTalk's Ready.txt so the plugin's existing file-poll pattern is reused).
  3. `error{fatal}` → exit non-zero so the plugin surfaces the failure.
- `startechokrautts <installRoot> <isWindows> <port> <language> <parentPid> [apiKey]`
  - run `bootstrap.py --start --host 127.0.0.1 --port <port> --language <language>
    --parent-pid <parentPid> [--api-key ...]`; bridge `ready` → Ready.EchokrauTTS.txt.
- `stopechokrautts` — the wrapper's parent-PID watchdog handles orphan cleanup; the plugin also
  calls `POST /shutdown` first (graceful), then deletes Ready.EchokrauTTS.txt.
- **No conda, no MSBuildTools, no XTTS-model download** for this path — the wrapper self-bootstraps.
- Requires a **new `EchokrautLocalInstaller` release** (new `ELI-*` tag) and a bumped
  `RemoteUrls.InstallerUrl` so the plugin downloads the version that understands the new modes.
  (Release is the user's step; flag at ship time.)

### Instance service — `Services/IEchokrauTtsInstanceService` + impl (new)
Mirror `IAlltalkInstanceService` surface so the UI builder is symmetric:
`Install()`, `StartInstance()`, `StopInstance(eventId)`, plus `Installing`, `CurrentInstallStatus`,
`CurrentInstallProgress`, `InstanceRunning`, `InstanceStarting`, `InstanceStopping`, and
`event Action? OnInstanceReady`.
- `Install()`: validate root → ensure `EchokrautLocalInstaller` present (reuse AllTalk's
  download/extract-from-`InstallerUrl` helper — factor it into a shared helper) → launch installer
  `installechokrautts` → on success set `EchokrauTts.LocalInstall = true`, `BaseUrl` to local
  `127.0.0.1:8765`, save.
- `StartInstance()`: launch installer `startechokrautts` with the **client language** + game PID →
  poll `Ready.EchokrauTTS.txt` (2 s) → fire `OnInstanceReady`.
- `StopInstance()`: `POST /shutdown` (best-effort) → delete Ready file → kill process.
- **Progress**: parse the installer console/log NDJSON `progress.percent` into
  `CurrentInstallProgress`/`CurrentInstallStatus` (same StatusProgressBar the First-Time/Backend UI
  already drive for AllTalk).

### Install detection — `Helper/Functional/TtsInstallDetection.cs` (new, pure)
- `bool IsAllTalkInstalled(root)` → `{root}\alltalk_tts\script.py` (or `alltalk_environment\`) exists.
- `bool IsEchokrauTtsInstalled(root)` → `{root}\echokrautts\.state\model.done` (and `.venv\`) exists.
Used to: (a) show install state per engine in UI, (b) gate the voice-copy source, (c) set the
`LocalInstall` flags on startup if a prior install is detected but the flag was lost.

### Voice-copy on engine switch — `Services/ITtsVoiceSyncService` + impl (new)
`void CopyVoicesForSwitch(TTSBackends from, TTSBackends to)`:
- source = `from` engine's voices folder, target = `to` engine's voices folder (via `TtsPaths`).
- if source missing/empty → no-op (log Info).
- copy every file from source into target, **overwriting same-named files, leaving the target's
  other files intact** (no delete). Recursive not needed (folders are flat), but copy `.txt`
  sidecars too if present.
- pure file logic extracted to `Helper/Functional/DirectoryMerge.cs`
  (`MergeCopy(srcDir, dstDir, overwrite:true)`) for unit testing; the service wraps it with config
  paths + logging + create-target-if-missing.
- Triggered from both UIs when `BackendSelection` actually changes (old != new), BEFORE
  `RefreshBackend`. Runs off the framework thread (file I/O) with a terminal-status note.
- **Caveat to document**: AllTalk→EchokrauTTS copies `.wav` without reference transcripts; F5-TTS
  then relies on ASR/`ref_text` fallback (lower quality). Emitting `.txt` sidecars from the Voice
  Sample Extractor is a follow-up.

### BackendService routing
`RefreshBackend()` switches on `BackendSelection`:
- `Alltalk` → existing logic (unchanged).
- `EchokrauTTS` → connect when `EchokrauTts.InstanceType==Remote` (reachable) OR
  (`Local` && `_echokrauTtsInstance.InstanceRunning`); construct `EchokrauTtsBackend`; `MapVoices`.
Subscribe to BOTH instance services' `OnInstanceReady`. `IsBackendReachableAsync` extended for the
EchokrauTTS engine (health endpoint / InstanceRunning).

### UI
**Engine choice** (both windows):
- `NativeConfigWindow`: `_backendDropDown` options become `["Alltalk","EchokrauTTS"]` (already
  deferred-selection + dynamically populated). On change → voice-copy + `SetBackendType` +
  `RefreshBackend`. Show the selected engine's Local/Remote/None sections; hide the other's.
- `NativeFirstTimeWindow`: add an engine selector at Step 0 (dropdown or two buttons) before the
  Local/Remote/None mode buttons; persist `BackendSelection`. Step 1 builds the selected engine's
  sections.

**EchokrauTTS builder** — `Windows/Native/NativeEchokrauTtsBuilder.cs` (new), mirroring
`NativeAlltalkBuilder`:
- `BuildLocalInstance`: shared install-path input (reads/writes `Alltalk.LocalInstallPath` — the
  shared root; reuse `ValidateInstallPath`), CPU-mode checkbox, Install/Reinstall button (→
  `IEchokrauTtsInstanceService.Install`), AutoStart checkbox, Start/Stop buttons, dynamic-label
  sizing + per-frame `Update(...)` dimming (copy the AllTalk builder's proven pattern).
- `BuildRemoteInstance`: base-URL input (`EchokrauTts.BaseUrl`) + Test button (→ `CheckReady`) +
  result label.
- None mode reuses the existing shared "Audio Files Only" section (engine-independent).
- Follow `Windows/CLAUDE.md`: TextDropDownNode deferred selection, button auto-size, collapsible
  sections via `CreateTrackedCollapsibleSection`, `RunOnFrameworkThread` for async UI callbacks.

**Install-state hint**: a small label per engine ("Installed" / "Not installed") driven by
`TtsInstallDetection`, so the user sees what's present (the "we need to know what's installed"
requirement).

### Language handling (v1)
- Local EchokrauTTS is started pinned to `IClientState.ClientLanguage`.
- In `EchokrauTtsBackend.GenerateAudioStreamFromVoice`, send the request **without** a mismatching
  `language` (omit it, or set it to the server language) so the server never 400s; the active model
  is the client language regardless.
- If a line's resolved language differs from the server language: **v1 = generate with the pinned
  language anyway** (F5-TTS voice-clones the sample; the text is spoken in the model's language).
  *Open question:* is that acceptable, or should differing-language lines be skipped / fall back to
  cached audio? (Auto-restart-the-server-on-language-change with debounce is a future option.)
- Remote EchokrauTTS: same — whatever language the remote server was started with.

### ServiceBuilder + Plugin wiring
- Register `IEchokrauTtsInstanceService`, `ITtsVoiceSyncService`.
- `Plugin.cs` auto-start gate: extend so that when `BackendSelection==EchokrauTTS &&
  EchokrauTts.LocalInstall && InstanceType==Local && AutoStartLocalInstance`, start the EchokrauTTS
  instance instead of AllTalk. Generalize the gate to the active engine.

### Localization
New `Loc.S` keys (EN→DE/FR/JP): `"EchokrauTTS"`, `"TTS Engine"`, `"AllTalk"`, engine-install
labels, `"Installed"`, `"Not installed"`, plus reuse of existing `"Local TTS"/"Remote Server"/
"Audio Files Only"/"Install"/"Reinstall"/"Start"/"Stop"/"Test"`.

---

## Files Touched

| Kind | File | Change |
|------|------|--------|
| modify | `Echokraut/Enums/TTSBackends.cs` | add `EchokrauTTS` |
| new | `Echokraut/DataClasses/EchokrauTtsData.cs` | per-engine config |
| modify | `Echokraut/DataClasses/Configuration.cs` | add `EchokrauTts`, `TtsInstallRoot`, `ActiveInstanceType`, engine-aware `HasLiveGeneration`; migrate `Alltalk.LocalInstallPath` → `TtsInstallRoot` in `Initialize()` |
| new | `Echokraut/Helper/Functional/TtsPaths.cs` | shared path composition |
| new | `Echokraut/Helper/Functional/TtsInstallDetection.cs` | per-engine install detection |
| new | `Echokraut/Helper/Functional/DirectoryMerge.cs` | merge-copy (overwrite, keep extras) |
| new | `Echokraut/Backends/EchokrauTtsBackend.cs` | `ITTSBackend` impl (PCM→WAV, language, cancel) |
| new | `Echokraut/Services/IEchokrauTtsInstanceService.cs` + impl | local install/start/stop |
| new | `Echokraut/Services/ITtsVoiceSyncService.cs` + impl | voice-copy on switch |
| modify | `Echokraut/Services/BackendService.cs` | route by `BackendSelection`; reachability |
| modify | `Echokraut/Services/AlltalkInstanceService.cs` | factor installer download into shared helper |
| modify | `Echokraut/Services/ServiceBuilder.cs` | register new services |
| modify | `Echokraut/Plugin.cs` | engine-aware auto-start gate |
| new | `Echokraut/Windows/Native/NativeEchokrauTtsBuilder.cs` | EchokrauTTS Local/Remote UI |
| modify | `Echokraut/Windows/Native/NativeConfigWindow.cs` | engine dropdown + EchokrauTTS sections + copy-on-switch |
| modify | `Echokraut/Windows/Native/NativeFirstTimeWindow.cs` | engine choice + EchokrauTTS sections |
| modify | `Echokraut/Localization/Loc.cs` | new strings (DE/FR/JP) |
| modify | `Echokraut/Resources/RemoteUrls.json` + `RemoteUrlsData` | add `EchokrauTtsUrl` (note: NOT `EchokrautTtsUrl`); bump `InstallerUrl` + add expected ELI tag |
| modify | `EchokrautLocalInstaller/Program.cs` (+ `Constants.cs`) | `installechokrautts`/`startechokrautts`/`stopechokrautts` modes, NDJSON bridge |
| modify | `Echokraut/Resources/Changelogs/v0.19.1.0_EN.txt` + `_DE.txt` (new) | feature entry; csproj embed |
| modify | `Echokraut/Echokraut.csproj` | bump `<Version>` to 0.19.1.0; embed new changelog files |
| tests | `Echokraut.Tests/EchokrauTtsBackendTests.cs` (new) | URL build, PCM→WAV header, voices/health parse, job-id capture |
| tests | `Echokraut.Tests/DirectoryMergeTests.cs` (new) | overwrite same-named, keep extras, missing source |
| tests | `Echokraut.Tests/TtsPathsTests.cs` + `TtsInstallDetectionTests.cs` (new) | path composition, detection |
| tests | `Echokraut.Tests/*` (extend) | engine-aware `HasLiveGeneration`, BackendService routing |

---

## Test plan (no game runtime)
- `EchokrauTtsBackend`: URL composition from BaseUrl + paths; PCM→WAV header bytes (RIFF/WAVE,
  24000/16/1, sizes) for a synthetic PCM buffer; `/samples` + `/health` JSON parsing; `X-Job-Id`
  capture; Bearer header when ApiKey set; mismatched-language request omits/adjusts `language`.
- `DirectoryMerge`: same-named overwritten, target extras kept, sidecars copied, missing source =
  no-op, target created if absent. (Use a temp dir.)
- `TtsPaths` / `TtsInstallDetection`: exact subfolder strings; detection true/false on marker files.
- Engine-aware `HasLiveGeneration`: returns the active engine's mode for each `BackendSelection`.
- `BackendService.RefreshBackend` routing: constructs the right backend per selection (mock
  instance services). Voice-sync invoked on change only when old != new.
- Requires runtime / manual: real bootstrap install, real `/tts` audio in BASS, language restart.

---

## Phasing (each phase: build + test + commit)
1. **Foundation**: `TtsPaths`, `DirectoryMerge`, `TtsInstallDetection`, `EchokrauTtsData`,
   `TTSBackends.EchokrauTTS`, engine-aware `HasLiveGeneration` (+ tests). No behavior change yet.
2. **Backend client (Remote-first)**: `EchokrauTtsBackend` + `BackendService` routing + reachability.
   Lets a user point at a remote/manually-run wrapper and generate. (+ tests)
3. **Voice-sync**: `ITtsVoiceSyncService` + wire into engine switch. (+ tests)
4. **Local install**: extend `EchokrautLocalInstaller` modes; `IEchokrauTtsInstanceService`;
   shared installer-download helper; auto-start gate. (Needs new ELI release to test end-to-end.)
5. **UI**: engine choice + `NativeEchokrauTtsBuilder` sections in Backend tab + First-Time wizard;
   install-state hints; localization.
6. **Changelog + docs + CLAUDE.md**; version bump.

Phases 1–3 are plugin-only and independently shippable (remote EchokrauTTS works after phase 2).
Phase 4 is the only one coupled to an installer release.

---

## Risks & open questions
- **Single-language server (highest impact)**: v1 pins to client language; differing-language lines
  spoken in the pinned model. *Confirm acceptable*, or choose skip/fallback, or schedule
  auto-restart-on-language-change. (See "Language handling".)
- **PCM→WAV buffering**: v1 buffers the whole clip (latency vs AllTalk's streaming). Acceptable for
  v1? Streaming WAV header is a later optimization.
- **Global stop**: only per-job `/cancel/{id}` exists; `StopGenerating` cancels the last job id
  (best-effort). Queued/next jobs aren't stopped as cleanly as AllTalk's `/stop-generation`.
- **`HasLiveGeneration` propagation**: many call sites assume AllTalk; must route all through the
  engine-aware helper or None-mode/live-path logic breaks for EchokrauTTS. Enumerate every caller.
- **Installer release coupling**: phase 4 needs a new `ELI-*` release + `InstallerUrl` bump (user).
- **Disk/first-run cost**: wrapper pulls uv + torch + 4 language models (GBs); long first install.
  Progress UI mitigates; document expectations.
- **GPU/CPU**: wrapper auto-detects; `CpuMode` forwards an override. Verify the bootstrap honors it.
- **ref_text quality**: copied `.wav` lack transcripts → ASR fallback. Sidecar emission is future.
- **Shared root naming**: `Alltalk.LocalInstallPath` doubles as the engine-agnostic root (awkward
  but avoids migration). Optional later promotion to `Configuration.TtsInstallRoot`.
- **Voice-name mapping**: confirm `/samples` basenames map through `MapVoices` the same way AllTalk
  `/api/voices` names do (race/gender tokens etc.).

---

## Rollback
Additive. Reverting = remove the new files, the `TTSBackends.EchokrauTTS` value, the `EchokrauTts`
config block, the UI sections, and the installer modes. No DB schema changes. Existing AllTalk
behavior is untouched on every path.

---

## Review resolutions (binding — supersede the body where they conflict)

From `docs/reviews/echokrautts-backend-review.md` (5 blockers / 7 gaps / 3 contradictions /
9 edge cases / 6 propagation misses). All verified against source.

### Blockers
- **BLK-1 — Active-engine accessor (centralize the None/engine gate).** Add to `Configuration`:
  `ActiveInstanceType => BackendSelection == EchokrauTTS ? EchokrauTts.InstanceType : Alltalk.InstanceType;`
  and `HasLiveGeneration => ActiveInstanceType != None;`. Route **every** raw site through it:
  `VoiceMessageProcessor.cs:156/162/169` (raw `Alltalk.InstanceType != None`); `BackendService.cs:70`
  (`RefreshBackend`), `:85` (`SetBackendType`), `:186-194` (`IsBackendAvailable`), `:242`
  (`PingBackendAsync`), `:346` (`CheckReady`) — all currently early-return for `!= Alltalk` and must
  switch by `BackendSelection`; plus the 8 `HasLiveGeneration` readers (`NativeWindowManager.cs:192`,
  `NativeVoiceClipManagerWindow.cs:385/661`, `NativeVoiceClipDetailWindow.cs:307`,
  `NativeNpcEditWindow.cs:283`, `NativeConfigWindow.cs:319/348/588`, `NativeConfigWindow.Logs.cs:97`,
  `BackendService.cs:297`, `VoiceClipManagerService.cs:231`). These three files are added to Files
  Touched. **Phase split (CON-1):** Phase 1 adds the accessor returning the *current* AllTalk values
  only (behavior-preserving); the `BackendService`/`VoiceMessageProcessor` re-routing lands in Phase 2
  with the routing change. Phase 1 exit criterion: AllTalk default behavior byte-identical (test).
- **BLK-2/BLK-3 — PCM→WAV: NO wrapping needed (verified, supersedes the WAV-wrap idea).** The
  existing pipeline already treats `message.Stream` as **raw PCM s16le/24 kHz/mono** — exactly what
  EchokrauTTS `/tts` returns (and what AllTalk's streaming endpoint returns). Verified in code:
  `AudioFileService.WriteStreamToFile:103-104` seeks the stream to 0 and calls
  `RawPcmToWav.CreateWaveFileAsync(stream, 24000, 16, 1)` (wraps raw PCM → WAV on save), and
  `Live3DAudioEngine` plays raw 16-bit mono at the `PlayStream` 24000 default. So
  `GenerateAudioStreamFromVoice` returns the **raw PCM response wrapped in a seekable
  `ReadSeekableStream`** (same as `AlltalkBackend.cs:71`) — no WAV header, no `X-Sample-Rate` parsing.
  Disk-save happens in `OnSourceEnded` (SaveToLocal-gated), which seeks to 0 itself. (24000 Hz is an
  existing hard-coded coupling shared with AllTalk; if a future server changes the rate, both engines
  would need the rate threaded through — out of scope.)
- **BLK-4 — `BackendVoice` key parity.** See the revised `GetAvailableVoices` bullet: keep the
  extension in the key; strip only for the `/tts` `sample` field. Test: EchokrauTTS voice keys
  round-trip identically to AllTalk for the same sample filename.
- **BLK-5 — Installer version handshake.** Add `Configuration.InstalledInstallerVersion` (persisted).
  The shared installer-download helper (factored out of `AlltalkInstanceService.CheckAndDownloadLocalInstaller`,
  `:448-472`) re-downloads when the on-disk version != the expected ELI tag from `RemoteUrls`, even if
  `EchokrautLocalInstaller.exe` already exists. Without this, existing AllTalk users keep the stale
  `ELI-1.0.0.1` exe (no new modes) → infinite Ready-file spinner. Lands in Phase 4.

### Gaps
- **GAP-1:** migrate inline `Path.Combine(LocalInstallPath, "alltalk_tts")` at
  `NativeGameDataToolsWindow.cs:516` and `AlltalkInstanceService.cs:126` to `TtsPaths` so the
  "single source of truth" actually holds. (Added to Files Touched.)
- **GAP-2 — process tree / stop.** `--parent-pid` = the **game PID** (so the wrapper's watchdog
  outlives the installer but dies with the game). `StopInstance` does graceful `POST /shutdown` first,
  waits, then deletes the Ready file; it does **not** hard-kill the installer (which wouldn't cascade
  to the Python server reliably). Document the tree: plugin → installer (transient) → bootstrap →
  uvicorn server (watchdog on game PID).
- **GAP-3:** First-Time `canNext` gate (`NativeFirstTimeWindow.cs:374`) and `BuildFinishSummary`
  (`:488-496`) must read the **active engine** (via the BLK-1 accessor), not `Alltalk.*`. (Added.)
- **GAP-4 — `/tts` contract PINNED (verified):** `server.py:154` is `if req.language and req.language
  != config.language` → **omitting `language` skips the check**. So `GenerateAudioStreamFromVoice`
  **omits `language`** entirely; the server synthesizes in its loaded model. No 422 risk.
- **GAP-5 — per-commit changelog.** Each phase commit follows the per-commit workflow: bump
  `<Version>` once to `0.19.1.0`, then append phase-specific bullets to `v0.19.1.0_EN/DE.txt`
  (the commit-gate hook denies commits otherwise). NOT deferred to a final phase.
- **GAP-6 — switch mid-flight.** An engine switch must `CancelAll` the queue + stop current playback
  BEFORE swapping `_backend` (else a stale `lastJobId` is cancelled on the wrong engine / NRE on a
  swapped `_backend`).
- **GAP-7 — no streaming toggle.** EchokrauTTS has no `StreamingGeneration` concept; it always
  buffers and saves via `OnSourceEnded` only. Hide the AllTalk "Streaming generation" checkbox when
  EchokrauTTS is the active engine.

### Contradictions
- **CON-1:** resolved by the Phase 1/2 split above.
- **CON-2 — `ReloadService`.** Hide any "Reload" UI for EchokrauTTS (reload = process restart with
  `--language`, owned by the instance service). `EchokrauTtsBackend.ReloadService` returns `true` as a
  documented no-op; the config UI does not surface a Reload button for EchokrauTTS.
- **CON-3:** `BaseUrl` is a property (fixed inline above).

### Edge cases
- Switch **to None** → voice-copy is a **no-op** (None has no voices folder).
- Switch when **target engine running** → copy then signal a restart need; prefer copying while the
  target is stopped (F5-TTS enumerates `samples/` at start). Document: a running target may not see
  new voices until its next start.
- **Repeated switches** → "keep extras" means a user-deleted voice can reappear from the other
  engine's copy; this is intentional (copy, never delete) — document as known behavior.
- **AllTalk voices are flat** `*.wav` (`AlltalkInstanceService.cs:127`) → flat merge is correct.
- **Port 8765 in use / no `ready`** → instance start has a timeout; surface bootstrap `error{fatal}`
  and stop polling instead of spinning forever.
- **ClientLanguage → supported code** → map to the 4 F5-TTS languages (en/de/fr/ja) with a default,
  mirroring `getAlltalkLanguage` (`AlltalkBackend.cs:176-187`). Unsupported client language → default
  (e.g. `en`) for the `--language` start arg.
- **`X-Sample-Rate` missing/malformed** → defensive parse, fallback 24000.
- **Empty vs unreachable `/samples`** → null on HTTP error, empty only on genuine empty (BLK-4 bullet).

### Other
- **CpuMode (DECIDED: defer):** the wrapper has **no CPU-override flag** — `gpu_detect.py`
  auto-detects cuda/rocm/dml/xpu/cpu. v1 EchokrauTTS has **no CPU-mode checkbox**; drop `<cpuMode>`
  from the `installechokrautts` arg list (auto-detect only). **TODO (later):** add a force-CPU
  option — requires a wrapper-side config/flag addition + a plugin checkbox. Tracked, out of v1.
- **`CheckReady` returns the literal `"Ready"`** on `health.status=="ok"` (not the raw JSON) so
  `NativeFirstTimeWindow.cs:458`'s exact-match success token works. (Added to test list.)
- **Casing convention:** enum/UI = `EchokrauTTS`; C# type prefix = `EchokrauTts`; on-disk folder +
  repo = `echokrautts`; RemoteUrls key = `EchokrauTtsUrl`.

### OPEN — needs user decision (language handling v1)
The local server is single-language (pinned to client language at start). For a line whose resolved
language differs from the pinned one, v1 options:
- (A) **generate anyway** in the pinned model (text spoken in that language/accent),
- (B) **skip** differing-language lines (no audio / fall back to cached),
- (C) **auto-restart** the server with the new language (debounced) — heavier, deferred.
Plan currently assumes (A). **DECIDED (user): (A) generate anyway in the pinned model.** Differing-
language lines are synthesized by the active (client-language) model; `language` is omitted from the
`/tts` request so the server never 400s. (B)/(C) are explicitly deferred.
