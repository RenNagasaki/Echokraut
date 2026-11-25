## Database Service (`IDatabaseService` / `DatabaseService`)
- EF Core with SQLite (`echokraut.db` in plugin config dir).
- Schema: `characters` → `character_contexts` → `character_instances` → `voice_clips` → `voice_clip_generations`, plus `voices`, `voice_allowed_races`, `voice_allowed_genders`, `phonetic_corrections`, `lodestone_lookups`.
- Schema version **6** (`CurrentSchemaVersion`; the old v1–v13 chain was dropped in the pre-release rewrite, so numbering restarted). `RunSchemaMigrations()` runs idempotent `EnsureXxx()` steps (`CREATE … IF NOT EXISTS` / `PRAGMA table_info`-guarded `ALTER`), each `internal` with its own test in `DatabaseMigrationTests`. v6 = `game_voice_references` (the plan still says "v14" — same thing).
- `LogOrUpdateVoiceClip()` upserts by CharacterId + NpcBaseId + OriginalText (composite index for performance). **Orphan-resolve fallback**: if the primary text-based lookup misses AND the incoming clip has a non-empty `WavFileName`, a secondary lookup by `(CharacterId, WavFileName)` runs; on hit, the existing row is promoted in place (text/base id/source/language fields filled from the live encounter, no duplicate row inserted). This rescues the wav-only rows produced by `BackfillAudioFiles` so legacy on-disk audio gets stitched onto the live runtime path the first time the same NPC says the same line again.
- `MigrateFromConfig` and `BackfillAudioFiles` are **deferred to post-login**, not run from `InitializeDatabase`. `Plugin.cs::RunDataMigrationsIfLoggedIn()` is invoked from `HandleStartup` (when already logged in) and from `OnLogin`. Reason: placeholder detection and player-content-id resolution need `LocalPlayer` to be present, which isn't true at plugin construction time. `Configuration.AudioFilesBackfillPending` gates the audio scan so it runs once and never repeats. The wav-filename hashing rule mirrors the runtime path: `IAudioFileService.VoiceMessageToFileName(IAudioFileService.RemovePlayerNameInText(originalText))` — `VoiceMessageProcessor` now depends on `IAudioFileService` so it computes and stores `WavFileName` on every new clip, which is what makes the backfill's orphan rows match.
- Characters unique on `(name, gender, race, language)` — NPCs stored per game language.
- `FindCharacter(name, gender, race, language)` — language is **required** (no default). Misleading default of `1`=English caused silent miss-then-duplicate bugs on non-EN clients; removed in current schema.
- Per-player + alias generations: `LogVoiceClipGeneration / GetVoiceClipGeneration / DeleteVoiceClipGeneration` take an optional `aliasGender` param (0=real player, 1=male alias, 2=female alias). Unique index `(voice_clip_id, player_content_id, alias_gender)` lets a clip carry the player's own audio plus shareable alias variants in parallel rows. Alias rows always pass `playerContentId=0`.
- `SuppressEvents` flag: set during batch operations (harvest) to prevent UI threads from querying DB concurrently. Call `NotifyVoiceClipLogged()` once after batch completes.
- `ClearChangeTracker()`: clears EF Core change tracker during bulk inserts to prevent progressive slowdown.
- `FindCharacter()` uses `_writeLock` for thread safety with concurrent harvest writes.
- `Dispose()` explicitly closes connection + calls `SqliteConnection.ClearAllPools()` to release file lock.
- Constructor wraps `InitializeDatabase` in try-catch; calls `Dispose()` on failure so DB is released.
- **`GetAllCharacters()` is the one uncached read — never call it on the live path.** It pulls the
  whole `characters` table (`AsNoTracking().ToList()`) under `_writeLock`; everything else
  (`GetNpcs`/`GetPlayers`/`GetVoices`/`GetPhoneticCorrections`/`GetMutedBaseIds`) serves from a
  cache. Use **`FindCharacterById(int)`** for a single row — the alias resolver in
  `VoiceMessageProcessor` used to call `GetAllCharacters().FirstOrDefault(c => c.Id == …)` per
  spoken line, which on a harvested DB is a five-figure row scan per line.
