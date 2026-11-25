## Audio packages (`IAudioPackageService`, `*.ekpack`)
Hand a big batch of generated audio to another user so their install attributes every file to the
right character, language and voice. UI: Game Data Tools → "Import / Export".

- **A package is a plain zip**: `manifest.json` at the root, the WAVs under `audio/<speaker>/<file>.wav`.
  Nothing is written *into* the audio files — no RIFF `LIST`/`INFO` tags, no custom chunk. That was
  the deliberate choice over embedding metadata (evaluated 2026-08-09): the live generation path and
  `RawPcmToWav` stay untouched (its size patching uses hard-coded offsets 4/40 that a chunk before
  `data` would invalidate), and a package can be built from audio generated years ago. **If you ever
  do embed a chunk, it is an addition to this, not a replacement** — the manifest stays the source of
  truth because a receiver must be able to index 20 000 clips without opening 20 000 files.
- **The manifest is versioned** (`AudioPackageFormat.CurrentVersion` / `MaxSupportedVersion`). A
  package from a newer plugin is refused with a readable message rather than half-read.
- **What never leaves the sender** (`Helper/Functional/AudioPackageRules`, pure + unit-tested):
  - `IsShareable` — a placeholder clip's `alias_gender == 0` generation speaks the *exporter's own
    character name*. Those are skipped and counted; their male/female alias variants
    (`alias_gender` 1/2, "Adventurer"/"Abenteurer") are exactly the shareable form and go in.
  - `RelativeEntryPath` keeps only `<speaker folder>/<file name>` — the sender's absolute
    `LocalSaveLocation` (which contains a Windows user name) never reaches the manifest.
  - No `player_content_id` and no player name are written; imports always store under
    `playerContentId = 0` (`ImportPlayerContentId`).
- **Import creates what's missing** — character → context → `voice_clips` → `voice_clip_generations`,
  so a receiver who never harvested still gets working audio. `UpsertCharacter` is called with an
  empty `VoiceKey` on purpose: which voice an NPC uses is the receiver's setting; the *generation*
  row records what the audio was actually made with.
- **Existing local audio is never overwritten.** The file is kept and counted under
  `SkippedExistingFile`, but the DB row is still written so the clip counts as generated.
- **Zip-slip guarded** via `ResolveImportTarget`; entries resolving outside `LocalSaveLocation` are
  counted as failures.
- **Idempotent**: a second import of the same package creates no characters, no clips and no
  duplicate generation rows (`LogVoiceClipGeneration` upserts on the unique key).
- **Bulk plumbing**: import sets `BulkMode` + `SuppressEvents` and restores both in `finally`,
  followed by `FlushChanges` / `RefreshCaches` / `NotifyVoiceClipLogged`. A per-run `ImportCache`
  memoizes character ids, ensured contexts and each character's existing clip keys — without it every
  entry re-queried that character's whole clip list. New clips need a `FlushChanges` before their
  generation row, because batch mode defers `SaveChanges` and the id would still be 0.
- Covered by `AudioPackageRulesTests` (rules) and `AudioPackageServiceTests` (two in-memory installs
  round-tripping a real zip through the temp folder).

## Game-voice reference (`IGameVoiceReferenceService`, phase 4 of `docs/plans/game-voice-reference.md`)
The only path that may use game audio (root `CLAUDE.md` › hard constraint). **Off by default**
(`Configuration.GameVoiceReferenceEnabled`); switched on only via the opt-in dialog in the Backend tab
(phase 5, see "Opt-in + output isolation" below and `Windows/CLAUDE.md`).
- **Pieces:** `GameVoiceDataSource` (Lumina adapter: `cut_scene/*` rows, ENpcResident names, SCD →
  `echoscd.ScdVoiceDecoder` → 24 kHz mono PCM, all in memory) · pure helpers in
  `Helper/Functional/GameVoice/` (`GameVoiceTextIndex`, `GameVoiceSpeakerMap`, `GameVoiceRanking`,
  `WordErrorRate`, `InlineReferenceAssembler`, phase 6: `StoryProgressTable`, `GameVoiceEpochs`,
  `SpeakerChangeDetector`, `GameVoiceMatchSuggester`, `GameVoiceAliasMerge`, `GameVoiceNpcOverride`) ·
  `GameVoiceReferenceService` (orchestration) · `GameVoiceMappingService` (alias + split files) ·
  `StoryProgressService` (MSQ progress) · `GameVoiceAssignmentSession` (maintenance walk) ·
  `EchokrauTtsBackend` as `IEchokrauTtsReferenceClient` (`/health`, `/asr-check`, `/speaker-embedding`).
