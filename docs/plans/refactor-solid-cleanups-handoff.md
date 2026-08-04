# Handoff: `refactor/solid-cleanups` — offene Themen

**Stand:** 2026-06-09 · Branch `refactor/solid-cleanups` · 12 Commits über `main` ·
Build grün (`-p:Platform=x64`, 0 Fehler) · **549 Tests grün** · **NICHT gepusht** ·
**noch nicht nach `main` gemergt**.

Grundlage: der projektweite Audit-Report `docs/reviews/dry-kiss-solid-audit-2026-06-04.md`
(62 Findings). Erzeugt vom wiederverwendbaren Workflow `/solid-audit`.

---

## 1. Was erledigt ist (Commits auf dem Branch)

```
d516551  refactor: GoogleDriveSyncService → Fassade + 4 Kollaboratoren (SRP)
16dc990  fix:      GoogleDrive await (async void→Task) + UpsertFileAsync (KISS/DRY)
ee7ae68  refactor: VoiceLineSkipGuard + ReadAddonOptions (DRY Theme E)
ee342da  refactor: GetCulture + GetEffectivePlayerContentId (DRY Theme F)
384e596  refactor: StringKeyedComparable Basisklasse (DRY Theme C)
798ee44  refactor: DynamicIconButtonNode Hover-Tint → WireIconButtonHover (Theme A)
c6c489f  refactor: Label/HeaderLabel/Separator/Spacer → NativeNodeFactory (Theme A)
1624395  fix:      AlltalkBackend statischer Streaming-HttpClient (Socket-Exhaustion)
ba30ef4  refactor: Button/Input/Dim/SetVisible/CreateCollapsibleSection → NativeNodeFactory (Theme A)
6c80624  refactor: LogConfig → LogSources (Dictionary<TextSource,LogSourceConfig>) (Theme D)
e5cb913  refactor: NpcIdentityHelper (DRY Theme B)
b96ceed  chore:    /solid-audit Workflow + Audit-Report
```
(`db97a0f` = Hook-Gates = `main`.)

**Erledigte Audit-Themen:** A, B, C, D, E, F (alle DRY-Querschnitt) + High-Severity-Socket-Fix
+ GoogleDrive (Klein-Fixes + SRP-Split).

---

## 2. Offen — zum Branch-Abschluss (in Reihenfolge)

