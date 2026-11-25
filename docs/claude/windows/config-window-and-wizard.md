# Windows — First-Time wizard and config window tabs

Moved verbatim from `Echokraut/Windows/CLAUDE.md` on 2026-11-20 (file was over the 15k budget). Does not load by itself — read it before working in this area.

## First-Time Setup wizard (`NativeFirstTimeWindow`)
- 3-step wizard: 0 = engine + mode choice, 1 = configure (per-engine/per-mode controls), 2 = finish/summary.
- **Engine-aware (P5c / GAP-3):** Step 0 has an engine selector (AllTalk / EchokrauTTS buttons → `Configuration.BackendSelection`, Alpha-highlighted) above the Local/Remote/None mode buttons. Mode buttons write the **active engine's** `InstanceType` via `IFirstTimeWizardController.ChooseMode` (mirrors `NativeConfigWindow.SetActiveInstanceType`); the gates are `CanAdvance` in the controller. Step 1 shows the AllTalk sections when `!isEk`, the EchokrauTTS sections (`NativeEchokrauTtsBuilder`) when `isEk`; None is engine-independent. All gates read `Configuration.ActiveInstanceType` / the active engine's `LocalInstall`/`BaseUrl`, never `Alltalk.*`.
- **Next button is mode-gated:** Local requires the active engine's `LocalInstall == true`, Remote requires a successful `IBackendService.CheckReady` against the *current* active `BaseUrl` (snapshot URL stored at test start; URL change invalidates), None always allowed.
- "Ready" is the only success token from `CheckReady` (both engines) — anything else counts as failure. `TestConnection` routes to the active engine's remote result label.
- The visibility signature includes `isEk` so switching engine re-flows the list. The install progress bar reads the active engine's install service.
- Step 2 summary (`BuildFinishSummary(instanceType, isEk)`) is rebuilt every frame from live config / install / test state; shows the chosen engine for Local/Remote.

## Config Window Tabs
1. **General** — backend settings, volume, "Voice Clip Manager" button
2. **Voices** — flat voice assignment list
3. **Phonetics** — phonetic correction rules
4. **Logs** — per-source log viewer

### None-mode tab gating (`HasLiveGeneration == false`)
Three TabBarNodes follow the same gate — anything that depends on a live backend (no audio-file fallback) is hidden when `Configuration.Alltalk.InstanceType == None`. Each tab bar uses a `bool? _xxxLiveGenSnapshot` field and only rebuilds when the snapshot flips, so radios aren't disposed/recreated every frame:

- **Top-level tabs** (`BuildTopTabs`): `Voices` + `Phonetics` hidden; `Settings` + `Logs` always present.
- **Settings sub-tabs** (`BuildSettingsTabs`): `Chat` hidden; `General`/`Dialogue`/`Storage`/`Backend` always present (Backend stays so the user can switch out of None mode).
- **Logs sub-tabs** (`BuildLogsTabs` in `NativeConfigWindow.Logs.cs`): `LogTabs` array carries a `LiveOnly` flag — `Chat`/`Cutscene`/`Choice`/`Backend` are LiveOnly and hidden in None mode. Panel + pagination arrays keep their full size so `OnLogUpdated`'s source→index lookups and the per-source accessor switches stay valid; only the visible tab buttons shrink. Background log writes continue, so history is preserved across mode switches.

Active-tab restoration after a rebuild: if the previously-active tab is still visible, re-select it; otherwise snap to index 0 (General/Settings). `TabBarNode.SelectTab` only updates the radio visual — panel visibility must still be applied via the corresponding `Show*Panel(index)` call.
