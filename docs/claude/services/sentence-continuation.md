## Sentence continuation across Talk boxes (`IDialogContinuationService`, option `JoinSplitSentences`, 2026-11-16)
A Talk box that ends mid-sentence gets the rest of that sentence from the game sheet's next row; the
next box is spoken without it. Off by default (Settings → General).
- **Two ways a sentence gets split** (in-game run 2026-11-22, DE, Endwalker MSQ start): (1) **the game
  breaks ONE long sheet row over several Talk boxes** — by far the most common; both open box ends of
  that scene (Urianger, G'raha Tia in `quest/043/AktKma101_04357`) were this kind, and the row-pair index
  alone missed both; (2) a row ends open and the next row ends the sentence (row pairs, 654 tails DE).
  The service tries (1) first: `SentenceContinuationIndex.PagedRowFor` (box = real start of a row ≥
  `MinPagedRowChars` = 200 chars; ~44k of 529k rows DE, stored UTF-8 ≈ 12 MB, keyed by a hash of the first
  `TailChars` normalised chars; two rows going on differently → null), then `ContinueInRow(row, spoken)`
  takes the head from the row itself, judged by the **row's** punctuation (live text has `...` already
  turned into `.` by `AddonTalkHelper` → `NormalizePunctuation`). The service keeps `_pendingRow` +
  `_pendingRowSpoken` (normalised chars spoken incl. the appended head), so page 2/3 of the row are found
  via `EndInRow` even when they do not start the row; a page ending a sentence appends nothing but keeps
  the row. A page break inside a word (`Vor-`/`bereitungen`) joins without space and drops the hyphen.
- **Ellipsis inside a sentence**: `SplitHead` does not end the head at `...`/`…` followed by a lower-case
  word (`war ... verstörend.`); without that Urianger got only `war ...` appended.
- Checking the raw rows: a throwaway Lumina console probe on a machine with the game installed (reference `Lumina.dll` +
  `Lumina.Excel.dll` from Dalamud's `Hooks\dev`, `GetSheet<RawRow>(lang, "quest/…")`, col 0 key, col 1 text)
  answers "is this one row or two?" in seconds; not kept in the repo.
- **Pure parts** in `Helper/Functional/SentenceContinuation.cs`: `SentenceContinuation` (ending
  classification Open/Ellipsis/Complete, `SplitHead`, `Normalize`, `TailKey`, `Join`, `StripHead`) and
  `SentenceContinuationIndex` (tail key → head of the next row). Ellipsis endings only join when the next
  box starts with `...` or lower case. Not stored: different speaker groups (text key without numeric
  segments), a next row without its own sentence end (one hop only), heads > 300 chars, a gender branch
  near the box end, tails < 12 normalised chars; a tail with two different continuations is dropped.
- **Sheets** via `IDialogSheetSource` / `LuminaDialogSheetSource`: `quest/*` + `cut_scene/*` (col 0 key,
  col 1 text) and `DefaultTalk` (3 boxes per row). Text through `GameSheetText.ExtractTextWithPlayerName`
  (moved out of `DialogHarvestService`, same placeholders as the harvest). The index is built once per
  language in the background (`Prepare`, also triggered by the first `Apply`); until then boxes pass through.
- **Live**: `VoiceMessageProcessor.ProcessSpeechAsync` calls `Apply` **before the first await** (boxes
  must arrive in display order). Live text is tokenised (`PlayerNameTokenizer`) before the tail lookup,
  the head gets `SubstitutePlaceholders`; a head that still has a placeholder (name unknown) is not
  appended. **Appending is stateless, stripping is stateful** (only the box right after a box that really
  got the head appended loses its start) — a stateless strip would silence text whenever the previous box
  could not be matched. A box that was only the sentence end becomes empty and is skipped.
  Nothing plays for it, so the playback end never advances it: `VoiceMessageProcessor` raises
  `AlreadySpoken` and `AddonTalkHelper.AdvanceAlreadySpoken` clicks it after 800 ms (a click while the
  box is still writing only completes the text), only with auto-advance on and only while that very box
  is still shown (player may have advanced it meanwhile). Wired in `Plugin.WireEvents`.
- **Echolines**: every result of the paged-row path carries `PagedRow` (the whole row, tokenised); the
  processor reports that row with the "paged" flag (`database.md` › Echolines). Row-pair joins and a box
  that is the whole row leave it null.
- `OriginalText` (DB key, WAV name) stays the box text; only the spoken/`CleanedText` changes, so the
  Voice Clip Manager regenerates the joined text. **Cache**: `VoiceMessage.SentenceJoined` +
  `CleanedTextChanged` (from `LogOrUpdateVoiceClip`, `[NotMapped]` flag on `VoiceClipEntity`) make
  `TryLoadCachedAudio` regenerate a WAV made before the join. Known gaps: switching the option off keeps
  the joined WAVs until regenerated; skipping box N early (cancel on advance) still strips box N+1.
- Tests: `SentenceContinuationTests`, `SentenceContinuationIndexTests`, `DialogContinuationServiceTests`,
  `LogOrUpdateVoiceClipOrphanTests` (CleanedTextChanged).
