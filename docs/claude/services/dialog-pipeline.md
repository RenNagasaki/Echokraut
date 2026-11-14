## Dialog lifecycle & `DialogState.CurrentVoiceMessage`
- **Two close signals for the Talk addon.** `AddonTalkHelper` handles the close in `HandleAddonClosed` (clears `InDialog`, flushes the AddonTalk queue via `IAddonCancelService.Cancel(..., dialogClosed: true)`, drops the current speaker, resets alias tracking). It is reached from `AddonEvent.PostUpdate` (addon hidden but still updating) **and** `AddonEvent.PreFinalize` (addon torn down outright — cutscene end, zoning, ESC), guarded idempotently by `wasTalking`. PostUpdate alone missed the teardown case, leaving `InDialog` true so later lines still played.
- **`DialogState.CurrentVoiceMessage` is published early** — in `VoiceMessageProcessor.ProcessSpeechAsync` at step 3.5, right after the NPC data is resolved and **before** the skip checks (muted NPC, no voice assigned, volume 0). The in-dialog toolbar reads it to know who is talking; publishing only for lines that actually play was a catch-22 (an NPC with no usable voice offered no speaker → the voice dropdown stayed empty → the user could never assign the voice that would have made it play). `Volume` is filled in at step 6; `LogVoiceClip` still runs after the checks, so the DB side is unchanged.

## Addon Event Helpers
Hook into game addon lifecycle to capture dialog events:
- `IAddonTalkHelper` — main Talk addon (NPC dialog)
- `IAddonBattleTalkHelper` — battle dialog
- `IAddonSelectStringHelper` / `IAddonCutSceneSelectStringHelper` — player choices
- `IAddonBubbleHelper` — NPC speech bubbles
- `IChatTalkHelper` — chat-based dialog
- `ISoundHelper` — hooks game sound to mute original VO. Fires `TalkVoiceLine`/`BattleBubbleVoiceLine`.

## Player-name placeholders (`-PlayerName-` / `-PlayerFirstName-` / `-PlayerLastName-`)
The harvest stores lines that address the player with these tokens
(`DialogHarvestService.ExtractTextWithPlayerName`); the live runtime gets the same line from the
game with the name already substituted. Four rules keep the two halves in sync — breaking any one
of them reproduces a bug that was live in 0.19.3.0:

- **`IGameObjectService.LocalPlayerName` must be ticked, not stumbled upon.** It (plus content id
  and gender) used to be filled ONLY as a side effect of the `LocalPlayer` getter, which nothing
  calls until the first dialog line is processed — so a freshly loaded plugin reported an empty
  player name while the user was logged in. Everything downstream degrades *silently* in that
  window: `TalkTextHelper.SubstitutePlaceholders` returns the text unchanged, `BackfillAudioFiles`
  loses its placeholder detection, and `GetEffectivePlayerContentId` answers 0.
  `RefreshLocalPlayer()` is now called from `Plugin.OnFrameworkUpdate` (every 60 s; every 2 s while
  the name is still empty) and once from `RunDataMigrationsIfLoggedIn`. It **clears** the cache
  when no player is present, because a stale name from the previous character is worse than none.
- **`voice_clips` only ever stores tokens.** `VoiceMessageProcessor.LogVoiceClip` runs the text
  through `Helper/Functional/PlayerNameTokenizer` before persisting (the spoken `message.Text` /
  `OriginalText` stay untouched). Without it the live row's `OriginalText` differed from the
  harvested row's, the `(CharacterId, NpcBaseId, OriginalText)` upsert missed, and the same dialog
  line ended up as two rows — both pointing at the same WAV (the filename hash collapses
  `-PlayerFirstName-` and `<PLAYERFIRSTNAME>` to the same token) but tracked under different
  `player_content_id` buckets via `HasPlayerPlaceholder`.
- **A placeholder must never reach the backend.** `VoiceClipManagerService.HasUsableText` refuses
  the generation when `ContainsPlayerPlaceholder` is still true after substitution. The failure is
  sticky otherwise: the WAV in which the TTS reads "-PlayerFirstName-" out loud lands on exactly
  the path `TryLoadCachedAudio`'s disk fallback adopts, so one bad generation is replayed forever.
- **Two placeholder vocabularies exist and are NOT interchangeable.** Text/DB side uses
  `-PlayerName-` (`TalkTextHelper.Placeholder*`); the file-name hash uses `<PLAYERNAME>`
  (`AudioFileService.RemovePlayerNameInText`). They deliberately normalise to the same on-disk
  name — that is what lets the harvested row, the live row and a restored backup share one file.

