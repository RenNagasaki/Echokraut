# DataClasses

Models, configuration, and database entities.

## Configuration (`Configuration.cs`)
- Dalamud JSON config for plain settings (backend URL, UI mode, volume, etc.).
- NPC/player mappings, voices, phonetics, and muted dialogues have been migrated to SQLite — see `Database/`.
- `AutoGenerateShareableAliases` (default false) — when on, every successful generation of a placeholder clip also produces male + female alias variants using `TalkTextHelper.GetPlayerAlias(language, isMale)`. Stored in `voice_clip_generations` with `alias_gender` 1/2 and `player_content_id=0`. UI checkbox lives in Config → Settings → Save/Loading.
- `LastSeenChangelogVersion` (default `"v0.18.0.6"`) — last plugin version whose changelog popup the user dismissed. Read by `IChangelogService` on every plugin start; if any embedded `Resources/Changelogs/v{X}_{LANG}.txt` entry sits in (LastSeen, current], `NativeChangelogWindow` opens after FirstTime. The default points at the last release before the changelog system landed so existing users see the v0.19.0.0 changelog on their first start with the new system. Brand-new installs get this bumped to the current version when the FirstTime wizard completes (so they don't see notes about features they already start with).
- **One-shot config migrations** are run from `Configuration.Initialize()` after Dalamud deserializes the JSON. Each migration must be **idempotent** (it runs every plugin start) and should clear any legacy fields it has translated so future `Save()` writes the canonical form.

## AlltalkData (`AlltalkData.cs`)
- `InstanceType` (enum `Local | Remote | None`) is the canonical setting; persisted directly as the auto-property's backing field.
- Legacy booleans `LocalInstance`/`RemoteInstance`/`NoInstance` are `[Obsolete]` and only kept so configs from before the migration can be deserialized. `MigrateLegacyInstanceTypeFields()` derives `InstanceType` from them on first load and forces them back to `false`. Remove the booleans + the migration call after enough releases have passed for active users to have saved at least once.

## TTS engine selection (EchokrauTTS feature — BLK-1)
- `Configuration.BackendSelection` (enum `TTSBackends.Alltalk | EchokrauTTS`) = which engine is active. **Default is `EchokrauTTS`** (fresh installs land on the recommended remote-ready engine → the First-Time engine selector highlights it by default). Because the field is new, existing installs would silently inherit this default and flip a working AllTalk setup; `MigrateBackendSelectionForExistingInstalls()` (run once from `Initialize`, gated on `Version < 1`) pins any config with `FirstTime == false` back to `Alltalk`, then sets `Version = 1`. Fresh installs (`FirstTime == true`) keep `EchokrauTTS`. After the one-shot bump the user's engine choice persists normally.
- Each engine carries its OWN mode + connection: `AlltalkData` and **`EchokrauTtsData`** (`EchokrauTtsData.cs`) each have `InstanceType`, `BaseUrl`, `LocalInstall`, `AutoStartLocalInstance`, per-engine `HasLiveGeneration`. Defaults: AllTalk `:7851`, EchokrauTTS `:8765`.
- **`EchokrauTtsData.TtsBackend`** (enum `EchokrauTtsEngine.XTTS | F5`, default **XTTS**) = which sub-engine the LOCAL wrapper loads at startup, passed to the installer/wrapper as `--tts-backend` (lower-cased via `TtsBackendArg`). Only meaningful for Local (a Remote server's engine is fixed by its operator). The bootstrap installs BOTH engines, so `IEchokrauTtsInstanceService.SwitchTtsBackend()` just **restarts** the running local instance (no reinstall). Dropdown lives in the shared `NativeEchokrauTtsBuilder.BuildLocalInstance` → appears in both Backend tab + First-Time wizard. **Threading the arg requires the installer** (`EchokrautLocalInstaller` `echokrautts` mode, positional `args[7]`, → `--tts-backend`), so it needs an installer re-release to take effect.
- **`EchokrauTtsData.CustomModelUrl` / `CustomVoicesUrl`** (default `""`) = optional zip URLs (direct or Google Drive) for a user's own model + voice samples, analog to `AlltalkData.CustomModelUrl`/`CustomVoicesUrl`. Installed via `IEchokrauTtsInstanceService.InstallCustomData` → installer `installcustomdataek` mode → model into `echokrautts/models/echokraut_custom` (wrapper auto-detects it for the active engine), samples merged additively into `echokrautts/samples`. Needs the installer re-release. See `Services/CLAUDE.md` (EchokrauTTS engine section).
- **`EchokrauTtsData.XttsFp16`** (bool, default **false**) = load the LOCAL XTTS model in half precision for ~1.3–1.8x faster inference. Passed as `--xtts-fp16 <true|false>` (via `XttsFp16Arg`, positional `args[8]`). Only has an effect with the XTTS engine on a CUDA/ROCm GPU (the wrapper's config field `xtts_fp16` gates it; ignored for F5 / CPU). UI is a checkbox in the shared local-instance builder, shown **only when a GPU is detected** (`IAlltalkInstanceService.IsCudaInstalled` — Windows always true, Linux `which nvcc`); the checkbox is omitted from `AllNodes` (not just hidden) when no CUDA so it reserves no layout slot. Applied via `IEchokrauTtsInstanceService.SetXttsFp16()` which persists + restarts the running local instance (shares `RestartLocalIfRunning` with `SwitchTtsBackend`). Also needs the installer re-release.
- **Engine-aware aggregates on `Configuration` — always gate runtime/UI on THESE, never `Alltalk.*`:**
  - `ActiveInstanceType` → the active engine's `InstanceType`.
  - `HasLiveGeneration` → `ActiveInstanceType != None`.
  - `StreamingGeneration` → shared "play as it streams in" toggle, honored by BOTH backends. Off = wait for the full clip before playback (AllTalk writes to disk first; `EchokrauTtsBackend.MaterializeAudioStream` buffers the response). `[JsonIgnore]` accessor backed by `Alltalk.StreamingGeneration` (no migration; the General-tab checkbox + both backends read this). EchokrauTTS previously ignored the setting and always streamed — fixed by routing both backends through this accessor.
- `TtsInstallRoot` = the shared install folder for BOTH engines (they live side by side under it). Migrated once from the legacy `Alltalk.LocalInstallPath` in `Initialize` (`MigrateTtsInstallRoot`). The Local install-path UI field binds to `TtsInstallRoot`.
- `InstalledInstallerVersion` = the `EchokrautLocalInstaller` release tag currently extracted on disk (BLK-5 version handshake; the provisioner re-downloads when it differs from the remote `InstallerVersion`).
- `EchokrauTtsResponses.cs` = DTOs for the EchokrauTTS `/tts` + health endpoints. `EchokrauTtsData.cs` also holds `HealthPath` (ready-probe path).
- Enum lives in `Enums/TTSBackends.cs`. The Backend-tab dropdown + First-Time engine selector both write `BackendSelection`.

## Database Entities (`Database/`)
EF Core entities for the SQLite database (`echokraut.db`). Managed by `IDatabaseService`.

| Entity | Table | Purpose |
|--------|-------|---------|
| `CharacterEntity` | `characters` | Unique NPCs/players by (name, gender, race, language). |
| `CharacterContextEntity` | `character_contexts` | Context per character (NPC/Player/Bubble). FK → characters. |
| `CharacterInstanceEntity` | `character_instances` | Per-instance data: voice, muted state, notes, volume, location. FK → character_contexts. |
| `VoiceClipEntity` | `voice_clips` | Logged TTS dialog: text, source, timestamp, save_path, has_player_placeholder. FK → characters. |
| `VoiceClipGenerationEntity` | `voice_clip_generations` | Per-player + alias TTS generations. Unique on (voice_clip_id, player_content_id, alias_gender). `alias_gender`: 0=real player, 1=male alias, 2=female alias. FK → voice_clips. |
| `VoiceEntity` | `voices` | Named voice configurations. |
| `VoiceAllowedRaceEntity` | `voice_allowed_races` | Race filter per voice. FK → voices. |
| `VoiceAllowedGenderEntity` | `voice_allowed_genders` | Gender filter per voice. FK → voices. |
| `PhoneticCorrectionEntity` | `phonetic_corrections` | Text replacement rules for TTS pronunciation. |
| `CharacterSpeakerAliasEntity` | `character_speaker_aliases` | Per-language fakename → character mapping (harvest-discovered `(-Fakename-)` hints). FK → characters. Unique on (character_id, language, alias). Lookup index on (language, alias). |
| `EchokrautDbContext` | — | EF Core DbContext with all DbSets. |

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
