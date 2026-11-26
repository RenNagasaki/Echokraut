# Windows — EchokrauTTS settings builder

Moved verbatim from `Echokraut/Windows/CLAUDE.md` on 2026-11-20 (file was over the 15k budget). Does not load by itself — read it before working in this area.

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
- **Visibility flips must re-flow the row.** `HorizontalListNode.OnUpdateLayout` skips invisible
  nodes, but only when it actually runs — so the builder tracks `_prevUpdateAvailable` and calls
  `InstallRow.UpdateLayout()` on the flip. Doing it every frame would be a per-frame node
  mutation; not doing it leaves the label sitting where the hidden button used to be.
- The button reads the remote tag from `IEchokrauTtsInstanceService.LatestWrapperVersion`, so the
  builder signature (and both call sites) stay unchanged — no `IRemoteUrlService` in the UI layer.
- Click stops a running instance and calls `UpdateWrapper()` inside `Task.Run` (same off-UI-thread
  rule as Install/Stop — it does blocking download + process work).

## EchokrauTTS engine options follow the selected engine (`NativeEchokrauTtsBuilder`)
- FP16 → XTTS, Qwen checkboxes → `qwen*`, Pocket captions/dropdowns → `pocket`; the mapping is
  `Helper/Functional/EngineOptionScope`. Both callers (Backend tab, wizard) AND
  `LocalInstanceNodes.IsShownFor(node, config)` into their own visibility, and re-flow when
  `LayoutKey(config)` (the engine id) changes. The Backend tab also re-flows the frame after the
  section is expanded: its toggle shows every node and re-flows before the engine filter runs.
- A new engine option: add its node to `GroupOf` (and a group to `EngineOptionScope` if needed).
