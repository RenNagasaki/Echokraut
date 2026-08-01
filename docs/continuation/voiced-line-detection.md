# Voiced Line Detection — Work in Progress

## Goal
Identify which BattleTalk / ContentTalk / NpcYell entries are already voice-acted by the game, so we can:
1. Add a `Voiced` column to the `/ekdump` battle talk dump (`battle_talk_speakers.tsv`).
2. Use the same check in `AddonBattleTalkHelper` to skip TTS generation when the game is already playing a VO line (in addition to the existing live `nextIsVoice` flag from `ISoundHelper.BattleBubbleVoiceLine`).

## Findings so far

### NpcYell sheet has no usable Sound column
Inspected `Lumina.Excel.Sheets.NpcYell` via PE metadata reader (Lumina.Excel.dll fails normal reflection due to source-generator quirks; used `System.Reflection.Metadata.PEReader` against the Dalamud Hooks copy at `C:\Users\jkatz\AppData\Roaming\XIVLauncher\addon\Hooks\14.0.4.5\Lumina.Excel.dll`).

Properties found:
- `Text`, `OutputType`, `BalloonTime`, `BattleTalkTime`, `IsBalloonSlow`
- `Unknown0`–`Unknown8`, `Unknown_70`

No labeled `Sound` / `VoiceData` field. One of the `Unknown*` columns *might* be a voice reference but it's pure guesswork without testing.

→ The "easy win" of filtering `NpcYell` by sound column is **not viable** without first reverse-engineering which Unknown holds the voice ref.

### SqPack file probing is the only static fallback
FFXIV stores VO files at predictable paths based on sheet RowId. Candidate patterns (none confirmed yet):
- `sound/voice/vo_line/<lang>/<RowId>.scd`
- `cut/<ex>/sound/voiceman/voiceman_<RowId>.scd`
- `sound/voice/vo_battle/<RowId>.scd`

Need the actual convention before implementing. Can be found by:
- Asking the user (they may already know from FFXIV Explorer poking)
- OR: dumper probes a list of candidate patterns per RowId and emits which one matched into the `Voiced` column. User runs once, we identify the working pattern, then hard-code it.

## Already done in this session
`Echokraut/Services/DialogHarvestService.cs` `DumpBattleTalkMapping`:
- Added `BNpcName`/`ENpcResident` German lookup for speaker IDs (`ResolveSpeakerNameDe`).
- New TSV columns: `Source | DutyName_DE | SpeakerID | SpeakerName_DE | RowId | Text_DE | Text_EN`.
- BattleTalk rows attempt name resolution; ContentTalk/NpcYell rows leave name + duty empty (no reliable link).
- Build: 0 errors.

## Path conventions (confirmed by user)
- **Cutscene VO** (out of scope here): `cut/<exX>/sound/voicem/voiceman_<id>.scd` — subfolders ffxiv, ex1..ex5, plus a final patch-state subfolder.
- **BattleTalk VO**: `sound/voice/vo_battle/vo_npc<XX>_battle_<lang>.scd` — XX is a battle-NPC catalog index (01..~84), **not** BattleTalk.RowId or BNpcName.RowId. Each .scd contains multiple sub-indices (e.g. vo_npc71_battle_de.scd holds indices 0..5).
- **ContentTalk + NpcYell + (likely) Bubbles**: `sound/voice/vo_line/<id>_<lang>.scd` — id is an increasing counter that may or may not equal RowId. Verified empirically by the dumper.

## Implemented (this iteration)
`Echokraut/Services/DialogHarvestService.cs` `DumpBattleTalkMapping`:
- New TSV `battle_voice_files.tsv` — enumerates all existing `vo_battle/vo_npc<XX>_battle_<lang>.scd` for XX 01..99 across de/en/fr/ja. Used to discover XX → speaker mapping manually.
- Build: 0 errors. Tests: 172 passed.

## Dead end: static vo_line lookup via raw column reads
Attempt: column-probe diagnostic scanned uint columns 0..19 of ContentTalk / NpcYell / ContentDirectorBattleTalk subrows for values that resolve to existing `vo_line/<value>_de.scd` files.

Result: 245 apparent "hits" on ContentDirectorBattleTalk col 4. **All false positives** — verified by inspecting written rows:
- The col 0 (SpeakerID) reads also produce nonsense like `2551121152`, `2649163008` — way outside the legitimate NPC ID range (~1-20000).
- The col 4 values that "matched" vo_line files: e.g. row in "Tausend Löcher von Toto-Rak" (ARR) pointing to `vo_line/8205058_de.scd` which actually contains Wuk Lamat's voice (Dawntrail, 2024) — semantic nonsense.

**Root cause:** `RawSubrow.ReadUInt32Column(N)` for ContentDirectorBattleTalk reads bytes that aren't actually uint32 fields at the assumed offsets. The sheet has packed/non-uint columns we don't have schema for. Coincidental matches are because vo_line filenames live in a numeric range (~8.2M) overlapping with random uint32 reinterpretations of those bytes.

The probe code and `VoicedFile`/`VoicedLangs` columns have been **removed** from `DumpBattleTalkMapping`. The TSV is back to its 7-column form. Only `battle_voice_files.tsv` remains (the per-NPC vo_battle file inventory is still useful).

## To make static detection work, we'd need
- Proper column type definitions for `ContentDirectorBattleTalk` (from SaintCoinach's `ex.json` or FFXIVClientStructs sheet bindings) so we can read the actual fields, not bytes.
- Or: parse the duty Lua scripts (`game_script/content/<dungeon>/...luab`) for explicit voice-playback calls. We already dump these to `instance_scripts.tsv`/`content_scripts/`.

## Recommended: rely on runtime detection only
`ISoundHelper.BattleBubbleVoiceLine` already fires when the game plays a VO file — `AddonBattleTalkHelper` reads this as `nextIsVoice` and skips TTS. This works for all three sources (BattleTalk, ContentTalk, NpcYell) without needing static mapping. No further static-detection work is recommended unless the runtime path proves unreliable in practice.

## Relevant files
- `Echokraut/Services/DialogHarvestService.cs` — `DumpBattleTalkMapping` (line ~2933)
- `Echokraut/Services/AddonBattleTalkHelper.cs` — `HandleChange` (line 103), already has `nextIsVoice` from `ISoundHelper.BattleBubbleVoiceLine`
- `Echokraut/Services/SoundHelper.cs` — fires `BattleBubbleVoiceLine` when game plays a VO file