- **Index per speaker TOKEN + epoch (phase 6, §11.1, schema v8 `game_voice_index`):** language,
  normalized token (`yshtola`), epoch, audio base, rank, score, `IndexVersion`, `lines_hash`. Replaced
  v6 `game_voice_references` (per character — lost on every mapping reset; Papalymo was character 7,
  11 and 15 in one test). The v8 migration DROPS the old table (no token in its rows; re-ranking is
  cheap). A row with `Rank 0` + empty audio base = "ranked, nothing usable" so a resumed batch skips
  it. `lines_hash` = sha256 over (audio base, text) of the epoch's lines → a game patch that adds,
  removes or rewords a line re-ranks only that token. Suffix `:noasr` = ranked without Whisper
  (counts as current live, as outdated for a Whisper batch). Ids only, never audio or vectors.
- **Same NPC only:** speaker → ENpcResident ids (live `VoiceMessage.NpcBaseId`, the character's
  `character_instances`, exact client-language name) → English names → tokens they own. A token is
  owned via the alias file (`"Disabled": true` = owned by nobody on purpose) or an exact name match; a
  token whose resolved ids carry **different full names** (shared first name) is owned by nobody
  (`AmbiguousTokens`) — never "similar" voices — **unless the token IS one of those full names**
  (2026-11-15, live: `ESTINIEN` hit "Estinien" ×229 and "Estinien Wyrmblood" ×2 → no game voice):
  then that name owns it and the longer names (same first name + surname) are the same NPC written
  out in full and speak with it too (`GameVoiceSpeakerMap._sameNpcNames`; user call). Real data
  with this shape: Estinien, Nero / Nero tol Scaeva, Sphene / "Sphene actress". **User override first:**
  `Configuration.GameVoiceNpcOverrides["DE|y'shtola"]` (NPC edit window field: empty = automatic,
  `-` = none, else a token).
- **Sperrlogik (§5.3):** `Availability` = `Disabled` (switch off) / `WrongBackend` (AllTalk) /
  `Unknown` / `ServerUnsupported` (`/health` without `inline_reference: true`) / `Available`. Only an
  explicit `true` counts. **Unreachable `/health` = `Unknown`, not Unsupported** (a restarting wrapper
  would otherwise mean 5 min of voice pack). Probe cached 5 min, refreshed in the background;
  `ReportReferenceRejected` (a `/tts` with reference failed) resets it. **`IEchokrauTtsInstanceService.OnInstanceReady`
  (every local (re)start, e.g. engine switch) drops probe + LRU and probes at once** — otherwise the first
  line sends the old engine's reference length (Qwen 20 s → Pocket 400). Logged once per change.
- **Mapping files (§11.5, `GameVoiceMappingService`):** `GameVoiceAliases.json` and
  `GameVoiceActorSplits.json`, each embedded < remote (`RemoteUrls.gameVoiceAliasesUrl` /
  `gameVoiceActorSplitsUrl`, fetched in the background, failure = embedded) < local file in the plugin
  config dir. `Changed` → the service rebuilds its token maps and clears the LRU — **no re-ranking**
  (index is per token). The two RemoteUrls fields go into `RemoteUrls.json` only AFTER the plugin
  release (it is read live from `main`). Splits are deduplicated per name+language before
  `VoiceActorSplits.Build` (which warns on every duplicate).