- **DB events must be raised OUTSIDE `_writeLock`.** `VoiceClipLogged` has `NpcDataService.LoadFromDatabase` attached; firing it under the write lock runs a foreign handler inside our lock. Any handler that takes a lock of its own then forms a lock-order cycle with the dialogue path, which constantly holds that lock and calls into `IDatabaseService` — a full game freeze. `LogVoiceClip`/`LogOrUpdateVoiceClip` used to do this; both now collect a `notify` flag and raise after the lock (`WipeAll` was already correct). **This is what makes it possible to ever put a lock around `NpcDataService`'s mapped-NPC lists** (audit doc B5) — don't reintroduce in-lock event raises.
- **`DatabaseWiped` event** fires from `WipeAll()` after the row-level cleanup completes. `BackendService` subscribes to it in its constructor and calls `RefreshBackend()` so the voices table is repopulated from the running TTS backend instead of staying empty until the next plugin reload.
- **BaseId-first runtime resolver** (`VoiceMessageProcessor.TryResolveByBaseId`): when the live speaker has a non-zero `IGameObject.BaseId` AND `_db.FindCharacterIdByNpcBaseId(baseId, language)` returns a hit, that character row is returned immediately — beating any name-based alias lookup, including unambiguous single-candidate matches. FFXIV cutscene actors keep their real ENpcBase even when the dialog box hides their name as "???" or a fakename, so this is the most reliable speaker identifier. Without it, anonymous speakers fell through to the alias map; if the real speaker hadn't been harvested with `(-???-)`, the resolver picked a wrong-but-present alias candidate and silently attributed the line to that NPC. The historical mis-attribution in existing DBs is what `INpcAttributionRepairService` cleans up.
- **Attribution repair helpers** on `IDatabaseService`:
  - `GetAllInstancesForRepair()` — flat-projected join of every `character_instance` with its parent character's (name, gender, race, language). No EF tracking.
  - `ReassignAttribution(oldCharId, newCharId, npcBaseId)` — moves the instance row (delete+reinsert because PK is composite) AND every `voice_clip` with the same `(CharacterId, NpcBaseId)` pair from old to new. Unique-index collision on `voice_clips (CharacterId, NpcBaseId, OriginalText)` → canonical wins, orphan + its generations dropped via cascade. Returns `(moved, mergedAndDeleted)`. Refreshes muted cache.
  - `DeleteCharacterIfEmpty(characterId)` — returns true if the character row had no instances and no voice_clips and was deleted; idempotent on already-deleted / still-populated rows.

