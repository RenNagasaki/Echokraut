# Plan Review — voice-sample-improvements.md

**Reviewed document:** `docs/plans/voice-sample-improvements.md` (854 lines)
**Reviewer:** plan-reviewer agent
**Scope note:** targeted audit verifying that user corrections (SCD source, voice-key selection, client-language only, wipe-on-rerun, output path rules, 19 s cap, 200+24 cap as last filter, `VoiceActorSplits.json`) propagated through every section, plus internal-consistency/architecture sanity checks.

## Summary

- **Verdict:** not-ready
- **Blocking issues:** 6
- **Gaps:** 4
- **Contradictions:** 5
- **Propagation misses:** 7
- **Top 3 highest-impact findings:**
  - The plan is split-brained: a complete duplicate Issue 2 spec (lines ~520–698) describes the OLD runtime-AllTalk-WAV pipeline and was never removed when the SCD pivot was applied to lines 214–518.
  - `VoiceActorSplits.json` `audioPathPatterns` use prefixes (`cut/ffxiv/`, `cut/ex4/cut0`) that do not exist in any value the candidate-collection code produces — `audioBase` is e.g. `vo_voiceman_06006_000010` and the *resolved* SCD path is `cut/{exp}/sound/voicem/voiceman_06006/...`, never `cut/ex4/cut0...`. The split mechanism cannot work as drafted.
  - `IDatasetExportService.RunAsync` is declared TWICE with incompatible signatures (lines 422–432 and 607–619) — the second one still carries `includeAliasClips` / `applySpeechFilter` parameters that contradict user direction #4 (always wipe + fixed pipeline) and #1 (SCD source has no alias-clip concept).

---

## Blocking issues

### BLK-1: Duplicate Issue 2 specification — old runtime-WAV plan never deleted

- **Where:** `docs/plans/voice-sample-improvements.md` lines ~520–698 (sections "File layout", "metadata_train.csv / metadata_eval.csv schema", "Train / eval split", "UI placement", "State management", "Service interface", "Audio pipeline reuse", "Step-by-step implementation order").
- **Finding:** Lines 214–518 describe the corrected SCD pipeline. Lines 520–698 then re-describe the same sub-sections as if the SCD pivot never happened: file layout points at `<LocalSaveLocation>/finetune-datasets/<SanitizedVoiceName>/` (line 524), schema cites `voice_clip_generations.voice_key` and `generated_at` ordering (lines 535–539, 558–559), UI section adds a Language dropdown and "Include alias clips" / "Apply speech filter" checkboxes (lines 572–581), service constructor lists only `IDatabaseService / ILogService / Configuration` and explicitly states "The service does not depend on `IDataManager` or `IClientState`" (lines 624–629), `RunInternal` queries `voice_clip_generations` joined to `voice_clips` (lines 632–642), audio pipeline reuse references `WavInspector` and a new `PcmWavReader` for AllTalk-output WAVs (lines 647–660), implementation order step 2 mandates `PcmWavReader` (lines 667–668).
- **Impact:** An implementor following the plan top-to-bottom will read two contradictory designs. The bottom half is the *old* one and is wrong on every requirement (source data, output path, dropdown set, dependencies, sort key, filter chain).
- **Suggested fix:** Delete lines ~520–698 entirely. The corrected canonical descriptions for File Layout, CSV schema, Train/Eval Split, UI, State Management, Service Interface, Audio Pipeline Reuse, and Step-by-Step Order already exist at lines 330–518 — the second copies are pure residue.

### BLK-2: `VoiceActorSplits.json` prefix-match scheme cannot match real candidate data

- **Where:** lines 277–328 (schema + matching rule) and risk-register row at line 836.
- **Finding:** The plan's example `audioPathPatterns` use `"cut/ffxiv/"`, `"cut/ex1/"`, `"cut/ex4/cut0"`, `"cut/ex4/cut1"`, `"cut/ex5/"`. Two problems:
  1. `VoiceExtractKey.TryParse` produces `audioFileBase` values like `vo_voiceman_06006_000010` — no `cut/...` prefix, no expansion token. A prefix match against `audioBase` will never hit.
  2. The *resolved* SCD path (built by `VoiceScdPaths.Build`) is `cut/{exp}/sound/{folder6}/{folder14}/{audioBase}_{gender}_{lang}.scd`, e.g. `cut/ex4/sound/voicem/voiceman_06006/vo_voiceman_06006_000010_m_de.scd`. There is no `cut/ex4/cut0...` segment — the second token after the expansion is always `sound`, then `voicem` (or `voiceman`-variant), not a `cutNNNN` directory. The expansion segment (`ffxiv`/`ex1`/.../`ex5`) is iterated by the resolver, not encoded in the file's identity — the same `audioBase` may resolve under any expansion archive (the loop "first hit wins" — see `VoiceScdPaths.Build`).