- **Epoch by the story patch OF THE LINE (§11.4, changed 2026-11-15 after the live test):** a split
  entry is found under the token itself or a client/English name of the token's owner
  (`LanguageIndex.NamesByOwner`). The epoch follows the content being played, not the player's
  furthest progress — a replayed 6.0 chapter (New Game+) or a late 5.x side quest must match the
  voice of its own voiced cutscenes. `IStoryProgressService.ContextFor(lineText, lang)` →
  `StoryContextResolver` (pure), comparing word TRIPLES (shingles; JA = character triples; ≥ 2 per
  line, ≥ 60 % of them in the quest's sheet `quest/<nnn>/<Id>`, cached per quest+language):
  (1) an accepted quest → `LineQuest`; (2) ANY quest with text — the talk before accepting (sequence
  0 of an NG+ chapter quest, a side-quest offer) → `LineQuestNotAccepted`, MSQ wins a tie;
  (3) the accepted MSQ quest → `AcceptedMainScenario`; (4) highest completed patch → `Progress`;
  null → newest epoch. Step 2 needs `IStoryProgressService.Prepare(lang)` (background, once,
  started by `GetOrBuildIndex`; log `Quest texts (German): N quest(s), M MSQ, in X s`; MSQ alone
  was 1053 quests in 0.9 s). Quest → patch: `StoryProgressTable.KeyForQuest` = MSQ row directly,
  otherwise where it unlocks: the `PreviousQuest[0]` chain (≤ 64 hops) to the MSQ quest it follows —
  a side quest added in 6.2 but playable after the 6.0 finale counts as 6.0 (user call: story
  position decides). Only without such a chain `KeyByRowId` (latest patch among MSQ quests with a
  lower row id; ids are handed out in creation order).
  `StoryProgressService` (framework thread): completed progress every 10 s + `IUnlockState.Unlock`
  (binary search over `ScenarioTree` gate/first quests), accepted quests every 1 s
  (`QuestManager.NormalQuests`, ushort id + 0x10000). **Nothing is stored** — the former
  `Configuration.GameVoiceStoryProgress` "never lower" was removed (it made NG+ speak with the new
  voice). `GameVoiceSpeaker.LineText` carries the line (`BackendService` passes `message.Text`;
  batch/tools pass none → steps 2/3). The context is only asked for a token with > 1 epoch, and
  then logged once per call: `Game voice for 'Y'shtola': DE:YSHTOLA:Pre06100 — story T_VER600
  (LineQuest 70…)`. `GameVoiceEpochs.Choose`: latest epoch whose start is reached; before
  the first → first epoch; unknown progress → newest. Clip token `XXYZZ` = expansion/patch/part
  (`06100` = 6.1, `06006` = 6.0 part 6, `06505` = 6.55; `06010` would be 6.0 part 10!). LRU key = the
  selection `DE:yshtola:Post06100 …`, so a progress change picks the other epoch on the next line.
- **Live path (`BackendService.TryGetGameVoiceReferenceAsync` → `GetReferenceAsync`):** text index →
  selection → index rows of each (token, epoch); best `MaxReferences` by score across the speaker's
  tokens. Missing/outdated pair → ranking queued (only with `GameVoiceRankOnConversationStart`) and the
  line waits ≤ `FirstLineWait` (4 s) for the quick stage of THAT pair (newest 10 clips **inside the
  chosen epoch** — fixes the old "quick and refine stage pick different actors"), which leaves a
  provisional reference (`_provisional`, not the LRU) — only when a line waits (batch must not evict
  live NPCs). Two gates (`_rankGate` quick / `_asrGate` Whisper). A stored clip that no longer
  decodes drops that pair's index. 20-entry LRU, nothing on disk. A refused `/tts` with reference is
  retried **without** it. Live NPCs owning no token are remembered for the report.
  **Pitfall (CI 2026-11-23):** a step (probe, index, quick stage) can finish between `TryGetReference`
  and `PendingWorkFor` → "nothing pending" → voice pack. Every step calls `MarkProgress()` BEFORE
  completing its task; the loop retries when `_stepsFinished` moved. Test seam `BeforePendingCheck`.
- **Ranking per (token, epoch):** `SpeechTextFilter.IsSpeech` → newest 40 lines decoded → 3–15 s hard
  → pre-score (duration 0.35 / tempo 0.25 / signal 0.40) → top 5 Whisper-checked (`/asr-check`, 40 %)
  → best + up to 2 duration-spread, all ≥ `MinScore` 0.35. **Bump `GameVoiceRanking.IndexVersion`**
  (now 2) when a filter/weight changes.
- **Batch (§11.2, `BuildIndexAsync(language, GameVoiceBatchOptions, progress, ct)`):** over ALL tokens
  of the text index (voiced NPCs, met or not), per epoch; resumable (current pairs skipped);
  cancellable; decodes in parallel (`BatchDecodeParallelism`), Whisper/embedding serial; options
  `WithAsr` ("fast without Whisper") and `DetectVoiceChanges`. Writes
  `<save>/harvest/game_voice_unmatched_<lang>.json` (`GameVoiceUnmatchedReport`: unmatched tokens with
  clip count, 3 samples, name suggestions; ambiguous tokens; NPCs seen live without token). UI:
  Backend tab (options, cancel, remaining time, "N speakers without a current index" via
  `CountOutdatedTokensAsync`). `BatchOperation.GameVoiceIndex` via `IBatchModeService`.
- **Voice-change detection (§11.3, suggestions only):** with `/health speaker_embedding`, up to 3 clips
  per patch of every token with ≥ 4 patches → `/speaker-embedding` → `SpeakerChangeDetector`
  (centre on the run mean, patch centroids, own patch left out, ≥ 2 patches per side, recursion depth
  2) → `harvest/game_voice_actor_splits_suggested_<lang>.json` in the split format. **Never applied
  automatically.** Measured 2026-11-15 (978 German clips): XTTS encoder Y'shtola 6.1 gap 0.65,
  Hydaelyn 6.0 0.36, worst false 0.28 → threshold 0.32; **Qwen x-vector does not separate** (0.41 vs
  0.39) → uncalibrated threshold 0.6. Details: `docs/research/game-voice-actor-splits-de.md`.
- **Maintenance (§11.5):** Game Data Tools › "Assign game voices": load the report, per token assign an
  English NPC name / "no game voice" / skip (→ local alias file, effective at once), "Export for the
  repo" → `harvest/game_voice_aliases_for_repo.json` (merged, sorted, resource format) to copy over
  `Resources/GameVoiceAliases.json`.
- **Wire format:** `/tts` gets `ref_audio` (base64 s16 **LE** PCM), `ref_text` (game text, joined),
  `ref_ids` (audio bases) and **no `sample`** (wrapper: exactly one of both). Clips are joined with
  300 ms pauses up to 15 s (`inline_ref_max_seconds`). Tests: `GameVoice*Tests` (incl.
  `GameVoicePhase6HelperTests`), `WordErrorRateTests`, `InlineReferenceAssemblerTests`,
  `EchokrauTtsBackendTests`, `BackendServiceTests` (gate), `DatabaseMigrationTests` (v8).
- **Opt-in (§5.2, phase 5):** `Helper/Functional/GameVoice/GameVoiceConsent` holds the dialog text
  (English = `Loc` keys), `CurrentVersion` and `IsGranted`. Confirming stores
  `GameVoiceConsentVersion`/`GameVoiceConsentUtc`; `Configuration.MigrateGameVoiceConsent` (every
  start) turns the switch off when the stored version is not the current one. **Any edit of the
  consent strings → bump `CurrentVersion`** — `GameVoiceConsentTests` pins the text hash per version.
- **Output isolation (§6, schema v7):** `voice_clip_generations.reference_source`
  (`Enums/ReferenceSource`: 0 VoicePack, 1 GameVoice) — on the **generation** row, not the clip,
  because it describes the file and a clip can be regenerated either way (upsert overwrites it).
  `BackendService.GenerateVoice` sets `VoiceMessage.ReferenceSource = GameVoice` only when the
  `/tts` **with** reference returned a stream (a refused reference retries as VoicePack). It flows
  into `LogVoiceClipGeneration` (live: `AudioPlaybackService` → `LiveGenerationLogger`; VCM:
  `VoiceClipManagerService`). Excluded: `GetAudioPackageExportRows` filters GameVoice rows (no
  `.ekpack`), `AudioFileService.WriteStreamToFile` never uploads a GameVoice clip to Drive.
  Feature off: `TryLoadCachedAudio` treats a GameVoice generation as a cache miss (voice pack
  regenerates, §5.5); the Backend tab offers `IVoiceClipManagerService.DeleteGameVoiceClips`
  (files + rows). VCM detail rows are prefixed `[Game voice]` (`IsGameVoiceClip`).
  `VoiceClipManagerService.BuildVoiceMessage` sets `CharacterId`/`NpcBaseId` so VCM regenerations
  can use the reference too. Tests: `GameVoiceClipIsolationTests`, `GameVoiceConsentTests`,
  `BackendServiceTests` (`GenerateVoice_*`).
