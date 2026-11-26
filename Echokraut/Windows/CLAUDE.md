# Windows

Native-only UI layer using KamiToolKit (FFXIV ATK nodes). ImGui was removed.

## Further documentation — read the file before working in that area
Split by area on 2026-11-20 (verbatim move). The files under `docs/claude/windows/` NEVER load by themselves.

| Area | File |
|---|---|
| KamiToolKit migrations (2026-06/07), current pin + which shared workarounds still apply, init/dispose contract | `docs/claude/windows/kamitoolkit.md` |
| `NativeEchokrauTtsBuilder`: wrapper Update button, engine options per selected engine | `docs/claude/windows/echokrautts-builder.md` |
| First-Time Setup wizard, config window tabs, None-mode tab gating | `docs/claude/windows/config-window-and-wizard.md` |
| Voice Clip Manager (virtualized tree, detail window, header color) | `docs/claude/windows/voice-clip-manager.md` |
| `DialogTalkController`: in-dialog voice dropdown, clicks in own windows must not advance the dialogue (`OwnedWindows()`, scaled hit-test) | `docs/claude/windows/dialog-talk-controller.md` |

Short rules from the moved files that apply everywhere in this layer:
- Any construction-time KTK op (Enable/Open/node mutation) must run on the framework thread (`kamitoolkit.md`).
- Every new `NativeAddon` must be yielded by `NativeWindowManager.OwnedWindows()`, popups included, or clicks in it skip dialogue (`dialog-talk-controller.md`).
- **E2E stage `ui` (B5)**: a window with tabs, dropdowns, pages or harmless buttons implements `E2eUi.IE2eSweepable`
  (`Windows/E2eUi/E2eSweepSteps.cs`) and lists them as named steps — **only steps without a side effect** (tab, dropdown
  open/close but never a pick, next/prev page, a button that only shows or tests). A tab step does what the click does:
  `TabBarNode.SelectTab(label)` (radio visual only) + the tab's own `Show*Panel`. New tabs/dropdowns/lists belong there too.
  `NativeWindowManager.OnProbe` answers `IOwnedWindowsService.Probe` (snapshot with `AtkTextCollector`'s visible texts,
  open/close/move, steps); window ids are `OwnedWindowIds` (= `InternalName`). ⚠ KamiToolKit links a component's children
  as **siblings of the component's root node**, not below it — an ATK walk must follow that sibling chain (it missed every
  `ScrollingListNode` item before). Texts only count when visible: Backend-tab sections are collapsible (EK local closed by default).

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
| `NativeFirstTimeWindow` | `NativeFirstTimeWindow.cs` | 3-step first-time setup wizard (engine-aware — AllTalk/EchokrauTTS). **Flow lives in `IFirstTimeWizardController`** (Services/): buttons call its actions, `OnUpdate` reads `Step`/`CanAdvance`; the install-path input and Install button come from the builders with the controller's `setInstallRoot`/`startInstall` (Backend tab passes none and keeps its direct calls). No logic in lambdas here — the E2E run drives the same controller. |
| `NativeEchokrauTtsBuilder` | `NativeEchokrauTtsBuilder.cs` | EchokrauTTS settings section builder (Local + Remote), mirrors `NativeAlltalkBuilder`. Used by Backend tab + First-Time wizard. The install row also carries the **wrapper Update button + version label** (see below). |
| `NativeChangelogWindow` | `NativeChangelogWindow.cs` | One-shot popup after plugin update — concatenates every unseen changelog entry into a `ScrollingListNode` with a bottom "I've read it" button that calls `IChangelogService.MarkAllSeen()`. Driven by `Plugin.HandleStartup`/`OnLogin`. **Body must be split** at `===…===` divider lines AND single blank lines (see `SplitIntoSections`); a single giant MultiLine `TextNode` with `Size.Y > ~500px` does not render in ATK. **Always call `list.RecalculateLayout()` after AddNode** — without it the scrollbar doesn't know the cumulative content height and scrolling silently does nothing. |
| `NativeVoiceConfigWindow` | `NativeVoiceConfigWindow.cs` | Per-Voice config popup (Allowed races/genders, **"Use as narrator voice"** → `Configuration.NarratorVoice`; the Voices list marks the resolved narrator with `(Narrator)` — see `docs/claude/services/voice-pack.md`). |
| `NativeNpcEditWindow` | `NativeNpcEditWindow.cs` | Per-NPC override popup (Race, Gender, Voice, Volume, Mute). Opened from VC Manager's `Edit Character` button. |
| `NativeAlltalkBuilder` | `NativeAlltalkBuilder.cs` | AllTalk settings section builder. |
| `NativeGameDataToolsWindow` | `NativeGameDataToolsWindow.cs` | Data-pipeline window: Quest Dialog Harvest, Voice Pack download, NPC Attribution Repair, **Player Name Cleanup** (`IDatabaseService.RetokenizePlayerNames`), **Import / Export** of `*.ekpack` audio packages (`IAudioPackageService` — path input + language dropdown + Export/Import buttons, both driven by the window's shared progress bar; buttons go through `Dim` since one service serialises both directions), Quest Lua Debug. The two DB-cleanup sections share one shape — Dry Run first, Apply enabled only after a dry run reported work, both run in `Task.Run` with their own `CancellationTokenSource`, output text written under `_statusLock` and drained into the `TextNode` in `OnUpdate`. |
| — Voices tab › Game voice (collapsible, top) | `NativeConfigWindow.GameVoice.cs` | Game-voice reference (plan `game-voice-reference` §5/§6). **Lives at the top of the Voices tab since 2026-11-15**: in the Backend tab, ONE more control made the last checkboxes/buttons of this section vanish (KamiToolKit list, cause not found; reproduced by removing one control) — do not move it back. Expanded, `_gvPanel` covers the voice list (search + list hidden), collapsed the list is back; `GvToggleHeight` shifts search/headers down. `UpdateGameVoice` asks `/health` itself when `Availability` is Unknown (throttled 10 s) — otherwise after a plugin reload the build button stayed disabled until the first spoken line. switch (on only via the opt-in `NativeConfirmWindow`; a declined dialog puts the tick back in `UpdateGameVoice`), "rank on conversation start", Remote hint (only EchokrauTTS + Remote), `Availability` status line, export/sync note, "Build index" + `StatusProgressBar` (button and handler share `CanBuildGameVoiceIndex`). Switching off clears memory and, if game-voice clips exist, asks to delete them (Delete first). Await is impossible in this `unsafe` class → `ContinueWith`. |
| `NativeConfirmWindow` | `NativeConfirmWindow.cs` | Generic question popup (paragraphs + confirm/cancel). Exactly one callback runs once; closing it any other way (X/Escape) = cancel via `OnHide`. Clicks deferred to `OnUpdate`. Owned via `NativeConfigWindow.GameVoiceConfirmWindow` → listed in `OwnedWindows()`. |
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

## Native window teardown (KamiToolKit gotcha)
- `NativeAddon.Close()` is **async** — it queues an ATK detach on the framework thread and returns immediately. `NativeAddon.Dispose()` calls `Close()` internally but then runs managed cleanup right away, so the ATK addon may still be attached when our managed handles get freed → dangling pointer → game crashes later in `AtkUldManager.Finalizer`.
- **Mitigation:** in any teardown path that owns NativeAddons, explicitly call `Close()` BEFORE `Dispose()` so the framework thread starts the ATK detach as early as possible. See `NativeWindowManager.Dispose()` (`SafeClose` + `SafeDispose`) and `NativeVoiceClipManagerWindow.Dispose()` for the pattern.
- **A closed window stays alive for its closing animation + finalize** (several frames). If the plugin unloads
  in between, the game finalizes it through KamiToolKit code of the unloaded plugin → C0000005 in
  `AtkEventManager.ClearEvents` (E2E run 20261123-130938: the config window `OnWizardCompleted` opens was
  still open at the restore reload). `NativeWindowManager.Dispose` therefore waits off the framework thread
  until no own window has a native addon (≤ 1 s, `AnyAlive` = addon pointer non-null, nulled in the
  addon's destructor); on the framework thread it cannot. The E2E self-reload closes the windows itself
  first through `IOwnedWindowsService` (window manager reports `AnyAlive` every frame, handles `CloseAllRequested`).
- **Measuring nodes** (a throwaway `TextButtonNode` only for `GetTextDrawSize`) must be `Dispose()`d right
  after measuring; never attached, they leak and show up at unload as "Forcing disposal of: …".
  Same for a control whose `AddNode` was removed but whose creation stayed: delete the creation too
  (the Voices tab's "[+] Advanced Filters" toggle leaked one `TextButtonNode` per config `OnSetup`
  from 2026-04 until 2026-11-23). Since KTK 62349f6 `RemoveNode(IEnumerable)` also leaves nodes undisposed.
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

## Pagination
- All paginated lists use `PaginationBar` from `Echotools.UI.Nodes`.
- `PaginationBar.SetTotalItems()` only resets page when total changes.
- Deferred page changes: button clicks set a flag, processed in next `OnUpdate`.
