# Open TODOs

High-level items not yet started, in addition to the in-flight tracks in `docs/continuation/`.

---

## 1. Backup / Restore solution

Full round-trip backup covering everything that represents user state.

**Scope:**
- `Configuration` (Dalamud JSON config in plugin config dir)
- SQLite database (`echokraut.db`) — characters, voices, voice clips, generations,
  phonetic corrections, lodestone cache, schema version
- *Optional*: generated audio files under `Configuration.LocalSaveLocation`
  (large; default off, opt-in toggle)

**Open design questions:**
- Format: zip vs. tarball; one file or three? Versioned manifest at minimum so a backup made
  on schema vN restores cleanly into vN+M (run forward migrations on the imported DB).
- Trigger: manual button only, or also a scheduled "weekly auto-backup to user-chosen dir"?
- Restore behaviour: full overwrite, or merge per-table? Merge is significantly more work
  (UPSERT logic, FK reconciliation) but safer for users.
- UI placement: Config → Settings → Save/Loading panel (right next to alias settings).

---

## 2. Functional test coverage with mocked Dalamud + game input

Today's tests cover the deterministic units (DB, helpers, parsers, RemoteUrlService). The
event-driven layer that does the actual work — chat handler, addon helpers, voice processor,
backend pipeline — has **no** test coverage because it touches Dalamud (`IClientState`,
`IObjectTable`, `IGameObject`, `IDataManager`, `IChatGui`, …) and FFXIVClientStructs unsafe
pointers.

**Goal:** mock the Dalamud surface so we can write end-to-end-ish tests for:
- "Chat message arrives → speaker found / not found → DB entry created or updated"
- "AddonTalk fires → voice generation request hits backend with the right parameters"
- "Bubble fires → muted NPC short-circuits before generation"
- "Player gender + race detection across all four client languages"
- "Lodestone fallback triggers exactly when expected (Race=Unknown OR Gender=None)"

**Approach:**
- Use NSubstitute or Moq to fake the Dalamud interfaces.
- Wrap `unsafe` Character/CharaStruct reads behind a thin testable seam (likely an interface
  layer over `CharacterDataService` so the unsafe casts don't need to be reachable in tests).
- Build a small `TestServiceContainer` that wires mocked Dalamud interfaces into the real
  services so we exercise actual production logic.

This is a multi-day chunk. Probably worth landing alongside #3 so the cleanup catches the
testability seams that turn out to be needed.

---

## 3. Full codebase code-quality pass

Run a code-quality / cleanup agent over the whole `Echokraut/` source tree to surface:
- Dead code (unused fields, methods, classes)
- Architectural drift (services bypassing the DI pattern, post-construction setters
  sneaking back in, places where events should be used instead of direct refs)
- Duplicate logic that should be extracted into a service or helper (per project rules)
- Missing localization (any hard-coded user-facing string)
- SonarQube issues that have piled up (S2696, S1104, S2486 from prior memory)
- Old TODOs / commented-out code blocks

Output: a single review report (likely under `docs/reviews/`), then prioritize the findings
into actionable follow-ups.

---

---

## See also
- `docs/continuation/shareable-alias-clips.md` — alias generation done; **Export/Import** is
  the only open item there.
- `plans/cutb-parser.md` — TODO. Cutscene NPC attribution via the per-cutscene `.cutb`
  Havok timeline file. Not started; closes the ~85% gap of unvoiced cutscene dialog the
  current harvest can't attribute.
