# Windows — KamiToolKit migrations, version pin, workarounds

Moved verbatim from `Echokraut/Windows/CLAUDE.md` on 2026-11-20 (file was over the 15k budget). Does not load by itself — read it before working in this area.

## KamiToolKit (migrations 2026-06-21 → 2026-07-04)
The plugin tracks the latest KamiToolKit. Full map in memory `project_kamitoolkit_migration`. Key facts when editing this layer:
- `NodeBase`/`NativeAddon` → `KamiToolKit.BaseTypes`; `Simple*Node` → `KamiToolKit.Nodes.Simplified` — both covered project-wide by `Echokraut/GlobalUsings.cs`.
- `ButtonIcon` → renamed `CircleButtonIcon` (`KamiToolKit.Enums`). `CircleButtonNode.Icon` is `CircleButtonIcon`.
- **`ScrollingListNode` was REMOVED from KamiToolKit** → re-created as a wrapper in `Echotools.UI.Nodes` with the identical old public surface. Consumers need `using Echotools.UI.Nodes;`. (`ScrollingListNode.ScrollPosition` is now `float`.)
- **`AddonController.Enable()` now asserts the main/framework thread**. Plugin construction runs OFF the framework thread, so calling `Enable()` in a ctor throws → `DialogTalkController` enables via `_framework.RunOnFrameworkThread(...)`. Any construction-time KTK op (Enable/Open/node mutation) must be bounced to the framework thread.

### 2026-07-04 KTK update (`ff7a689` → `376d8f9`, 45 commits) — dropdown/tree/slider reworks
Done to fix the dialog voice-switch crash. Full detail: `docs/plans/ktk-update-migration.md`.
- **Dropdown**: `TextDropDownNode`/`OptionListNode` REMOVED → use **`StringDropDownNode`** (`KamiToolKit.Nodes`) with direct `.Options` (List<string>) / `.SelectedOption` / `.OnOptionSelected`; `.LabelNode` is always non-null (drop old `LabelNode.Node != null` guards); `.MaxListOptions` defaults to 5 → set to 8 for long lists. The old UpdateLabel-crash workarounds are obsolete (upstream reworked it + added IsDisposed guards). Deferred `_pending…` → OnUpdate selection is still kept (good ATK practice, no longer a crash workaround).
- **Tree**: old `TreeListNode`/`TreeListCategoryNode`/`TreeListHeaderNode` + the Echotools `ScrollingTreeNode` wrapper REMOVED. New **`TreeListNode<T,TU> where TU:TreeListItemNode<T>`** is a virtualized, self-scrolling list: `Options = Dictionary<ReadOnlySeString,List<T>>` (header→rows), `OnItemSelected(T)` (renamed `OnOptionSelected` 2026-11-23), `NoResultsString`. No dispose/rebuild cycle → the ATK dispose-during-draw crash class is gone for trees. Item view impls `SetNodeData(T)` + static `ItemHeight`. Category headers use the native journal-brown (`GetColor(1)`); recolor via reflection if you need `LabelColor` (see `NativeVoiceClipManagerWindow.RecolorTreeHeaders`).
- **Slider**: `SliderNode.DecimalPlaces` REMOVED (`Range`/`Value`/`OnValueChanged(int)` stay; `OnValueChanged` renamed `OnValueUpdate` 2026-11-23).
- No `KamiToolKit_Experimental` NoWarn needed anymore for the new TreeListNode.

## KamiToolKit version + which shared workarounds still apply (checked 2026-11-23)

Pinned at `62349f6` ("Remove redundant interface", 2026-11-22), updated from `6b7b191` (71 commits).
Echotools' own `KamiToolKit` submodule (standalone build only) is on the same commit. The shared KTK
workarounds for all plugins were written against older pins, so this is what re-checking them against
`62349f6` found. **Re-check this list on the next KTK bump** — a workaround kept past its cause is
worse than none, because it hides the real API.

- **`TextDropDownNode` crash workaround — DEAD.** The type no longer exists in KTK; the plugin has
  been on `StringDropDownNode` for a while. Nothing to work around.
