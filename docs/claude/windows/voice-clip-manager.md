# Windows — Voice Clip Manager

Moved verbatim from `Echokraut/Windows/CLAUDE.md` on 2026-11-20 (file was over the 15k budget). Does not load by itself — read it before working in this area.

## Voice Clip Manager
- Standalone window (not a config tab), accessible from General settings or `/ekhistory` command.
- **Virtualized tree (post-2026-07-04 KTK update):** `TreeListNode<VcRow,VoiceClipRowNode>`. Headers = NPCs (`"Name | Gender | Race"`), rows = quest-type groups. Row click fires `OnOptionSelected` (was `OnItemSelected` before KTK 62349f6) → detail window with that group's clips. NO custom node dispose/rebuild — the tree pools row views internally (this replaced the old ScrollingTreeNode/TreeListCategoryNode build-and-destroy model that caused the ATK crash class).
- **Options built progressively** into a `Dictionary<ReadOnlySeString,List<VcRow>>` (batch of 5 NPCs/frame in `ContinueProgressive`) and re-assigned to `tree.Options` per batch — the setter preserves collapse + scroll state (it does not touch `CollapsedEntries`/scrollPosition), so re-assign is also how count-refresh + page-change repaint without losing state.
- **Per-NPC actions moved to the detail window** (P5c-era redesign): the homogeneous tree rows can't host buttons, so Generate All Unsaved / Delete All Saved / Edit Character live in `NativeVoiceClipDetailWindow` (Edit Character via the `onEditCharacter` callback passed to `ShowVoiceClips`).
- **Header color:** the internal KTK header defaults to journal-brown (`GetColor(1)`); `RecolorTreeHeaders` recolors to `LabelColor` via reflection on the private `TreeListNode.HeaderNodes`, after each Options set. Fail-soft. Caveat: a bare window resize (no rebuild) leaves headers brown until the next filter/page change.
- Uses `IVoiceClipManagerService` for business logic. **Quest-type dropdown** at top is a tree pre-filter only. Language tracks `IClientState.ClientLanguage`.
- Harvest is **not** triggered here — run it from `NativeGameDataToolsWindow`. VCM is a viewer/editor over persisted data; GDT is the data-pipeline window.
- **The detail window shows the STORED text, placeholders included** — it must not substitute the
  local player's name into `-PlayerFirstName-` & co. The row is player-independent, and the
  substitution silently no-ops while the player-name cache is cold, so the same row used to read
  differently before and after the session's first dialog (which looked like the DB row had been
  overwritten). See `docs/claude/services/dialog-pipeline.md` → "Player-name placeholders".
- Detail window uses `ContentPadding = new Vector2(8.0f, -20.0f)` to remove empty tab bar space; reloads on `VoiceClipUpdated` (not just `VoiceClipLogged`); auto-refreshes on `VoiceClipLogged` for the open group.
