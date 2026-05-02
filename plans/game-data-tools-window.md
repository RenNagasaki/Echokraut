# Plan: Game Data Tools Window

## Context

Today the harvest controls live inside `NativeConfigWindow` under a "Data Harvest" collapsible
section. As we add more bulk-data features (in-game voice extraction, backup/restore,
voice-set import/export), Settings becomes the wrong home. We want a dedicated window for
all batch / data-pipeline operations:

- Quest dialog harvest (existing — moves here)
- In-game voice **starter set** extraction (new — this plan's main feature)
- Future: backup/restore, voice-set import/export

The new feature this plan focuses on: extract FFXIV's built-in voice acting from game `.scd`
files, decode to WAV, save N samples (user-configurable) per matched NPC into the user's
voice-files directory, so users can build a fresh voice set whenever they want.

## Source reference

`Echokraut Tools` (`F:/Git-Repositories/Dalamud/Echokraut Tools/`) already implements the
extraction logic against SaintCoinach. We port the same approach into the plugin. The
critical detail there: **no NPC mapping** — Tools writes to `wavs/<character_name>/...` based
on the 5th underscore-segment of the text key (e.g. `alisaie`, `hraesvelgr`). For the plugin
we need to map that short name to the matching DB character row.

---

## 1. Window: `NativeGameDataToolsWindow`

**File:** `Echokraut/Windows/Native/NativeGameDataToolsWindow.cs` (new)

Loc key: `"Game Data Tools"` (window title — user can redirect at review time).

Layout (top to bottom inside `ScrollingListNode`):

```
┌─ Game Data Tools (window) ─────────────────────┐
│ [Quest Dialog Harvest] (collapsible)           │
│   description label                            │
│   [Start Harvest] (button)                     │
│   [Quest ID input] [Export Lua Debug]          │
│                                                │
│ [Voice Starter Set] (collapsible)              │
│   description label                            │
│   "Samples per NPC (1—5): [input]" (field)     │
│   [Build Starter Set] (button)                 │
│   status / progress label                      │
│                                                │
│ [Import / Export] (collapsible, future)        │
│   placeholder for backup/restore + voice-set   │
│   import/export buttons                        │
└────────────────────────────────────────────────┘
```

- Collapsibles via the existing `CreateCollapsibleSection` helper used in
  `NativeConfigWindow`.
- All text via `Loc.S(...)` with EN/DE/FR/JP entries.
- Window registered in `NativeWindowManager` alongside config/voice-clip-manager.
- Hit-testable so click suppression works (`SetWindowHitTest` pattern).
- Open via:
  - Existing chat command: extend `/ek` with a sub-command (`/ek tools`) OR add a button in
    `NativeConfigWindow` General tab.
  - `IWindowManager.ToggleGameDataTools()` — new method on the interface.

### Migration of existing harvest UI

Move out of `NativeConfigWindow.cs:701-706`:
- `_harvestButton`, `_debugQuestIdInput`, `_debugExportButton`
- `OnHarvestClick`, `OnDebugExportClick`
- `_harvestCts` field

Move into the new window. Leave nothing behind in the General config tab.

---

## 2. Service: `IVoiceSampleExtractorService` / `VoiceSampleExtractorService`

**Files:**
- `Echokraut/Services/IVoiceSampleExtractorService.cs` (new, interface)
- `Echokraut/Services/VoiceSampleExtractorService.cs` (new, impl)

Mirrors the harvest service shape: long-running async run, BeginPhase progress events,
cancellation token, per-source unmatched-JSON output.

### Interface

```csharp
public interface IVoiceSampleExtractorService
{
    bool IsRunning { get; }
    event Action<string, int, int>? ProgressChanged; // label, current, total
    Task RunAsync(ClientLanguage language, int samplesPerNpc, CancellationToken ct);
}
```

### Algorithm (per `RunAsync`)

1. **Phase 1: Harvest text-key → speaker-shortname map.**
   Iterate Lumina sheets `cut_scene/*`, `Balloon`, `InstanceContentTextData`, `ManFst` (same
   set Tools uses). For each row, parse the text key:
   - Pattern: `TEXT_<expansion>_<questId>_<speakerName>_<scene>_<line>` — 5 underscore-
     separated segments after `TEXT`.
   - Extract `speakerName`, lowercase it (Tools does this already).
   - Build map `(textKey, speakerShortName, languageText)`.
   - Matches Tools' `WorkText` / `CleanUpLine` cleanup (player-gender if/else expansion,
     `<forename>` strip, etc.) — **port that logic verbatim** into a private helper.