`IDatabaseService.RetokenizePlayerNames(playerName, dryRun, ct)` is the one-shot cleanup for
databases written before this (UI: Game Data Tools → "Player Name Cleanup", dry run + apply). It
rewrites literal-name rows back to tokens and, when that makes a row identical to its harvested
twin, moves the generation rows over and deletes the duplicate. Generation rows that would collide
on `(voice_clip_id, player_content_id, alias_gender)` are dropped — they described the same file.
Nothing on disk is touched. Covered by `RetokenizePlayerNamesTests` + `PlayerNameTokenizerTests`.

## Live-path orphan WAV adoption
- `VoiceMessageProcessor.TryLoadCachedAudio` (Step 8.5 in `ProcessSpeechAsync`) does a **two-stage cache lookup** before handing the message to the backend:
  1. DB `voice_clip_generations` row — fast path for clips this install previously generated/adopted.
  2. **Disk fallback** via `IAudioFileService.TryFindExistingLocalAudio` — probes the deterministic `{LocalSaveLocation}/{Speaker.Name}/{VoiceMessageToFileName(RemovePlayerNameInText(OriginalText))}.wav` path. A hit means the WAV exists on disk without a DB row (e.g. backup copied from a friend's install, manual restore). The file is adopted by writing a `voice_clip_generations` row with the speaker's current `voice` key and the effective player id, then loaded into the message stream the same way as a DB-cache hit.
- **Gating**: `Configuration.LoadFromLocalFirst` only. `SaveToLocal` is the write side of the cache and has no effect on playback or adoption. (Earlier code gated on either flag — tightened in feature/none-mode-ui-polish to reflect the user's intent: a user with `LoadFromLocalFirst=false` explicitly opted out of disk reuse.)
- **Manager (re)generation sends `no_cache` (2026-11-13).** The EchokrauTTS wrapper caches identical `/tts` requests for `response_cache_minutes` (default 5) and answers them with the SAME audio, also sharing a still-running generation. `GenerateForVoiceClip` and `GenerateAliasVariant` therefore set `VoiceMessage.NoCache = true` → `EchokrauTtsBackend.BuildTtsPayload(…, noCache)` adds `"no_cache": true` (field omitted otherwise; older wrappers ignore it). The live path keeps the cache: a replayed or doubled line is the case it exists for.
- **Manager paths are intentionally not adopting orphans.** `VoiceClipManagerService.GenerateForVoiceClip` and `GenerateAllUnsaved` go straight to the backend (or short-circuit on None-mode) — adoption happens lazily, dialog-by-dialog, the first time the line plays in-game. Rationale: no need to bulk-scan disk on demand when the natural live-path adoption covers everything as it gets used.
- Adoption uses `LogVoiceClipGeneration` (upsert-keyed on `(voice_clip_id, player_content_id, alias_gender)`), so re-firing the same dialog never duplicates rows even if the disk file existed before the DB row did.
- The bulk one-shot scan (`DatabaseService.BackfillAudioFiles`) still exists for the migration story, but is no longer the only way orphan files reach the DB.

## Live generation logging (`ILiveGenerationLogger`)
- Mirrors the bulk path's `_db.LogVoiceClipGeneration(...)` call so that voice clips produced through chat / addon talk / bubble events become visible in the Voice Clip Manager (status: generated, save path stored).
- Wiring: `VoiceMessageProcessor.LogVoiceClip` captures the `Id` returned by `IDatabaseService.LogOrUpdateVoiceClip` into `VoiceMessage.VoiceClipId`. After `AudioPlaybackService.OnSourceEnded` writes the audio file, a `ContinueWith` continuation calls `_generationLogger.LogIfApplicable(voiceClipId, hasPlayerPlaceholder, savePath, eventId)` on `TaskScheduler.Default`.
- `LogOrUpdateVoiceClip` returns the persisted `VoiceClipEntity` (with `Id` populated in non-batch mode) — needed because the live-path code needs to know which DB row to attribute the generation to. In batch mode (`SuppressEvents=true` during harvest), the returned `Id` may still be 0 and is intentionally ignored by the harvest caller.
- **`HasPlayerPlaceholder` must travel with the message.** `VoiceClipManagerService.GetEffectivePlayerId` keys generation rows on this flag (placeholder clips → local content id, non-placeholder → 0), so the live path must store under the same id. `VoiceMessageProcessor.LogVoiceClip` computes it via `TalkTextHelper.ContainsPlayerPlaceholder(originalText)` when persisting the clip, mirrors it onto `VoiceMessage.HasPlayerPlaceholder`, and the logger replicates the same effective-player-id rule. Storing under the wrong id silently breaks the manager's "is generated" lookup — symptom seen in practice: rows are written but the UI keeps showing clips as "not generated".
- Auto-alias generation stays bulk-only. Generating male+female alias variants would mean three backend calls per chat line — too expensive for the live path. Users who want alias variants opt in via the Voice Clip Manager UI.

## Mapped-NPC caches (`NpcDataService`) — copy-on-write
`_mappedNpcs` / `_mappedPlayers` are read from the UI (frame thread), the dialogue pipeline (thread pool) and the `VoiceClipLogged` handler. They used to be shared `List<NpcMapData>` handed straight out of `MappedNpcs`/`MappedPlayers`, which raced (`Collection was modified` while a reader iterated) and let callers mutate them from anywhere.

- **Readers** take the `volatile` field once and get a complete, never-mutated snapshot. Safe to iterate without copying.
- **Writers** build a new list under `_mapLock` and publish it by assignment (`AddToCache*` / `RemoveFromCache*` / `ClearMappedCaches`).
- **`_mapLock` must never wrap an `IDatabaseService` call.** That is the whole reason for copy-on-write instead of a plain lock: this service calls into the DB constantly, and the DB raises events that land back here (`LoadFromDatabase`). A lock spanning both directions is a lock-order cycle → game freeze. `GetAddCharacterMapData` therefore decides *inside* the lock and does its DB work *after* it.
- **`LoadFromDatabase` builds both lists fully, then publishes.** The old in-place `Clear()` + `Add()` loop left a window where a reader saw an empty cache.
- **The properties are `IReadOnlyList` on purpose.** Handing back a `List<>` would let a caller mutate the snapshot, which silently affects nothing — the type makes that a compile error instead. Eight call sites used to do `RemoveCharacter(x)` **and** `MappedXxx.Remove(x)`; `RemoveCharacter` now maintains the cache itself, and `ClearMappedCaches()` covers the wipe path.
- **Find-or-add in `GetAddCharacterMapData` is atomic.** Two dialogue lines for the same unseen NPC could otherwise both miss and both insert.
- What this does *not* guard: the `NpcMapData` objects inside the lists. Those stay shared mutable state by design (`SaveCharacter` updates the canonical entry in place).
- Covered by `NpcDataServiceCacheTests`, including a reader/writer concurrency test.

## Dialogue state (`IDialogStateService` / `DialogStateService`)
Cross-cutting dialogue state: `CurrentVoiceMessage`, `IsVoiced`, `IsInsideOwnedWindow`, plus speaker tracking (`MarkSpeakerResolved` / `WasSpeakerResolved` / `ClearResolvedSpeakers`).

- **Injected, not static.** This was a static `DialogState` class (SonarQube **S2696**); it is now a normal service consumed by `VoiceMessageProcessor`, all four addon helpers, `VoiceTestService`, `DialogTalkController` and `Plugin.WireEvents`.
- **It only works because container factories are lazy singletons.** `ServiceContainer.GetService` caches what a factory returns, so every consumer shares one instance. A per-resolve instance would split the state between the addon hooks, the pipeline and the toolbar without any visible error — that caching is pinned by `ServiceContainerTests.GetService_FactoryRegistration_RunsOnceAndCachesTheInstance`.
- **Speaker tracking is a `ConcurrentDictionary` behind methods.** It used to be a public `HashSet<int>`, written from `ProcessSpeechAsync` (thread pool) and cleared from the addon lifecycle (framework thread) — an overlap a plain `HashSet` does not survive. `CurrentVoiceMessage`/`IsVoiced` are `volatile` for the same reason.
- Covered by `DialogStateTests` (per-test instances now — no shared process state, unlike the static version).
- `IsInsideOwnedWindow` — shared hit-test function set by `DialogTalkController.SetWindowHitTest()`, consumed by `AddonTalkHelper.OnPreReceiveEvent()` to prevent speech cancellation on window clicks. Built by `NativeWindowManager` over **every** owned window; see `Windows/CLAUDE.md` → "Clicks in our own windows must not advance the dialogue" for the two ways it silently under-reports.
