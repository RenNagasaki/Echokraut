# DataClasses

Models, configuration, and database entities.

## Further documentation — read the file before working in that area
Split by area on 2026-11-20 (verbatim move). The files under `docs/claude/dataclasses/` NEVER load by themselves.

| Area | File |
|---|---|
| TTS engine selection (`BackendSelection`), `EchokrauTtsData` options (engine id, catalog/ratings, custom data, wrapper version, fp16, Qwen, Pocket), engine-aware aggregates, `TtsInstallRoot` | `docs/claude/dataclasses/tts-engine-config.md` |
| Schema version history (pre-rewrite v1–v13, post-rewrite v2/v6–v8) | `docs/claude/dataclasses/schema-history.md` |

- ⚠ Gate runtime/UI on the engine-aware aggregates on `Configuration` (`ActiveInstanceType`, `HasLiveGeneration`, `StreamingGeneration`), never on `Alltalk.*` — details in `tts-engine-config.md`.

## Configuration (`Configuration.cs`)
- Dalamud JSON config for plain settings (backend URL, UI mode, volume, etc.).
- NPC/player mappings, voices, phonetics, and muted dialogues have been migrated to SQLite — see `Database/`.
- `AutoGenerateShareableAliases` (default false) — when on, every successful generation of a placeholder clip also produces male + female alias variants using `TalkTextHelper.GetPlayerAlias(language, isMale)`. Stored in `voice_clip_generations` with `alias_gender` 1/2 and `player_content_id=0`. UI checkbox lives in Config → Settings → Save/Loading.
- `LastSeenChangelogVersion` (default `"v0.18.0.6"`) — last plugin version whose changelog popup the user dismissed. Read by `IChangelogService` on every plugin start; if any embedded `Resources/Changelogs/v{X}_{LANG}.txt` entry sits in (LastSeen, current], `NativeChangelogWindow` opens after FirstTime. The default points at the last release before the changelog system landed so existing users see the v0.19.0.0 changelog on their first start with the new system. Brand-new installs get this bumped to the current version when the FirstTime wizard completes (so they don't see notes about features they already start with).
- `GameVoiceReferenceEnabled` (default false) / `GameVoiceRankOnConversationStart` (default true) /
  `GameVoiceConsentVersion` (0 = never confirmed) / `GameVoiceConsentUtc` — game-voice reference opt-in
  (plan `docs/plans/game-voice-reference.md` §5). `MigrateGameVoiceConsent` switches it off when the
  confirmed text version is not `GameVoiceConsent.CurrentVersion`. See `docs/claude/services/game-voice-and-packages.md`.
- **One-shot config migrations** are run from `Configuration.Initialize()` after Dalamud deserializes the JSON. Each migration must be **idempotent** (it runs every plugin start) and should clear any legacy fields it has translated so future `Save()` writes the canonical form.

## AlltalkData (`AlltalkData.cs`)
- `InstanceType` (enum `Local | Remote | None`) is the canonical setting; persisted directly as the auto-property's backing field.
- Legacy booleans `LocalInstance`/`RemoteInstance`/`NoInstance` are `[Obsolete]` and only kept so configs from before the migration can be deserialized. `MigrateLegacyInstanceTypeFields()` derives `InstanceType` from them on first load and forces them back to `false`. Remove the booleans + the migration call after enough releases have passed for active users to have saved at least once.

## Database Entities (`Database/`)
EF Core entities for the SQLite database (`echokraut.db`). Managed by `IDatabaseService`.

| Entity | Table | Purpose |
|--------|-------|---------|
| `CharacterEntity` | `characters` | Unique NPCs/players by (name, gender, race, language). |
| `CharacterContextEntity` | `character_contexts` | Context per character (NPC/Player/Bubble). FK → characters. |
| `CharacterInstanceEntity` | `character_instances` | Per-instance data: voice, muted state, notes, volume, location. FK → character_contexts. |
| `VoiceClipEntity` | `voice_clips` | Logged TTS dialog: text, source, timestamp, save_path, has_player_placeholder. FK → characters. |
| `VoiceClipGenerationEntity` | `voice_clip_generations` | Per-player + alias TTS generations. Unique on (voice_clip_id, player_content_id, alias_gender). `alias_gender`: 0=real player, 1=male alias, 2=female alias. `reference_source` (v7): 0=voice pack, 1=game voice (never exported/uploaded). FK → voice_clips. |
| `GameVoiceIndexEntity` | `game_voice_index` | (v8, replaced v6 `game_voice_references`) Ranked game-voice clip ids per (language, speaker token, voice-actor epoch) + `lines_hash`; Rank 0 = "nothing usable" marker. Ids + score only, no audio, no vectors. |
| `VoiceEntity` | `voices` | Named voice configurations. |
| `VoiceAllowedRaceEntity` | `voice_allowed_races` | Race filter per voice. FK → voices. |
| `VoiceAllowedGenderEntity` | `voice_allowed_genders` | Gender filter per voice. FK → voices. |
| `PhoneticCorrectionEntity` | `phonetic_corrections` | Text replacement rules for TTS pronunciation. |
| `CharacterSpeakerAliasEntity` | `character_speaker_aliases` | Per-language fakename → character mapping (harvest-discovered `(-Fakename-)` hints). FK → characters. Unique on (character_id, language, alias). Lookup index on (language, alias). |
| `EchokrautDbContext` | — | EF Core DbContext with all DbSets. |

## Key Models

| Class | Purpose |
|-------|---------|
| `VoiceMessage` | Carries all data for a single TTS request through the pipeline. |
| `NpcMapData` | Per-NPC voice config (loaded from DB via `INpcDataService`). Uses `BodyType` enum (not bool IsChild). Has `Language` field. |
| `EKEventId` | Plugin-specific `EchoEventId` subclass. Construct via `new EKEventId(baseId.Id, baseId.TextSource)`. |
| `HarvestedDialog` | Result from `DialogHarvestService` — NPC name, text, source. |
| `PhoneticCorrection` | In-memory model for phonetic rules. |
| `AlltalkData` / `AlltalkVoices` | AllTalk backend response models. |
| `SpeechBubbleInfo` | Bubble dialog metadata. |
| `VoiceLine` | Sound hook event data. |

## EKEventId Convention
- `EKEventId` extends `EchoEventId` (from Echotools.Logging).
- Lives in `DataClasses/EKEventId.cs`, NOT in the Echotools submodule.
- `_log.Start()` returns base `EchoEventId` — wrap it: `new EKEventId(baseId.Id, baseId.TextSource)`.
- Never cast the return of `_log.Start()` to `EKEventId` — it will crash at runtime.

## Install root default (`C:\LocalTTS`, moved 2026-09-07)

`Configuration.DefaultTtsInstallRoot` is `C:\LocalTTS`; the previous default `C:lltalk_tts`
survives as `LegacyDefaultTtsInstallRoot`. Only the **root** moved — the per-engine subfolders
(`alltalk_tts\`, `echokrautts\`, see `TtsPaths`) are unchanged.

- **Never compare against `DefaultTtsInstallRoot` alone** to answer "is this still the shipped
  default?" — use `IsDefaultTtsInstallRoot`, which accepts both. `MigrateTtsInstallRoot` asks that
  question twice, and getting it wrong breaks in both directions: an existing install sitting on the
  old root reads as a deliberate custom root, and — the sharp edge — `AlltalkData.LocalInstallPath`
  **still initialises to the old path**, so on a fresh install the untouched initialiser reads as a
  "custom" legacy root and drags every new user back onto `C:lltalk_tts`.
  `Migrate_FreshInstall_IsNotDraggedOntoTheLegacyRoot` guards exactly that (verified to fail when the
  legacy arm is removed).
- `LocalSaveLocation` is derived from `DefaultTtsInstallRoot` now; it used to hard-code the old root
  a second time and would have been left pointing at an unused folder.
- `DialogHarvestService.HarvestBaseDir` replaced five copies of
  `SaveToLocal ? LocalSaveLocation : @"C:lltalk_tts"` and follows `TtsInstallRoot` instead — right
  for an existing install, a fresh one, and a user who moved their root.
- Existing users are never relocated: their root is persisted, so the new default only reaches fresh
  configs.
