# DataClasses — Schema version history

Moved verbatim from `Echokraut/DataClasses/CLAUDE.md` on 2026-11-20 (file was over the 15k budget). Does not load by itself — read it before working in this area.

## Schema Versions
1. v1: Initial tables (characters, contexts, instances, voices, phonetics)
2. v2: Cascade delete, non-nullable character_id on voice_clips
3. v3: zone_name, map_x, map_y on voice_clips
4. v4: last_seen, zone_name, map_x, map_y on character_instances
5. v5: Rename dialog_encounters → voice_clips
6. v6: Add save_path to voice_clips
7. v7: Add is_adult_voice, is_elder_voice to voices; add language to characters; composite index on voice_clips (character_id, npc_base_id, original_text); update unique index to (name, gender, race, language)
8. v8: Migrate old config.MutedNpcDialogues into character_instances.IsMuted
9. v9: quest_type column on voice_clips
10. v10: drop do_not_delete column from characters
11. v11: world column on characters + lodestone_lookups cache table
12. v12: Merge case-only-different character rows (e.g. "stille Druidin" + "Stille Druidin") and recreate `IX_characters_name_gender_race_language` with `COLLATE NOCASE` on `name`. Going forward `FindCharacter`/`UpsertCharacter` use `EF.Functions.Collate(name, "NOCASE")`. Harvest also normalizes the first letter via `DialogHarvestService.NormalizeNpcName` so German `[a]→"e"` adjective stems can no longer create lowercase variants.
13. v13: `alias_gender` column on `voice_clip_generations` + recreate unique index as `(voice_clip_id, player_content_id, alias_gender)` so a clip can carry the player's own generation (alias_gender=0) plus shareable male (1) and female (2) variants without index collisions. Alias rows always have `player_content_id=0`.

> **Note:** The pre-release rewrite collapsed v1–v13 into a single baseline at `CurrentSchemaVersion = 1`. The list above is kept for historical context. Real upgrades go on top of that baseline:

14. v2 (post-rewrite): `character_speaker_aliases` table — captures `(-Fakename-)` speaker hints per `(character, language)` for the live runtime to map dialog-box names back to canonical characters. EnsureCreated builds it on fresh installs; v1 → v2 migration adds the table via `CREATE TABLE IF NOT EXISTS` (see `EnsureSpeakerAliasTable`).
15. v6/v7/v8 (post-rewrite): `game_voice_references` table (v6, dropped again by v8); `reference_source` on `voice_clip_generations` (v7); `game_voice_index` per speaker token (v8) (see `docs/claude/services/game-voice-and-packages.md` › Game-voice reference).
