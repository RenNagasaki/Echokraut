# Windows

Native-only UI layer using KamiToolKit (FFXIV ATK nodes). ImGui was removed.

## KamiToolKit (migrations 2026-06-21 → 2026-07-04)
The plugin tracks the latest KamiToolKit. Full map in memory `project_kamitoolkit_migration`. Key facts when editing this layer:
- `NodeBase`/`NativeAddon` → `KamiToolKit.BaseTypes`; `Simple*Node` → `KamiToolKit.Nodes.Simplified` — both covered project-wide by `Echokraut/GlobalUsings.cs`.
- `ButtonIcon` → renamed `CircleButtonIcon` (`KamiToolKit.Enums`). `CircleButtonNode.Icon` is `CircleButtonIcon`.
- **`ScrollingListNode` was REMOVED from KamiToolKit** → re-created as a wrapper in `Echotools.UI.Nodes` with the identical old public surface. Consumers need `using Echotools.UI.Nodes;`. (`ScrollingListNode.ScrollPosition` is now `float`.)
- **`AddonController.Enable()` now asserts the main/framework thread**. Plugin construction runs OFF the framework thread, so calling `Enable()` in a ctor throws → `DialogTalkController` enables via `_framework.RunOnFrameworkThread(...)`. Any construction-time KTK op (Enable/Open/node mutation) must be bounced to the framework thread.

### 2026-07-04 KTK update (`ff7a689` → `376d8f9`, 45 commits) — dropdown/tree/slider reworks
Done to fix the dialog voice-switch crash. Full detail: `docs/plans/ktk-update-migration.md`.
- **Dropdown**: `TextDropDownNode`/`OptionListNode` REMOVED → use **`StringDropDownNode`** (`KamiToolKit.Nodes`) with direct `.Options` (List<string>) / `.SelectedOption` / `.OnOptionSelected`; `.LabelNode` is always non-null (drop old `LabelNode.Node != null` guards); `.MaxListOptions` defaults to 5 → set to 8 for long lists. The old UpdateLabel-crash workarounds are obsolete (upstream reworked it + added IsDisposed guards). Deferred `_pending…` → OnUpdate selection is still kept (good ATK practice, no longer a crash workaround).
- **Tree**: old `TreeListNode`/`TreeListCategoryNode`/`TreeListHeaderNode` + the Echotools `ScrollingTreeNode` wrapper REMOVED. New **`TreeListNode<T,TU> where TU:TreeListItemNode<T>`** is a virtualized, self-scrolling list: `Options = Dictionary<ReadOnlySeString,List<T>>` (header→rows), `OnItemSelected(T)`, `NoResultsString`. No dispose/rebuild cycle → the ATK dispose-during-draw crash class is gone for trees. Item view impls `SetNodeData(T)` + static `ItemHeight`. Category headers use the native journal-brown (`GetColor(1)`); recolor via reflection if you need `LabelColor` (see `NativeVoiceClipManagerWindow.RecolorTreeHeaders`).
- **Slider**: `SliderNode.DecimalPlaces` REMOVED (`Range`/`Value`/`OnValueChanged(int)` stay).
- No `KamiToolKit_Experimental` NoWarn needed anymore for the new TreeListNode.

## Window Manager
- `NativeWindowManager` — creates native ATK windows, manages `DialogTalkController`, implements `IWindowManager`.
- `IWindowManager` interface: `ToggleConfig()`, `ToggleFirstTime()`, `ToggleVoiceClipManager()`, `Draw()`, `Dispose()`.

## Native Windows (`Native/`)

