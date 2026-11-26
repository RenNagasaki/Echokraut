# Plan Review — EchokrauTTS backend

**Reviewed document:**
- `docs/plans/echokrautts-backend.md` (391 lines)

**Cross-referenced source (read-only):**
- `Echokraut/DataClasses/AlltalkData.cs`, `Backends/AlltalkBackend.cs`,
  `Services/BackendService.cs`, `Services/AlltalkInstanceService.cs`,
  `Services/AudioPlaybackService.cs`, `Services/Live3DAudioEngine.cs`,
  `Services/VoiceMessageProcessor.cs`, `Windows/Native/NativeFirstTimeWindow.cs`,
  `Windows/Native/NativeGameDataToolsWindow.cs`, `Windows/Native/NativeConfigWindow.cs`,
  `DataClasses/RemoteUrlsData.cs`, `Resources/RemoteUrls.json`, `Enums/TTSBackends.cs`.

**Reviewer:** plan-reviewer agent
**Scope note:** full pre-implementation audit, focused on the six areas the user flagged.

## Summary

- **Verdict:** ready-with-reservations (NOT-ready to start phase 4/5 without resolving the blockers; phases 1–3 are largely safe)
- **Blocking issues:** 5
- **Gaps:** 7
- **Contradictions:** 3
- **Unaddressed edge cases:** 9
- **Decision-propagation misses:** 6
- **Terminology drift issues:** 2
- **Untested assumptions:** 6

- **Top 3 highest-impact findings:**
  - **BLK-1**: `HasLiveGeneration` and the raw `InstanceType != None` checks are NOT centralized today — at least 13 call sites read `_config.Alltalk.*` directly. The plan's "engine-aware helper" only covers `HasLiveGeneration`; it misses the runtime gate sites in `VoiceMessageProcessor` (lines 156/162/169) and the routing/reachability sites in `BackendService` that hard-`return false` for non-Alltalk. Generation for EchokrauTTS will silently no-op.
  - **BLK-2**: The PCM→WAV plan is over-engineered AND under-specified. `Live3DAudioEngine` already auto-detects WAV *and* falls back to raw 16-bit/mono PCM at the `sampleRate` passed to `PlayStream` — but `AudioPlaybackService` calls `PlayStream(...)` WITHOUT passing a sample rate (defaults to 24000) and the returned `Stream` is consumed by a non-seekable read path. The plan must specify the seekable `MemoryStream` AND confirm the 24000 default matches; otherwise audio plays at the wrong pitch or the WAV header path silently disagrees.
  - **BLK-4**: The voice-name mapping is a correctness landmine. `MapVoices` stores `BackendVoice` as the *raw string returned by the backend* and matches NPCs by it; AllTalk returns names *with* `.wav`. The plan says EchokrauTTS should "strip extensions" — that makes the two engines' `BackendVoice` keys incompatible, so the voice-copy + voice-DB carry over produces unresolvable voice keys after an engine switch.

---

## Blocking issues

### BLK-1: `HasLiveGeneration` / None-mode gating is not centralizable by the plan's stated scope
- **Where:** plan §"Config model" (lines 163-166) and §Risks (line 373) vs. actual call sites.
- **Finding:** The plan proposes a single `Configuration`-level engine-aware `HasLiveGeneration` and says "Audit every existing `Alltalk.HasLiveGeneration` caller." But the live-generation gate in the runtime pipeline does NOT use `HasLiveGeneration` — it uses the raw expression `_config.Alltalk.InstanceType != AlltalkInstanceType.None`:
  - `VoiceMessageProcessor.cs:156`, `:162`, `:169` — voice assignment / inference gates.
  - `BackendService.cs:70` (`RefreshBackend` early-returns unless `Alltalk`), `:85` (`SetBackendType` early-returns unless `Alltalk`), `:186-194` (`IsBackendAvailable` only has an `Alltalk` case), `:242` (`PingBackendAsync` returns false for non-Alltalk), `:246-250`, `:346` (`CheckReady` returns "No backend selected" for non-Alltalk).
  Additionally `HasLiveGeneration` is read directly off `Alltalk` in 8 UI/service files (`NativeWindowManager.cs:192`, `NativeVoiceClipManagerWindow.cs:385,661`, `NativeVoiceClipDetailWindow.cs:307`, `NativeNpcEditWindow.cs:283`, `NativeConfigWindow.cs:319,348,588`, `NativeConfigWindow.Logs.cs:97`, `BackendService.cs:297`, `VoiceClipManagerService.cs:231`).
