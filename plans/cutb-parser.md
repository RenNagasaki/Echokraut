# Plan: `.cutb` Parser for Cutscene NPC Attribution

## Status

**TODO — not started.** Diagnostic confirms the gap; implementation deferred until the
existing harvest output is exhausted.

## Context

`HarvestCutsceneDialogs` (in `DialogHarvestService.cs`) currently captures **only the subset
of unvoiced cutscene lines whose TEXT key includes a speaker shortname**:

```
TEXT_VOICEMAN_06006_000010_YSHTOLA       ← speaker "YSHTOLA" → resolved via name index
TEXT_VOICEMAN_05400_Q1_000_001_NONE_VOICE ← speaker UNKNOWN, lives in .cutb timeline
```

The `_NONE_VOICE` family is FFXIV's explicit marker for "this line is unvoiced; the speaker
is determined by the cutscene's Havok timeline". A real-world harvest run reported:

| Category | Count |
|---|---|
| Voiced (skipped — FFXIV plays its own audio) | 25,404 |
| Unvoiced + speaker shortname → in DB | 132 |
| Speaker shortname unmatched (alias-fixable) | 25 |
| **`_NONE_VOICE` marker, no speaker in key** | **1,223** |
| Other unparseable keys | 0 |

→ ~85% of unvoiced cutscene dialog is currently dropped because we have no speaker
attribution. Closing the gap requires parsing the per-cutscene `.cutb` timeline file to
extract the line-number → ACTOR mapping.

Example file: `cut/ffxiv/manwil/manwil20030/manwil20030.cutb`

## Technical reference

`.cutb` is a **Havok packfile** — the same serialization Square Enix uses across animation
files. Layout in broad strokes:

- File header + section table
- Type registry (Havok class definitions)
- Object instances (cutscene-related Havok types)
- Pointer fixups across sections

Key Havok types we care about (names from FFXIV-specific extension classes, may differ):

- `hkaCutscene` (or SE-specific `LayerCutscene`) — the timeline root
- `hkaActor` / scene-actor entries — list of speakers in the scene, tied to ENpcBase IDs
- `hkaVoiceTrigger` (or similar) — references TEXT key + ACTOR index at a given timeline
  position; this is the missing link

## Reverse-engineering resources

Open-source parsers we can lean on (none directly portable to C#, all useful as reference):

- **Sapphire-server** (https://github.com/SapphireServer/Sapphire) — server emulator,
  parses `.cutb` for cutscene replication; C++; Havok class layout reference
- **xivapi** (https://github.com/xivapi/xivapi) — has tooling that touches cutscenes
  indirectly via SaintCoinach
- **SaintCoinach** — primarily Excel-focused but documents some cutscene-related sheets
- **Lumina** (`FFXIVClientStructs.FFXIV.Client.System.Cut`) — has minimal `.cutb` types
  exposed in client structs; worth checking what's already available without us writing
  a parser from scratch

## Approach (when we tackle this)

### Phase A — Format reconnaissance
1. Dump `manwil20030.cutb` raw bytes; identify the section table boundaries
2. Walk Havok type registry; list which classes appear in this file
3. Cross-reference with Sapphire's Havok header definitions to map types → names
4. Find where `TEXT_VOICEMAN_*_NONE_VOICE` keys are stored (string table) and where
   they're referenced from (instance fields)
5. Find the actor list (likely `LayerCutsceneActor`-style records with ENpcBase IDs)

### Phase B — Minimal parser
1. Implement just enough Havok packfile reading to:
   - Extract all string-table entries
   - Walk the actor list with their ENpcBase IDs
   - Walk voice-trigger entries with `(TEXT_key, actor_index, timeline_position)`
2. Export as `Dictionary<string textKey, uint enpcBaseId>` per cutscene file

### Phase C — Harvest integration
1. New service `ICutsceneTimelineService` exposing
   `Dictionary<string, uint> ResolveSpeakers(string cutbPath)` (cached per file)
2. In `HarvestCutsceneDialogs`, when a `_NONE_VOICE` key arrives:
   - Compute the corresponding `.cutb` path from the sheet name
     (e.g. sheet `cut_scene/05/manwil_20030` → `cut/ffxiv/manwil/manwil20030/manwil20030.cutb`)
   - Look up the ENpcBase from the timeline mapping
   - Fall through to the existing race/gender/text persist path
3. Cache resolved timelines per process — each `.cutb` is read once

### Phase D — Robustness
1. Schema-version detection — Havok bumped versions over the years
2. Graceful skip when a timeline can't be parsed; log + count
3. Tests with checked-in fixture `.cutb` files (small ones from base game)

## Effort estimate

- **Phase A**: 1 day (mostly reading bytes + cross-referencing Sapphire C++)
- **Phase B**: 1–2 days (Havok packfile reader is the bulk)
- **Phase C**: 0.5 day (integration into existing harvest)
- **Phase D**: 0.5 day (hardening + tests)

Total: **~3–4 days** for first working version, plus expect 1–2 days/year ongoing if SE
bumps the format.

## Definition of done

- `Cutscene harvest: ... NONE_VOICE marker (no speaker in key — needs .cutb)` count drops
  from 1,223 to ~0 on a fresh harvest
- ENpcBase attribution verified against in-game playback for 5–10 sample lines
- Parser failure for any single `.cutb` does NOT crash the harvest — line gets logged and
  skipped, run continues
- Unit tests cover at least one base-game cutscene file fixture

## Open questions

- Do we ship `.cutb` fixtures in the repo (test reproducibility) or fetch them at test
  time from Lumina via a mock IDataManager?
- Is there a way to detect when SE changes the Havok version without us testing? (Probably:
  fail-closed → log + skip + warn user)
- Could `FFXIVClientStructs` already give us enough that we skip Phase A/B entirely? Check
  before starting.

## Pointers in the current codebase

- `Echokraut/Services/DialogHarvestService.cs` — `HarvestCutsceneDialogs` (the hook site)
- `Echokraut/Helper/Functional/VoiceExtractKey.cs` — TEXT-key parsing (extend if cutb keys
  follow a different pattern)
- `Echokraut/Helper/Functional/VoiceScdPaths.cs` — example of cut/* path layout helper