1. **In-Game-Sanity-Check** (alles unten ist verhaltenserhaltend gebaut, aber NICHT unit-testbar):
   - **GoogleDrive**: Download (periodisch + „Download now") spiegelt Ordner; Upload (`UploadFile`/`UploadVoiceLine`) legt Dateien + `lastUpload.txt` an.
   - **Theme E**: vom Spiel vertonte Zeilen (Talk/BattleTalk/Bubble) werden übersprungen; Auswahl-Dialoge (SelectString/Cutscene) lesen Optionen.
   - **Hover-Tint**: Dialog-Toolbar + Config-/VCM-Icon-Buttons hellen auf/Tooltip; None-Mode-disabled bleibt dim.
2. **`/review-push`** über den Branch laufen lassen (frischer Reviewer-Blick auf die 12 Commits).
3. **Changelog-Bullets** in die WIP-`v0.19.0.1`-Dateien setzen (Texte in §4).
4. **Merge nach `main`** + **Push** (macht der User; Claude pusht nie).

---

## 3. Offen — größere Audit-Brocken (bewusst NICHT gemacht — nicht erneut diskutieren ohne neuen Grund)

- **`NpcDataService`-SRP-Split → übersprungen.** Begründung:
  - **DIP-Finding ist False Positive / by-design:** `GetAddCharacterMapData(... IBackendService backend)` nimmt `IBackendService` als Parameter, weil `BackendService` bereits `INpcDataService` injiziert (`BackendService.cs:28/46/55`). Injektion → **DI-Zyklus**. Param = bewusste Zyklus-Vermeidung.
  - **SRP-Wert gering:** ~90 % der 502 Zeilen sind EIN kohärenter Concern (Character-/Voice-Mapping, in-memory `_mappedNpcs/_mappedPlayers`). Mute/Phonetik/Voice sind **triviale 1-Zeilen-DB-Durchreichungen** — Extraktion = bloße Wrapper.
  - **Echter Gewinn nur via ISP** (3 Interfaces) → ripplet in **16 Aufrufer-Dateien + `ServiceBuilder`**, davon mehrere in der User-WIP → kein sauberer Commit möglich. Aufwand/Risiko ≫ Nutzen.
  - Falls doch: nur als ISP-Variante MIT WIP-Verschränkung, klar gekennzeichnet.
- **GoogleDrive-Secrets → kein Issue (False Positive).** `*.Secrets.cs` ist **gitignored** (`.gitignore:6`); nur ein `.Secrets.cs.example`-Template ist committet. Die echten Credentials waren NIE in git. Es ist ein Installed-App-OAuth-Client mit PKCE → Secret per Google-Design nicht vertraulich, wird mit der App ausgeliefert. By-design.
- **Audit-Long-Tail:** diverse low/medium Per-File-Findings im Report (`docs/reviews/dry-kiss-solid-audit-2026-06-04.md`, §3 „Most Impactful Per-File Findings" + Anhang) sind NICHT abgearbeitet — bei Bedarf einzeln picken.

---

## 4. Changelog-Bullets (bereit zum Einfügen in die WIP-`v0.19.0.1`-Dateien)

Beide gehören in `BUG FIXES` / `BUGFIXES`. (`*_EN.txt`/`*_DE.txt` liegen in der User-WIP.)

**Socket-Fix (AlltalkBackend) — EN:**
```
- AllTalk audio generation no longer leaks network sockets. Each spoken
  line used to spin up a brand-new HTTP client + connection, which under
  sustained dialogue could exhaust the OS socket pool and make generation
  start failing until the game was restarted. Generation now reuses a
  single long-lived client (keep-alive), so heavy sessions stay stable.
```
**Socket-Fix — DE:**
```
- Die AllTalk-Audioerzeugung verliert keine Netzwerk-Sockets mehr. Bisher
  wurde pro gesprochener Zeile ein komplett neuer HTTP-Client samt
  Verbindung aufgebaut, was bei längeren Dialogsitzungen den Socket-Pool
  des Systems erschöpfen und die Erzeugung bis zum Spielneustart lahmlegen
  konnte. Jetzt wird ein einziger, langlebiger Client (Keep-Alive)
  wiederverwendet, sodass auch intensive Sitzungen stabil bleiben.
```
**GoogleDrive-Sync-Fix (optional, Nische) — EN:**
```
- Periodic Google Drive sync no longer overlaps itself. A slow download
  could previously start again before the prior one finished; the loop now
  awaits each sync before scheduling the next.
```
**GoogleDrive-Sync-Fix — DE:**
```
- Der periodische Google-Drive-Sync überlappt sich nicht mehr. Ein langsamer
  Download konnte bisher erneut starten, bevor der vorige fertig war; die
  Schleife wartet jetzt jeden Sync ab, bevor der nächste geplant wird.
```

---

## 5. WIP-Verschränkung (wichtig beim Abschluss)

Claude hat WIP-verschränkte Teile **bewusst aus den Commits ausgeschlossen**; sie liegen im
Working Tree und „reisen" mit dem nächsten User-Feature-Commit:

| Datei (User-WIP) | enthält zusätzlich Claude-Refactor |
|---|---|
| `Services/VoiceMessageProcessor.cs` | Theme-F (`GetEffectivePlayerContentId`), Theme-B (`NpcIdentityHelper`) Call-Sites |
| `Windows/Native/NativeGameDataToolsWindow.cs` | Theme-A (`NativeNodeFactory`-Factories + Hover-Tint) Dedup |
| `Services/ServiceBuilder.cs` | (rein User-WIP — von Claude NICHT angefasst) |

Übrige reine User-WIP (NPC-Attribution-Feature): `DatabaseService.cs`, `IDatabaseService.cs`,
`DatabaseServiceTests.cs`, `Loc.cs`, `Changelogs/v0.19.0.1_*.txt`, `NativeWindowManager.cs`,
`AttributionInstanceRow.cs`, `INpcAttributionRepairService.cs`, `NpcAttributionRepairService.cs`.

> **NpcAttributionRepairService.cs** trägt zudem Claude's Theme-B-Dedup (aus `e5cb913` ausgeschlossen,
> weil die Datei zu dem Zeitpunkt untracked User-WIP war).

---

## 6. Gotchas / Reminders (gelten ab jetzt dauerhaft)

- **Hook-Gates sind LIVE** (`db97a0f`, global in `~/.claude/` + projektlokal `.claude/`):
  - `git push` → deny · `git commit` → ask · AI-Attribution/CLAUDE.md-Commit → deny · Submodul-Edit → deny.
  - **Stop-Gate**: Build+Test laufen am Turn-Ende bei `.cs`-Änderung; blockt bei Fehler. Plus globaler `agent`-Stop-Hook für DRY/KISS/SOLID.
  - **Echokraut-Commit-Gate**: `git commit` verlangt Changelog-Datei + `<Version>` > letztem GitHub-Tag + EN/DE-Sync; Merge + `(no changelog: …)` sind exempt.
  - Folge: Bash-Befehle mit wörtlichem `git commit`/`git push` werden selbst abgefangen → über `_test-*.js` testen, nicht via Shell.
- **`*.Secrets.cs` ist gitignored.** Lokale Credentials liegen in `Services/DriveAuthProvider.Secrets.cs` (von `GoogleDriveSyncService.Secrets.cs` migriert; Template = `DriveAuthProvider.Secrets.cs.example`).
- **`/solid-audit`** (Workflow) jederzeit erneut für eine frische Vollprüfung; **`/review-push`** vor dem Push.
- Build: `/c/Users/jkatz/.dotnet/dotnet.exe build -p:Platform=x64` · Test: `… test`.

---

## 7. Schnellstart nächste Session
1. `git log --oneline main..HEAD` + `git status` → Stand verifizieren.
2. In-Game-Check (§2.1) oder direkt Branch-Abschluss (§2.2–2.4).
3. Für neue Arbeit: `/solid-audit` → §3-Long-Tail picken, oder neues Thema.