- **Impact:** If EchokrauTTS is selected, every `BackendService` method that early-returns on `!= Alltalk` will refuse to initialize/route/reach the backend, and the `VoiceMessageProcessor` gates keyed on `Alltalk.InstanceType` will read the *AllTalk* instance type (likely None for a user who only set up EchokrauTTS) and skip voice assignment entirely. Net: EchokrauTTS generates nothing, with no error.
- **Suggested fix:** Expand the plan's "engine-aware helper" into a named **active-engine accessor** on `Configuration`, e.g. `AlltalkInstanceType ActiveInstanceType => BackendSelection == EchokrauTTS ? EchokrauTts.InstanceType : Alltalk.InstanceType;` plus `bool HasLiveGeneration => ActiveInstanceType != None;` and route ALL of the sites above through it (not just the `HasLiveGeneration` ones). Add an explicit checklist of the 5 `BackendService` `!= Alltalk` returns and the 3 `VoiceMessageProcessor` raw checks to the Files-Touched table and the phase-1 exit criteria. Phase 1 currently claims "No behavior change yet" — that is only true if these raw sites are migrated to read the active engine and AllTalk remains the default.

### BLK-2: PCM→WAV path is under-specified against the actual playback engine
- **Where:** plan §"Backend client" `GenerateAudioStreamFromVoice` (lines 172-179) and §Risks (line 369).
- **Finding:** The plan says "prepend a 44-byte WAV header (24000/16/1), return a `MemoryStream` so the existing BASS playback path consumes it unchanged." Verified against the real path:
  - `AudioPlaybackService.PlayAudioAsync:255` calls `_audioEngine.PlayStream(message.Stream, channels: 1, initialPosition: ..., use3d: message.Is3D)` — it does **not** pass `sampleRate`, so the engine default `24000` applies (`Live3DAudioEngine.cs:184`). That happens to match F5-TTS's 24000 Hz, but the coupling is implicit and undocumented.
  - `Live3DAudioEngine.Source.Start()` (lines 379-432) auto-detects a RIFF/WAVE header AND has a raw-PCM fallback that assumes 16-bit mono. So a 44-byte WAV header is sufficient but **not strictly required** — raw s16le mono at the engine's 24000 default would also play. The plan presents WAV-wrapping as mandatory ("so the existing path consumes it unchanged") without noting either alternative or the 24000 coupling.
  - AllTalk's stream is wrapped in `ReadSeekableStream` (`AlltalkBackend.cs:71`) because the HTTP body is non-seekable and the WAV-header parse uses the seekable branch. A `MemoryStream` IS seekable, so the EchokrauTTS path will take the seekable WAV-parse branch — good, but the plan must say so explicitly.
- **Impact:** If the implementer hard-codes 24000 in the WAV header but a future server emits a different `X-Sample-Rate`, OR relies on the raw-PCM fallback without the header, pitch/speed will be wrong and there is no validation. The header byte-order/size math is exactly the kind of thing that silently produces noise.
- **Suggested fix:** Specify: (a) read `X-Sample-Rate`/`X-Channels`/`X-Sample-Format` headers and build the WAV header from them (fallback 24000/1/s16le); (b) return a seekable `MemoryStream` (state explicitly that this is required so the engine's seekable WAV-parse branch runs and `OnSourceEnded`'s `WriteStreamToFile` can re-read it); (c) note that the engine ignores the `sampleRate` arg when a WAV header is present, so the header's rate is authoritative; (d) add a unit test asserting the 44-byte header fields against the header values, which the plan already lists — keep it.

