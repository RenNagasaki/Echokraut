# Plan: CLAUDE.md-Regeln → Hooks (Anti-Vibecode-Gates)

**Ziel:** Erzwingbare, objektiv prüfbare Regeln aus den drei CLAUDE.md-Ebenen
(global / Dalamud / Echokraut) von „Kontext, dem Claude folgen *soll*" zu
deterministischen **Gates** umbauen, die die Harness ausführt — unabhängig davon,
ob die Regel im Kontextfenster präsent ist.

Urteilsbehaftete Regeln (DI-Stil, „keine Duplikate", „interface+impl") bleiben in
CLAUDE.md: ein Hook kann sie nicht zuverlässig prüfen.

> **STATUS: UMGESETZT (2026-06-04).** Alle Phasen gebaut, getestet und in
> `~/.claude/settings.json` (global/Dalamud) + `<repo>/.claude/settings.json`
> (Echokraut, committed) verdrahtet. Entscheidungen: Build+Test-Stop-Gate **voll
> (block)**; CLAUDE.md-Konflikt → **nie committen, globale CLAUDE.md korrigiert**;
> Projekt-Hook → **committed** (`.gitignore`-Ausnahme ergänzt); **alle** Phasen inkl.
> optionaler Warner gebaut; zusätzlich **DRY/KISS/SOLID** als globaler `agent`-Stop-
> Hook (experimentell). Test-Suites: `~/.claude/hooks/_test-git-guard.js`,
> `_test-block-submodule-edits.js` (ALL PASS).

---

## 0. Harte Randbedingung (verifiziert)

Claude Code lädt `settings.json`/Hooks **nicht** aus Eltern-Verzeichnissen — nur:

| Scope | Datei | Gilt für |
|-------|-------|----------|
| User | `~/.claude/settings.json` | **alles, überall** |
| Projekt (shared) | `<repo>/.claude/settings.json` | nur dieses Repo (committet) |
| Projekt (lokal) | `<repo>/.claude/settings.local.json` | nur dieses Repo (gitignored) |

> CLAUDE.md *wird* aus Ancestors geladen — `settings.json` **nicht**. Ein
> `F:\Git-Repositories\Dalamud\.claude\settings.json` würde beim Arbeiten in
> Echokraut **ignoriert**.

**Konsequenz für die mittlere „Dalamud"-Ebene:** Es gibt kein natürliches
settings.json-Zuhause dafür. Lösung: Dalamud-Hooks werden in den **User-Settings**
registriert, aber die Hook-Skripte **self-scopen** sich — sie prüfen, ob das
aktuelle Repo ein Dalamud-Plugin ist (Marker: `KamiToolKit/`-Submodul vorhanden),
und no-op'en sonst mit `{}` (allow). Damit bleibt die 3-Ebenen-Semantik exakt
erhalten, ohne den Hook über N Plugin-Repos zu duplizieren.

---

## 1. Vorhandene Konvention (beibehalten)

- Skripte: `~/.claude/hooks/*.js`, Shebang `#!/usr/bin/env node`, stdin→JSON.
- Registrierung: `~/.claude/settings.json` → `hooks` (bereits aktiv:
  `block-secrets.js` auf PreToolUse, `plan-review-trigger.js` auf UserPromptSubmit).
- Allow = `console.log("{}")`; Deny (PreToolUse) =
  `{hookSpecificOutput:{hookEventName:"PreToolUse",permissionDecision:"deny",permissionDecisionReason:"…"}}`.
- Ask = `permissionDecision:"ask"` (zwingt User-Bestätigung statt Hard-Block).
- Command-Form mit `$HOME` funktioniert bereits (läuft über Shell) → spiegeln.

**Hook-Mechanik (Kurzreferenz):**

| Event | blockt? | Mechanismus |
|-------|---------|-------------|
| PreToolUse | ja | `permissionDecision: deny\|ask` (oder exit 2 + stderr) |
| PostToolUse | nur Folge-Call | `{"decision":"block","reason":…}`; sonst `additionalContext` (Warnung, kein Block) |
| Stop | ja (erzwingt Weiterarbeit) | `{"decision":"block","reason":…}` |

---

## 2. Regel-Inventar je CLAUDE.md (Verdikt)

