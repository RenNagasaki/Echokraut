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
  `WordErrorRate`, `InlineReferenceAssembler`) · `GameVoiceReferenceService` (orchestration) ·
  `EchokrauTtsBackend` as `IEchokrauTtsReferenceClient` (`/health`, `/asr-check`) · DB table
  `game_voice_references` (v6; clip ids + score + `IndexVersion` only).
- **Same NPC only:** speaker → ENpcResident ids (live `VoiceMessage.NpcBaseId`, the character's
  `character_instances`, exact client-language name) → English names → tokens they own. A token is
  owned via `Resources/GameVoiceAliases.json` (+ local `game_voice_aliases.json`, later wins) or an
  exact name match; a token whose resolved ids carry **different full names** (shared first name) is
  owned by nobody — never "similar" voices.
- **Sperrlogik (§5.3):** `Availability` = `Disabled` (switch off) / `WrongBackend` (AllTalk) /
  `Unknown` / `ServerUnsupported` (`/health` without `inline_reference: true`, or unreachable) /
  `Available`. Only an explicit `true` counts. Probe cached 5 min, refreshed in the background;
  `ReportReferenceRejected` (a `/tts` with reference failed) resets it. Logged once per change.
- **Live path (`BackendService.TryGetGameVoiceReference` → `TryGetReference`) never waits:** no
  index / stale `IndexVersion` / text index not built → null (voice pack) and background work is
  queued (ranking only with `GameVoiceRankOnConversationStart`). A stored clip that no longer
  decodes (patch) drops the index. Decoded references sit in a 20-NPC LRU, nothing on disk.
  A refused `/tts` with reference is retried **immediately without** it, so the line is never lost.
  `VoiceMessage.CharacterId`/`NpcBaseId` are set by `VoiceMessageProcessor` next to `VoiceClipId` —
  the generation loop must not read `SpeakerObj` (game object may be gone).
- **Ranking (`RankAsync`, serialised by a semaphore):** latest voice-actor epoch only
  (`GameVoiceActorSplits.json` + local `game_voice_actor_splits.json`, F4) → `SpeechTextFilter.IsSpeech`
  → newest 40 lines decoded → 3–15 s hard → pre-score (duration 0.35 / tempo 0.25 / signal 0.40) →
  top 5 re-decoded for `/asr-check` (WER vs game text, 40 % of the final score; skipped when
  `/health asr_check` is false) → best + up to 2 duration-spread (`SampleSelector.PickDiverse`),
  all ≥ `MinScore` 0.35. Thresholds are start values; **bump `GameVoiceRanking.IndexVersion`** when
  any filter or weight changes so stored indexes re-rank. Unranked NPCs are remembered per session.
- **Batch (F5):** `BuildIndexAsync(language, progress)` → `BatchOperation.GameVoiceIndex` via
  `IBatchModeService`; button "Build index" in the Backend tab (client language).
- **Wire format:** `/tts` gets `ref_audio` (base64 s16 **LE** PCM), `ref_text` (game text, joined),
  `ref_ids` (audio bases) and **no `sample`** (wrapper: exactly one of both). Clips are joined with
  300 ms pauses up to 15 s (`inline_ref_max_seconds`). Tests: `GameVoice*Tests`, `WordErrorRateTests`,
  `InlineReferenceAssemblerTests`, `EchokrauTtsBackendTests`, `BackendServiceTests` (gate).
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