### BLK-3: `ReadSeekableStream` / save-to-disk produces a malformed WAV unless the header is part of the stream
- **Where:** plan §"Backend client" (line 176) + interaction with `AudioPlaybackService.OnSourceEnded` (`AudioPlaybackService.cs:137-178`).
- **Finding:** On playback end, if `SaveToLocal` is on and the clip wasn't loaded locally, `OnSourceEnded` calls `_audioFiles.WriteStreamToFile(..., message.Stream, ...)`. For AllTalk the stream is the *WAV* the server produced. For EchokrauTTS the plan's `MemoryStream` is `[WAV header || PCM]` which is correct ONLY if the header is included in the same stream that gets written to disk (it is, in the plan) AND the stream position is reset to 0 before write. The engine reads the stream during playback (advancing position); `WriteStreamToFile` then needs position 0.
- **Impact:** AllTalk's non-streaming branch seeks back to 0 (`AlltalkBackend.cs:76`) before writing; the playback engine consumes the live stream. For EchokrauTTS, after playback the `MemoryStream` position is at the end. If `WriteStreamToFile` doesn't seek to 0, the saved `.wav` is empty/garbage and the cache-adoption + generation-logging paths (which assume a valid on-disk WAV) break silently.
- **Suggested fix:** State in the plan that the `MemoryStream` must be rewound to 0 before being handed to the queue, OR confirm `WriteStreamToFile` seeks to 0 internally (read it; AllTalk relies on the caller seeking). Add this to the `EchokrauTtsBackend` test list: "saved stream is a valid re-readable WAV after playback consumption."

### BLK-4: `BackendVoice` key format diverges between engines (extension stripping)
- **Where:** plan §"Backend client" `GetAvailableVoices` (lines 170-171), §"Voice-copy" (lines 229-243), §Risks line 383, vs. `BackendService.MapVoices` (`BackendService.cs:103-179`).
- **Finding:** `MapVoices` stores the backend's voice string verbatim into `VoiceEntity.BackendVoice` and uses it as the identity key for add/remove/migrate (lines 117-119, 129, 146-147). `AlltalkBackend.GetAvailableVoices` returns names exactly as AllTalk reports them (with `.wav`, per the AllTalk `/api/voices` convention). The plan says EchokrauTTS should "strip extensions to match the voice-name convention used by `MapVoices`." But `MapVoices` does NOT strip — it derives `VoiceName` via `Path.GetFileNameWithoutExtension` (line 126) while keeping the full `BackendVoice` with extension. So AllTalk voices are stored as `Female_Hyur_Iceheart.wav` and EchokrauTTS (stripped) would store `Female_Hyur_Iceheart`.
- **Impact:** After a voice-copy + engine switch, every NPC's persisted `voice` (a `BackendVoice` key) was assigned under AllTalk's `.wav`-suffixed key. Under EchokrauTTS the available voices are suffix-less, so `MapVoices` treats every existing voice as an "old voice" to be migrated/deleted (lines 146-172), and `GenerateVoice` looks up `message.Speaker.Voice?.BackendVoice` which won't match `/samples` entries. Result: mass voice re-assignment churn and/or no voice found. This is the single most likely "it built and ran but produces silence" bug.
- **Suggested fix:** Decide a single canonical `BackendVoice` format and state it explicitly. Recommended: EchokrauTTS `GetAvailableVoices` returns names **with the source extension preserved** (the F5-TTS `/samples` response already returns filenames like `Female_Hyur_Iceheart.wav`), so the key matches AllTalk's. Then `GenerateAudioStreamFromVoice` strips the extension only when building the `sample` field for the `/tts` request body (F5-TTS wants the basename). Add a test: "EchokrauTtsBackend voice keys round-trip identically to AllTalk for the same sample filename."