Legende: **GATE** = als Hook umsetzen · **CTX** = bleibt CLAUDE.md (Urteilssache)
· **OPT** = technisch machbar, aber Heuristik/False-Positive-Risiko → optional.

### 2a. Global (`~/.claude/CLAUDE.md`)

| Regel | Verdikt | Scope | Event / Mechanismus |
|-------|---------|-------|---------------------|
| Build muss grün enden (0 errors) | GATE | Dalamud¹ | Stop → build, block bei Fehler |
| Tests Pflicht / 0 failures am Ende | GATE | Dalamud¹ | Stop → test, block bei Fehler |
| DI / interface+impl / events / keine Duplikate | CTX | – | nicht maschinell prüfbar |
| CLAUDE.md aktuell halten | CTX | – | Urteilssache |
| Keine Doku-Dateien außer auf Anfrage | OPT | Global | PreToolUse Write → `ask` bei neuer `.md` |
| **Nie `git push`** | GATE | Global | PreToolUse Bash → deny |
| **Nie auto-committen / vorher fragen** | GATE | Global | PreToolUse Bash `git commit` → `ask` |
| **Keine AI-Attribution (`Co-Authored-By` etc.)** | GATE | Global | PreToolUse Bash → deny bei Match |
| **Keine Submodul-Edits** | GATE | Global | PreToolUse Edit\|Write → deny via `.gitmodules` |
| CLAUDE.md committen ↔ NIE committen | ⚠️ KONFLIKT | – | siehe §5, Entscheidung nötig |
| Feature-Branch bei 3+ Dateien | CTX/OPT | – | „Anzahl Dateien je Task" schwer hookbar |
| Strings lokalisierbar | OPT | Projekt | Heuristik, framework-spezifisch |

¹ Build/Test-Kommando ist projektspezifisch → praktisch nur auf Dalamud/Projekt-Ebene
sinnvoll, wo das exakte Kommando bekannt ist. Global wäre zu fragil.

### 2b. Dalamud (`F:\Git-Repositories\Dalamud\CLAUDE.md`)

| Regel | Verdikt | Scope | Event / Mechanismus |
|-------|---------|-------|---------------------|
| **Build `~/.dotnet/dotnet.exe build -p:Platform=x64` grün** | GATE | Dalamud | Stop → build (self-scoped) |
| **`dotnet test` 0 failures** | GATE | Dalamud | Stop → test (self-scoped) |
| SonarQube nach jedem Build+Test | OPT/NEIN | – | zu schwer/netzabhängig für Auto-Hook → manuell (§5) |
| DI / ServiceBuilder / interface+impl | CTX | – | Urteilssache |
| OpenConfigUi + OpenMainUi registrieren | OPT | Dalamud | grep Plugin.cs, Warnung (kein Block) |
| Version im Fenstertitel | OPT | Dalamud | grep, Warnung |
| KamiToolKit-Gotchas, Native-UI-Wissen | CTX | – | reines Wissen |
| Log-Call braucht Methodennamen (nie `""`) | OPT | Dalamud | PostToolUse `.cs`, Warnung bei `Start("")` |
| `_log.Error` nur user-actionable | CTX | – | Urteilssache |
| Full + Short Command registrieren | CTX | – | Urteilssache |
| **Nie CLAUDE.md committen (auch nicht `-f`)** | GATE | Global | = Global-Hook (dedupe) |
| **Nie Submodule editieren** (KamiToolKit/Echotools/OtterGui) | GATE | Global | = Global-Hook via `.gitmodules` (dedupe) |
| Keine manuell geklonten Repos | OPT | Dalamud | SessionStart-Audit, kein Gate |
| GitLab `origin` / `.gitlab-ci.yml` Pflicht | OPT | Dalamud | SessionStart-Audit, kein Gate |

### 2c. Echokraut (`…\Echokraut\CLAUDE.md`)

