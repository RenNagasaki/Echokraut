# Voice Sample Improvements Plan

Two connected improvements to the voice-data pipeline: a speech-content filter for the existing
starter-set extractor, and a new fine-tuning dataset export feature.

**Version target:** 0.19.0.1 (current csproj)
**Branch convention:** both changes touch >3 files → single feature branch,
e.g. `feature/voice-sample-improvements`.

---

## Goals & Non-Goals

### Goals
1. **Issue 1 — Speech-content filter**: Drop non-speech candidates (laughs, grunts, single-word
   exclamations, onomatopoeia) from the starter-set extractor before the length filter and
   sample-picker run. The text is already in-hand; no extra Lumina lookups needed.

2. **Issue 2 — Dataset export**: Add a new "Export Fine-Tuning Dataset" action in
   `NativeGameDataToolsWindow` that produces an AllTalk/XTTS-compatible dataset directory from
   the **same FFXIV SCD voice files** the Voice Sample Extractor already uses — NOT from
   runtime-generated AllTalk output. The user wants real ingame voice acting as training data.
   Per-voice cap of 200 train samples + 10–15% eval. Hard 19-second max duration (AllTalk
   `finetune.py` limit). Actor-change-aware: voices that switched dub actor mid-game (e.g.
   Y'shtola DE pre-6.0 vs 6.1+) get split into multiple datasets driven by a new
   `VoiceActorSplits.json` config.

### Non-Goals
- Changing the existing SCD-decode or resample pipeline.
- Supporting XTTS multi-speaker training in this iteration (AllTalk's `finetune.py` is
  single-speaker per run; the UI should export one NPC voice at a time).
- OGG decoding (already deferred; no change here).
- Modifying KamiToolKit or Echotools submodules.
- Automatic upload to AllTalk or trigger of training — the feature writes files and stops.

---

## Research Findings — XTTS / AllTalk Fine-Tuning Dataset Format

Sources consulted:
- AllTalk wiki: "XTTS Finetuning Manually Creating Your Own Dataset"
  `https://github.com/erew123/alltalk_tts/wiki/XTTS-Finetuning-Manually-Creating-Your-Own-Dataset`
- AllTalk `finetune.py` (main branch, inspected via WebFetch)
- AllTalk wiki: "XTTS Model Finetuning Guide (Simple Version)"

### Canonical dataset layout

```
<project-root>/
├── metadata_train.csv
├── metadata_eval.csv
├── lang.txt
└── wavs/
    ├── clip_001.wav
    ├── clip_002.wav
    └── ...
```

### CSV format

Separator: **pipe** (`|`). Three columns. Header row required.

```
audio_file|text|speaker_name
wavs/clip_001.wav|She speaks so softly, yet carries great resolve.|Yshtola
wavs/clip_002.wav|The stars hold many secrets, traveler.|Yshtola
```

- `audio_file`: relative path from the project root to the WAV file. Always `wavs/<filename>`.
- `text`: cleaned transcript, no pipe characters. Must match the audio exactly.
- `speaker_name`: arbitrary label; AllTalk's `finetune.py` uses this as the project identifier.
  For single-NPC export use the NPC's canonical voice name (e.g. `Yshtola`).
  For a multi-NPC batch export (future work, not in this plan) different speaker names per row
  would enable multi-speaker training, which AllTalk supports in theory but does not expose via
  a simple UI button. Defer multi-speaker to a future iteration.

### Train / eval split

- `metadata_eval.csv`: 10–15% of total clips, no overlap with train set.
- Deterministic split: sort by filename, take every Nth row into eval (e.g. every 10th).
  Seeded shuffle is also acceptable; sorting is simpler and reproducible.

### Audio format

From `finetune.py` source:
- `XttsAudioConfig(sample_rate=22050, dvae_sample_rate=22050)` — training uses **22050 Hz**.
- Output inference runs at 24000 Hz (different stage, irrelevant here).
- Accepted input extensions: `.wav`, `.mp3`, `.flac`. The script calls `torchaudio.load()`
  which handles resampling internally if needed, but 22050 Hz WAVs are the cleanest path.
- Bit depth and channel count: not explicitly documented, but 16-bit mono PCM at 22050 Hz is
  what AllTalk's own preprocessing produces and what `VoiceSampleExtractorService` already
  writes. The existing `MsAdpcmDecoder` → `WavResampler` → `PcmWavWriter` pipeline produces
  exactly that output for SCD inputs, so dataset clips reuse it as-is.

### Hard 19-second duration cap

AllTalk's `finetune.py` rejects training samples longer than ~19 seconds (the XTTS DVAE has a
fixed-length conditioning window and longer clips are dropped or fail the preprocessor). Per
user direction: **enforce a hard 19.0-second max duration on every clip before it's added to the
dataset**. Clips longer than that are silently skipped (logged at Debug level, count surfaced in
the run summary).

### `lang.txt`

Two-letter ISO language code. Must match the language the transcript is in.
Under the single-language design (only client language is exported), `lang.txt` is just the
2-letter code of the client language at run time — `en` / `de` / `fr` / `ja`. No
majority-vote / fallback logic. Reuse `VoiceScdPaths.LanguageCodeForScd(language)` so the
mapping stays single-sourced.

---

## Issue 1 — Speech-Content Filter

### Precise insertion point

File: `Echokraut/Helper/Functional/VoiceExtractTextCleaner.cs`

Create a new `internal static` method `IsSpeech(string cleanedText, ClientLanguage language)`.
Call it in `VoiceSampleExtractorService.RunInternal` (line ~171) immediately after the
`VoiceExtractTextCleaner.Clean(text, language)` call and the null/empty guard, before the
candidate is added to `perNpcKeys`. No structural change to the existing pipeline; this is a
pure additional predicate.

Approximate insertion point in `RunInternal` (currently lines 170–215):

```csharp
var cleaned = VoiceExtractTextCleaner.Clean(text, language);
if (cleaned == null || cleaned.Length == 0 || string.IsNullOrWhiteSpace(cleaned[0])) continue;

// NEW: speech-content filter
if (!VoiceExtractTextCleaner.IsSpeech(cleaned[0], language)) continue;
```

Using `cleaned[0]` (male variant) is sufficient for the predicate — both variants share the
same structural content type; if the male version is non-speech, the female version will be too.

### Proposed heuristic (with rationale)

The heuristic operates on the already-cleaned text (after `CleanLine` and gender expansion,
before the candidate list). It is fast, allocation-light, and locale-aware.

**Step 1 — Reject pure interjection patterns** (locale-agnostic):

Reject if the text matches any of:
- Only punctuation and whitespace (e.g. `...`, `……`, `!?`)
- A run of repeated characters (laughs/cries): match `^[A-Za-zÀ-ÖØ-öø-ÿ]\1{4,}[!?]*$` — catches
  `Hahaha!`, `Wahaha`, `Heehee`, `Noooo`. Threshold: 5+ repetitions of the same leading char.
  The regex can be pre-compiled as a static field.