- **Impact:** The whole actor-split feature is non-functional as specified. Every voice will land in the default `"all"` bucket; Y'shtola DE pre-6.0 and post-6.1 will not split.
- **Suggested fix:** Pick a discriminator that actually exists in the data:
  - Option A — match on the numeric range inside `audioBase`. Schema becomes `audioBaseRanges: [ { prefix: "vo_voiceman_", from: 0, to: 6005 } ]` (or simpler: a regex per epoch against `audioBase`).
  - Option B — match against the resolved full SCD path **after** path resolution, but accept that the expansion token is determined by which archive resolves first, not by content origin.
  - Either way, replace the schema example and the matching paragraph (lines 314–318) with the chosen scheme, drop the misleading `cutN` references, and remove Open Question #3 (line 716–720) which is moot once the discriminator works against real data.

### BLK-3: Two contradictory `IDatasetExportService` definitions

- **Where:** lines 422–432 (first definition) vs. lines 607–619 (second definition).
- **Finding:**
  - Line 428–432: `Task RunAsync(string? voiceKey, ClientLanguage language, CancellationToken ct);` — nullable voiceKey (null = all), no extra flags. Matches user direction (single fixed pipeline, "Export All" button passes null).
  - Line 612–618: `Task RunAsync(string voiceKey, ClientLanguage language, CancellationToken ct, bool includeAliasClips = true, bool applySpeechFilter = true);` — non-nullable voiceKey, plus two booleans that don't fit the SCD-source design (no alias rows in SCD data; speech filter is documented as always-on per line 178–181 and line 248).
- **Impact:** Implementor cannot tell which is canonical. The second signature also re-introduces optional knobs the user explicitly did not ask for.
- **Suggested fix:** Delete the second interface block (lines 604–619) along with the rest of the duplicate section (see BLK-1). Keep only the line 422–432 version. Update the `RunInternal` flow at line 631–644 (also part of the duplicate) to match the line 453–466 flow.

### BLK-4: Constructor-dependency list contradicts itself

- **Where:** lines 437–444 vs. lines 623–629.
- **Finding:**
  - First list (correct, SCD-aware): `ILogService`, `Configuration`, `IDataManager`, `IClientState`, `IJsonDataService`, `IRemoteUrlService`, `IVoiceSampleExtractorService`.
  - Second list (residue): `IDatabaseService`, `ILogService`, `Configuration`, with an explicit "The service does not depend on `IDataManager` or `IClientState`" — directly contradicts the first list.
- **Impact:** Mis-registration in `ServiceBuilder.cs`; `DatasetExportService` would fail to compile against either spec depending on which the implementor picks.
- **Suggested fix:** Removed by the BLK-1 deletion. Additionally: confirm in the surviving list (line 437–444) whether `IDatabaseService` is needed — it currently is *not* listed but step 3a at line 458 calls `IDatabaseService.FindCharactersByVoiceKey(...)`. Add `IDatabaseService _db` to the constructor list.

### BLK-5: `IDatabaseService.FindCharactersByVoiceKey` is missing — and the plan doesn't say what it returns