2. **Phase 2: Map shortname → DB character row.**
   We already have the harvest-built `npcNames` lookup (`Dictionary<uint, Dictionary<string,
   string>>`). Build a normalized index: English name → lowercase, strip spaces / apostrophes
   / hyphens, take the **first word** OR full normalized form. Match the speakerShortName
   against it:
   - **Direct match**: `speakerShortName == normalized(en)` → resolved.
   - **Substring match**: `normalized(en).StartsWith(speakerShortName)` (e.g. `alisaie`
     matches "alisaieleveilleur") → resolved.
   - **Multi-match**: take first, log Debug with alternatives (same pattern as alias system).
   - **No match**: append to `voice_extract_unmatched.json` with the shortname,
     example text, and example file path. Same pattern as `quest_alias_candidates.json`.

   *Aliases:* extend the existing `quest_npc_aliases.json` schema or introduce a parallel
   `voice_extract_aliases.json` (decision below in Open Questions). Initial vote: **parallel
   file**, since the keys are different (shortname vs. NpcNameKey).

3. **Phase 3: Locate + extract SCD files.**
   For each matched `(textKey, npcId)` entry, build the SCD path the same way Tools does:
   ```
   cut/{expansion}/sound/{NNN}/{NNNNNN}/vo_<base>_{lang}.scd
   ```
   where `{NNN}` and `{NNNNNN}` are derived from the audio file name (Tools does
   `audioFile.Substring(3, 6)` / `Substring(3, 14)`). Resolve via Dalamud's
   `IDataManager.GetFile(path)` (Lumina). For each expansion key in `PackIdentifier
   .ExpansionToKeyMap`, try the path until one resolves. (Tools iterates expansions for the
   same reason.)

4. **Phase 4: Decode SCD → WAV.**
   Use `SaintCoinach.Sound.ScdFile`. Approach:
   - Add SaintCoinach as a NuGet/project reference. The SCD code lives in
     `SaintCoinach.dll` and depends on the larger SaintCoinach.IO file abstraction.
   - **Adapter required**: `SaintCoinach.Sound.ScdFile` constructor takes a
     `SaintCoinach.IO.File`. We have raw bytes from Lumina. Write a thin
     `RawBytesPackFile : SaintCoinach.IO.File` adapter — or, if SaintCoinach's API requires
     a full `Pack`, fork just the SCD-parsing part into our own `ScdReader` class.
   - First try the adapter; fall back to a forked parser only if the dependency surface is
     too wide. **This is a research+implement step**, see Open Questions.
   - Decoded entries: `e.GetDecoded()` returns the decoded MS-ADPCM/OGG bytes that Tools
     writes directly to a `.wav` file.

5. **Phase 5: Length filter (with fallback).**
   Read each decoded WAV's duration from the WAV header (manual parse — no NAudio).
   - **Primary**: keep clips in **6—12 seconds**.
   - **Fallback when an NPC has zero clips in the 6—12s window**: take the clip whose
     duration is **closest to the [6, 12] interval** (i.e. for clips < 6s, pick the longest;
     for clips > 12s, pick the shortest). Ensures every matched NPC contributes at least one
     sample even when their voice lines are unusually short or long.
   - The fallback applies per-NPC, not per-clip: only invoked if the NPC's in-range bucket
     is empty after the primary filter.

