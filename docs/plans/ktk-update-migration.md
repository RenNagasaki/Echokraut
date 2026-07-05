# KTK-Update-Migration (ff7a689 → 376d8f9, +45 Commits)

Branch: `feature/echokrautts-backend` (User: „ja, auf diesem Branch"). KTK-Submodul-Pointer bereits auf `376d8f9` gezogen. **KTK = third-party-Submodul → NUR Pointer, KEINE KTK-Quelle editieren.** Echotools = eigenes Submodul → editierbar.

## Ziel
Crash #3 beheben: 2× Stimmenwechsel im Dialogfenster → C0000005 (`ComponentNode.OnReceiveEvent` → Event an disposed Component). Upstream-Fixes: `Rework Dropdown Node`, `IsDisposed`-Guards, Finalizer-Dispose, `Prevent recursive self attach`.

## Entscheidungen (User)
1. Volles KTK-Update auf diesem Branch.
2. VoiceClipManager-Tree → **virtualisierter `TreeListNode<T,TU>`** (Redesign; alte heterogene Kategorie passt nicht ins homogene Zeilenmodell).
3. Per-NPC-Aktionen (Generate All Unsaved / Delete All Saved / Edit Character) → **ins Detail-Fenster** (`NativeVoiceClipDetailWindow`) verschieben.

## Neue KTK-APIs (bestätigt)
- **DropDown**: `TextDropDownNode` + `OptionListNode` ENTFERNT. Neu: `StringDropDownNode : DropDownNode<string>` mit direktem `.Options` (List<string>), `.SelectedOption`, `.OnOptionSelected`, `.OnCollapsed`, `.LabelNode` (immer non-null → alte `LabelNode.Node != null`-Guards + Options-Bypass entfallen), `.Collapse(bool)`, `.Uncollapse()`, `.Toggle()`, `.IsCollapsed`, `.PlaceholderString`, `.MaxListOptions` (default 5!). Enum-Variante: `EnumDropDownNode`.
- **Tree**: `TreeListNode<T,TU> where TU:TreeListItemNode<T>,ITreeListItemNode,new()`. `Options = Dictionary<ReadOnlySeString,List<T>>` (Header→Zeilen), `OnItemSelected(T)`, `SelectedItem`, `ItemSpacing`, `NoResultsString`. Selbst-scrollend (eigene ScrollBarNode), virtualisiert (fester Item-Pool). `TreeListItemNode<T> : SelectableNode` → abstract `SetNodeData(T)`, `ItemData`, `IsSelected`, `OnClick(SelectableNode)`; Interface braucht `static abstract float ItemHeight`.
- `TreeListCategoryNode`/`TreeListHeaderNode`/alter `TreeListNode`/`ScrollingTreeNode` (KTK) + Echotools-Wrapper `ScrollingTreeNode.cs` → ENTFERNT/obsolet.

## Migrations-Landkarte
### Tree
- [ ] `Echotools/UI/Nodes/ScrollingTreeNode.cs` löschen (obsolet).
- [ ] Neu: `VcRow`-Modell + `VcRowNode : TreeListItemNode<VcRow>` (Icon+Titel+Untertitel, Vorbild `IconListItemNode`). `ItemHeight => 41f`.
- [ ] `NativeVoiceClipManagerWindow`: `ScrollingTreeNode[]` → `TreeListNode<VcRow,VcRowNode>[]`. Progressives Bauen + manuelle Pagination ENTFALLEN (Virtualisierung). Header = NPC-String, Zeilen = Quest-Typ-Gruppen. `OnItemSelected` → Detail-Fenster. Options pro Seite/Tab füllen (Daten eager pro Seite laden). Entfernt: `_npcCategoryNodes`, `TreeListCategoryNode`, `PopulateNpcCategory`, `_oldTrees`, progressive Felder.
- [ ] Per-NPC-Aktionen → `NativeVoiceClipDetailWindow` (ShowVoiceClips-Signatur um NpcMapData/charId erweitern; Aktionsleiste Generate All / Delete All / Edit Character).

### Dropdown (6 Dateien) — `TextDropDownNode`→`StringDropDownNode`, `.OptionListNode.X`→`.X`, Guards raus
- [ ] DialogTalkController.cs (7) — **Crash-Ziel**; alte Workarounds (OptionListNode-Bypass, LabelNode.Node-Guard, Content-Guard SequenceEqual) prüfen/vereinfachen.
- [ ] NativeConfigWindow.cs (5) — Backend-Dropdown.
- [ ] NativeConfigWindow.VoiceSelection.cs (3)
- [ ] NativeGameDataToolsWindow.cs (5)
- [ ] NativeNpcEditWindow.cs (15) — Voice-Dropdown.
- [ ] NativeVoiceClipManagerWindow.cs (4) — Quest-Typ-Dropdown (im Zuge des Rewrites).

### Weitere KTK-Relocations
- [ ] Nach Build sichtbar; iterativ fixen (GlobalUsings/Namespace-Verschiebungen wie bei letzter Migration).

## Abschluss
- [ ] Build 0 Fehler (`dotnet build -p:Platform=x64`).
- [ ] Tests grün (`dotnet test`, aktuell 674).
- [ ] SonarQube neue Issues fixen.
- [ ] In-game: 2× Stimmenwechsel im Dialog (Crash weg?), VoiceClipManager-Tree (Browse/Detail/Aktionen), alle Dropdowns.
- [ ] Changelog v0.19.1.0 (BUG FIXES: Dialog-Crash; ggf. IMPROVEMENTS VoiceClipManager).

## Status — CODE FERTIG, in-game-Test offen
Alle Punkte umgesetzt. **Build 0 Fehler, 674 Tests grün.** Umgesetzt:
- Echotools: `ScrollingTreeNode.cs` gelöscht; `ScrollingListNode.ScrollPosition` int→float.
- Dropdown: alle 6 Dateien `TextDropDownNode`→`StringDropDownNode`, `.OptionListNode.X`→`.X`, `LabelNode.Node`-Guards raus; DialogTalkController-Content-Guard vereinfacht (Kommentare aktualisiert, kein Options-Bypass mehr nötig).
- Slider: `DecimalPlaces` (7×) entfernt (KTK-Slider-Rework; funktional irrelevant, Value 0–100 → /100).
- VoiceClipManager: komplett auf virtualisierten `TreeListNode<VcRow,VoiceClipRowNode>` umgebaut. Neue Datei `VoiceClipRowNode.cs` (Modell `VcRow` + Item-View). Kein progressives Node-Bauen/Recreate/`_oldTrees` mehr — Dict `Options` wird pro Batch reassigned (preserved collapse+scroll). `PopulateNpcCategory`/`RefreshCounts` entfernt, tote Caches aufgeräumt.
- Detail-Fenster: `ShowVoiceClips(...4. Param Action? onEditCharacter)`; Aktionsleiste um „Delete All Saved" + „Edit Character" erweitert; Progress-Bar auf eigene Zeile.
- Keine weiteren KTK-Relocations (GlobalUsings deckten den Rest).

**IN-GAME-TEST (User) — kritisch, ATK-Verhalten nicht per Build verifizierbar:**
1. **Dialog-Stimmenwechsel 2× hintereinander** → KEIN Crash mehr (Kernziel #3).
2. VoiceClipManager: NPCs browsen (Header ein-/ausklappen, scrollen bei großer Liste), Quest-Typ-Filter, Suche, Tabs NPC/Player, Pagination.
3. Zeile klicken → Detail-Fenster; dort Generate All / Delete All / Edit Character.
4. Alle anderen Dropdowns (Backend-Tab Engine, NPC-Edit Race/Gender/Voice, GameDataTools Quest-Typ, VoiceSelection Voice) + Volume-Slider.
5. Hell/Dunkel-Theme.

**Nach erfolgreichem Test:** Commit (User-Freigabe) — Version-Bump + Changelog v0.19.1.0 (Dialog-Crash-BUG-FIX + VCM-IMPROVEMENT EN/DE bereits ergänzt).