- **`TabBarNode.SelectTab` only highlights — STILL TRUE.** It routes through `ClickHandler`
  (`Nodes/TabBarNode.cs:43-48`), which only flips `IsChecked`/`IsSelected` on the radio buttons
  (`:163-171`). No per-tab callback runs, so still call the show method yourself.
- **`DropDownNode` always opens scrolled to the top — STILL TRUE, and NOT fixable from a plugin.**
  `Uncollapse()` sets `scrollPosition = 0` and `PopupScrollbarNode.ScrollPosition = 0`
  (`Nodes/ComponentNode/DropDownNode.cs:206-208`) regardless of `SelectedOption`. Both the field
  (`:530`) and `PopupScrollbarNode` (`:527`) are `private`, as is `PopulatePopupList` (`:446`) — no
  public seam to scroll to the selection afterwards ("voice list always starts from the top").
  A plugin-side mitigation is to order the current selection first; the real fix belongs upstream.
- **`TreeListCategoryNode` collision inside `ScrollingListNode` — DEAD.** The type no longer exists
  in KTK (trees are the virtualized `TreeListNode<T,TU>`, see above).
- **Echotools `ReservedScrollingNode` (16 px scrollbar reserve) — REMOVED.** `ScrollingNode<T>` now
  narrows `ContentNode` itself while its scrollbar is visible (`Width - ScrollBarNode.Width - 2`);
  keeping the reserve would subtract twice. `ScrollingListNode` wraps a plain `ScrollingNode<T>` again.
- **`KamiToolKit_Experimental` `NoWarn` — REMOVED** from `Echokraut.csproj` and Echotools `UI.csproj`:
  KTK no longer uses that diagnostic id (only `KTKExperimental_TextureImageNode`, unused here).

### 2026-11-23 KTK update (`6b7b191` → `62349f6`) — layout engine + interfaces
Upstream reworked the layout engine (`LayoutNodeBase`/`LayoutListNode`), added ~100 interfaces
(`Interfaces/Nodes/I*Node`) and relicensed MIT → AGPL-3.0. Dalamud.NET.Sdk stays 15. Renames hit here:
- `KamiToolKit.BaseTypes.ComponentNode` is a **type** now, not a namespace (`ComponentNode` lives in
  `KamiToolKit.BaseTypes`).
- Layout lists: `RecalculateLayout()` → `UpdateLayout()`, `ScrollingNode.RecalculateSizes()` →
  `UpdateLayout()`, `FitWidth` → `StretchWidth`, `ClipListContents` → `ClipContents`. Layout nodes also
  re-run their layout on every real size change. The Echotools `ScrollingListNode` keeps its old
  public names (`RecalculateLayout`, `FitWidth`, `ClipListContents`) and maps them internally.
- `CheckboxNode.OnClick` → `OnChecked`; `SliderNode.OnValueChanged` → `OnValueUpdate`. Setting
  `IsChecked`/`Value` still fires them; `SetValueSilent(...)` sets without the callback.
- Tree: `ITreeListItemNode` → `IListItemNode` (now `: INode`); `TreeListNode.OnItemSelected` →
  `OnOptionSelected`.
- ⚠ **`LayoutListNode.RemoveNode(IEnumerable<NodeBase>)` no longer disposes** the removed nodes (the
  single-node `RemoveNode(node)` and `Clear()` still do). Removed-but-undisposed nodes end up in KTK's
  "not disposed safely" unload warning — remove one by one, or dispose them yourself.
- `NativeAddon.Title`/`Subtitle` setters now update an open window directly.

### Init / dispose contract (changed in this bump)
`KamiToolKitLibrary.Initialize`/`Cleanup` are gone: `InitializeAsync` and `Dispose`/`DisposeAsync`.
See `Plugin.cs` for why init is awaited synchronously (everything built afterwards assumes KTK is
ready, and the ctor is off the framework thread) and why dispose uses the **sync** overload (it
requires the framework thread and refuses otherwise, which is exactly where Dalamud disposes plugins).