- Bracketed/asterisked sound descriptions: `^\*.*\*$` or `^\[.*\]$` — catches `*sigh*`,
  `[laughter]`. (Note: `VoiceExtractTextCleaner.CleanLine` already strips `[...]` at line ~159,
  so this is a belt-and-suspenders guard for any that slip through.)
- Only ellipsis variants: `^[\.…\s]+$`

**Step 2 — Minimum word count by locale:**

Count words by splitting on whitespace. Require:

| Language | Min words | Rationale |
|----------|-----------|-----------|
| English  | 4         | "Yes." / "I see." / "Hmph." are non-speech; "Thank you for coming." is fine |
| German   | 3         | German tends to compound words; 4-word EN ≈ 3-word DE |
| French   | 4         | Similar structure to EN |
| Japanese | 0 (char-based) | Word-boundary splitting is unreliable in CJK; use char count instead |

For Japanese: require `cleanedText.Length >= 8` (after cleaning). A 4-character Japanese line
is typically a single grammatical unit — functionally equivalent to "I see." in English.
Kanji density means 8 chars is roughly equivalent to 3–4 EN words of content.

**What NOT to filter on:**
- Line duration (already handled by `ApplyLengthFilter` / `PickN`; the text filter runs on the
  full candidate list, not on decoded PCM).
- Speaker-prefix lines (already stripped by `CleanLine` at line ~158 via the `\(-.*?-\)` regex).
- Proper nouns or short names (a 4-word line like "I trust you, Yshtola." is valid; don't add
  a stop-list of known interjections because it would be a maintenance burden and brittle).
- Gender-split lines that produce different word counts per variant — use only cleaned[0].

### Configurability

Do **not** add a config toggle for the initial implementation. The filter is structural
(removes objectively non-speech content) rather than aesthetic. If power users want unfiltered
output they can already get it by running the extractor without the speech filter in the DB-seeded
export path (Issue 2). If a toggle is later requested, add it as a `bool FilterNonSpeech` to
`Configuration` with a UI checkbox in the Voice Starter Set collapsible section.

### Language-awareness approach

`IsSpeech(string cleanedText, ClientLanguage language)` receives the client language and branches
on the word/char threshold as described above. This mirrors the existing pattern in
`VoiceExtractTextCleaner.CleanLine` which already switches on `ClientLanguage.Japanese`.

### Test plan

All tests belong in `Echokraut.Tests/VoiceSampleExtractorTests.cs` (existing file, extend it).
No game runtime or Lumina needed — `IsSpeech` is a pure static method.

Test cases to add:
- `IsSpeech_RejectsPurePunctuation` — `"..."`, `"!?"`, `"……"` → false (all languages)
- `IsSpeech_RejectsRepeatedCharLaughs` — `"Hahaha!"`, `"Wahaha"`, `"Heeheeheehee"` → false
- `IsSpeech_RejectsBracketedSoundDesc` — `"*sigh*"` → false
- `IsSpeech_RejectsSingleWord_EN` — `"Hmph."`, `"Yes."` → false (EN, <4 words)
- `IsSpeech_AcceptsShortRealLine_EN` — `"I trust you."` (3 words) → may be borderline; test at
  the threshold boundary and document the chosen value in the test name
- `IsSpeech_AcceptsNormalLine_EN` — `"She speaks so softly, yet carries great resolve."` → true
- `IsSpeech_AcceptsShortLine_DE` — `"Ich verstehe."` (2 words) → borderline per German threshold;
  adjust threshold based on real-data inspection before settling
- `IsSpeech_Japanese_CharCountThreshold` — 7-char string → false, 8-char string → true
- `IsSpeech_Japanese_AcceptsNormalLine` — realistic JP dialog → true
- `IsSpeech_AcceptsLaugh_WhenNotRepeated` — `"Ha, that was unexpected."` → true
  (contains "Ha" but not a pure repeat run)

Threshold values should be expressed as constants inside `VoiceExtractTextCleaner` so tests can
reference the same values without duplication.

---

## Issue 2 — Dataset Export

### Source data: FFXIV in-game SCD voice files (NOT runtime-generated audio)

Per user direction, the dataset draws from the same source as the Voice Sample Extractor — FFXIV's
own voice-acted SCD files via Lumina. Runtime-generated AllTalk output is NOT used. Rationale:

- The user wants real human voice acting as training data, not synthetic-on-synthetic which risks
  mode collapse.