| Regel | Verdikt | Scope | Event / Mechanismus |
|-------|---------|-------|---------------------|
| Build/Test (Echokraut.Tests) grün | GATE | Projekt | verfeinert Dalamud-Stop-Hook (genauer Testpfad) |
| Projektstruktur / Service-Pattern | CTX | – | Urteilssache |
| **Changelog Pflicht pro Commit** | GATE | Projekt | PreToolUse Bash `git commit` → deny ohne Changelog-Datei im Stage |
| **`<Version>` > letzter GitHub-Tag** | GATE | Projekt | PreToolUse `git commit` → deny wenn Version nicht gebumpt |
| **EN + DE Changelog synchron** | GATE | Projekt | PreToolUse `git commit` → deny bei Asymmetrie |
| Merge-Commits ausgenommen | GATE-Regel | Projekt | `.git/MERGE_HEAD` vorhanden → Gate überspringen |
| SonarQube-Key/Exclusions | CTX | – | Konfigwissen |

---

## 3. Konkrete Umsetzung — Dateien & Skripte

### 3a. Global-Tier → `~/.claude/hooks/` + `~/.claude/settings.json`

**Neue Skripte:**

1. `git-guard.js` (PreToolUse, matcher `Bash`)
   - Parst `tool_input.command`.
   - `git push` (ohne `--dry-run`) → **deny** („Nur der User pusht.").
   - `git commit` → **ask** (erzwingt Bestätigung; Regel „nie auto-committen").
   - `git commit`-Message enthält `Co-Authored-By` / `Generated with` / `🤖` /
     `Claude` (als Attribution) → **deny** („Keine AI-Attribution.").
   - `git add`/`git commit`/`git rm` referenziert `CLAUDE.md` (auch `-f`) → **deny**
     (siehe §5-Entscheidung; standardmäßig deny).
   - Sonst `{}`.

2. `block-submodule-edits.js` (PreToolUse, matcher `Edit|Write|NotebookEdit`)
   - Findet Repo-Root (aufwärts bis `.git`), liest `.gitmodules`, sammelt
     `path =`-Einträge.
   - Wenn `tool_input.file_path` unter einem Submodul-Pfad → **deny**
     („Submodul X ist ein Fremd-Repo — nicht editieren.").
   - Generisch (kein hartkodierter Submodul-Name) → funktioniert für alle Repos.

3. `no-stray-docs.js` (PreToolUse, matcher `Write`) — **optional/niedrig**
   - Nur bei **neuer** `.md`-Datei (Write auf nicht-existierende Datei), die nicht
     in Allowlist (`docs/plans/`, `docs/reviews/`, `CLAUDE.md`, `Changelogs/`) liegt
     → **ask** („Doku-Datei nur auf explizite Anfrage.").

**settings.json-Eintrag (User):**
```jsonc
"PreToolUse": [
  { "matcher": "Bash", "hooks": [{ "type":"command",
      "command":"node \"$HOME/.claude/hooks/git-guard.js\"", "timeout":10 }] },
  { "matcher": "Edit|Write|NotebookEdit", "hooks": [{ "type":"command",
      "command":"node \"$HOME/.claude/hooks/block-submodule-edits.js\"", "timeout":10 }] }
  // optional: no-stray-docs.js auf "Write"
]
```
> Bestehende PreToolUse-Einträge (`block-secrets.js`) bleiben — Array nur ergänzen.

### 3b. Dalamud-Tier → `~/.claude/hooks/` (self-scoped) + User-`settings.json`

**Shared lib:** `~/.claude/hooks/lib/dalamud.js`
- `findRepoRoot(cwd)` — aufwärts bis `.git`.
- `isDalamudPlugin(repoRoot)` — true, wenn `repoRoot/KamiToolKit` existiert
  **oder** `.gitmodules` `KamiToolKit` referenziert. (Marker laut Konvention:
  Native UI/KamiToolKit ist für alle Plugins Pflicht.)
- `dotnet()` — Pfad `${HOME}/.dotnet/dotnet.exe`.

**Neue Skripte:**

4. `dalamud-mark-dirty.js` (PostToolUse, matcher `Edit|Write`)
   - Wenn `isDalamudPlugin` **und** `file_path` endet auf `.cs`/`.csproj` →
     `touch <repoRoot>/.git/.build-dirty` (Marker, nie getrackt).
   - Sonst no-op. (Vermeidet Build/Test, wenn nichts Relevantes geändert wurde.)

5. `dalamud-verify-on-stop.js` (Stop)
   - No-op wenn nicht `isDalamudPlugin` **oder** kein `.git/.build-dirty`.
   - Sonst: `dotnet build -p:Platform=x64`; bei Fehler →
     `{"decision":"block","reason":"Build hat Fehler — vor Abschluss fixen:\n<tail>"}`.
   - Bei Build-OK: `dotnet test`; bei Failures → `decision:block` mit Test-Tail.
   - Bei Erfolg: Marker löschen, `{}`.
   - **Tradeoff/Entscheidung:** Latenz am Turn-Ende (inkrementeller Build+Test).
     Dirty-Flag minimiert das (läuft nur bei .cs/.csproj-Änderungen). Siehe §5.

**settings.json-Eintrag (User):**
```jsonc
"PostToolUse": [
  { "matcher": "Edit|Write", "hooks": [{ "type":"command",
      "command":"node \"$HOME/.claude/hooks/dalamud-mark-dirty.js\"", "timeout":10 }] }
],
"Stop": [
  { "hooks": [{ "type":"command",
      "command":"node \"$HOME/.claude/hooks/dalamud-verify-on-stop.js\"",
      "timeout":600, "statusMessage":"Build & Tests…" }] }
]
```

**Optional (niedrig):** `dalamud-lint-warn.js` (PostToolUse `.cs`) →
`additionalContext`-Warnung bei `\.Start\(\s*""` (Log ohne Methodenname).
Kein Block.

### 3c. Echokraut-Projekt-Tier → repo-lokal

> **Skript im Repo** (`<repo>/.claude/hooks/`), damit es mit dem Repo reist und
> ggf. für andere Contributor gilt. Registrierung in `<repo>/.claude/settings.json`
> (committet, shared) **oder** `settings.local.json` (lokal) — Entscheidung §5.

6. `.claude/hooks/echokraut-commit-gate.js` (PreToolUse, matcher `Bash`)
   - Nur aktiv bei `git commit` im Echokraut-Repo. Frühe `{}`-Rückgabe sonst.
   - **Merge-Ausnahme:** `.git/MERGE_HEAD` vorhanden → `{}` (Merges sind exempt).
   - **Changelog-Gate:** `git diff --cached --name-only` muss ≥1 Datei unter
     `Echokraut/Resources/Changelogs/` enthalten → sonst **deny**.
   - **EN/DE-Sync:** wenn `*_EN.txt` gestaged, muss passendes `*_DE.txt` gestaged
     sein (und umgekehrt) → sonst **deny**.
   - **Version-Gate:** `<Version>` aus `Echokraut/Echokraut.csproj` lesen; letzten
     GitHub-Tag via dokumentiertem `git ls-remote --tags github | … | sort -V | tail -1`
     ermitteln; wenn `<Version>` ≤ Tag → **deny** („Version vor Commit bumpen.").
     - **Graceful:** schlägt `ls-remote` fehl (offline) → Warnung, **allow**.
   - Alle Deny-Reasons zitieren die jeweilige CLAUDE.md-Regel knapp.

**settings.json-Eintrag (Projekt, `<repo>/.claude/settings.json`):**
```jsonc
{
  "hooks": {
    "PreToolUse": [
      { "matcher": "Bash", "hooks": [{ "type":"command",
          "command":"node \"$CLAUDE_PROJECT_DIR/.claude/hooks/echokraut-commit-gate.js\"",
          "timeout":30 }] }
    ]
  }
}
```
> `$CLAUDE_PROJECT_DIR` (von der Harness gesetzt) statt `$HOME`, damit der Pfad
> repo-relativ bleibt. (Falls die Variable in dieser Version nicht verfügbar ist:
> Fallback auf relativen Aufruf prüfen — Test in §6.)

---

## 4. Scope-Zusammenfassung (Antwort auf die Kernfrage)

| Hook | Datei-Heimat | Registrierung | Warum dieser Scope |
|------|--------------|---------------|--------------------|
| git-guard (push/commit/attrib/CLAUDE.md) | `~/.claude/hooks/` | User | Git-Regeln gelten überall |
| block-submodule-edits | `~/.claude/hooks/` | User | generisch via `.gitmodules`, jedes Repo |
| no-stray-docs (opt) | `~/.claude/hooks/` | User | globale Stilregel |
| dalamud-verify-on-stop (+mark-dirty) | `~/.claude/hooks/` (self-scoped) | User | Build/Test-Kmd nur in Dalamud-Plugins bekannt; self-scope statt Ancestor-settings |
| dalamud-lint-warn (opt) | `~/.claude/hooks/` (self-scoped) | User | Dalamud-Logging-Konvention |
| echokraut-commit-gate (Changelog/Version/Sync) | `<repo>/.claude/hooks/` | Projekt-settings.json | rein Echokraut-spezifisch (Pfade, csproj, github-Tag) |

---

## 5. Offene Entscheidungen (User)

1. **CLAUDE.md-Commit-Konflikt.** Global-CLAUDE.md sagt „committen", Dalamud- &
   Projekt-CLAUDE.md + Memory sagen „NIE committen". Der Hook erzwingt **nie
   committen** (dominante Regel). → Bestätigen + globale CLAUDE.md-Zeile
   korrigieren?
2. **`git commit` → `ask` vs. kein Hook.** „ask" garantiert die „immer
   nachfragen"-Regel deterministisch, kostet aber 1 Bestätigungsklick pro Commit.
   Akzeptabel?
3. **Build+Test am Stop (Latenz).** Auto-Verify am Turn-Ende = stärkstes
   Anti-Vibecode-Gate, aber inkrementeller Build+Test kostet Sekunden pro
   Abschluss (mit Dirty-Flag nur nach Code-Änderungen). Alternativen: (a) nur
   Build, kein Test; (b) nur Warnung statt Block; (c) manuell lassen.
4. **SonarQube:** bewusst **kein** Auto-Hook (Netz, erzeugt Scans, langsam) →
   bleibt manuell in CLAUDE.md. OK?
5. **Projekt-Hook committen?** `<repo>/.claude/settings.json` + Skript ins Repo
   (alle Contributor bekommen das Changelog-Gate) **oder** lokal in
   `settings.local.json` lassen (nur du)?
6. **Optionale Heuristik-Hooks** (no-stray-docs, lint-warn, UI-Callback-Check)
   jetzt bauen oder erstmal weglassen?

---

## 6. Rollout-Reihenfolge & Test

1. **Phase 1 (risikoarm, hoher Wert):** `git-guard.js` + `block-submodule-edits.js`
   (Global). Reine Deny/Ask-Gates, keine Latenz.
2. **Phase 2 (Projekt):** `echokraut-commit-gate.js`. Hoher Wert, isoliert auf
   `git commit`.
3. **Phase 3 (Dalamud, Latenz):** `mark-dirty` + `verify-on-stop` nach Klärung von
   Entscheidung 3.
4. **Phase 4 (optional):** Heuristik-Warner.

**Test je Hook** (vor settings.json-Eintrag): Skript mit gemocktem stdin füttern
und Exit/JSON prüfen, analog zum vorhandenen `_test-block-secrets.js`:
```bash
echo '{"tool_name":"Bash","tool_input":{"command":"git push origin main"}}' \
  | node ~/.claude/hooks/git-guard.js   # erwartet: deny
echo '{"tool_name":"Bash","tool_input":{"command":"git status"}}' \
  | node ~/.claude/hooks/git-guard.js   # erwartet: {}
```
Für jeden neuen Hook eine `_test-<name>.js`-Datei mit Positiv-/Negativfällen
anlegen (matcht bestehende Konvention). **Windows:** `$HOME`-Form spiegeln (läuft
bereits); dotnet-Pfad im Skript absolut aus `process.env.HOME`/`USERPROFILE` +
`/.dotnet/dotnet.exe` auflösen, nicht auf `~`-Expansion verlassen.

---

## 7. Was bewusst NICHT zum Hook wird (bleibt CLAUDE.md)

DI-/Architektur-Stil, „keine Duplikate", interface+impl, Service-/ServiceBuilder-
Pattern, `_log.Error`-Semantik, Native-UI/KamiToolKit-Wissen, Command-Aliase,
„CLAUDE.md aktuell halten", Feature-Branch-Heuristik. Diese sind urteilsbehaftet —
ein Hook würde nur false-positiv blocken oder gar nicht greifen. Sie bleiben
Kontext; die Hooks sind das **Sicherheitsnetz** darunter, kein Ersatz.