### BLK-5: Old cached installer is silently reused — version-coupling handshake missing
- **Where:** plan §"Local install/run" (lines 203-204) + §Risks (line 375), vs. `AlltalkInstanceService.CheckAndDownloadLocalInstaller` (`AlltalkInstanceService.cs:448-472`).
- **Finding:** `CheckAndDownloadLocalInstaller` downloads + extracts the installer **only if `EchokrautLocalInstaller.exe` does not already exist** on disk. A user who installed AllTalk earlier already has an `EchokrautLocalInstaller.exe` from tag `ELI-1.0.0.1`, which does NOT understand the new `installechokrautts`/`startechokrautts` modes. The plan requires a new ELI release + `InstallerUrl` bump but never addresses the cache-skip: the new URL is never consulted because the old exe is present.
- **Impact:** EchokrauTTS local install launches the stale installer with an unknown arg mode → the installer no-ops or errors in an unhelpful way → the plugin polls `Ready.EchokrauTTS.txt` forever → user sees an indefinite spinner. This will hit every existing AllTalk user.
- **Suggested fix:** Add a version check to the shared installer-download helper the plan already wants to factor out: persist the installed ELI tag (e.g. `Configuration.InstalledInstallerVersion`) and re-download when it differs from `RemoteUrls`'s embedded expected tag, OR probe the on-disk installer for capability (e.g. a `--version` / `--capabilities` arg) and re-download on mismatch. Document the migration: existing users must get the new installer pulled even though the exe already exists. Add this to phase 4.

---

## Gaps

### GAP-1: No handling for "AllTalk is the active engine but `Alltalk.LocalInstallPath` is the EchokrauTTS root too" path-collision validation
- **Where:** §"Shared install root" (lines 111-135).
- **Missing:** The plan reuses `Alltalk.LocalInstallPath` as the shared root and composes `{root}\echokrautts\samples`. But `NativeGameDataToolsWindow.cs:516` and `AlltalkInstanceService.cs:126` independently compose `Path.Combine(LocalInstallPath, "alltalk_tts")` inline — they do NOT go through the proposed `TtsPaths` helper. The plan's `TtsPaths` is "single-sourced so installer, voice-copy, and detection all agree," but two existing AllTalk sites will still hard-code `"alltalk_tts"`.
- **Why it matters:** "single source of truth" is the plan's stated guarantee; it is false unless these two sites are migrated. Future path changes will silently desync.
- **Suggested addition:** Add `NativeGameDataToolsWindow.cs` and `AlltalkInstanceService.cs` to the Files-Touched table with "replace inline `alltalk_tts` Path.Combine with `TtsPaths.AllTalkRoot/AllTalkVoices`."

### GAP-2: `stopechokrautts` mode is declared but the instance-service `StopInstance` says "kill process" — ownership of the spawned process is unclear
- **Where:** §"Local install/run" (line 200-201) and §"Instance service" (line 218).
- **Missing:** AllTalk's `StartInstance` keeps `_instanceProcess` = the installer process and kills it on stop (`AlltalkInstanceService.cs:306`). The EchokrauTTS wrapper has its own parent-PID watchdog + Windows job object (research line 60). The plan says the wrapper "self-terminates if the host dies" and also that the plugin kills the process — but the process the plugin spawns is the *installer*, which then spawns `bootstrap.py`. Killing the installer may not kill the Python server unless the watchdog fires on the right parent PID. The `--parent-pid` is passed as "game PID" (line 216) not the installer PID.
- **Why it matters:** Orphaned Python servers holding port 8765 across plugin reloads; second start fails to bind.
- **Suggested addition:** Specify the process tree: who is `--parent-pid` (game vs. installer vs. plugin), and confirm killing the installer cascades to the server, OR rely solely on `/shutdown` + watchdog and document that `StopInstance` must NOT hard-kill before the graceful path completes.