- Audio quality is uniform (FFXIV's own production-quality recordings).
- Transcript fidelity: SCD text-key text passes through `VoiceExtractTextCleaner.Clean` and is
  the canonical source of what the actor said.
- Coverage: every voiced NPC line in the game is reachable, not just lines the user has seen.

The legal grey area around training on Square Enix's audio is acknowledged. The plugin produces
the dataset on the user's machine; what they do with it (local fine-tuning vs. distribution) is
their decision. No upload or redistribution is automated.

### Pipeline reuse with `VoiceSampleExtractorService`

The dataset export reuses the SCD-discovery + decode + resample pipeline already in
`VoiceSampleExtractorService`. Refactor target: extract the per-NPC candidate-collection loop
(`RunInternal` lines ~140–220, the part that populates `perNpcKeys`) into an internal helper that
both the starter-set extractor and the dataset exporter call. The two services then differ only
in the *selection* and *output* phases:

| Phase | Voice Sample Extractor | Dataset Export |
|-------|------------------------|----------------|
| Discovery (Lumina + alias map) | shared | shared |
| Per-NPC candidate list | shared | shared |
| Speech-content filter (Issue 1) | applied | applied |
| `ApplyLengthFilter` | applied | applied |
| Per-clip duration cap | n/a (PickN trims via length filter) | **hard 19.0 s max** |
| Actor-split partitioning | n/a | **applied** (per `VoiceActorSplits.json`) |
| Selection | `PickN` returns N samples | **all candidates pass through** |
| Per-voice + per-epoch cap | `samplesPerNpc` (1–5) | **200 train + 24 eval (~12%)**, applied LAST |
| Output format | per-NPC starter `{NPC}.wav` files | XTTS dataset folder + CSVs |

The shared helper signature should look like:
```csharp
internal IDictionary<uint, List<VoiceLineCandidate>> CollectCandidatesPerNpc(
    ClientLanguage language,
    Dictionary<string, uint> aliasMap,
    EKEventId eventId,
    CancellationToken ct);
```
Both services call it; the difference is what they do with the dictionary.

### Filter pipeline (in order, as applied in `DatasetExportService.RunInternal`)

The filter chain runs ONCE per candidate, in this order. Earlier filters reject more cheaply.

1. **Speech-content filter** (`VoiceExtractTextCleaner.IsSpeech`) — same predicate as Issue 1.
2. **Existing `ApplyLengthFilter`** — drops obvious silence + outliers.
3. **Hard 19.0 s max duration** — measured from the decoded PCM (frame count / sample rate).
   Computed *after* MS-ADPCM decode but *before* resample (cheaper to drop early). Logged at
   Debug. Surfaced in run summary as `skipped_too_long`.
4. **Actor-split partitioning** (see below) — assigns each surviving candidate to an "epoch"
   bucket based on the audio path. Voices not in the splits config use a single default epoch
   `"all"`.
5. **Per-voice + per-epoch cap: 200 train + ~12% eval** — applied LAST, after all rejection
   filters have run. Deterministic shuffle (seed = stable hash of `{voiceKey}|{epoch}`), take
   first 200 for train, next 24 (12% of 200) for eval. Excess clips dropped.

### Voice-actor splits — `Resources/VoiceActorSplits.json`

A new embedded JSON config drives per-voice partitioning.

**Discriminator** — extract the 5-digit patch token from `audioFileBase` (the lowercase
underscore-joined string produced by `VoiceExtractKey.TryParse`). The third underscore-segment
is always a 5-digit number that encodes the FFXIV patch the line shipped in:

```
vo_voiceman_07410_000010 → Split('_') → [vo, voiceman, 07410, 000010]
                                                        ^^^^^
                                                  patch token = "07410" (= patch 7.41)

vo_manfst_06005_000020   → [vo, manfst, 06005, 000020]
                                        ^^^^^
                                  patch token = "06005" (= patch 6.005, an EW hotfix)

Token format: 5 zero-padded digits; the leading digit is the expansion major (0–9),
the remaining 4 digits are the patch within that expansion encoded as `XYZW` where
the patch reads as `<expansion>.XYZW`. Examples: `06000` = 6.0, `06010` = 6.01,
`06050` = 6.05, `06100` = 6.10, `07410` = 7.41. Implementation only needs the
zero-padded property — string comparison gives correct chronological ordering
without parsing the version semantics.
```

The patch token sorts lexicographically the same way it sorts numerically (since all tokens
are zero-padded to 5 digits), so plain string comparison gives correct chronological ordering.

**Schema** — one entry per voice that has actor changes; each entry lists the boundary patch(es)
where the actor switched. Boundaries split the timeline into N+1 epochs:

```jsonc
{
  "splits": [
    {
      "voiceKey": "Female_Hyur_Iceheart",
      "language": "DE",
      "comment": "DE dub actor changed somewhere around patch 6.01 — verify the exact boundary",
      "boundaryPatches": ["06010"]
    }
  ]
}
```

- `boundaryPatches`: array of 5-digit patch tokens, **strictly ascending**. Each boundary
  marks "the first patch in which the NEW actor speaks". Patches **strictly less than** the
  boundary are pre-boundary; patches **greater than or equal to** the boundary are
  post-boundary.
- One boundary → 2 epochs. Two boundaries → 3 epochs. The schema supports N boundaries from
  day one even though the Y'shtola example only needs 1; this avoids a breaking change later
  if a voice with two actor changes shows up.
- Auto-generated epoch names from boundary positions:
  - 1 boundary at `06010`: epochs `Pre06010`, `Post06010`.
  - 2 boundaries at `[03000, 06010]`: epochs `Pre03000`, `From03000`, `From06010`.
  - General rule: first epoch = `Pre{firstBoundary}`, last epoch = `From{lastBoundary}`,
    middle epochs (if any) = `From{prev}` — chosen so each name unambiguously declares its
    inclusive lower bound.

**Matching rule** — for each candidate:

1. Extract patch token: `audioFileBase.Split('_')[2]` (5 chars expected; reject if not 5
   digits).
2. Look up the voice's split entry by `voiceKey` + `language`. No entry → single epoch
   `"all"`; no further matching needed.
3. Find the first boundary the patch is `<` ; if found, epoch is `Pre{boundary}` (or
   `From{prevBoundary}` if not the first boundary). If the patch is `≥` the last boundary,
   epoch is `From{lastBoundary}`.
4. All comparisons are pure string comparison — works because tokens are uniformly 5-digit
   zero-padded.

**Output naming** — dataset and starter-set filenames use the epoch name as a suffix when the
voice has > 1 epoch:

- Single-epoch voice (no split entry): `Female_Hyur_Iceheart.wav` (unchanged).
- 1-boundary voice: `Female_Hyur_Iceheart_Pre06010.wav`, `Female_Hyur_Iceheart_Post06010.wav`.
- 2-boundary voice: `_Pre03000`, `_From03000`, `_From06010`.

The patch number in the filename is self-documenting — a glance at
`Female_Hyur_Iceheart_Pre06010.wav` immediately tells the user "use this voice for FFXIV
content from patches before 6.01".

**Loader contract**:

- Validate boundaryPatches are 5-digit strings, all-numeric, strictly ascending. Drop the
  whole entry on validation failure (log Error). Voices left without an entry collapse to
  the default `"all"` behavior.
- Local override path: `<localSaveLocation>/FF14-Voices/voice_actor_splits.json`. If
  present, fully replaces the embedded JSON (not merged). No remote layer in v1.

### Output paths

The dataset destination depends on whether AllTalk is locally installed:

| Configuration | Output root |
|---------------|-------------|
| `_config.Alltalk.InstanceType == AlltalkInstanceType.Local && _config.Alltalk.LocalInstall == true` | `{LocalInstallPath}\alltalk_tts\finetune\FF14-Finetune\` |
| Otherwise (Remote / None / Local-but-not-yet-installed) | `{LocalSaveLocation}\FF14-Finetune\` (sibling of `FF14-Voices`) |

Both flags must be true for the alltalk_tts path: `InstanceType == Local` means the user
selected Local mode in Settings → Backend, `LocalInstall == true` means an install actually
exists at `LocalInstallPath` (set by `IAlltalkInstanceService.Install` on success). If the user
is in Local mode but hasn't installed yet, the dataset still goes to `LocalSaveLocation` so the
export doesn't error out trying to write under a missing folder.

The Local-TTS path drops the dataset directly where AllTalk's `finetune.py` looks for projects,
so the user can immediately start a fine-tune run without copying files.

Per-dataset folder layout (one per voice × epoch combination):

```
FF14-Finetune/
└── {VoiceKey}[_{epochName}]/         (e.g. "Female_Hyur_Iceheart_Pre06010")
    ├── metadata_train.csv
    ├── metadata_eval.csv
    ├── lang.txt
    └── wavs/
        ├── clip_0001.wav
        ├── clip_0002.wav
        └── ...
```

### Wipe-on-rerun

Always wipe the entire `FF14-Finetune/` folder at the start of a run before writing the new
content. `Directory.Delete(path, recursive: true)` if it exists, then recreate. This guarantees
no stale clips from a previous export linger after the user changes the source data, the splits
config, or the speech-filter thresholds.

### metadata_train.csv / metadata_eval.csv schema

Same as the AllTalk canonical format documented earlier:

```
audio_file|text|speaker_name
wavs/clip_0001.wav|She speaks so softly, yet carries great resolve.|Female_Hyur_Iceheart_Pre06010
wavs/clip_0002.wav|The stars hold many secrets, traveler.|Female_Hyur_Iceheart_Pre06010
```

- `audio_file`: relative `wavs/{filename}`.
- `text`: the cleaned text (`cleaned[0]` of the `VoiceExtractTextCleaner.Clean` result), with
  any pipe characters replaced by commas (`|` → `,`) and leading/trailing whitespace trimmed.
- `speaker_name`: `{VoiceName}` for single-epoch voices, `{VoiceName}_{epochName}` for split
  voices. Matches the folder name. AllTalk uses this as the project identifier.

### Train / eval split

Per-voice, per-epoch:
- Total target: 200 train + 24 eval = 224 clips per dataset.
- After all filters run: deterministic shuffle (seed: stable string hash of
  `{voiceKey}|{epochName}|{language}`), take first 200 → train, next 24 → eval. Drop the rest.
- If fewer than 224 surviving candidates: take what's available, write smaller train/eval sets
  with the same 200:24 ratio (≈ 89% / 11%). Log a warning when below 50 total — model quality
  will be poor.
- `metadata_train.csv` rows ordered by clip filename; `metadata_eval.csv` likewise. Determinism
  matters for reproducibility; ordering matters for human inspection.

### UI placement

Add a new collapsible section **"Fine-Tuning Dataset Export"** to `NativeGameDataToolsWindow`,
between the existing "Voice Starter Set" section and the right-side help/links area. Pattern:
call `CreateCollapsibleSection(...)`, collapsed by default.

Controls inside the section:

1. **Voice name dropdown** (`TextDropDownNode`): populated from
   `IDatabaseService.GetVoices()` filtered to the **client language**. Label: "Export voice:".
   Follow the deferred-selection pattern (`_pendingExportVoiceSelection` field, processed in
   `OnUpdate`). No language dropdown — language is fixed to client language as per user
   direction.
2. **"Export All" button** (also): generate datasets for every configured voice in one run.
   Useful when seeding a brand-new fine-tune sweep.
3. **"Export Dataset" button** (`TextButtonNode`, min-width sized to fit longest label): starts
   the export for the selected voice. Becomes "Stop" during run (toggle pattern from existing
   `_starterSetButton`).
4. **Output path label** (`TextNode`, read-only): shows the resolved output root so the user
   knows whether their build is going to `alltalk_tts/finetune/` or `LocalSaveLocation`. Updated
   whenever `Alltalk.LocalInstall` flips.

Progress feeds into the shared `_progressBar` via the existing `ProgressChanged` event pattern.

Run summary (`SetTerminalStatus`) format:
`"Done — N voices, M datasets, T clips written to <path>. Skipped: too_long=X, non_speech=Y, length=Z, capped=W"`.

### Service architecture

New file: `Echokraut/Services/IDatasetExportService.cs`

```csharp
public interface IDatasetExportService
{
    bool IsRunning { get; }
    event Action<string, int, int>? ProgressChanged;

    /// <summary>Export a single voice (or all eligible voices when voiceKey is null).</summary>
    Task RunAsync(
        string? voiceKey,
        ClientLanguage language,
        CancellationToken ct);
}
```

**Implementation contract:**
- `IsRunning` is set `true` on `RunAsync` entry, reset to `false` in a `finally` block before
  the awaited Task completes. Mirror the pattern in `IVoiceSampleExtractorService` so the UI
  Stop button reliably re-enables.
- Cancellation throws `OperationCanceledException` *after* the wipe step. Any partial output
  is left in place — the next click cleanly re-wipes (see Risk Register).
- `ProgressChanged(stage, done, total)`: `stage` is a short label ("Discovering", "Filtering",
  "Writing wavs"…); `total` is the count appropriate to the stage (NPC count, candidate count,
  clip count). Fired at most every 10 clips/items to avoid flooding the framework thread.

New file: `Echokraut/Services/DatasetExportService.cs`

Constructor dependencies (injected via `ServiceBuilder`):
- `ILogService _log`
- `Configuration _config`
- `IDataManager _dataManager` (Lumina access for SCD enumeration)
- `IClientState _clientState` (current language)
- `IJsonDataService _jsonData` (NPC name index, alias map)
- `IRemoteUrlService _remoteUrls` (remote alias overrides — same layered pattern as the harvest)
- `IVoiceSampleExtractorService _extractor` — call into the shared candidate-collection helper
- `IDatabaseService _db` — `FindCharactersByVoiceKey` to resolve voiceKey → NPC IDs, and
  `GetVoiceKeysWithCharactersInLanguage` for the dropdown filter

Note: the candidate-collection helper extraction in `VoiceSampleExtractorService` should expose
a new `internal` method (visible to the same assembly + tests) so `DatasetExportService` can
call it without reimplementing 80 lines of Lumina iteration. Alternative: pull the helper into a
new `internal static` class `Helper/Functional/SCDCandidateCollector.cs`. Decide based on how
much state the helper needs from the extractor instance — if it's mostly the alias map and
language, a static helper is cleaner.

Key `RunInternal` flow:
1. Load `VoiceActorSplits.json` (embedded + optional local override). On parse / regex-compile
   failure, abort the run before touching the output dir — the previous successful dataset
   stays intact.
2. Resolve the voice list:
   - Single-voice mode (`voiceKey != null`): `[voiceKey]`.
   - Export-All mode (`voiceKey == null`): `IDatabaseService.GetVoiceKeysWithCharactersInLanguage(language)`,
     iterated in `voiceKey` ascending order so output is reproducible across runs.
3. Pre-flight check: resolve the output root (Local-install vs. fallback path), verify the
   parent directory exists and is writable. If not, terminal-status error + abort.
4. Wipe and recreate the output root (`FF14-Finetune/`). Single wipe for the whole run, NOT
   per voice — covers Export-All in one delete.
5. For each voice in the resolved list:
   a. `IDatabaseService.FindCharactersByVoiceKey(voiceKey, language)` → `List<CharacterEntity>`.
   b. Collect SCD candidates for those NPCs via the shared helper.
   c. Apply filter chain (speech, length, ≤19 s, actor-split partitioning).
   d. For each (voice × epoch) bucket:
      - Deterministic shuffle (seed = stable hash of `{voiceKey}|{epochName}|{language}`),
        take 200 for train + next 24 for eval, drop excess.
      - Decode + resample each clip → write `wavs/clip_NNNN.wav` (4-digit padded; cap is 224
        per bucket so 4 digits suffice).
      - Write `metadata_train.csv`, `metadata_eval.csv`, `lang.txt`.
   e. Per-voice exception: log Warning, skip the voice, continue with the next one. Never
      let one bad voice take down an Export-All run.
6. `ProgressChanged(stage, doneClips, totalClipsAcrossAllVoices)` every 10 clips written.
   `total` is computed after step 5c (post-filter survivor count) before any clip is written.
7. Log run summary: total voices, total datasets (voices × epochs), total clips,
   per-skip-reason counts (`too_long`, `non_speech`, `length`, `capped`).

**Order rationale:** the per-voice cap runs strictly last so the deterministic shuffle samples
from the post-rejection survivor pool, not the raw candidate list — keeps the eval set
representative of what survived all quality filters. The wipe runs after splits-config validation
so a malformed JSON aborts before nuking the previous successful dataset.

### Audio pipeline reuse

Reuse without modification:
- `MsAdpcmDecoder.Decode(byte[])` — decode SCD payload to int16 PCM (already used by extractor).
- `WavResampler.Resample(short[], int srcRate, int dstRate)` — resample to 22050 Hz.
- `WavResampler.DownmixToMono(short[], int channels)` — handle the rare stereo SCD.
- `PcmWavWriter.Build(short[], int sampleRate, int channels)` — write output WAV bytes.

No `PcmWavReader` needed — input is SCD (decoded by `MsAdpcmDecoder`), not WAV.

### Step-by-step implementation order

1. **`VoiceExtractTextCleaner.IsSpeech`** (Issue 1) — pure static, no dependencies.
   Add constants for thresholds. Add test cases.

2. **Refactor: extract shared candidate collector** from `VoiceSampleExtractorService.RunInternal`
   into an `internal static` helper or `internal` method. Verify existing extractor tests still
   pass (`VoiceSampleExtractorTests.cs`).

3. **`Resources/VoiceActorSplits.json`** + parser (`Helper/Functional/VoiceActorSplits.cs`).
   Schema-validating loader, embedded resource registration in `Echokraut.csproj`.

4. **`IDatasetExportService` + `DatasetExportService`** skeleton — constructor + `IsRunning` +
   `ProgressChanged`, no logic.

5. **`BatchOperation.DatasetExport`** in `Echokraut/Enums/BatchOperation.cs`. Update
   `BatchModeService.CurrentOperation` to check the new service.

6. **Register `IDatasetExportService`** in `ServiceBuilder.cs`.

7. **`DatasetExportService.RunInternal`** — wipe, candidate collection, filter chain, splits,
   per-bucket selection, decode + resample loop, CSV writer. Test the deterministic-shuffle and
   CSV-writer logic (`SplitTrainEval`, `BuildCsvRows`) as pure functions if extracted.

8. **`NativeGameDataToolsWindow`** — UI additions: new `IDatasetExportService` constructor
   parameter, new fields (`_exportCts`, `_pendingExportVoiceSelection`, `_selectedExportVoice`),
   new collapsible section in `OnSetup`, deferred dropdown processing in `OnUpdate`,
   `OnExportClick` and `OnExportAllClick` handlers.

9. **Localization**: add all new UI strings to `Loc.cs` with DE/FR/JP translations.

10. **Changelog entry**: append to `Resources/Changelogs/v0.19.0.1_EN.txt` and
    `Resources/Changelogs/v0.19.0.1_DE.txt`.

11. **Tests**: 
    - Extend `VoiceSampleExtractorTests.cs` with `IsSpeech` cases and a regression test for the
      refactored shared helper.
    - New `VoiceActorSplitsTests.cs` — schema parsing + boundary validation (strictly ascending,
      5-digit), patch-token extraction from `audioFileBase`, epoch-name auto-generation for 1
      and 2 boundaries, voice-not-in-config fallback to `"all"`, local-override replaces
      embedded.
    - New `DatasetExportServiceTests.cs` — filter chain (each filter individually + together),
      duration cap exact-boundary, deterministic shuffle, CSV row construction, output path
      resolution (Local vs. non-Local).
    - Extend `BatchModeServiceTests.cs` with `DatasetExport` case.

---

## Issue 3 — Voice Sample Extractor splits-awareness + cross-epoch validity

User direction: VoiceActorSplits.json applies to the **starter set** too — for split voices the
extractor produces ONE sample-set per epoch, with the epoch name encoded in the output filename
so AllTalk's voice picker can keep pre-/post-actor-change variants distinct. Plus: an NPC with
configured epoch voices should not have its existing on-disk generations flagged as outdated
when the active voice flips between those epochs (auto-switch by line) or when the user manually
swaps in a fine-tuned variant of the same speaker (manual edit). Both cases mean "the clip's
voice_key is in the NPC's valid voice set, even if it's not the *currently selected* one".

The decision was: **one Character row per NPC**, with a per-(character × epoch) voice mapping
that the live path consults to pick the right voice for each line.

This is the largest architectural extension across the three Issues. Splitting into 4 sub-issues
so each can be scoped/scheduled independently.

### Sub-issue 3a — Voice Sample Extractor: split-aware partitioning + filename suffix

Smallest, fully contained inside the extractor + filename helper.

**Behavior change:**
- Load `VoiceActorSplits.json` once per run (same loader as Dataset Export from Issue 2).
- In the per-NPC candidate loop, partition each NPC's candidates into epochs via the same
  `audioFileBase`-regex scheme. Voices not in the splits config use a single epoch `"all"`
  (existing behavior preserved).
- For each (NPC × epoch) bucket, pick `samplesPerNpc` samples and write them.
- Filenames: when the voice has > 1 defined epoch, append `_{epochName}` to the canonical
  name (e.g. `Female_Hyur_Iceheart_Pre06010.wav`, `Female_Hyur_Iceheart_Post06010.wav`). When the
  voice has 0 or 1 epoch defined, filename is unchanged from today.

**Files:**
- `Echokraut/Services/VoiceSampleExtractorService.cs` — load splits, per-epoch partition.
- `Echokraut/Helper/Functional/VoiceExtractFileNames.cs` — extend `GetNamedTargetPath` to take
  optional `epochName` argument; emit suffix `_{epochName}` when non-empty. `GetCatalogTargetPath`
  follows the same pattern. Default value keeps existing call sites compiling unchanged.
- `Echokraut/Helper/Functional/VoiceActorSplits.cs` — already needed for Issue 2; no new file.

**Tests:**
- Extend `VoiceSampleExtractorTests.cs` with a fixture using a synthetic splits config; verify
  two output filenames are produced when the voice has two epochs.
- Extend `VoiceExtractFileNamesTests.cs` (create if missing) with the new `epochName` parameter
  cases — empty → no suffix, non-empty → `_{epochName}` suffix, special-char sanitization.

### Sub-issue 3b — Per-character epoch voice mapping (DB schema + service API)

The data layer that every other live-path change depends on.

**Schema migration** (next version after current `character_speaker_aliases`):

```
CREATE TABLE character_epoch_voices (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    character_id INTEGER NOT NULL,
    epoch_name TEXT NOT NULL,        -- matches VoiceActorSplits.json's "name" field; "all" for no-split
    voice_key TEXT NOT NULL,
    FOREIGN KEY (character_id) REFERENCES characters(id) ON DELETE CASCADE,
    UNIQUE (character_id, epoch_name)
);
CREATE INDEX IX_character_epoch_voices_character ON character_epoch_voices(character_id);
```

- One row per (character, epoch). For NPCs without split config, exactly one row with
  `epoch_name = "all"` and the voice_key the user picked.
- For split NPCs, one row per defined epoch (e.g. two for Y'shtola DE: pre60 + post61).

**New `IDatabaseService` methods:**

```csharp
/// <summary>Set or upsert the voice for a (character, epoch) pair.</summary>
void UpsertCharacterEpochVoice(int characterId, string epochName, string voiceKey);

/// <summary>All configured (epoch, voice) pairs for a character. Empty list → no entries
/// configured yet (live path falls back to the legacy CharacterEntity.VoiceKey field).</summary>
List<(string EpochName, string VoiceKey)> GetCharacterEpochVoices(int characterId);

/// <summary>The set of voice_keys considered VALID for the character — the union of
/// (legacy CharacterEntity.VoiceKey) ∪ (all character_epoch_voices.voice_key for the
/// character). Used by the validity check to determine whether a clip's voice_key is still
/// "right" for the NPC.</summary>
HashSet<string> GetValidVoiceKeysForCharacter(int characterId);
```

**Backward compat:** existing `CharacterEntity.VoiceKey` stays as the "legacy single voice" /
"current default voice" field. `character_epoch_voices` augments it. Lookup precedence is
documented in 3c. No data migration of existing rows is required at v3 ship — the table is
empty until users configure a split NPC.

**Files:**
- `Echokraut/Services/IDatabaseService.cs` + `DatabaseService.cs` — new methods.
- `Echokraut/DataClasses/Database/CharacterEpochVoiceEntity.cs` — new EF entity.
- `Echokraut/DataClasses/Database/EchokrautDbContext.cs` — register the entity.
- `Echokraut/Services/DatabaseService.cs` `RunSchemaMigrations` — add the `CREATE TABLE`.
- `Echokraut.Tests/DatabaseServiceTests.cs` — extend with the three new methods.

### Sub-issue 3c — Live generation epoch dispatch

When generating audio for a line, the system needs to pick the right voice based on the line's
epoch. Two parts: looking up the line's `audioFileBase` from the harvest, and picking an
epoch voice.

**Lookup**: the harvest's `LinkedDialog` table already maps text → NPC + (probably)
text-key/audioFileBase. At live time, `VoiceMessageProcessor.ProcessSpeechAsync` has access to
the cleaned text and speaker; it can call a new `IDatabaseService.GetAudioBaseForLinkedDialog(
characterId, originalText)` to retrieve the audioFileBase if the harvest captured this line.
Cache the result on `VoiceMessage` to avoid repeated lookups.

**Epoch resolution**: with the audioFileBase in hand, run it through the loaded splits config
(same `VoiceActorSplits` helper as 3a) to get the matching epoch name (or "all" if no split
applies / no audioFileBase available).

**Voice pick**:
1. If the character has a `character_epoch_voices` row matching the resolved epoch → use that
   voice_key.
2. Else if the character has a row with epoch_name = `"all"` → use that.
3. Else fall back to `CharacterEntity.VoiceKey` (legacy / current behavior).

**Files:**
- `Echokraut/Services/VoiceMessageProcessor.cs` — call audioFileBase lookup + epoch resolver
  before voice selection.
- `Echokraut/DataClasses/VoiceMessage.cs` — add `string? AudioFileBase` + `string? Epoch` fields.
- `Echokraut/Services/IDatabaseService.cs` — new `GetAudioBaseForLinkedDialog` query.
- `Echokraut/Services/BackendService.cs` — `EnsureFittingVoice` consults the epoch-voice map
  before falling through to PickVoice.

**Open behavior question (defer to implementation):** if a line has no harvest entry (e.g.,
a chat-typed message or a recently added NPC the user hasn't harvested yet), default to which
epoch? Two sensible policies:
- (A) Default to the most-recent epoch (= last one in the JSON entry order).
- (B) Default to `"all"`-style fallback (legacy `CharacterEntity.VoiceKey`).
Recommend (A) — newest content is what the user is most likely to be playing. Document the
choice in `VoiceActorSplits` docs.

### Sub-issue 3d — Cross-epoch validity (don't invalidate working generations)

The user's wording: "bei diesen NPC darf dann bei einem Stimmenwechsel nicht die bisherigen
Generations als ungültig erkannt werden, da die ja trotzdem korrekt sind."

Two trigger cases (per user clarification):

1. **Auto-switch between epochs**: line A is from pre-6.0, line B is from post-6.1; clip A was
   generated with voice_pre60, clip B was generated with voice_post61. Both rows in
   `voice_clip_generations` carry `voice_key` = the actually-used voice. When the live path
   later replays line A, it must NOT regenerate just because the NPC's "current" voice (per
   epoch dispatch) for line B is voice_post61.
2. **Manual edit**: the user opens NPC Edit and swaps voice_pre60 for a fine-tuned variant
   `voice_pre60_v2`. They want existing voice_pre60 generations to stay playable, not auto-
   regenerated as outdated.

**Implementation**:

The "is this clip's voice still right for this NPC" check happens implicitly today via
`VoiceClipManagerService.GetEffectivePlayerId` + `IDatabaseService.GetVoiceClipGeneration` —
neither does a voice_key equality check, they just look up the persisted row. So the
**FUNCTIONAL behavior is already correct**: existing generations play back regardless of the
NPC's current voice. Confirm this empirically before adding code.

The **VISIBLE** behavior (Voice Clip Manager UI showing a clip's voice) might display the
generation's voice_key as informative metadata. If any UI element marks a clip as "outdated"
or "voice mismatch", that check needs to consult `IDatabaseService.GetValidVoiceKeysForCharacter`
(from 3b) instead of comparing against the single `CharacterEntity.VoiceKey`.

For case 2 specifically (manual edit replaces pre60 with pre60_v2): the user's intent is
"keep old generations". The existing logic already does that. To make it *explicit* and prevent
future regressions, add a comment + a regression test in `VoiceClipManagerServiceTests.cs`:
"voice_key mismatch with character.VoiceKey does NOT cause regeneration on play".

**Files:**
- `Echokraut/Windows/Native/NativeVoiceClipDetailWindow.cs` — if there's an "outdated voice"
  indicator (audit during implementation; remove or rewrite to use `GetValidVoiceKeysForCharacter`).
- `Echokraut.Tests/VoiceClipManagerServiceTests.cs` — regression test for cross-voice playback.
- No DB schema change.

### Issue 3 implementation order

3a → 3b → 3c → 3d. Each is shippable on its own; each depends on the previous.

- 3a alone gives: starter set produces `_Pre{patch}` / `_Post{patch}` files. User can manually create two
  voice_keys in AllTalk from those and assign one to the NPC (using the legacy single-voice
  field). Live behavior unchanged.
- 3a + 3b: data model in place but no UI yet. Skipped if the user wants to wait.
- 3a + 3b + 3c: live path picks the right voice per line. UI for managing the mapping is the
  last step — until 3d's UI lands, mapping is configured via the harvest path that pre-fills
  `character_epoch_voices` from `VoiceActorSplits.json` matching.
- 3d: explicit validity-test + UI cleanup.

### Issue 3 deferred items (NOT in v1)

- Programmatic auto-creation of `character_epoch_voices` rows from harvest (e.g. when the
  harvester sees Y'shtola DE in a 6.1+ cutscene, it could pre-populate the post61 entry).
  Manual UI configuration via NPC Edit is sufficient for v1.
- Multi-language epoch splits (e.g. Y'shtola DE has actor split, Y'shtola EN does not). The
  schema supports it (per-character row, language is implicit via the `characters.language`
  column), but the JSON uses an explicit `language` field per split entry — already in 2.

---

## Open Questions

The user already pre-decided most of the original open questions. Remaining open items:

1. **Speech-filter threshold values**: the word counts and char counts proposed above (EN: 4
   words, DE: 3 words, JP: 8 chars) are estimates. Before locking the implementation, run the
   filter against a real SCD text dump and count what percentage of lines it drops per language.
   If the drop rate exceeds ~30% for any language, revisit the thresholds.

2. **Initial `VoiceActorSplits.json` content**: the user provided one concrete example
   (Y'shtola DE pre-6.0 vs 6.1+). Other known actor changes across the FFXIV dub history exist
   but are scattered. For v1, ship the JSON with that single Y'shtola DE entry as a worked
   example, document the schema in CLAUDE.md, and let the user / community grow the file via PR
   or local override. Confirm — or supply a list of additional known splits to include up-front.

3. **Exact `boundaryPatches` value for the Y'shtola DE example**: the plan uses `"06010"` as a
   placeholder for "patch 6.01 = first DE Y'shtola line spoken by the new actor". The actual
   boundary patch needs one-time empirical validation: pick a known DE Y'shtola line from a
   post-change cutscene, read the patch token (digits 12–16 of the audio file path), and use
   that value. Trivial implementation-phase task.

### Already-decided (per user direction during planning)

- **Source data**: FFXIV in-game SCD voice files (NOT runtime-generated AllTalk audio).
- **Voice selection granularity**: Voice Key (AllTalk voice name).
- **Single-language scope**: only client language is exported. Voice dropdown filtered to voices
  with ≥1 character row in the client language; no language dropdown in the UI.
- **Wipe-on-rerun**: always full wipe + fresh export, AFTER splits-config load + validation
  passes (so a malformed JSON aborts before nuking the previous successful dataset).
- **Output root path**: `{LocalInstallPath}\alltalk_tts\finetune\FF14-Finetune\` when AllTalk is
  installed locally, else `{LocalSaveLocation}\FF14-Finetune\` next to `FF14-Voices`.
- **Hard duration cap**: 19.0 seconds — clips above this are dropped.
- **Per-voice cap**: 200 train + 24 eval (~12%), applied as the LAST filter.
- **Actor-change splits**: driven by new `VoiceActorSplits.json`; voices not in the JSON get a
  single default epoch.

---

## Full Test Plan

### Testable without the game runtime

All of the following can be tested in `Echokraut.Tests/` using xUnit + Moq, no `IDataManager`
needed (Lumina iteration is mocked behind the shared candidate-collector helper):

| Test file | What it covers |
|-----------|----------------|
| `VoiceSampleExtractorTests.cs` (extend) | `IsSpeech` — all languages, edge cases; helper-extraction regression |
| `VoiceActorSplitsTests.cs` (new) | Schema parse + boundary validation, patch-token extraction (5-digit happy path + edge: missing token, non-numeric token, fewer than 3 underscores), epoch-name auto-generation (1 boundary → Pre/Post; 2 boundaries → Pre/From/From), voice-not-in-config fallback to `"all"`, local-override replaces embedded |
| `DatasetExportServiceTests.cs` (new) | Filter chain (each filter individually + combined), 19 s boundary inclusive at exactly 19.000 s and exclusive at 19.001 s, deterministic shuffle stability, CSV row construction (pipe escape, trim), output-path resolution (Local vs. non-Local), wipe-and-recreate semantics, voice-with-zero-survivors graceful skip |
| `DatabaseServiceTests.cs` (extend) | `FindCharactersByVoiceKey` happy path + language filter + non-existent voice; `GetVoiceKeysWithCharactersInLanguage` returns only voices with ≥1 character row in the language |
| `BatchModeServiceTests.cs` (extend) | `DatasetExport` correctly reported as `CurrentOperation` |

Mocking strategy for `DatasetExportServiceTests`:
- Mock `IDatabaseService.FindCharactersByVoiceKey` and `GetVoiceKeysWithCharactersInLanguage`
  to return canned `CharacterEntity` lists. No EF Core context needed in the unit tests.
- Mock the shared SCD candidate-collector helper to return canned `VoiceLineCandidate` lists,
  bypassing Lumina iteration.
- Use `MsAdpcmDecoder` decode output as input to the resample step in tests; build a tiny
  synthetic int16 PCM `short[]` directly when testing the duration-cap math.

### Requires game runtime / real game files

- End-to-end export with real Lumina text-key sheets and SCD audio.
- Verifying AllTalk actually accepts the produced `metadata_train.csv` (manual user test).

---

## Changelog Impact

### `Resources/Changelogs/v0.19.0.1_EN.txt`

Append to the existing v0.19.0.1 file (which already has the auto-start-checkbox entry).

```
IMPROVEMENTS
- Voice Sample Extractor: added speech-content filter that drops non-speech candidates
  (laughs, grunts, single-word exclamations) before sampling. Cleaner starter sets for
  NPCs with many incidental vocalizations.
- Voice Sample Extractor: voices flagged in VoiceActorSplits.json as having a dub-actor
  change (e.g. Y'shtola DE pre-6.0 vs 6.1+) now produce two starter sets, one per epoch,
  with the epoch name appended to the filename (e.g. _Pre06010 / _Post06010).

MAJOR NEW FEATURES
- Fine-Tuning Dataset Export: new section in Game Data Tools. Select an AllTalk voice and
  hit Export to produce an XTTS fine-tuning dataset (metadata_train.csv, metadata_eval.csv,
  lang.txt, wavs/) from FFXIV's own ingame voice acting. Up to 200 train + ~24 eval samples
  per voice, hard 19-second per-clip cap, all clips resampled to 22050 Hz mono. Voices with
  known dub-actor changes get split into separate datasets via Resources/VoiceActorSplits.json.
  Output goes directly to <LocalInstallPath>\alltalk_tts\finetune\FF14-Finetune\ for local
  AllTalk installs, else to <LocalSaveLocation>\FF14-Finetune\.
- Per-character epoch voices: NPCs with actor splits can now have a separate voice
  configured per epoch. The plugin picks the right voice for each line based on its source
  cutscene. Existing generations made with any of the NPC's epoch voices stay valid even
  when the active voice flips between epochs or after a manual voice swap.
```

Matching `_DE.txt` file required (same structure, translated body).

---

## Files Touched

| File | Change |
|------|--------|
### Issues 1 & 2

| File | Change |
|------|--------|
| `Echokraut/Helper/Functional/VoiceExtractTextCleaner.cs` | Add `IsSpeech(string, ClientLanguage)` + threshold constants |
| `Echokraut/Helper/Functional/VoiceActorSplits.cs` | New — load + parse `VoiceActorSplits.json`, regex-match `audioFileBase` to epochs |
| `Echokraut/Resources/VoiceActorSplits.json` | New embedded JSON with initial Y'shtola DE example |
| `Echokraut/Services/VoiceSampleExtractorService.cs` | Refactor: extract candidate-collection helper |
| `Echokraut/Services/IDatasetExportService.cs` | New interface |
| `Echokraut/Services/DatasetExportService.cs` | New implementation |
| `Echokraut/Enums/BatchOperation.cs` | Add `DatasetExport` value |
| `Echokraut/Services/BatchModeService.cs` | Check `IDatasetExportService.IsRunning` |
| `Echokraut/Services/ServiceBuilder.cs` | Register `IDatasetExportService` |
| `Echokraut/Services/IDatabaseService.cs` | Add `FindCharactersByVoiceKey` and `GetVoiceKeysWithCharactersInLanguage` |
| `Echokraut/Services/DatabaseService.cs` | Implement both lookups (read-only EF Core queries; no schema change) |
| `Echokraut/Windows/Native/NativeGameDataToolsWindow.cs` | New Dataset Export section, voice dropdown, export buttons |
| `Echokraut/Localization/Loc.cs` | New UI strings (all 4 languages) |
| `Echokraut/Echokraut.csproj` | Embed `VoiceActorSplits.json` |
| `Echokraut/Resources/Changelogs/v0.19.0.1_EN.txt` | Changelog entry |
| `Echokraut/Resources/Changelogs/v0.19.0.1_DE.txt` | Changelog entry (German) |
| `Echokraut.Tests/VoiceSampleExtractorTests.cs` | Extend with `IsSpeech` tests + helper-extraction regression |
| `Echokraut.Tests/VoiceActorSplitsTests.cs` | New test file |
| `Echokraut.Tests/DatasetExportServiceTests.cs` | New test file |
| `Echokraut.Tests/BatchModeServiceTests.cs` | Extend with `DatasetExport` case |

### Issue 3 (additive on top of 1 & 2)

| Sub-issue | File | Change |
|-----------|------|--------|
| 3a | `Echokraut/Services/VoiceSampleExtractorService.cs` | Per-epoch partition loop + write per (NPC × epoch) |
| 3a | `Echokraut/Helper/Functional/VoiceExtractFileNames.cs` | `GetNamedTargetPath`/`GetCatalogTargetPath` accept optional `epochName` |
| 3a | `Echokraut.Tests/VoiceSampleExtractorTests.cs` | Add fixture: split-voice produces 2 outputs |
| 3a | `Echokraut.Tests/VoiceExtractFileNamesTests.cs` (new) | Filename-suffix cases |
| 3b | `Echokraut/DataClasses/Database/CharacterEpochVoiceEntity.cs` (new) | EF entity |
| 3b | `Echokraut/DataClasses/Database/EchokrautDbContext.cs` | Register entity + DbSet |
| 3b | `Echokraut/Services/DatabaseService.cs` | Schema migration v(next) + 3 new methods |
| 3b | `Echokraut/Services/IDatabaseService.cs` | New method signatures |
| 3b | `Echokraut.Tests/DatabaseServiceTests.cs` | Cover 3 new methods + migration |
| 3c | `Echokraut/DataClasses/VoiceMessage.cs` | Add `AudioFileBase` + `Epoch` fields |
| 3c | `Echokraut/Services/VoiceMessageProcessor.cs` | Resolve audioFileBase via harvest, pick epoch voice |
| 3c | `Echokraut/Services/IDatabaseService.cs` | Add `GetAudioBaseForLinkedDialog(characterId, originalText)` |
| 3c | `Echokraut/Services/BackendService.cs` | `EnsureFittingVoice` consults epoch-voice map |
| 3c | `Echokraut.Tests/VoiceMessageProcessorTests.cs` (new or extend) | Epoch dispatch happy path + fallback |
| 3d | `Echokraut/Windows/Native/NativeVoiceClipDetailWindow.cs` | If "outdated voice" indicator exists, switch to `GetValidVoiceKeysForCharacter` |
| 3d | `Echokraut.Tests/VoiceClipManagerServiceTests.cs` | Regression: cross-voice clip replays without regen |

---

## Risk Register

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Speech filter drops too many lines in DE/JP (over-aggressive threshold) | Medium | Run against real data before finalising constants; expose as config knob if needed |
| AllTalk rejects pipe characters that survived cleaning | Low | Strip / replace `\|` in transcript text before writing CSV |
| Per-voice cap of 200 means a popular voice (Y'shtola etc.) loses ~70% of candidates | Expected | Documented behavior; with split-aware bucketing, popular voices that span actor changes still produce 2× datasets and capture both actor styles |
| `VoiceActorSplits.json` boundary ambiguity (boundaries not strictly ascending or duplicate) | Low | Validated at load time; entry rejected as a whole on validation failure with Error log; voice falls back to `"all"` |
| `audioFileBase` missing the patch token (e.g. unexpected key shape) | Very low | `Split('_')[2]` length check + numeric check; clip skipped from any split routing, lands in `"all"` epoch with Debug log |
| Cut-number boundaries shift when SquareEnix re-orders patches (very rare) | Very low | JSON is editable; bad attribution shows up as low-quality fine-tune output and the user can regenerate |
| Wipe-on-rerun deletes a half-written previous run mid-flight | Low | Wipe runs after the splits-config load + validation step (step 2 of `RunInternal`). If the splits JSON is malformed, the run aborts BEFORE any deletion happens — the previous successful dataset survives. Once wipe runs, any later abort leaves a partially populated tree which the next click cleanly re-wipes. Cancellation-safe by construction. |
| Splits config load fails after the wipe → user is left with empty `FF14-Finetune/` | Low | Mitigated by step 1 / step 2 ordering above: load + validate happens BEFORE the wipe. Loader-error path returns without touching the output dir. |
| User has AllTalk Local install but `LocalInstallPath` is empty / unwritable → `Directory.Delete` throws on first run | Medium | Pre-flight check `Directory.Exists(parent) && IsWritable(parent)` + fallback to the non-Local path with a Warning. Surface as terminal-status error before wiping anything. |
| `NativeGameDataToolsWindow` ATK node crash from missing `IFramework.RunOnFrameworkThread` in `ContinueWith` | Low but severe | Follow the pattern from `NativeFirstTimeWindow.TestConnection()` exactly |
| `TextDropDownNode.UpdateLabel` crash (known KamiToolKit issue) | Medium | Follow existing workaround: use `OptionListNode.Options`, guard `LabelNode.Node != null`, defer to `OnUpdate` |
| Total candidate count for one voice × one language exceeds `int.MaxValue` (joke) | Zero | n/a |
| Decode + resample of ~224 clips per voice causes visible game freeze | Low | Same thread-pool pattern as `VoiceSampleExtractorService.RunInternal`; UI stays responsive |
| Hard 19.0 s cap drops too many viable clips for some voices | Medium | This is an AllTalk constraint, not a plugin choice; document in run summary. If a voice has < 50 surviving candidates, log warning; user can verify by checking the SCD durations. |

## Rollback Strategy

Both features are additive. Issue 1 only adds a new filter method and one `continue` statement —
reverting means removing those two additions. Issue 2 adds new files and a new window section;
reverting means removing the new service files, the `BatchOperation.DatasetExport` value, and
the window section nodes (not removing nodes from `OnSetup` crashes ATK — remove the `AddNode`
calls and the new `CreateCollapsibleSection` call).

No database schema changes are required. No migration needed.
