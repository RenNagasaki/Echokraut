# Windows — DialogTalkController: toolbar dropdown, click suppression

Moved verbatim from `Echokraut/Windows/CLAUDE.md` on 2026-11-20 (file was over the 15k budget). Does not load by itself — read it before working in this area.

## In-dialog toolbar voice dropdown (`DialogTalkController`)
- Options are built via `VoiceOptionList.BuildLabels` (`Helper/Functional/`), **not** `voices.ConvertAll(v => v.VoiceName)`. `EchokrautVoice.VoiceName` is a display name derived from the sample filename, so two samples can produce the same label (`Male_All_NPC001.wav` / `Male_All-Elder_NPC001.wav` → both "NPC001") and a filename made only of grammar tokens produces an empty one. Duplicates get the sample filename appended; empty ones fall back to it.
- The offered voices are cached in `_dropDownVoices` and the picked label is resolved with `VoiceOptionList.Resolve` against **that** snapshot. The old name lookup in a freshly fetched list returned the first same-named entry, so picking the second voice silently applied the first — the selection looked ignored.
- The "did it actually change?" test compares `BackendVoice` against `Speaker.voice` (the persisted key), not object references: `NpcMapData.Voice` resolves through the per-instance `Voices` list and yields null when that list was never populated.
- The dropdown needs `DialogState.CurrentVoiceMessage` to have a `Speaker` — see the dialog-lifecycle section in `docs/claude/services/dialog-pipeline.md` for why that is published before the mute/no-voice/volume skip checks.
- ⚠ **The rebuild cache key is three values, not just the speaker** (fixed 2026-09-26). `OnUpdateInner`
  runs every frame, so the option list is only rebuilt on a change — but keying that on
  `speaker.ToString()` alone missed both ways the content can change under a *constant* speaker:
  - the voice **list** changes (voices reloaded / added / removed). Tracked by
    `INpcDataService.VoicesRevision` → `IDatabaseService.VoicesRevision`, a counter bumped on every
    replacement of `DatabaseService._cachedVoices`. Needed because `GetVoices()` hands out a cache:
    a caller cannot otherwise tell a reloaded list from the one it already has.
  - the speaker's **assigned** voice changes elsewhere — the Voice Clip Manager's Edit Character
    popup mutates the very same `NpcMapData` instance the toolbar reads, and `ToString()` covers
    gender/race/name only.
  So the key is `speakerKey + VoicesRevision + speaker.voice`.
- **Preselect with `VoiceOptionList.LabelForKey(voices, speaker.voice)`, never `LabelFor(voices, speaker.Voice)`.**
  The `NpcMapData.Voice` getter resolves the key through that character's own `Voices` snapshot and
  answers null for a voice that only arrived with the latest reload — the dropdown then shows no
  selection although the voice is right there in the list.
- **The assigned voice is force-added to the options** when the race/gender filter would reject it
  (a voice picked before a gender correction, a narrator). Otherwise the selection reads as empty and
  picking something else is the only way out of it. Same rule as `NativeNpcEditWindow`.

## Clicks in our own windows must not advance the dialogue
FFXIV's Talk addon advances on a click, and a click in one of our native windows reaches it unless we
suppress it. `DialogTalkController.SetWindowHitTest()` takes the hit-test; `OnPreReceiveEvent` calls
`eventArgs.PreventOriginal()` on a hit (setting `AtkEventType = 0` is **not** enough — Dalamud
forwards the modified event to the original handler). `IDialogStateService.IsInsideOwnedWindow`
carries the same function to `AddonTalkHelper`, which uses it to not cancel speech.

The test lives in `NativeWindowManager.IsInsideWindow` + the pure `Helper/Functional/WindowHitTest`.
Both ways it silently under-reported were fixed 2026-09-26 — and a miss does not look like a UI bug,
it **skips dialogue lines**:
- ⚠ **The game draws an addon at `Size * Scale`, not at `Size`.** Comparing the click against the
  configured `NativeAddon.Size` made the rectangle too small at any HUD scale above 100%, and clicks
  in the missing right/bottom strip advanced the dialogue. Use the addon's own
  `GetScaledWidth(true)`/`GetScaledHeight(true)`, with `Size * addon->Scale` as the fallback for the
  frames before it has measured itself (it reports 0 between open and first layout).
- ⚠ **Every owned window has to be in the list, popups included.** `NativeWindowManager.OwnedWindows()`
  is the single source of truth. `NativeVoiceConfigWindow` and `NativeNpcEditWindow` are created on
  demand by their parent window, so the manager cannot hold a reference of its own — they are reached
  through `NativeConfigWindow.VoiceConfigWindow` / `NativeVoiceClipManagerWindow.NpcEditWindow`. A new
  `NativeAddon` that is not yielded there is a dialogue-skipping bug waiting to be reported.