### GAP-3: First-Time wizard `Next`-gate and summary not extended for EchokrauTTS
- **Where:** §UI (lines 258-260) vs. `NativeFirstTimeWindow.cs:373-374, 488-496`.
- **Missing:** The wizard's `canNext` gate reads `_config.Alltalk.LocalInstall` and `_config.Alltalk.BaseUrl` directly (lines 374, 496), and the summary (`BuildFinishSummary`) prints `_config.Alltalk.*`. The plan adds an engine selector at Step 0 but doesn't say these gates/summaries must read the active engine.
- **Why it matters:** A user who picks EchokrauTTS + Local in the wizard will have the Next button gated on `Alltalk.LocalInstall` (false) → cannot proceed; or the summary shows AllTalk's URL.
- **Suggested addition:** List the specific `NativeFirstTimeWindow` sites (374, 488-496) that must switch to the active engine, mirroring the BLK-1 active-engine accessor.

### GAP-4: No test/spec for the F5-TTS `/tts` request body schema beyond field names
- **Where:** §"Backend client" line 173.
- **Missing:** The body `{sample, text, language, speed?, nfe_step?}` is asserted without a citation to the wrapper's `server.py` request model. The single-language rejection is at `server.py:152` — the exact field name (`language`) and whether omitting it bypasses the check (plan line 281 says "omit it, or set it to the server language") is an untested assumption.
- **Why it matters:** If the server *requires* `language` and validates it even when present-and-equal, "omit it" may 422; if it defaults to the loaded language when omitted, omit is correct. The plan hedges between two behaviors.
- **Suggested addition:** Pin the exact contract from `server.py` (does an omitted `language` skip the check, or default-and-pass?), and add a unit test for the chosen behavior. Until verified, treat as a phase-2 spike.

### GAP-5: No SonarQube / changelog / CLAUDE.md compliance mention for the new files
- **Where:** §Phasing line 358 (changelog/docs in phase 6 only).
- **Missing:** Project CLAUDE.md mandates a changelog touch *per commit* and a SonarQube scan after each build+test. The plan batches changelog to phase 6, which conflicts with the per-commit changelog gate (`.claude/hooks/echokraut-commit-gate.js` denies commits without a staged, EN/DE-synced changelog and a `<Version>` > latest GitHub tag).
- **Why it matters:** Phases 1-5 each end with a commit (plan line 348 "build + test + commit"); each will be blocked by the commit-gate hook unless it bumps `<Version>` and touches both changelog files.
- **Suggested addition:** Note that each phase commit must follow the per-commit changelog workflow (bump `<Version>` once to `0.19.1.0`, then append bullets per phase to `v0.19.1.0_EN/DE.txt`), not defer all changelog to phase 6.

### GAP-6: No graceful handling of engine switch while a generation/playback is in flight
- **Where:** §"Voice-copy" (line 240 "BEFORE RefreshBackend") + §"BackendService routing".
- **Missing:** Switching `BackendSelection` reconstructs `_backend` (BLK-1 fix) while `GenerationLoopAsync`/`PlaybackLoopAsync` may be mid-flight using the old backend and its captured `lastJobId`. `StopGenerating` on the new EchokrauTTS backend would POST `/cancel/{id}` for a job id that belongs to AllTalk.
- **Why it matters:** Stale job-id cancels, possible NRE if `_backend` swapped mid-call.
- **Suggested addition:** Specify that an engine switch must cancel/flush the queue (`CancelAll`) and stop the current playback before swapping `_backend`.

### GAP-7: No spec for what happens to `Alltalk.StreamingGeneration` semantics under EchokrauTTS
- **Where:** §Non-Goals line 38 (buffer-then-play) vs. AllTalk's `StreamingGeneration` flag (`AlltalkData.cs:21`, used at `AlltalkBackend.cs:73`).
- **Missing:** AllTalk's non-streaming branch is what writes to disk during generation. EchokrauTTS always buffers (v1). The plan doesn't say whether `EchokrauTtsData` needs a `StreamingGeneration` equivalent or how the disk-write timing differs (it now only happens in `OnSourceEnded`).
- **Why it matters:** If any UI surfaces a "Streaming generation" checkbox bound to the active engine, EchokrauTTS has no such concept; if disk-save relies on the AllTalk non-streaming write, EchokrauTTS save happens only post-playback (BLK-3).
- **Suggested addition:** State explicitly that EchokrauTTS has no streaming toggle and saves via `OnSourceEnded` only; hide the streaming checkbox when EchokrauTTS is active.