| Window | File | Purpose |
|--------|------|---------|
| `NativeConfigWindow` | `NativeConfigWindow.cs` | Main settings (partial class, split by tab). |
| — Voices tab | `NativeConfigWindow.VoiceSelection.cs` | Voice assignment with PaginationBar. No sub-tabs. |
| — Phonetics tab | `NativeConfigWindow.Phonetics.cs` | Phonetic corrections with PaginationBar. |
| — Logs tab | `NativeConfigWindow.Logs.cs` | Log viewer with per-source tabs + PaginationBar. |
| `NativeVoiceClipManagerWindow` | `NativeVoiceClipManagerWindow.cs` | Virtualized `TreeListNode<VcRow,VoiceClipRowNode>`: headers = NPCs, rows = quest-type groups. Row click → detail window. |
| `VoiceClipRowNode` | `VoiceClipRowNode.cs` | `TreeListItemNode<VcRow>` item view (icon+title+subtitle) + the `VcRow` model for the VCM tree. |
| `NativeVoiceClipDetailWindow` | `NativeVoiceClipDetailWindow.cs` | Detail view for a quest-type group's clips. Hosts the per-NPC actions (Generate All / Delete All / Edit Character via `ShowVoiceClips`'s `onEditCharacter` callback). PaginationBar, word-wrapped text, play/stop buttons. |
| `NativeFirstTimeWindow` | `NativeFirstTimeWindow.cs` | 3-step first-time setup wizard (engine-aware — AllTalk/EchokrauTTS). |
| `NativeEchokrauTtsBuilder` | `NativeEchokrauTtsBuilder.cs` | EchokrauTTS settings section builder (Local + Remote), mirrors `NativeAlltalkBuilder`. Used by Backend tab + First-Time wizard. The install row also carries the **wrapper Update button + version label** (see below). |
| `NativeChangelogWindow` | `NativeChangelogWindow.cs` | One-shot popup after plugin update — concatenates every unseen changelog entry into a `ScrollingListNode` with a bottom "I've read it" button that calls `IChangelogService.MarkAllSeen()`. Driven by `Plugin.HandleStartup`/`OnLogin`. **Body must be split** at `===…===` divider lines AND single blank lines (see `SplitIntoSections`); a single giant MultiLine `TextNode` with `Size.Y > ~500px` does not render in ATK. **Always call `list.RecalculateLayout()` after AddNode** — without it the scrollbar doesn't know the cumulative content height and scrolling silently does nothing. |
| `NativeVoiceConfigWindow` | `NativeVoiceConfigWindow.cs` | Per-Voice config popup (Allowed races/genders). |
| `NativeNpcEditWindow` | `NativeNpcEditWindow.cs` | Per-NPC override popup (Race, Gender, Voice, Volume, Mute). Opened from VC Manager's `Edit Character` button. |
| `NativeAlltalkBuilder` | `NativeAlltalkBuilder.cs` | AllTalk settings section builder. |
| `DialogTalkController` | `DialogTalkController.cs` | Hooks AddonTalk lifecycle, manages click suppression. |

## ⚠ Greyed out = unusable — always go through `NodeState`
Alpha is cosmetic: ATK delivers hover, press animation and click to a node whatever its alpha is.
A control that only had its alpha lowered still fires its handler — found in practice on the wrapper
Update button, which kept reinstalling the release after the update had already run.

**`Echotools.UI.NodeState.SetEnabled(node, enabled)` is the single place that gets this right**, and
`NativeNodeFactory.Dim(node, enabled)` is just its local alias (kept because ~76 call sites use that
name). It lives in Echotools so every plugin inherits the rule. What it does:
- `INodeEnableState` implementers disable themselves (escape hatch, see below);
- otherwise any `ComponentNode` — buttons, checkboxes, sliders, text inputs, dropdowns — gets
  `AtkComponentBase.SetEnabledState`, the game's own disabled state;
- otherwise a plain node loses its mouse flags (`RespondToMouse | HasCollision | EmitsEvents`), and
  enabling restores **exactly** the ones that were taken, never a superset.

Consequences to keep in mind:
- **Writes happen only on a state change.** `NodeState` remembers the last applied state per node in a
  `ConditionalWeakTable`, deliberately *not* by reading it back off the node — the native disabled
  state may change the node's appearance, which would make an alpha-derived comparison flip every
  frame and turn this into the per-frame ATK write the conventions warn about. A node it has never
  touched counts as enabled/opaque, which is how KamiToolKit creates them.
- **`DynamicIconButtonNode` is the escape-hatch case.** It is a button component, but its click
  arrives as a raw `MouseClick` on its inner `ImageNode`, which `SetEnabledState` knows nothing about.
  It therefore implements `INodeEnableState.ApplyEnabled`, which flips both. **Never set
  `IsEnabled` on one of these directly** — that dims it and leaves the click alive. Call
  `ApplyEnabled` when you manage the dimming yourself (per-clip play/generate buttons, Game Data
  Tools button), or `NodeState.SetEnabled` when you want the standard dim too.