- **Where:** "Files Touched" line 815 (`Add FindCharactersByVoiceKey method (or similar)`); `RunInternal` step 3a at line 458.
- **Finding:** No such method exists today (see `IDatabaseService.cs`: `GetVoices`, `GetVoiceByKey`, `GetCharactersWithVoiceClips` — none are by voice-key). The plan also doesn't specify the return type, language filter, or how the lookup turns into NPC IDs that the SCD candidate collector consumes.
- **Impact:** The "voice has at least one configured character with the client language" UI filter (line 723–724, Open Question #4 "decided") cannot be answered without the method existing. Without a contract, two implementors will produce two different return shapes.
- **Suggested fix:** In "Files Touched" (line 815) replace with a concrete signature, e.g.:
  ```csharp
  /// <summary>Characters configured with the given voice key. Pass language to filter
  /// to one client language; pass null to return rows for all languages.</summary>
  List<CharacterEntity> FindCharactersByVoiceKey(string voiceKey, int? language = null);
  ```
  Add a sibling helper to feed the dropdown filter:
  ```csharp
  /// <summary>Voice keys that have ≥1 character row in the given language.
  /// Used by the dataset-export voice dropdown.</summary>
  HashSet<string> GetVoiceKeysWithCharactersInLanguage(int language);
  ```
  Add both to `IDatabaseService` and `DatabaseService`, with tests in `DatabaseServiceTests.cs`. Reference the new methods explicitly in the Service Architecture flow (line 458).

### BLK-6: Audio path source for actor-split partitioning isn't pinned

- **Where:** line 314 ("a candidate's `audioBase` is checked against each epoch's `audioPathPatterns`") vs. line 268–269 (filter step 4 mentions partitioning by "the audio path").
- **Finding:** The plan oscillates between matching against `audioBase` (line 314) and "the audio path" (line 269). These are two different strings (the bare base vs. the resolved full SCD path including expansion token). Compounds BLK-2.
- **Impact:** Even if BLK-2's path patterns are corrected, the implementor doesn't know whether to match before or after `VoiceScdPaths.Build` resolves the file. They differ in expansion-token availability.
- **Suggested fix:** Once BLK-2 is decided, replace both the line 269 phrase and the line 314 sentence with the single, exact field name (e.g. "match `audioFileBase` via regex" OR "match the resolved full SCD path returned by the candidate collector"). Pick one and use it consistently.

---

## Gaps

### GAP-1: `IDatasetExportService.IsRunning` cancellation/disposal contract missing

- **Where:** lines 423–432 (interface) and 588–600 (state management — but that section is in the duplicate block).
- **Missing:** No description of how `IsRunning` is reset on cancellation, exception, or window dispose. Existing pattern (`IVoiceSampleExtractorService`) sets `IsRunning=false` in `finally`; the plan should say so explicitly so implementors don't leak the flag and brick the button.
- **Suggested addition:** After the interface block at line 432, add: "Implementation contract: `IsRunning` is set true on `RunAsync` entry, reset to false in a `finally` block before the awaited Task completes. Cancellation throws `OperationCanceledException` *after* the wipe step so partial output is always cleaned up on the next click."

### GAP-2: No spec for "Export All" iteration order, or behaviour on partial failure

- **Where:** UI placement section line 403–404 mentions an Export All button; service architecture line 456 says "For each voice to export (one or all)".
- **Missing:** Order (alphabetical? insertion?), behaviour when one voice fails (skip and continue? stop?), how progress percentage is computed across N voices, whether the wipe at line 454 happens once at the start of the All run or once per voice.
- **Suggested addition:** New paragraph after line 466: "Export-All semantics: voices iterated in `voiceKey` ascending order. Wipe is a SINGLE pre-step covering the whole `FF14-Finetune/` root, not per-voice. A per-voice exception is logged at Warning, the bucket is skipped, and the remaining voices continue. Progress: `ProgressChanged(stage, doneClips, totalClipsAcrossAllVoices)` where the total is computed after the candidate collection + filter step before any clip is written."

### GAP-3: No spec for `lang.txt` content under the corrected single-language design

- **Where:** lines 104–106 (research findings: 2-letter ISO from `ClientLanguage`) and line 540–542 (residue: "majority of clips" — DEAD with single-language pivot).
- **Missing:** Explicit statement that under the new design `lang.txt` is just `LanguageCodeForScd(clientLanguage)` (`en`/`de`/`fr`/`ja`) with no fallback / majority logic.
- **Suggested addition:** Replace lines 540–542 (which will be deleted with BLK-1) by adding a one-liner to the corrected metadata schema section after line 376: "`lang.txt` is the 2-letter ISO code of the **client language** at run time — `en` / `de` / `fr` / `ja`. Reuse `VoiceScdPaths.LanguageCodeForScd(language)` so the mapping stays single-sourced."

### GAP-4: Missing handling for voices that have no characters in client language

- **Where:** Open Question 4 line 723–724 says "voice dropdown is filtered to voices that have at least one configured character with the client language", but nothing in §UI Placement (lines 391–415) or §Service Architecture explains the "Export All" behaviour when this filter excludes a voice.
- **Missing:** Does Export All iterate over the *filtered* dropdown set, or over `IDatabaseService.GetVoices()` raw? If the latter, the user gets empty datasets for voices with no client-lang characters.
- **Suggested addition:** In the UI section after line 405, add: "Export All iterates the same filtered voice list shown in the dropdown — i.e. only voice keys with ≥1 character row in the client language. Voices without coverage are skipped silently."

---

## Contradictions

### CON-1: Speech filter is "always applied" vs. exposed as a UI toggle

- Lines 178–181: "Do **not** add a config toggle for the initial implementation."
- Lines 244, 248, 265: filter chain has speech filter as the always-on step 1.
- Lines 577–581 (residue): UI section adds an "Apply speech filter" checkbox defaulting on.
- **Resolution needed:** Delete the residue block (BLK-1 covers this). Keep the always-on rule.

### CON-2: Per-voice cap stated as "10–15%" vs. "~12%" vs. "every 10th"

- Line 24: "200 train samples + 10–15% eval".
- Line 79: research finding "10–15% of total clips".
- Line 248: "200 train + 10–15% eval".
- Line 275: "200 train + ~12% eval … take first 200 for train, next 24 (12% of 200)".
- Line 381: "Total target: 200 train + 24 eval = 224 clips per dataset."
- Line 558–560 (residue): "every 10th row into eval … 90/10 train/eval".
- **Resolution needed:** The user-pre-decided value is 200 + 24 (≈12%). Normalize all references: change line 24 to "200 train + 24 eval (~12%)", change line 79 research finding to a neutral "AllTalk wiki recommends ~10–15% for eval; this plan ships 24/224 = ~10.7%", and delete the residue at lines 558–560 with BLK-1.

### CON-3: "Always wipe" vs. "wipe is the FIRST step"

- Line 357: "Always wipe the entire `FF14-Finetune/` folder at the start of a run".
- Line 838 risk register: "Wipe is the FIRST step of `RunInternal`".
- Line 454: `RunInternal` step 2 — wipe is the SECOND step (after splits config load).
- **Resolution needed:** Either move splits-config load to step 0 (pre-wipe), OR change line 838 to "wipe runs after the splits-config load, which only reads files and cannot leave partial state". Recommend the former: load config → validate → wipe → write. That way a malformed splits JSON aborts the run before nuking the previous successful dataset.

### CON-4: Filter pipeline "5 steps" vs. order in narrative text

- Lines 261–275: numbered list — speech (1), length (2), 19 s cap (3), actor split (4), per-voice cap (5).
- Lines 244–249 (table): listed as speech, length, per-clip cap, selection, per-voice cap — no actor-split row.
- **Resolution needed:** Update the table at lines 240–249 to add an "Actor-split partitioning" row between "Per-clip duration cap" and "Selection", marked "applied" for Dataset Export and "n/a" for Voice Sample Extractor.

### CON-5: 19-second cap "after MS-ADPCM decode but before resample" — needs sanity check

- Line 268–269: "Computed *after* MS-ADPCM decode but *before* resample (cheaper to drop early)".
- **Issue:** The decode is the expensive step (MS-ADPCM frame iteration); resample is windowed-sinc on already-decoded int16 PCM. Ordering "after decode, before resample" is correct *for cost reasons* (you need the PCM to know the duration anyway, and you avoid the resample), so the rationale stands — but the cheaper alternative would be to compute duration from the SCD header (sample count / sample rate) **without decoding at all**, and reject before decode.
- **Resolution needed:** Either:
  - Keep current ordering and re-word: "Computed from decoded PCM length / sample rate. Decoding is required up front anyway to detect MS-ADPCM-failure, so the cap runs there. Skipping resample on rejected clips is the cost saving."
  - OR add a faster pre-check: read the `data` chunk size + `fmt` chunk's nSamplesPerSec before invoking `MsAdpcmDecoder.Decode`, drop if est. duration > 19 s, only decode survivors. (Better — saves the decode itself for ~30 % skip rate.) If chosen, mention `MsAdpcmDecoder` may need a `TryEstimateDurationSeconds(byte[])` helper.

---

## Untested assumptions

| Quoted phrase | Location | Verifying evidence |
|---|---|---|
| "AllTalk's `finetune.py` rejects training samples longer than ~19 seconds" (line 97–98) | line 97 | Cite the exact line/range in the upstream `finetune.py` (commit hash). The "~" hedge weakens the hard cap rule below. |
| "the XTTS DVAE has a fixed-length conditioning window and longer clips are dropped or fail the preprocessor" (line 98) | line 98 | XTTS paper section + `finetune.py` reference. Otherwise this is unprovable folklore. |
| "if the male version is non-speech, the female version will be too" (line 132–133) | line 132 | True for current `VoiceExtractTextCleaner` because gender expansion only swaps tokens, not structure. Add a one-line test (`IsSpeech(maleVariant) == IsSpeech(femaleVariant)` for a representative gendered line) to lock this in. |
| "popular voices that span actor changes still produce 2× datasets" (line 835) | line 835 | Conditional on BLK-2 being fixed — currently false. |
| "FFXIV's own preprocessing produces … 16-bit mono PCM at 22050 Hz" (line 90–93) | line 90 | The plan asserts the existing extractor pipeline produces this. Confirmed by `Services/CLAUDE.md` ("16-bit PCM mono at 22050 Hz"). OK. |
| "Kanji density means 8 chars is roughly equivalent to 3–4 EN words of content" (line 165–166) | line 165 | Linguistic claim — fine as a starting heuristic; Open Question #1 already plans empirical re-tuning, so leave as-is. |
| "samplesPerNpc (1–5)" (table at line 248, "PickN" path) | line 248 | Verify against `VoiceSampleExtractorService` — in particular that `samplesPerNpc` is bounded. Not blocking. |

---

## Decision-propagation misses

| Decision | Where decided (Open Questions / user direction) | Missing-from doc | Suggested paragraph |
|---|---|---|---|
| Source = SCD only, no runtime audio | line 728, line 19–22 | Lines 520–698 (entire residue) still describe `voice_clip_generations`/`voice_clips` reads | Delete BLK-1 block. |
| Voice selection by Voice Key | line 729 | UI section at lines 391–415 references "AllTalk voice" (line 569 residue: `IDatabaseService.GetVoices()`). The corrected block (line 397–401) says "voice name dropdown" but never explicitly states it's keyed on `VoiceKey`. | Insert at line 401: "Each dropdown entry's value is the `VoiceEntity.VoiceKey` (the BackendVoice string). The displayed label may be the voice's friendly name; the export call passes the underlying key." |
| Only client language exported | line 730, line 723 | Research findings line 105–106 still says "Map from `ClientLanguage`" without stating the run is single-language by design. The Risk Register doesn't mention the constraint. Issue 1 § "Language-awareness approach" line 184–187 doesn't note that only the client language flows through. | Add a one-line heading note at line 38: "**Language scope: client language only.** Multi-language export is non-goal — see Goals." Also remove the dead paragraph at line 540–542 (covered by BLK-1). |
| Always wipe + rebuild | line 731 | Mentioned at line 357 and risk register, but not in the Files Touched table (no entry for output-folder management). | Add a row to the Files Touched table after line 819: "`Echokraut/Helper/Functional/SafeWipe.cs` (or inline in `DatasetExportService`) — recursive delete + recreate of `FF14-Finetune/`. Add unit test that wipe leaves any sibling `FF14-Voices/` untouched." |
| Output path conditional on `Alltalk.LocalInstall` | line 732 | Configuration today exposes `_config.Alltalk.InstanceType` (enum `Local | Remote | None`), not `LocalInstall`. The plan at lines 333–337 uses `_config.Alltalk.LocalInstall` — that property does not match the canonical setting. | Change line 335 to `_config.Alltalk.InstanceType == AlltalkInstanceType.Local` (verify exact enum name in `DataClasses/AlltalkData.cs`), and document the resolved-path computation on whichever path the user has configured. |
| Hard 19.0 s cap | line 733 | Issue 1 doesn't mention it (correctly — it's an Issue 2 concern). Test plan at line 749 doesn't list a 19 s boundary test. | Add to test list (line 691–697 or its replacement): "DatasetExportServiceTests: clip exactly 19.000 s included; 19.001 s excluded; 0.0 s gracefully handled." |
| 200 + ~24 cap as LAST filter | line 734 | Filter table at lines 240–249 places "Per-voice cap" last in the column header but doesn't note the LAST-filter property explicitly; CON-2 above. | After line 275 add: "**Order rationale:** the cap runs strictly last so deterministic shuffle samples from the post-rejection survivor pool, not the raw candidate list — keeps the eval set representative of what survived all quality filters." |
| Actor splits via `VoiceActorSplits.json` | line 735 | Files Touched line 819 references csproj embedding but doesn't mention adding the resource to `ChangelogService`/embed-pattern docs in `Services/CLAUDE.md`; `Echokraut/CLAUDE.md` would need an entry like the existing `RemoteUrls.json` one. | Add a row to Files Touched: "`Echokraut/CLAUDE.md` and/or `Echokraut/Resources/CLAUDE.md` — document the `VoiceActorSplits.json` schema, prefix-vs-regex match rule, and three-vs-two layer override (embedded only + local override; no remote layer per line 326)." |