---

## Contradictions

### CON-1: "No behavior change yet" in phase 1 vs. centralizing the None-mode gate
- **Plan §Phasing (line 350):** phase 1 "engine-aware `HasLiveGeneration` ... No behavior change yet."
- **Plan §Risks (line 373) + reality:** centralizing requires rewriting the raw `Alltalk.InstanceType != None` checks in `VoiceMessageProcessor` and the `!= Alltalk` returns in `BackendService` (BLK-1). Re-routing those *is* a behavior-relevant change (it changes which engine's instance type is read).
- **Resolution needed:** Either accept that phase 1 changes behavior (and test that AllTalk-default behavior is preserved bit-for-bit), or split: phase 1 adds the accessor returning the AllTalk values only, and the re-route lands with phase 2 routing.

### CON-2: `ReloadService` no-op vs. `ITTSBackend` contract used by `NativeConfigWindow`
- **Plan §"Backend client" (line 184):** `ReloadService` is "a no-op returning true for v1."
- **Reality:** `BackendService.ReloadService` (`BackendService.cs:97-101`) calls `_backend.ReloadService(...).Result` and is invoked from the config UI (AllTalk model reload). For EchokrauTTS, "reload" semantically means restart-with-`--language`, which is the *instance service's* job, not the HTTP client's. A silent `true` no-op means any UI "Reload" button does nothing for EchokrauTTS with a success indication.
- **Resolution needed:** Decide whether the UI exposes Reload for EchokrauTTS at all (it should be hidden, since reload = process restart), or whether `ReloadService` should route to `IEchokrauTtsInstanceService` restart. Returning `true` for "did nothing" is misleading.

### CON-3: `EchokrauTtsData` properties are get-only but the model must be JSON-deserialized
- **Plan §"Config model" (lines 148-153):** `TtsPath`, `SamplesPath`, `HealthPath`, `ShutdownPath`, `CancelPath` are `{ get; } = "..."` (get-only with initializer).
- **Reality / AllTalk parity:** This mirrors `AlltalkData` (lines 10-14) which uses the same pattern, so it deserializes fine (Dalamud's config serializer skips get-only). BUT `BaseUrl` in `AlltalkData` is a *public field* (`AlltalkData.cs:9`), whereas the plan declares `EchokrauTtsData.BaseUrl` as a field too (line 148) — inconsistent with the `{ get; set; }` used for the mutable ones. Minor, but the existing SonarQube backlog (memory `project_sonarqube_cleanup`: S1104 public fields → properties) means adding a new public field re-introduces a flagged issue.
- **Resolution needed:** Make `BaseUrl` a `{ get; set; }` property (not a field) to avoid the S1104 regression the project is actively trying to clear.

---

## Unaddressed edge cases

| Case | Affected area | Consequence | Where to address |
|------|---------------|-------------|------------------|
| Switch to **None** | voice-copy (line 230) | `TtsPaths` has no "None voices folder"; `CopyVoicesForSwitch(x, None)` has no target | §Voice-copy: define that None is a no-op (no copy) |
| Switch when **source not installed** | voice-copy | covered (line 232 "source missing → no-op") — OK | — |
| Switch when **target engine running** | voice-copy + instance | copying into a live `samples/` dir while the server enumerates it; F5-TTS may not see new files until restart | §Voice-copy: note copy-then-restart, or copy only when target stopped |
| **Repeated switches** A→B→A | voice-copy "keep extras" | each switch re-copies + overwrites; extras accumulate; an AllTalk voice deleted by the user reappears from the EchokrauTTS copy | §Voice-copy: document that delete is not propagated (extras-kept is intentional but surprising) |
| **Folder-name mismatch** `voices/` vs `samples/` | voice-copy | covered by `TtsPaths` if both sides use it — but the merge is flat-file; AllTalk `voices/` may contain subfolders (per-voice dirs in some AllTalk layouts) | §Voice-copy: confirm AllTalk voices are flat files, not per-voice subdirs; the plan says "recursive not needed" — verify against the extractor output (`alltalk_tts/voices/*.wav` flat, per `AlltalkInstanceService.cs:127`) |
| **Port 8765 already in use** | instance start | bind failure → server never prints `ready` → infinite Ready-file poll | §Instance service: add a start timeout + NDJSON `error{fatal}` surfacing |
| **Client language unsupported by F5-TTS** | language handling | `--language` for a non-de/en/fr/ja client → bootstrap step-5 only preloaded 4 checkpoints; an unsupported `ClientLanguage` enum value falls through | §Language handling: map `ClientLanguage` → 4 supported codes with a default, mirroring `getAlltalkLanguage` (`AlltalkBackend.cs:176-187`) |
| **`X-Sample-Rate` header missing/malformed** | PCM→WAV | plan says fallback 24000 — OK, but parse failure (non-int) not specified | §Backend client: parse defensively, fallback on any failure |
| **Empty `/samples` response** | MapVoices | returns empty list (not null) → `MapVoices` treats all existing voices as "old" and deletes them | §Backend routing: distinguish "0 voices" (don't wipe) from "unreachable" (null), as `AlltalkBackend.GetAvailableVoices` already does (returns null on error, line 109) — ensure `EchokrauTtsBackend` returns **null** on HTTP failure, **empty** only on a genuine empty server |

---

## Untested assumptions

| Quoted phrase | Plan loc | Evidence that would verify it |
|---------------|----------|-------------------------------|
| "send the request without a mismatching `language` ... so the server never 400s" | line 280-282 | Read `server.py:152` request model — confirm omitting `language` defaults to loaded language vs. 422s on missing field |
| "buffer the full body, prepend a 44-byte WAV header (24000/16/1) ... so the existing BASS playback path consumes it unchanged" | line 176 | Verified partially: engine auto-detects WAV (`Live3DAudioEngine.cs:388`) — but the 24000 default coupling (`PlayStream` called w/o sampleRate) is unverified-by-plan; add an integration smoke test |
| "strip extensions to match the voice-name convention used by `MapVoices`" | line 171 | FALSE per `MapVoices` (`BackendService.cs:126/129`) — `BackendVoice` keeps extension; see BLK-4 |
| "the wrapper's parent-PID watchdog handles orphan cleanup" | line 200 | Confirm `--parent-pid` is the PID whose death the watchdog monitors, and that it's the right PID in the plugin's process tree (GAP-2) |
| "Verify the bootstrap honors it" (CpuMode) | line 378 | Read the wrapper bootstrap CLI — does `install_win.ps1`/`bootstrap.py` accept a CPU override flag? The plan's `installechokrautts` arg list passes `<cpuMode>` but the wrapper CLI (research line 61) lists no `--cpu` flag |
| "return `"Ready"` on success so the existing First-Time `TestConnection` works uniformly" | line 181 | Confirmed correct: `NativeFirstTimeWindow.cs:458` matches the literal `"Ready"` (case-insensitive). EchokrauTTS `CheckReady` must return exactly `"Ready"` on health=ok, NOT the raw `{status:"ok"}` body |

---

## Decision-propagation misses

| Decision | Where decided | Missing-from | Suggested addition |
|----------|---------------|--------------|--------------------|
| Engine-aware active-instance-type | §Config model (164) | `VoiceMessageProcessor.cs:156,162,169`; `BackendService.cs:70,85,186,242,346` | Route all raw `Alltalk.InstanceType != None` and `!= Alltalk` checks through the active-engine accessor (BLK-1) |
| Shared root via `TtsPaths` "single source" | §Shared root (130-135) | `NativeGameDataToolsWindow.cs:516`; `AlltalkInstanceService.cs:126` | Migrate inline `"alltalk_tts"` Path.Combine to `TtsPaths` (GAP-1) |
| New ELI release + `InstallerUrl` bump | §Local install (203) | `CheckAndDownloadLocalInstaller` cache-skip (`AlltalkInstanceService.cs:453`) | Add installer-version check so the new URL is actually fetched (BLK-5) |
| Engine selector in First-Time | §UI (258) | `NativeFirstTimeWindow` `canNext` (374) + summary (488-496) | Switch gates/summary to active engine (GAP-3) |
| `"Ready"` success token | §Backend client (181) | not reflected in the test list as an exact-string assertion | Add test "CheckReady returns literal 'Ready' on health=ok" |
| Per-commit changelog + version bump | project CLAUDE.md | §Phasing defers changelog to phase 6 (358) | Each phase commit bumps/touches changelog (GAP-5) |

---

## Terminology drift

| Concept | Synonyms found | Proposed canonical |
|---------|----------------|--------------------|
| The product/engine name | `EchokrauTTS` (plan, enum value), `EchokrauTtsBackend`/`EchokrauTtsData`/`EchokrauTts` (C# identifiers, lower-case `tts`), `Echokrautts` (the wrapper repo + `{root}\echokrautts\` folder + `EchokrautTtsUrl` JSON key, line 323) | Pick one casing per layer and document it: enum/UI string = `EchokrauTTS`; C# type prefix = `EchokrauTts`; on-disk folder + repo = `echokrautts`. The RemoteUrls key is spelled `EchokrautTtsUrl` (line 323, extra `t`) — should be `EchokrauTtsUrl` to match the type prefix. Flag the `EchokrautTtsUrl` typo. |
| Voice sample folder | AllTalk `voices/`, EchokrauTTS `samples/` | Keep both (they are real folder names) but always reference via `TtsPaths.AllTalkVoices` / `TtsPaths.EchokrauTtsSamples` so the mismatch is centralized, never hard-coded |

---

## Suggestions (non-blocking)

- The plan's `EchokrauTtsData.BaseUrl` should be a property not a field (CON-3) to avoid re-introducing SonarQube S1104.
- Add an explicit `Configuration.InstalledInstallerVersion` (or capability probe) — useful beyond this feature for any future installer change.
- Consider promoting `Alltalk.LocalInstallPath` to `Configuration.TtsInstallRoot` now rather than "future cleanup" — the plan already touches every consumer; doing it later means a second migration pass over the same files. (Plan acknowledges this as out-of-scope; flagging the cost.)
- `MapVoices` race/gender token logic (`UseAsRandom = voiceName.Contains("NPC")`, `IsDefault = Constants.NARRATORVOICE`) assumes AllTalk naming. Confirm the EchokrauTTS starter set uses the same `Male_Race_Name` / `NPC` / narrator conventions (it should, since the same extractor feeds both), else random-voice selection degrades.
- The plan's `/cancel/{id}` "best-effort, last job only" is weaker than AllTalk's global `/stop-generation`. Document the UX difference (queued EchokrauTTS jobs keep generating after a Stop) in the changelog/known-issues so it isn't reported as a bug.

## Scope of this review

- Documents read: 1 plan + 14 source files (listed above).
- Documents explicitly skipped: the `Echokrautts/wrapper` repo source (sibling repo, not in this working tree — could not verify `server.py:152` request-model details first-hand; flagged as untested assumptions GAP-4 / language table).
- Web searches performed: none.
- Confidence: **high** for BLK-1/BLK-2/BLK-3/BLK-4 and all propagation misses (verified against source); **medium** for BLK-5 / GAP-2 (depends on installer + wrapper internals not in this tree); **medium** for the language/`/tts` contract (wrapper source not available here).