- **A handler that must not run in a state should still say so itself.** The wrapper update button
  routes both the `Dim` call and its handler's early-return through
  `WrapperUpdatePolicy.IsButtonActionable(state)`, so look and behaviour cannot drift apart.
- **⚠ Radio-style selectors dim the SELECTED option, not the unselected ones.** Backend tab's
  Local/Remote/None (`NativeConfigWindow`) and the wizard's AllTalk/EchokrauTTS
  (`NativeFirstTimeWindow`) mark the active choice by dimming it: it is the one with nothing left to
  do, while the alternatives stay bright and clickable. Highlighting the active one instead — which
  is what they did while dimming was cosmetic — now disables exactly the buttons the user switches
  with. Do not "fix" this back.

## EchokrauTTS wrapper Update button (`NativeEchokrauTtsBuilder`)
- `InstallRow` holds three nodes: Install/Reinstall, the **wrapper button**, and a version label.
  Both are visible whenever a local install exists (nothing to update without one).
- **One button, two jobs**, driven by `IEchokrauTtsInstanceService.UpdateState`: "Check for updates"
  → `CheckForWrapperUpdateAsync()` (a GitHub request, hence user-triggered only), then "Update" —
  clickable on a find, greyed out when already current. Sized at build time to its longest label so
  it doesn't jump between states. The click handler branches on the state, so it must read
  `UpdateState` at click time, not capture it — and it must reject the dead states itself
  (`IsButtonActionable`), because dimming does not block clicks (see above).
- **After a finished install/update the state goes back to `NotChecked`**, i.e. the button offers
  the check again (`WrapperUpdatePolicy.StateAfterInstall`, called from `MarkInstalled`/`MarkUpdated`).
  `UpToDate` would be more informative but is a dead state, and the just-installed tag is the newest
  thing we know anyway. The found release is *kept* so a later reinstall still uses its zip.
- The label mirrors the state: installed only (before a check — repeating the shipped baseline would
  not answer "is there something newer"), `(up to date)`, `(latest: X)`, or the failure reason.
- **Visibility flips must re-flow the row.** `HorizontalListNode.OnRecalculateLayout` skips invisible
  nodes, but only when it actually runs — so the builder tracks `_prevUpdateAvailable` and calls
  `InstallRow.RecalculateLayout()` on the flip. Doing it every frame would be a per-frame node
  mutation; not doing it leaves the label sitting where the hidden button used to be.
- The button reads the remote tag from `IEchokrauTtsInstanceService.LatestWrapperVersion`, so the
  builder signature (and both call sites) stay unchanged — no `IRemoteUrlService` in the UI layer.
- Click stops a running instance and calls `UpdateWrapper()` inside `Task.Run` (same off-UI-thread
  rule as Install/Stop — it does blocking download + process work).

## Native window teardown (KamiToolKit gotcha)
- `NativeAddon.Close()` is **async** — it queues an ATK detach on the framework thread and returns immediately. `NativeAddon.Dispose()` calls `Close()` internally but then runs managed cleanup right away, so the ATK addon may still be attached when our managed handles get freed → dangling pointer → game crashes later in `AtkUldManager.Finalizer`.
- **Mitigation:** in any teardown path that owns NativeAddons, explicitly call `Close()` BEFORE `Dispose()` so the framework thread starts the ATK detach as early as possible. See `NativeWindowManager.Dispose()` (`SafeClose` + `SafeDispose`) and `NativeVoiceClipManagerWindow.Dispose()` for the pattern.
- **Don't `Thread.Sleep` in `Plugin.Dispose`** to wait for closes — `Plugin.Dispose` typically runs on the framework thread, so sleeping it deadlocks the very tasks we're waiting for.

## Backend calls must not run on the frame thread
`IBackendService.RefreshBackend` / `SetBackendType` → `MapVoices` → `ITTSBackend.GetAvailableVoices`
is a **blocking** HTTP call (`.GetAwaiter().GetResult()`, 5s timeout). Every UI path that triggers
it therefore wraps it in `Task.Run`: the deferred remap after a connection test
(`_pendingRemapAfterConnect` in `OnUpdate`), the "Reload voices" button, the engine-switch dropdown
(`ProcessBackendSelection` → `ITtsVoiceSyncService.SwitchEngine`), and `Plugin.StartActiveEngineInstance`
(reached from `HandleStartup`, which is bounced onto the framework thread). Without that, an
unreachable backend freezes the game for the full timeout.