6. **Phase 6: Resample to 22050 Hz.**
   FFXIV SCDs are typically 44100 Hz. AllTalk's voice-cloning pipeline expects 22050 Hz
   (Tools' default). For each kept clip, run a sample-rate conversion. Decision required —
   see "Open Questions: Resampling backend" — between bundling `ffmpeg.exe` (matches Tools
   exactly) or doing in-process resampling.

7. **Phase 7: Pick N samples per NPC + write to disk.**
   `samplesPerNpc` from the slider. After collecting all valid 22050 Hz clips per matched
   NPC:
   - If count <= N: keep all.
   - If count > N: random selection (deterministic seed off `NpcId` for reproducibility).
   - Build the canonical voice file name: `Gender_Race_Name` where:
     - `Gender` = "Male" / "Female" / "None" (from DB character row)
     - `Race` = English race string (from DB character row, e.g. "Hyur", "Elezen")
     - `Name` = NPC name **in the current client language** (so a German client gets
       "Y'shtola Rhul" written as the German variant; an English client gets the English
       form). Sanitize for filesystem: replace `/`, `\`, `:`, `*`, `?`, `"`, `<`, `>`, `|`
       with `_`.
   - **Output structure** (under `<Configuration.LocalSaveLocation>/FF14-Voices/`):
     - **N == 1** (flat): `FF14-Voices/Gender_Race_Name.wav` — one file per NPC.
     - **N > 1** (foldered): `FF14-Voices/<Name>/Gender_Race_Name_1.wav`,
       `Gender_Race_Name_2.wav`, … `Gender_Race_Name_N.wav`. Subfolder name is just the
       localized `Name` (sanitized, no Gender/Race prefix), so AllTalk's voice browser shows
       them grouped by character display-name.
   - **No DB write.** This output is purely a folder structure for AllTalk to consume —
     Echokraut's own `voice_clip_generations` table is **not** touched.

8. **Phase 8: Random-voice NPC catalog.**
   For NPCs whose **raw** matched-clip count (every text-key resolved to this NPC, before
   length filter and before slider truncation) is **< 20**, additionally write the same N
   samples into a fixed `FF14-Voices/NPC/` folder for use as random voices for unmapped
   NPCs at runtime.
   - **Filename**: `Gender_All_NPC{ID}_{n}.wav` where:
     - `Gender` = same Male/Female/None as in the main folder
     - `All` = literal string (replaces Race — random voices should match any race)
     - `NPC{ID}` = literal `NPC` followed by a globally-incrementing **3-digit zero-padded
       index**: `NPC001`, `NPC002`, …, `NPC999`. (3 digits matches your spec; if catalog
       grows past 999 we widen to 4 digits — flagged in Open Questions.)
     - `{n}` = sample index within the pack (1—N matching the slider).
   - **ID assignment**: deterministic — sort eligible NPCs by canonical English name (then
     by NpcId tie-break) and increment. Re-runs with the same set of NPCs produce the same
     IDs, so users who manually post-process the catalog don't see numbering churn.
   - **Eligibility threshold**: `availableClipsBeforeSliderTruncation < 20`. This is the
     "minor NPC" filter — main characters with hundreds of voice lines are not pooled into
     the random catalog.
   - **No deduplication** between the named folder and the NPC catalog: a minor NPC appears
     in **both** `FF14-Voices/Name/` and `FF14-Voices/NPC/`. Disk cost is negligible
     (5 small files × few hundred minor NPCs).

### Output root

Fixed to `<Configuration.LocalSaveLocation>/FF14-Voices/`. Created on demand
(`Directory.CreateDirectory`). No per-run override.

### What we DO NOT port from Tools

- The 240-cap dataset trim (Tools-specific to fine-tuning AllTalk).
- `metadata_train.csv` / `metadata_eval.csv` writeout (training-only).
- The `WavEffectGate` quality-gate filter (Tools-internal experiment).
- DB writeback (this set is consumed by AllTalk, not by Echokraut's runtime cache).

### Performance / robustness

- Run on background thread (`Task.Run`) like the harvest.
- Progressive `BeginPhase` updates so the slider window shows a moving bar.
- Cancellation: every loop iteration checks `ct.ThrowIfCancellationRequested()`.
- Errors per-file: log Warning (not Error — recoverable per the log-level rule), skip and
  continue.

---

## 3. Slider Component

The existing native UI doesn't have an integer-slider node. Two options:

**Option A — Reuse existing input**: `Input` field that accepts an int 1—5, validates on
LostFocus. Functional but no visual slider.

**Option B — Build `IntSliderNode`**: a thin KamiToolKit wrapper around an `HSliderNode` (if
KamiToolKit has one) or a custom drag-rect implementation. Reusable across the codebase.

**Vote: A**, until we need more sliders elsewhere — keep scope tight. The tooltip / label
makes the constraint obvious.

Loc key suggestion: `"Samples per NPC (1—5):"`. The ":max" extra slot you mentioned
("min and max for the slider") I read as just "1 and 5 are the bounds" — clarify if you
meant something else.

---

## 4. Localization

New keys (EN, DE, FR, JP):
- Window title: `"Game Data Tools"`
- Section: `"Quest Dialog Harvest"`
- Section: `"Voice Starter Set"`
- Section: `"Import / Export"` (placeholder, future)
- Description: `"Extract a voice sample set from in-game audio files. One SCD per matched NPC, length 6—12s. Re-runnable anytime — re-builds from scratch."`
- Field: `"Samples per NPC (1—5):"`
- Button: `"Build Starter Set"`
- Status formats: `"Extracting samples: {0}/{1}"`, `"Done: {0} clips saved across {1} NPCs"`

Existing keys reused: `"Start Harvest"`, `"Stop Harvest"`, `"Export Quest Lua Debug"`.

---

## 5. DI Wiring

**File:** `Echokraut/Services/ServiceBuilder.cs`

```csharp
container.RegisterFactory<IVoiceSampleExtractorService>(c => new VoiceSampleExtractorService(
    dataManager,
    clientState,
    c.GetService<ILogService>(),
    c.GetService<IJsonDataService>(),
    configuration));
```

`clientState` is needed for the current client `ClientLanguage` (file-name uses NPC's name
in the running client's language, not the harvest-language argument). No `IDatabaseService`
dependency — the starter set never touches the DB.

**File:** `Echokraut/Windows/NativeWindowManager.cs`
- Add `_gameDataTools` field
- `ToggleGameDataTools()` method
- Hit-test registration

---

## 6. Tests

**File:** `Echokraut.Tests/VoiceSampleExtractorTests.cs` (new)

Cover the deterministic units:
- TextKey parsing (5-segment → speakerShortName extraction)
- Player-gender if/else expansion (port Tools' `ReplaceGenderText` test cases)
- Name-mapping resolution (direct, substring, multi-match, no-match)
- Random sample selection (deterministic seed → same output)
- Length filter primary (6—12s gate)
- Length fallback (zero-in-window NPC → closest clip picked)
- File-name sanitization (`Gender_Race_Name` with FS-illegal chars stripped)
- Output structure decision (N==1 flat vs. N>1 foldered subfolder = `Name`)
- NPC catalog eligibility (clip count < 20 → catalog entry created; >= 20 → not)
- NPC catalog ID assignment (deterministic order across re-runs, zero-padded format)

Cannot easily test:
- SCD decoding (needs game files / SaintCoinach realm)
- BASS resampling (runtime-only, audio quality is subjective anyway)

Target: 12—15 tests on the parsing + mapping + selection + naming + catalog logic.

---

## 7. Open Questions / Risks

### SaintCoinach in plugin context

SaintCoinach was designed as a standalone game-data reader. The plugin runs **inside** the
game with Dalamud's Lumina pipeline. Risks:
- `SaintCoinach.IO.File` may pull in `Pack`, `PackIdentifier`, `ARealmReversed` — heavy
  dependencies on game-install path resolution.
- We don't want to instantiate `ARealmReversed` (slow, redundant since Lumina already has
  the data loaded).
- **Mitigation A**: write a tiny `SaintCoinach.IO.File` subclass that wraps a `byte[]` from
  Lumina. If the SCD parser only reads `GetData()` we're fine.
- **Mitigation B (fallback)**: fork just `SaintCoinach.Sound.ScdFile` + decoders into
  `Echokraut/Helper/Functional/ScdReader.cs`. Self-contained, no SaintCoinach dependency.
  More work upfront but cleaner long-term.

**Decision needed before implementation**: prototype mitigation A for 1 hour; if it works,
ship that. If the dependency surface is too wide, fall back to B.

### Alias file: shared or separate?

Quest aliases (`quest_npc_aliases.json`) key on `(QuestId, NpcNameKey)`. Voice-extract
unmatched names key on `speakerShortName` only (no quest scope — same shortname across
different quests is the same character). Schemas differ enough that a shared file is
awkward.

**Vote: separate file** `voice_extract_aliases.json`, embedded + remote + user-local
layering identical to the quest version. Different file, same loader pattern.

### NAudio dependency for length detection

Tools uses `NAudio.Wave.WaveFileReader`. NAudio is large (~1 MB DLL). Alternatives:
- Read the WAV header manually (44-byte standard header for the formats SCD outputs)
- The Echokraut plugin already uses BASS for playback — does BASS expose length?

**Vote: read the WAV header manually**. 30 lines of code, no new dependency. Length =
`dataChunkSize / (sampleRate * channels * bytesPerSample) * 1000` ms.

### Resampling backend (22050 Hz output) — DECIDED

Investigation against the SCD format (Ioncannon / xivapi research) showed that FFXIV uses
OGG only for music; voice content under `cut/*/sound/` is overwhelmingly MS-ADPCM. Tools'
ffmpeg pipeline only handles MS-ADPCM and silently loses OGG entries via NAudio's
`WaveFileReader` exception. We replicate the MS-ADPCM scope explicitly and skip OGG with a
counted log line.

**Decision: pure-C# pipeline, no ffmpeg, no BASS-Mix addon.** Three new helpers live under
`Helper/Functional/`:

- `MsAdpcmDecoder` — RIFF/WAVE → int16 PCM. Standard 7-coef MS-ADPCM (matches ffmpeg's
  `adpcm_ms`). Handles mono and stereo.
- `WavResampler` — windowed-sinc (Hann, 16 taps default), anti-aliased on downsampling.
  Plus a `DownmixToMono` helper.
- `PcmWavWriter` — uncompressed 16-bit PCM RIFF/WAVE byte buffer.

Quality is more than enough for AllTalk voice-cloning input (the network re-extracts
spectrograms internally). If a real run shows substantial OGG skip counts, the fallback is
to wire BASS as in-process decoder (`bass.dll` already ships) and reuse `WavResampler`/
`PcmWavWriter` unchanged.

### Slider semantics

"1—5 min and max" — I read as: bounds are 1 and 5. If you meant "min default = 1, max can
be slid up arbitrarily", flag this and I'll change the implementation to bounded but
expandable (slider 1—10, default 3).

### Re-run behaviour

If the user re-runs Build Starter Set, do we:
- Clear the previous output folder first? (Risks deleting user-edited samples)
- Skip files that already exist? (Faster but stale)
- Always overwrite? (Predictable, no surprises)

**Vote: always overwrite**. The whole point is "build a fresh set". If the user wants to
keep old samples, they'd back up the folder first. Document in the description label.

### NPC catalog ID format and cap

3-digit zero-padded `NPC001` matches the spec but caps at 999. FFXIV has thousands of named
NPCs, but only a fraction are voiced AND have < 20 clips, so 999 is probably enough. If a
real-world run exceeds 999 we widen at runtime (`NPC0001` once any ID >= 1000 is needed).
Worth a runtime warning log at >800 so the user gets a heads-up.

### Eligibility threshold for the catalog

The "< 20 clips" filter runs against the **raw** clip count — the total number of
text-keys that resolved to this NPC, before any length filter or slider truncation.
Reasoning: a "main character" is defined by how much voice content the game ships for them
in total, not by how many of those happen to fall in the 6—12s window. An NPC with 30 raw
clips but only 2 in-range is still a major character — keep them out of the random pool.

### Localized name in catalog filename

The catalog filename uses literal `NPC<ID>` and never the localized name, so the catalog is
language-stable across re-runs in different client languages. Only the **named folder**
(`FF14-Voices/Name/...` and `FF14-Voices/Gender_Race_Name.wav`) carries the client-language
name. This means a German client and an English client running the catalog produce the same
`NPC<ID>` filenames but different `Name`-folder content — intentional.

### Trigger UI before this window exists

The plan moves the harvest button out of the Settings General tab into the new window. The
new window needs a way to be opened. Suggested: add a "Game Data Tools" button to the
Settings General tab where the harvest button used to be (one-line replacement).

---

## 8. Phase Order (implementation steps)

1. New window skeleton (empty sections, opens/closes correctly, hit-test works).
2. Move harvest UI into the new window. Verify nothing regressed on the existing harvest.
3. SaintCoinach dependency check (1-hour prototype on mitigation A vs. B).
4. `IVoiceSampleExtractorService` skeleton with name-mapping (Phase 1+2 of the algorithm),
   no SCD decoding yet — just produce the matched-list and unmatched JSON.
5. SCD decoding (Phase 3+4). Verify against a known clip in-game.
6. Length filter incl. closest-fallback (Phase 5).
7. BASS resampler integration → 22050 Hz (Phase 6). Verify a single clip round-trips
   correctly before going bulk.
8. Sample picking + named folder-structure writeout with `Gender_Race_Name` naming
   (Phase 7).
9. NPC random-voice catalog writeout (Phase 8) with deterministic global ID assignment.
10. UI polish: slider, status updates, cancellation.
11. Localization sweep — every new string in EN/DE/FR/JP.
12. Tests for the deterministic units.
13. SonarQube scan, fix new findings.

Estimate: 2—3 days of focused work, assuming SaintCoinach prototype works (mitigation A) and
BASS resampling works (resampling option B). Add 1 day if either falls back.

---

## 9. Out of Scope (explicit non-goals)

- Generating training datasets (Tools' job, not the plugin's).
- Quality-gating clips via WavEffectGate (Tools' experiment).
- DB persistence — starter set is a pure file-output feature, never enters
  `voice_clip_generations`.
- Voice-set sharing / community pack distribution.
- Backup/restore (separate TODO #1, this window will host that button later).
- FirstTime wizard integration (deferred; this window is the first home).
