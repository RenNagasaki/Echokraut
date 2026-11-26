## Voice Pack (`IVoicePackService` / `VoicePackService`)
- **Why it exists:** the previous "voice starter set" feature decoded FFXIV's own `.scd` voice
  files (MS-ADPCM → PCM → resample) and wrote them out as cloning references. That was removed
  in v0.19.3.0 for legal reasons. The voice pack is the **default voice source and the fallback**.
  Game audio may only come back through the opt-in `IGameVoiceReferenceService` described in the
  root `CLAUDE.md` › "Hard constraint" (same NPC only, decoded in memory via `echoscd`, never written
  to disk, OGG never read). Never re-add an extractor that writes WAVs or a starter set from game audio.
  (`VoiceScdPaths.Exists` / `LanguageCodeForScd` survive, but only to answer "does FFXIV voice this
  line?" for the harvest filter — they never read sample data.)
- **What it does instead:** downloads one curated zip from `RemoteUrlsData.VoicePackUrl` and unpacks
  it into the target folder. `DownloadAsync(ct, outputRootOverride, outputSubfolder)`.
  - `outputRootOverride == null` → `<LocalSaveLocation>/<outputSubfolder>` (default `Voices`),
    **existing files are kept**.
  - `outputRootOverride != null` → `<root>/<subfolder>`, **target is wiped first** (fresh-install
    semantics, same as the old extractor). Used by both install flows:
    `AlltalkInstanceService.Install` → `alltalk_tts/voices`, `EchokrauTtsInstanceService.DownloadVoicePack`
    → `echokrautts/samples`. Both skip the download when the engine's `CustomVoicesUrl` is set.
  - Empty `VoicePackUrl` = pack not published yet → warn + no-op (never fails an install).
  - Zip-slip guarded: entries resolving outside the target folder are skipped with a warning.
- **UI**: `NativeGameDataToolsWindow` "Voice Pack" section (button + shared progress bar).
  `ResolveVoicePackTarget()` picks the ACTIVE engine's local voice folder, else `Voices/`.
- **Batch gating**: `BatchOperation.VoicePackDownload` (was `VoiceExtract`) via `IBatchModeService`.
- **On-disk contract the pack must satisfy** (unchanged from what the engines expect): 16-bit PCM
  mono WAV + a same-basename `.txt` transcript per sample. 22050 Hz (AllTalk) / 24000 Hz (XTTS
  native) both work. The `.txt` is for the engine (F5 needs a reference transcript); the plugin
  never reads it.
- **Filename grammar is load-bearing — get this wrong and voices install but stay inert.**
  `BackendService.MapVoices` stores the filename *with extension* as the voice identity
  (`BackendVoice`) and derives everything else from the basename exactly once, at first sight:
  `Gender_RacePool[-BodyType]_NPCnnn.wav`
  - `ReSetVoiceGenders` reads **segment[0] only** → must be `Male`/`Female`.
  - `ReSetVoiceRaces` reads **segment[1] only**, split on `-` → race token (`All` = every race in
    `Constants.RACELIST`, or an exact `NpcRaces` token: `Miqote`, `AuRa`, `Hyur` — no apostrophes,
    no spaces, not "Hyuran") plus optional `Child`/`Elder`/`Adult` body token. Adult = omit.
  - `UseAsRandom = basename.Contains("NPC")` — **case-sensitive substring**. `FitsNpcData` requires
    it, so a pack voice without `NPC` in the name is never auto-assigned, only manually pickable.
  - `EchokrautVoice.VoiceName` (display) = first `_`-segment that isn't gender/race/body-type.
  - Empty `AllowedRaces` is a hard reject in `FitsNpcData`; empty `AllowedGenders` is a wildcard.
  - **Numeric segments are a trap**: `Enum.TryParse` accepts `"01"` as the enum value 1, so
    `Male_01_Foo.wav` is silently filed as race Hyur. Never use bare numbers as a segment.
  - Optional `Narrator.wav` (exact spelling, top level) replaces the default narrator fallback —
    the `IsDefault` compare uses the full backend string, so it must not sit in a subfolder. It is
    the **shipped** narrator only — the user can override it, see below.
  - Renaming a sample after release = old voice deleted + new voice added + NPC migration. Fix the
    names before publishing the pack.
  - `VoicePackService.WarnAboutUnparsableNames` logs a warning for both failure modes after
    unpacking (malformed grammar / missing `NPC` marker). Good: `Male_All_NPC001.wav`,
    `Female_All-Child_NPC091.wav`. Bad: `voice_m_07.wav`, `Male_01.wav`, `Male_All_M01.wav`.

### Who is the narrator (`NarratorVoicePolicy`, 2026-09-26)
The narrator is the voice for every line **without a resolvable speaker** (quest system text,
`POPMESSAGE`, `QIB`) and the last-resort fallback in `BackendService.PickVoice`. Two inputs, in this
order:
1. `Configuration.NarratorVoice` — the user's pick, stored as the **backend voice key**. Set from
   the per-voice Configure window ("Use as narrator voice"); one config key, so it is exclusive by
   construction.
2. otherwise the `IsDefault` flag, which `MapVoices` puts on `Constants.NARRATORVOICE`
   (`Narrator.wav`) when that voice first appears.

`NarratorVoicePolicy.Resolve(voices, pick)` answers "which voice"; `IsFilterExempt(voice, pick)`
answers "may the race/gender filter reject it" and is what `IsVoiceFittingForNpc` calls.
Three decisions worth knowing before touching this:
- **A pick that names a missing or disabled voice falls back to the flagged default.** A stale key
  (voice pack replaced, voice switched off) must not leave narration mute.
- **`IsFilterExempt` exempts BOTH the pick and the flagged `Narrator.wav`,** even while overridden:
  the flagged one is still the fallback if the pick goes away, and revoking its pass would re-roll
  every NPC already sitting on it. Overriding changes what `Resolve` hands out, not who is exempt.
- **`EnsureFittingVoice` re-points nameless rows every line.** Narrator lines are persisted as a
  character row with an empty name, so their stored voice is not a choice about a character — it is
  whichever voice was narrator when that row was created. Without the re-point, changing the pick
  would never be heard on an installation that has already narrated once. `_db.GetVoices()` serves a
  cached list, and the steady state writes nothing.
- The pick is kept in the JSON config, **not** as a second `IsDefault` row: `MapVoices` re-flags
  `Narrator.wav` whenever it reappears as a new voice, which would fight the user's choice.
  - Subfolders inside the voice folder are **unverified** for both engines — keep the pack flat.
- **Pack content + licensing (must hold for anything published at `VoicePackUrl`)**:
  - Never include CC BY-NC / -ND / -SA material (EARS, Expresso, Emilia, MyST, People's Speech SA
    subset are all disqualified).
  - The zip **must** contain `LICENSE.txt` (full legalcode) and `ATTRIBUTION.csv` with one row per
    clip: source dataset, original speaker id, authors, license, source URL, and whether the clip
    was modified (CC BY 4.0 §3(a) requires the modification notice — every clip in the pack is
    modified, since utterances are concatenated and normalised).
  - Keep the pack versioned and the attribution keyed by speaker id so a GDPR erasure request can
    actually be honoured; don't surface original speaker ids in the UI.
  - Upstream datasets are 1–100 GB and cannot be fetched at runtime — the pack is always a
    pre-curated release asset hosted by us.

### Pack v1.1.0 — child voices rebuilt from English adults (2026-08-02)
Same 260 voices and the same 200 adults as v1.0.0; **only the 60 child voices were replaced**,
~112 MB zipped. The Samrómur (Icelandic) children of v1.0.0 did not hold up in practice — the
cloned output was not good enough — so the documented fallback was taken: child voices are now
**pitch- and formant-shifted English LibriTTS-R adults**, i.e. child-*sounding* adult speakers,
not recordings of children. The pack is therefore single-source (LibriTTS-R) and single-language.

- **The shift is Praat `Change gender...`** (`praat-parselmouth`): resample — which raises pitch
  AND formants together — then restore duration and retarget the pitch median. Moving the
  **formants** is the whole point; raising pitch alone reads as helium, raising formants with it
  reads as a physically smaller speaker.
- Chosen by ear from an A/B set (`demo_child/`), setting "B medium":
  girls `formant 1.20 / 262 Hz / range 1.15`, boys `formant 1.14 / 240 Hz / range 1.00`.
  Result 237–270 Hz, which is the real 7–10-year-old band.
- **Both child genders come from adult FEMALE sources.** Boys and girls are acoustically
  near-identical at that age, and a male source would need a formant ratio near 1.4 and audibly
  falls apart. Sources are paired lowest-pitch-first with the lower (boy) target so each clip gets
  the least manipulation that reaches its target. Source selection is by **measured** F0 ≥ 172 Hz,
  not by corpus gender metadata.
- **Sourcing pitfall — the clean splits are nearly exhausted.** `test-clean` + `dev-clean` +
  `train-clean-100` hold ~330 speakers and 200 already ship as adults; only 58 were left and the
  F0 filter passes ~45 % of those. `build_children.py` therefore has three source paths in order:
  the indexed splits → `stream_sources` over `train-clean-360` → reusing the pack's own adult
  female clips. The middle one walks the split **without a prior index**: rows are ordered by
  speaker, so one `/rows` window every 300 rows *is* a speaker sample and every throttled request
  produces a candidate instead of only mapping one. It filled 39 of the 60; the reuse path was not
  needed, so no speaker appears twice in the pack.
- CC BY 4.0 permits adaptations, but §3(a) requires declaring them: every child row in
  `ATTRIBUTION.csv` carries the exact formant ratio, target pitch and range factor, and `README.txt`
  states plainly that these are not recordings of children.
- `build_children_samromur_v1.py` is kept — it built the published v1.0.0 and is the record of it.

### Pack v1.0.0 — what was actually built (2026-08-01)
260 voices, 1:1 gender ratio in both age groups, ~105 MB zipped:
100 `Male_All_NPCM###` + 100 `Female_All_NPCF###` (adults),
30 `Male_All-Child_NPCCM##` + 30 `Female_All-Child_NPCCF##` (children).
One reference clip per voice, 7.5–14 s, 24 kHz / 16-bit / mono, plus its `.txt` transcript.
Build scripts + `ATTRIBUTION.csv` live in a local `VoicePack/` folder outside any repo.

- **Adults: LibriTTS-R** (CC BY 4.0, Google restoration), splits `test-clean` + `dev-clean` +
  `train-clean-100`. It is already 24 kHz mono — the pack's target format — so adults are never
  resampled. Speaker gender comes from the corpus's own `speakers.tsv` (`doc.tar.gz` on
  <https://www.openslr.org/141/>), never from pitch estimation.
- **Children: Samrómur Children** (CC BY 4.0, Icelandic), dev+test tarballs (~97 MB each).
  Real child speech, ages 6–14, ranked by an SNR/clipping score because the corpus is
  crowdsourced and per-speaker quality varies far more than content does. 16 kHz → 24 kHz.
  The language mismatch was a deliberate bet — cloning references carry timbre, not language —
  and it **lost**: see v1.1.0 above, which replaced these with the formant-shift fallback.
- **VCTK 0.92 was evaluated and rejected**: datashare.ed.ac.uk serves it as one 11.7 GB zip whose
  single inner member is one deflate stream, so HTTP range requests can't pull individual files —
  it's all-or-nothing. LibriTTS-R is reachable per clip through the HF `datasets-server` `/rows`
  endpoint (throttles hard: ~3 workers, long backoff, cache every page).
- **Never truncate a clip to hit the length cap.** Concatenate whole utterances only and skip any
  that would overflow. Cutting mid-sentence desyncs the audio from its `.txt`, which F5-style
  backends pair together — and it is invisible in every install-time check.