This is safe for the UI because `VoicesMapped`/`CharacterMapped` handlers only set `_vsXxxNeedRebuild`
flags — no ATK node is touched off-thread; the node work happens in `OnUpdate`. Anything in those
handlers that *does* touch nodes must go through `IFramework.RunOnFrameworkThread` (see below).
⚠ Note this does **not** make `NpcDataService`'s mapped-NPC lists thread-safe — they are plain
`List<>` and already raced before this (see the audit doc's B5).

## Async callbacks touching ATK nodes
- Any `Task.ContinueWith` / `await` continuation that mutates KamiToolKit nodes (string properties, visibility, position, …) must be bounced back to the framework thread via `IFramework.RunOnFrameworkThread(...)` before touching the node. ATK is not thread-safe; mutating from a thread-pool thread can corrupt state or crash later.
- Pattern: `task.ContinueWith(t => _framework.RunOnFrameworkThread(() => { /* ATK mutations */ }))`. See `NativeFirstTimeWindow.TestConnection()` for the canonical example.

## First-Time Setup wizard (`NativeFirstTimeWindow`)
- 3-step wizard: 0 = engine + mode choice, 1 = configure (per-engine/per-mode controls), 2 = finish/summary.
- **Engine-aware (P5c / GAP-3):** Step 0 has an engine selector (AllTalk / EchokrauTTS buttons → `Configuration.BackendSelection`, Alpha-highlighted) above the Local/Remote/None mode buttons. Mode buttons write the **active engine's** `InstanceType` via `SetActiveInstanceType` (mirrors `NativeConfigWindow`). Step 1 shows the AllTalk sections when `!isEk`, the EchokrauTTS sections (`NativeEchokrauTtsBuilder`) when `isEk`; None is engine-independent. All gates read `Configuration.ActiveInstanceType` / the active engine's `LocalInstall`/`BaseUrl`, never `Alltalk.*`.
- **Next button is mode-gated:** Local requires the active engine's `LocalInstall == true`, Remote requires a successful `IBackendService.CheckReady` against the *current* active `BaseUrl` (snapshot URL stored at test start; URL change invalidates), None always allowed.
- "Ready" is the only success token from `CheckReady` (both engines) — anything else counts as failure. `TestConnection` routes to the active engine's remote result label.
- The visibility signature includes `isEk` so switching engine re-flows the list. The install progress bar reads the active engine's install service.
- Step 2 summary (`BuildFinishSummary(instanceType, isEk)`) is rebuilt every frame from live config / install / test state; shows the chosen engine for Local/Remote.

## Config Window Tabs
1. **General** — backend settings, volume, "Voice Clip Manager" button
2. **Voices** — flat voice assignment list
3. **Phonetics** — phonetic correction rules
4. **Logs** — per-source log viewer

### None-mode tab gating (`HasLiveGeneration == false`)
Three TabBarNodes follow the same gate — anything that depends on a live backend (no audio-file fallback) is hidden when `Configuration.Alltalk.InstanceType == None`. Each tab bar uses a `bool? _xxxLiveGenSnapshot` field and only rebuilds when the snapshot flips, so radios aren't disposed/recreated every frame:

- **Top-level tabs** (`BuildTopTabs`): `Voices` + `Phonetics` hidden; `Settings` + `Logs` always present.
- **Settings sub-tabs** (`BuildSettingsTabs`): `Chat` hidden; `General`/`Dialogue`/`Storage`/`Backend` always present (Backend stays so the user can switch out of None mode).
- **Logs sub-tabs** (`BuildLogsTabs` in `NativeConfigWindow.Logs.cs`): `LogTabs` array carries a `LiveOnly` flag — `Chat`/`Cutscene`/`Choice`/`Backend` are LiveOnly and hidden in None mode. Panel + pagination arrays keep their full size so `OnLogUpdated`'s source→index lookups and the per-source accessor switches stay valid; only the visible tab buttons shrink. Background log writes continue, so history is preserved across mode switches.

Active-tab restoration after a rebuild: if the previously-active tab is still visible, re-select it; otherwise snap to index 0 (General/Settings). `TabBarNode.SelectTab` only updates the radio visual — panel visibility must still be applied via the corresponding `Show*Panel(index)` call.

## Voice Clip Manager
- Standalone window (not a config tab), accessible from General settings or `/ekhistory` command.
- **Virtualized tree (post-2026-07-04 KTK update):** `TreeListNode<VcRow,VoiceClipRowNode>`. Headers = NPCs (`"Name | Gender | Race"`), rows = quest-type groups. Row click fires `OnItemSelected` → detail window with that group's clips. NO custom node dispose/rebuild — the tree pools row views internally (this replaced the old ScrollingTreeNode/TreeListCategoryNode build-and-destroy model that caused the ATK crash class).
- **Options built progressively** into a `Dictionary<ReadOnlySeString,List<VcRow>>` (batch of 5 NPCs/frame in `ContinueProgressive`) and re-assigned to `tree.Options` per batch — the setter preserves collapse + scroll state (it does not touch `CollapsedEntries`/scrollPosition), so re-assign is also how count-refresh + page-change repaint without losing state.
- **Per-NPC actions moved to the detail window** (P5c-era redesign): the homogeneous tree rows can't host buttons, so Generate All Unsaved / Delete All Saved / Edit Character live in `NativeVoiceClipDetailWindow` (Edit Character via the `onEditCharacter` callback passed to `ShowVoiceClips`).
- **Header color:** the internal KTK header defaults to journal-brown (`GetColor(1)`); `RecolorTreeHeaders` recolors to `LabelColor` via reflection on the private `TreeListNode.HeaderNodes`, after each Options set. Fail-soft. Caveat: a bare window resize (no rebuild) leaves headers brown until the next filter/page change.
- Uses `IVoiceClipManagerService` for business logic. **Quest-type dropdown** at top is a tree pre-filter only. Language tracks `IClientState.ClientLanguage`.
- Harvest is **not** triggered here — run it from `NativeGameDataToolsWindow`. VCM is a viewer/editor over persisted data; GDT is the data-pipeline window.
- Detail window uses `ContentPadding = new Vector2(8.0f, -20.0f)` to remove empty tab bar space; reloads on `VoiceClipUpdated` (not just `VoiceClipLogged`); auto-refreshes on `VoiceClipLogged` for the open group.

## Pagination
- All paginated lists use `PaginationBar` from `Echotools.UI.Nodes`.
- `PaginationBar.SetTotalItems()` only resets page when total changes.
- Deferred page changes: button clicks set a flag, processed in next `OnUpdate`.

## In-dialog toolbar voice dropdown (`DialogTalkController`)
- Options are built via `VoiceOptionList.BuildLabels` (`Helper/Functional/`), **not** `voices.ConvertAll(v => v.VoiceName)`. `EchokrautVoice.VoiceName` is a display name derived from the sample filename, so two samples can produce the same label (`Male_All_NPC001.wav` / `Male_All-Elder_NPC001.wav` → both "NPC001") and a filename made only of grammar tokens produces an empty one. Duplicates get the sample filename appended; empty ones fall back to it.
- The offered voices are cached in `_dropDownVoices` and the picked label is resolved with `VoiceOptionList.Resolve` against **that** snapshot. The old name lookup in a freshly fetched list returned the first same-named entry, so picking the second voice silently applied the first — the selection looked ignored.
- The "did it actually change?" test compares `BackendVoice` against `Speaker.voice` (the persisted key), not object references: `NpcMapData.Voice` resolves through the per-instance `Voices` list and yields null when that list was never populated.
- The dropdown needs `DialogState.CurrentVoiceMessage` to have a `Speaker` — see the dialog-lifecycle section in `Services/CLAUDE.md` for why that is published before the mute/no-voice/volume skip checks.

## Click Suppression
- `DialogTalkController.SetWindowHitTest()` registers a hit-test function.
- `NativeWindowManager` tests all windows: config, first-time, voice clip manager, voice clip detail.
- Prevents dialog advance and speech cancellation when clicking inside plugin windows.