---

## Terminology drift

| Concept | Synonyms found | Proposed canonical |
|---|---|---|
| Output root for the dataset feature | `FF14-Finetune/` (line 336, 344, 357, 795), `finetune-datasets/` (line 524 — residue), "the project root" (line 69) | `FF14-Finetune/` (matches sibling-of-FF14-Voices convention) |
| Dataset directory per voice | `{VoiceName}/` (line 321, 345), `{VoiceName}_{epochName}/` (line 321), `<SanitizedVoiceName>/` (line 525 — residue) | `{VoiceKey}[_{epochName}]/` (BLK-5 fix: clarifies it's the VoiceKey, not a display name; sanitization needs an explicit sanitizer reference) |
| Speaker label in CSV | `speaker_name` column header (line 64, 367, 547), values: `Yshtola` / `Yshtola_pre60` (line 65, 369, 548) | Keep `speaker_name`. State explicitly that the value matches the directory name. |
| The clip filter call | "speech-content filter" (line 16, 244), "speech filter" (line 263, 581), `IsSpeech` (line 116, 192, 209) | "speech-content filter" in headings, `IsSpeech` for the method name. |
| The voice-actor split unit | "epoch" (line 26, 286, 312), "split" (line 25), "bucket" (line 272, 461) | "epoch" — already dominant; replace "bucket" usages at 272, 461 with "epoch". |
| The audio path discriminator | "audio path" (line 269), "`audioBase`" (line 314) | Pick after BLK-6 is resolved. |

---

## Open Questions consistency

The user pre-decided 6 items (lines 728–736). The four remaining open questions:

- **OQ-1 (line 705–708) Threshold values for speech filter:** genuinely open — empirical measurement against real data is required. Keep.
- **OQ-2 (line 710–714) Initial `VoiceActorSplits.json` content:** genuinely open. Keep, but tie to BLK-2 — content discussion is moot until the matching scheme is fixed.
- **OQ-3 (line 716–720) `cutXXXX` boundary for Y'shtola DE:** **NOT open as currently framed.** It presupposes the broken prefix-match scheme (BLK-2). After BLK-2 is fixed, restate as "What `audioBase` numeric range / regex marks the 6.0 → 6.1 transition for Y'shtola DE?" Or drop entirely if OQ-2's empirical-research scope already covers it.
- **OQ-4 (line 722–724):** flagged as "confirmed" but it's in the Open Questions section. Move it to "Already-decided" at line 727 and delete from open list.

**Suggested fix:** Reorder the Open Questions section: move OQ-4 down into "Already-decided" as a bullet "Voice dropdown filter: only voices with ≥1 character in client language; no language dropdown." Reframe OQ-3 to match the post-BLK-2 discriminator. Keep OQ-1 and OQ-2.

---

## Files Touched accuracy issues

- **Missing entry:** the new helper that loads/parses splits — line 807 mentions `VoiceActorSplits.cs`, but the matching `VoiceActorSplitsTests.cs` is at line 823. Good. However, no entry for the local-override loader pattern (the layered three-source approach used elsewhere requires a service or static helper plus tests). Add: "splits loader merges only embedded + local override (no remote, per line 326). Implement in `Helper/Functional/VoiceActorSplits.cs` with the exact two-layer pattern, not the harvest's three-layer pattern."
- **Wrong scope:** line 815 — "Add `FindCharactersByVoiceKey` method (or similar)". Replace with the two specific methods proposed in BLK-5.
- **Missing test entry:** Add "`Echokraut.Tests/DatabaseServiceTests.cs` — extend with `FindCharactersByVoiceKey` and `GetVoiceKeysWithCharactersInLanguage` cases."
- **Missing entry:** the corrected `RunInternal` calls into the shared candidate collector. If the helper is extracted as `Helper/Functional/SCDCandidateCollector.cs` (line 449–451 mentions it as an alternative), add it as a row.

---

## Risk Register accuracy

- Line 835: "popular voices that span actor changes still produce 2× datasets" — false until BLK-2 is fixed. Either downgrade to "*if* split discriminator is correct" or rewrite after BLK-2 lands.
- Line 836: "epoch ordering ambiguity" — depends on BLK-2 outcome. Currently moot.
- Line 838: contradicts CON-3 — wipe is step 2, not "the FIRST step". Rewrite.
- Missing risk: "Splits config load fails after wipe → user is left with empty `FF14-Finetune/` and no dataset". Add row: "Mitigation: load + validate splits config BEFORE the wipe step (see CON-3 fix)."
- Missing risk: "User has AllTalk Local install but `LocalInstallPath` is empty / unwritable → `Directory.Delete` throws on first run". Add row: "Mitigation: pre-flight check `Directory.Exists(parent) && IsWritable(parent)` and surface a terminal-status error before wiping anything."

---

## Suggestions (non-blocking)

- The "Files Touched" table conflates new files and modified files. Add a column "Kind: new / modify".
- Step-by-step implementation order (lines 477–518 corrected, plus the residue at 663–697) lists 11 steps in the corrected version and a different 10 steps in the residue. After BLK-1 deletion only the corrected 11-step list survives — verify the order builds top-down (e.g. step 4 `BatchOperation.DatasetExport` is referenced by step 5 `ServiceBuilder` registration, fine).
- The `metadata_*.csv` schema documentation appears three times (lines 60–67, 363–370, 545–550). The third copy disappears with BLK-1. Verify line 60–67 (research findings) stays purely descriptive ("AllTalk's canonical format") and line 363–370 (corrected dataset spec) is the project's binding spec.
- Consider noting in the plan that `VoiceExtractFileNames.Sanitize` (referenced at line 535 in the residue) is the right sanitizer for the directory name even after BLK-1 deletion — pull that one sentence into the corrected file-layout block at line 343–353 if the helper is real.
- The 200/24 split is taken from the *survivors* via deterministic shuffle (line 274–275, 383). Worth noting: if survivors < 224, the per-voice cap effectively becomes "take all" and the train/eval ratio shifts (correctly noted at line 384–386). Add a hard floor: "below 25 surviving clips, abort the bucket entirely and log Warning — datasets that small are not worth writing to disk."
- The plan mentions per-clip filenames `clip_NNNNNN.wav` (6-digit) but the cap is 224 — 4 digits suffice. Pick one (`clip_0001.wav` is plenty). Same fix in lines 350–352, 530–532.

---

## Scope of this review

- Documents read: 1 (`docs/plans/voice-sample-improvements.md`), plus cross-reference reads of `Echokraut/Services/IDatabaseService.cs`, `Echokraut/DataClasses/Database/CharacterEntity.cs`, `Echokraut/Helper/Functional/VoiceExtractKey.cs`, `Echokraut/Helper/Functional/VoiceScdPaths.cs`, `Echokraut/Services/VoiceSampleExtractorService.cs` (focused grep). Project `CLAUDE.md` files (root, `Services/`, `DataClasses/`) consulted via the in-context system reminders.
- Documents explicitly skipped: source code beyond the cross-reference reads (per user direction "Don't review the source code — just the plan doc").
- Web searches performed: none (the AllTalk / `finetune.py` claims were taken at face value as research findings; flagged in §"Untested assumptions" for citation rather than verified).
- Confidence: **high** for BLK-1 / BLK-3 / BLK-4 (textual duplication, easy to confirm); **high** for BLK-2 / BLK-6 (verified against `VoiceScdPaths.cs` and `VoiceExtractKey.cs`); **high** for BLK-5 (verified against `IDatabaseService.cs`); **medium** for CON-2 / CON-5 (judgement calls about which value to standardize on).