## Changelog on update (`IChangelogService` + `NativeChangelogWindow`)
- Embedded changelog files live at `Resources/Changelogs/v{MAJOR.MINOR.BUILD.REVISION}_{EN|DE}.txt` and are referenced by `Echokraut.csproj` as `<EmbeddedResource>`. Add **two files per release** (EN + DE); FR/JA fall back to EN at runtime via `ChangelogService.PickLanguage`.
- Filename **must** start with `v` and match the regex `^Echokraut\.Resources\.Changelogs\.v(\d+\.\d+\.\d+\.\d+)_(EN|DE)\.txt$`. The `v` prefix anchors the version regex against the surrounding dotted resource path (otherwise the four version dots blend with the namespace dots).
- `Configuration.LastSeenChangelogVersion` (default `"v0.18.0.6"`) holds the last version whose changelog the user dismissed. `ChangelogService.GetUnseenChangelogs()` returns every embedded entry with version > LastSeen and ≤ `Plugin.PluginVersion`, ordered ascending.
- **Brand-new install handling**: the FirstTime wizard's "I Understand" callback (wired in `NativeWindowManager`) calls `IChangelogService.MarkAllSeen()` before opening the config window — without this, the changelog popup would immediately follow the wizard with notes about features the user already starts with.
- **Show gate** lives in `Plugin.HandleStartup` and `Plugin.OnLogin`. Order: `FirstTime` first → `Changelog` second. The changelog only opens when `!FirstTime && IsLoggedIn && !IsChangelogOpen && HasUnseenChangelogs()`.
- Defensive: if `LastSeenChangelogVersion` is corrupt/unparseable, the service returns an empty list (don't show the entire history) and warns. Same for unparseable plugin version.
- Source of truth: the embedded `Resources/Changelogs/v{TO}_{LANG}.txt` files. For external use (GitHub releases, Discord, etc.) reformat from the same content — there's no separate public-facing copy in the repo to keep in sync.

## Voice Clip Manager Service (`IVoiceClipManagerService` / `VoiceClipManagerService`)
- Encapsulates voice clip actions so both ImGui and Native windows stay thin.
- `PlayEncounter()` uses `TextSource.VoiceTest` to bypass dialog-closed check.
- `StopPlayback()` stops current message + clears VoiceTest queue.
- `DeleteAudioForEncounter()` calls `StopPlayback()` + `Thread.Sleep(100)` before delete (BASS holds file lock).
- `GetAudioPath()` uses `SavePath` from DB if available, falls back to computed path.
- `GenerateForEncounter()` saves path to DB via `UpdateVoiceClipSaved()`.
- `BuildVoiceMessage()` constructs `EKEventId` from base via `new EKEventId(baseId.Id, baseId.TextSource)`.
- **Auto alias generation**: when `Configuration.AutoGenerateShareableAliases` is on, every successful `GenerateForVoiceClip` for a clip with `HasPlayerPlaceholder` automatically calls `GenerateAliasVariant(clip, isMale: true)` and `GenerateAliasVariant(clip, isMale: false)` afterward. Each runs as its own backend call; alias-variant failure is logged at warning level and does not fail the main generation. The alias text is `TalkTextHelper.GetPlayerAlias(clip.Language, isMale)` — uses the **clip's** language, not the current client language. Audio file paths diverge naturally because the substituted text differs (alias name vs. real player name).

## Echolines community line database (client, phases 3 / 3b / 7)

`ILineSubmissionService` / `LineSubmissionService` reports dialogue lines to the Echolines service so
lines missing from the plugin's own data can be found. Server side lives in its own repo; the wire
format, limits and consensus rules are quoted in `docs/` of that repo, not restated here.

- **Base address is `RemoteUrlsData.EcholinesUrl`** (`RemoteUrls.json` → `echolinesUrl`), not a
  constant: the service can move without a plugin release, and **an empty value switches reporting
  off entirely** — that is the kill switch if the service has to go down.
- **Call site is step 3.5 in `VoiceMessageProcessor`**, *not* `LogVoiceClip`. `LogVoiceClip` sits
  behind four early returns, two of which fire in None mode — precisely the installations whose
  reports matter most, because they never generate audio and so never learn which lines exist.
- **`EcholinesRules` is the pure, tested half**: source whitelist, language code, suppression key,
  chunk maths, and the privacy guard. Anything decidable without a network or a game belongs there.
  - **Whitelist, never a blacklist.** The call site is passed by *every* source, so a source added to
    `TextSource` later must stay silent until someone adds it on purpose. `Chat_IsNeverReportable` is
    the test that matters — chat is player-authored text.
  - Reported sources: `AddonTalk`, `AddonBattleTalk`, `AddonBubble`. `AddonSelectString` /
    `AddonCutsceneSelectString` are deliberately out (user, 2026-09-07): candidates, but still buggy,
    and in a *shared* database a wrong line that reaches three votes becomes everyone's.
  - The privacy guard rejects text still carrying the player's name **including the first name alone**
    — FFXIV addresses the player by first name far more often than by full name. Name parts of two
    characters or fewer are skipped, or a character called "Yu Li" would silence half the dialogue.
- **Suppression set = "this installation already sent it"**, not "we know this line". Whether the line
  is in the local DB or has audio is irrelevant. Persisted to `echolines-sent.txt` in the plugin
  config directory, appended only after a successful send.
- **Live path**: buffered, flushed every 60 s or at 50 lines, ≤200 lines per request, on a timer
  thread — never the frame thread. A failed send is **dropped, not queued**: a retry queue grows
  without bound during an outage, and one lost report costs nothing when the database is fed by many
  installations.
- **Bulk import (3b)**: `SubmitHarvestAsync` walks the language's stored lines in pages of 1000, one
  request roughly every 11 s (the server's import budget is a few per minute — a harvest is meant to
  take hours). Resume position per language in `Configuration.EcholinesImportProgress`.
  - `GetVoiceClipsForLanguage` orders **by id, not by timestamp**: the run is resumable, so the order
    has to be stable across calls; a timestamp order reshuffles as new lines arrive mid-run and would
    silently skip or repeat pages.
  - `EcholinesRules.ResumeChunk` restarts from zero when the saved position no longer fits the current
    harvest. Skipping costs data and nothing downstream could detect it; restarting only costs time.
  - **Only an unfiltered harvest submits.** A quest-type-filtered run is a partial picture, and
    shipping it as if it were complete is worse than shipping nothing.
- **"Paged" flag** (`EcholinesLine.Paged`, wire `paged`, only written when true; 2026-11-23): the game
  shows long `quest/*`/`cut_scene/*` rows over several Talk boxes, the harvest reports them whole, live
  only the box texts — so the whole row never got a live vote. Server keeps it sticky and catches it up
  on known lines (server repo › `PagedFlagTests`).
  - **Harvest/import**: `EcholinesRules.LikelyPaged` = `AddonTalk` and > `PagedGuessMinChars` (300; DE
    box ≈ 260 chars, JA boxes hold fewer → only misses, never invents). A guess; the sheet has no marker.
  - **Live**: certain. `ContinuationResult.PagedRow` (set by `DialogContinuationService.ContinuePage`,
    i.e. `PagedRowFor`/`EndInRow` matched a row longer than the box) → `VoiceMessageProcessor` step 3.5b
    reports the box AND the whole row (`paged: true`). Only with `JoinSplitSentences` on — the index is
    not built otherwise.
  - **Suppression key** gets a `\0paged` suffix only when flagged: old keys in `echolines-sent.txt`
    stay valid, and a line already sent without the flag goes out exactly once more with it.
- **`installId` is created on first use**, not in the config initialiser — an existing config must not
  silently gain one before the user has seen the switch.
- **One switch, two places, one value**: `Configuration.ShareLinesWithCommunityDb`, default on, in the
  first-time wizard and Settings → General. Existing users never see the wizard again, so **the
  changelog entry is their disclosure**, not documentation.

### Phase 7 — what was actually dead
Only `UploadVoiceLine` (the per-line JSON upload to Drive) had no caller and was removed from
`IGoogleDriveSyncService`, `GoogleDriveSyncService` and `DriveUploadService`. ⚠ **`DriveUploadService`
itself stays** — the plan said to delete it, but it also hosts `UploadFile`, which `AudioFileService`
uses for the WAV upload. `DriveLinkHelper` and the whole download side stay too.
