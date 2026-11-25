# Echokraut — Pre-Release In-Game-Checkliste

Vor jedem Update-Release (Tag/GitHub-Release) einmal im Spiel durchgehen. Zwei Backends
(**AllTalk**, **EchokrauTTS**), je **Local / Remote / Audio Files Only**. Schwerpunkt: die
historisch fragilen Native-UI-Stellen + die eigentliche Sprachausgabe.

> Nicht jedes Release braucht alle Punkte — aber **Abschnitt ⚡ Quick-Smoke immer**, und alles
> was der aktuelle Release-Diff berührt. Die „Extra aufpassen"-Liste unten sind die Stellen, die
> in der Vergangenheit gebrochen sind.

---

## 0. Vor dem Spiel (Build/CI)
- [ ] `dotnet build -p:Platform=x64` → 0 Fehler
- [ ] `dotnet test` → 0 Failures
- [ ] SonarQube-Scan → keine neuen Issues
- [ ] GitLab-Pipeline grün (inkl. GitHub-Mirror)
- [ ] Changelog `v{Version}_EN/DE.txt` aktualisiert + `<Version>` gebumpt (> letzter GitHub-Tag)
- [ ] Bei Installer-/Wrapper-Änderungen: Installer (`ELI-*`) neu released + `RemoteUrls.json` passt

---

## ⚡ Quick-Smoke (immer, ~5 min)
- [ ] Plugin lädt ohne Crash / ohne „Not on main thread" (eingeloggt **und** über Login-Screen)
- [ ] `/ek` öffnet das Config-Fenster; `/ekfirst` öffnet den First-Time-Wizard
- [ ] Einen NPC ansprechen → Dialog wird vertont
- [ ] Stimme im Dialog-Toolbar-Dropdown **2–3× hintereinander** wechseln → **kein Crash**
- [ ] Config → Backend: Fenster schließen + wieder öffnen → **Backend-Sektion noch da** (beide Engines)
- [ ] Plugin entladen → `echokraut.db` (+ `-wal`/`-shm`) **löschbar** ohne Spiel-Neustart

---

## 🎯 Release-Testplan: Plugin 0.19.3.3 · Installer ELI-1.3.2.0 · EchokrauTTS 0.0.2.0

> Gilt nur für dieses Release, beim nächsten durch einen neuen Block ersetzen. Basis (2026-11-13):
> GitHub-Releases **0.0.1.9** (Wrapper, `dc3f82a`), **ELI-1.3.1.0** (≈ `e8ee7e3`), **0.19.3.2** (Plugin,
> ≈ `15372fb`; der GitHub-Spiegel des Plugins ist ein Snapshot, die Basis ist deshalb über den Zeitpunkt
> bestimmt). Diff: Wrapper 9 Commits, Installer nur `EchokrauTtsServeArgs` + Versionsnummer, Plugin
> alles aus Changelog `v0.19.3.3`. Die Tester-Checkliste `Testing/checklist.json` (`"new": true`) ist
> das öffentliche Gegenstück; hier steht, **wie** ich selbst es prüfe.
>
> **Plattform:** **W** = Windows-Spiel-PC (RTX 5090) · **L** = Linux (XIVLauncher/Wine) · **A** = AMD
> (der Bekannte) · **D** = Docker (GitHub-Workflow) · **M** = Mac (nur Unit-Tests).
> **Den Stand auf den PC bringen:** beide Repos sind vor GitLab (`voraus 9` / `voraus 5`) → Wrapper
> per `tools/windows-test.sh` (Arbeitskopie per ssh nach `E:/EchokrauttsTest`), Plugin als
> Dev-Build in Dalamud.
>
> ⚠ **Vor dem Release sieht auch das neue Plugin Qwen/Pocket nicht von selbst:** die Engine-Liste kommt
> von `engines.json` am Tag `RemoteUrls.echokrauTtsVersion` (nicht an der installierten Version), und
> der Installer wird neu geladen, sobald `InstalledInstallerVersion` ≠ `RemoteUrls.installerVersion`.
> **Lokale Testweiche auf dem PC (nie committen)**, eingerichtet 2026-11-14:
> `RemoteUrlService.RemoteJsonUrl` → `RemoteUrls.LOCAL-TEST-OFF.json` (404 → eingebettete Kopie) und in
> der eingebetteten `Resources/RemoteUrls.json` `installerVersion: ELI-1.3.2.0`, `echokrauTtsVersion: 0.0.2.0`
> (Tag fehlt → Rückfall auf `main` der Wrapper-`engines.json`). Dazu in `pluginConfigs/Echokraut.json`
> `InstalledInstallerVersion: ELI-1.3.2.0`, `InstalledWrapperVersion: 0.0.2.0`; Installer aus
> `dotnet publish` über `C:\LocalTTS\EchokrautLocalInstaller`, Wrapper-Zip ohne `samples/`/`models/`
> über `C:\LocalTTS\echokrautts`. ⚠ Das Plugin speichert seine Config beim Entladen — die Config vor dem
> Neubau schreibgeschützt setzen, nach dem Start des Installers wieder freigeben.
> **Rückbau vor R3/R6:** `git checkout -- Echokraut/Resources/RemoteUrls.json Echokraut/Services/RemoteUrlService.cs`
> im PC-Repo, Sicherung liegt in `E:\EK-Backup-20261114-1842`.

### Was die Unit-Tests schon abdecken (NICHT von Hand testen)
Cache-Logik: Treffer, Ablauf, Nachzügler bekommt Puffer und dann live, Abbruch eines von zwei Clients,
Abbruch aller, Trennung, `no_cache`, LRU-Budget, `0` = aus (`test_response_cache.py`) ·
i18n-Tabelle: alle 4 Sprachen, gleiche Platzhalter, Rückfall auf en (`test_i18n.py`) ·
Engine-Registry = `KNOWN_BACKENDS`, unbekannte Engine wird abgelehnt (`test_engines.py`, `test_engine.py`) ·
Migration der Altmarker `deps.done`/`model.done` (`test_bootstrap_repair.py`) · Qwen/Pocket-Optionen,
Pocket 6 Layer hebt Schritt 1 auf 4 (`test_qwen_backend.py`, `test_pocket_backend.py`) · Installer-
Argumente (`EchokrauTtsServeArgsTests`) · Bewertungstext und Sortierung im Dropdown
(`TtsEngineRatingTests`, `TtsEngineCatalogServiceTests`) · Plugin setzt `no_cache` nur aus der
Sprachclip-Verwaltung (`EchokrauTtsBackendTests`).
Von Hand bleibt nur, was echte Hardware, echte Downloads, das Spiel oder die Konsole braucht.

### R0. Vorab (Mac, vor dem ersten Spieltest)
- [ ] **M** `node .claude/verify.js` im Wrapper → grün (die 5 vorbestehend roten Tests in `test_bootstrap_rocm_windows.py` lässt es auf macOS aus) · Plugin: `dotnet build -p:Platform=x64` + `dotnet test` → 0 Fehler (Installer-Tests inklusive)
- [ ] **M** ⚠ GitHub-Spiegel des Plugins aktuell? (2026-11-14: GitHub `main` stand auf dem Snapshot vom 08.11., `RemoteUrls.json` dort noch ELI-1.3.0.0/0.0.1.8, obwohl GitLab 1.3.1.0/0.0.1.9 hat → Live-Nutzer bekommen die alten Versionen.) `curl -s https://raw.githubusercontent.com/RenNagasaki/Echokraut/main/Echokraut/Resources/RemoteUrls.json` muss den GitLab-Stand zeigen
- [ ] **M** `engines.json` (Wrapper-Root) == `Echokraut/Resources/Engines.json`, `minVersion` von qwen-0.6b/qwen-1.7b/pocket = **`0.0.2.0`**, Reihenfolge Qwen 1.7B → Qwen 0.6B → XTTS → F5 → Pocket → MOSS (am 2026-11-13 geprüft: stimmt)
- [ ] **M** `python build-release-zip.py --list` → enthält `src/i18n.py`, `src/response_cache.py`, `src/engines.py`, `src/qwen_backend.py`, `src/pocket_backend.py`, **kein** `CLAUDE.md`
- [ ] **D** Nach dem Push `docker-build.yml` von Hand starten (GPU- **und** CPU-Variante): Das Dockerfile nennt `f5-tts` jetzt ausdrücklich, weil es aus `pyproject.toml` geflogen ist. Erwartet: beide Images bauen, `F5W_TTS_BACKEND=xtts` → `ready` + `/tts` liefert Ton. Qwen/Pocket sind im Image bewusst nicht drin (Plan „Nicht-Ziele“): `F5W_TTS_BACKEND=pocket` einmal setzen und notieren, welche Meldung kommt (sie sollte klar sein und kein Stacktrace-Gewirr)

### R1. Wrapper 0.0.2.0 allein (W, `tools/windows-test.sh`, ohne Spiel)
- [ ] **W** `tools/windows-test.sh xtts f5 moss` (frisch, ohne `--keep`): alle drei → `ready`, `/tts` ≥ 0,5 s. **Achten auf:** Ordnergröße nach **nur** `xtts` deutlich unter den früheren ~18 GB (Lazy-Install), `.venv` enthält kein `f5_tts`, bis f5 an der Reihe ist
- [ ] **W** `tools/windows-test.sh qwen-0.6b qwen-1.7b` → `.venv-qwen` entsteht, `/health` meldet cuda, `/tts` liefert Ton. Erstes Wort hörbar nach ≈ 0,3 s (im NDJSON-Log `first` ansehen)
- [ ] **W** `tools/windows-test.sh pocket pocket-6` → `/health` CPU, beide Modellgrößen sprechen. Mit `--language fr` + `pocket-6` → Rückfall auf 24 Layer mit lesbarer Meldung (`pocket.layers_fallback`)
- [x] **W** (2026-11-14 ✅ über eine **0.0.1.6**-Installation mit MOSS: Altmarker → `deps-classic`/`engine-{f5,xtts,moss}`/`model-*`, kein venv-Neubau, `ready` nach 44 s, Meldungen deutsch mit `key`) **Update auf eine bestehende 0.0.1.9-Installation** (der eigentliche Nutzerweg, die Tests decken nur die Marker-Logik): zuerst 0.0.1.9 mit `xtts` installieren und starten, dann den Stand 0.0.2.0 darüberlegen (`--keep`). Erwartet: **kein** venv-Neubau (torch bleibt), kein erneuter Download des XTTS-Modells; danach Wechsel auf `pocket` → installiert nur Pocket nach
- [ ] **W** Antwort-Cache live: zweimal dieselbe `/tts`-Anfrage (gleicher Text, Stimme, `speed`) → Header `X-Cache: miss`, dann `hit` und die zweite Antwort kommt sofort · dieselbe mit `"no_cache": true` → `bypass`, neu erzeugt · zwei Anfragen **gleichzeitig** → eine `miss`, eine `shared`, und im Log steht nur **ein** `tts request start` · `/health` → `response_cache` mit Einträgen/MB. Web-UI (`/`) erzeugt bei jedem Klick neu (`bypass`)
- [ ] **W** Abbruch mit Refcount, live: zwei gleichzeitige Anfragen, eine per `/cancel/<X-Job-Id>` abbrechen → die andere läuft bis zum Ende · beide abbrechen → die GPU-Last fällt sofort ab (nvidia-smi), danach eine dritte identische Anfrage → `miss` (nichts Halbes gespeichert)
- [ ] **W** NDJSON-Sprache: `--language de` / `fr` / `ja` → Fortschritts- und Statusmeldungen (Installation, Voice-Pack, Modell-Download) in dieser Sprache, jede Zeile hat ein `key`-Feld; Entwickler-Zeilen (`tts request start/done`, `backend selected`) bleiben englisch

### R2. Installer ELI-1.3.2.0 + Plugin 0.19.3.3 (W, im Spiel)
- [ ] **W** Plugin-Dev-Build + Installer 1.3.2.0 (lokaler Build) + Wrapper 0.0.2.0: Backend → EchokrauTTS → Engine-Liste zeigt die **Bewertung je Eintrag** in der Clientsprache (z. B. „Qwen 1.7B (Qualität 5/5, GPU ~5 GB, hohe Last)“), beste zuerst; DE, EN und JA durchschalten → keine abgeschnittenen Einträge
- [ ] **W** Wechsel XTTS → **Qwen 0.6B**: Installer-Konsole zeigt die Nachinstallation **auf Deutsch** und ohne Mojibake; Server wird `ready`; NPC spricht. `EchokrautLocalInstaller.log`: Zeile `ttsBackend=qwen-0.6b | xttsFp16=… | qwenTemperature=…` (kein `qwenXvecOnly` mehr)
- [ ] **W** Qwen-Option „ruhigere Betonung“ umschalten → Instanz startet neu, Log zeigt `--qwen-temperature 0.7`, die Zeile klingt hörbar anders; eine Checkbox „nur Sprecher-Embedding“ gibt es NICHT mehr (entfernt 2026-11-15)
- [ ] **W** Wechsel auf **Qwen 1.7B** → `ready`, spricht; VRAM neben dem Spiel ≈ 5 GB (nvidia-smi)
- [ ] **W** ⚠ **Offener Punkt: FPS-Einbruch mit Qwen.** Gleiche Stelle (z. B. Limsa, 30 s Dialog durchklicken) je mit XTTS, Qwen 0.6B und Qwen 1.7B, FPS-Anzeige an. Notieren: FPS im Leerlauf, minimal während der Erzeugung, GPU-Last. Ergebnis entscheidet, ob die `description`/`load` in `engines.json` **vor dem Taggen** schärfer formuliert werden müssen (z. B. „für Spieler mit Bildratenbegrenzung“)
- [ ] **W** Wechsel auf **Pocket TTS**: GPU-Last bleibt beim Spiel (Server auf CPU), Zeile auf Deutsch · „Modellgröße“ 24 → 6: neues Modell wird geladen, Neustart, schneller; bei 6 Layern ist Schritt 1 **nicht** auswählbar · Schritte 4 → 8 → Neustart, Log zeigt `--pocket-decode-steps 8` · zurück auf 24: die eigene Schrittzahl für 24 ist noch gesetzt
- [ ] **W** Japanischer Client + Pocket → klare Meldung, dass es Japanisch nicht gibt (Konsole in Japanisch lesbar), kein halber Download
- [ ] **W** Sprachclip-Verwaltung: eine Zeile zweimal hintereinander **„Neu erzeugen“** → beide Male eine frische Aufnahme (Wrapper-Log: zwei `tts request start`, `X-Cache: bypass`). Gegenprobe: dieselbe Zeile im Dialog zweimal innerhalb von 5 min anfragen (lokales Speichern **aus**) → beim zweiten Mal kommt der Ton ohne Wartezeit (Cache-Treffer)
- [ ] **W** Schnell durch einen Dialog klicken (Cancel-on-Advance): keine hängende GPU-Last, nächste Zeile kommt normal (Refcount-Abbruch über das Plugin)
- [ ] **W** **Spielstimmen Phase 6 (Index für alle):** Wrapper mit **XTTS** (für die Erkennung) bzw. Qwen. Stimmen → „[+] Spielstimme als Referenz“ aufklappen → „Index für alle vertonten NPCs aufbauen“ (Button direkt nach dem Plugin-Neuladen aktiv, ohne erst eine Zeile zu sprechen), einmal **mit** Whisper, einmal **ohne** (frischer Lauf nach `game_voice_index` leeren): Laufzeit + Anzahl Tokens notieren (Schätzung 1–2 h / 15–30 min), Restzeit plausibel, Spiel bleibt flüssig. Abbrechen → erneut starten setzt fort („… aktuell“ steigt). Danach zeigt der Hinweis „Alle vertonten NPCs haben einen aktuellen Spielstimmen-Index“
- [ ] **W** Nach dem Batch: nie getroffener vertonter NPC spricht **sofort** (ohne Wartezeit) mit Spielstimme; `harvest/game_voice_unmatched_de.json` existiert, Einträge plausibel (Token, Clips, Beispieltexte, Vorschläge)
- [ ] **W** **Epoche nach Inhalt der Zeile:** `dalamud.log` → `Story progress: T_VER…` passt zum Charakter. Y'shtola in einer aktuellen Quest (nach 6.1) → neue Stimme (Birge Tetzner), Log `Game voice for 'Y'shtola': …:Post06100 — story T_VER… (LineQuest|AcceptedMainScenario …)`. **New Game+** Kapitel 6.0 starten, mit Y'shtola reden → alte Stimme (Annette Schreier), Log `…:Pre06100 — story T_VER600 …`. NG+ unterbrechen → wieder neue Stimme
- [ ] **W** Option „Sprecherwechsel erkennen“ mit XTTS → `harvest/game_voice_actor_splits_suggested_de.json` enthält Y'shtola `06100` und Hydaelyn `06000`, **keine** Alphinaud/Thancred-Grenze; der Index nutzt die Vorschläge NICHT
- [ ] **W** Game Data Tools → „Spielstimmen zuordnen“: Bericht laden, einen Token zuordnen → der NPC spricht beim nächsten Satz mit der Spielstimme (kein Neu-Ranking im Log); „Für das Repo exportieren“ schreibt `harvest/game_voice_aliases_for_repo.json` im Format der Ressource
- [ ] **W** „Charakter bearbeiten“ → Feld Spielstimme `-` → NPC spricht mit Paketstimme; leeren → wieder Spielstimme
- [ ] **W** Changelog-Popup 0.19.3.3 erscheint einmal; Version im Fenstertitel

### R3. Mischstände während des Rollouts (W, wichtig wegen der Reihenfolge)
Das alte Plugin 0.19.3.2 liest `RemoteUrls.json` von `main` und `engines.json` am Tag des installierten
Wrappers. Sobald 0.0.2.0 getaggt und eingespielt ist, **sieht das alte Plugin Qwen und Pocket als
wählbar**, und der alte Installer 1.3.1.0 reicht nur `--tts-backend` weiter.
- [x] **W** Plugin **0.19.3.2** (Release) + Installer **1.3.1.0** + Wrapper **0.0.2.0** (per „Nach Updates suchen“, sobald getaggt, oder vorher die Zip von Hand): Update läuft durch, XTTS spricht weiter, Stimmen/Modelle unangetastet
  — 2026-11-24: Release-Zip 0.0.2.0 von Hand ohne `samples/`/`models/` über die bestehende Installation (Teststand, kein echtes 0.0.1.9); XTTS spricht. Das Update-Angebot gibt es erst ab dem Voll-Release (`releases/latest` ignoriert Pre-Releases)
- [x] **W** Im selben Mischstand **Qwen 0.6B** und **Pocket** wählen → starten mit den Wrapper-Standardwerten und sprechen (die Optionen fehlen erwartungsgemäß). Bricht das, muss die Reihenfolge enger werden (ELI-1.3.2.0 + `RemoteUrls.json` direkt nach dem Wrapper-Tag)
  — 2026-11-24: **nicht wählbar, nicht angeboten.** Die Annahme oben stimmt nicht: 0.19.3.2 liest `engines.json` am Tag `RemoteUrls.echokrauTtsVersion` von `main` (dalamud.log: „Loaded 3 TTS engines from …/0.0.1.9/engines.json“), nicht am installierten Wrapper. Das alte Plugin sieht Qwen/Pocket erst, wenn `main` auf 0.0.2.0 zeigt. ⚠ **Deshalb müssen `installerVersion` ELI-1.3.2.0 und `echokrauTtsVersion` 0.0.2.0 im selben `RemoteUrls.json`-Push stehen**: dann lädt das alte Plugin den neuen Installer neu (`InstalledInstallerVersion` ≠), bevor es Qwen/Pocket anbietet, und der Fall „alter Installer + Qwen“ kann nicht eintreten
- [x] **W** Mischstand: Neu erzeugen in der Sprachclip-Verwaltung des **alten** Plugins innerhalb von 5 min liefert den Cache-Treffer, also dieselbe Aufnahme. Bekannt und hinnehmbar, nur bestätigen, dass es nicht schlimmer ist
  — 2026-11-24: bestätigt, den Cache gibt es erst ab Wrapper 0.0.2.0; vorher nicht schlimmer
- [ ] **W** (erst nach dem Voll-Release prüfbar, solange `releases/latest` = 0.0.1.9) Plugin 0.19.3.3 + Wrapper **0.0.1.9**: Qwen/Pocket erscheinen ausgegraut mit „ab Version 0.0.2.0“; „Nach Updates suchen“ bietet 0.0.2.0 an, danach wählbar

### R4. Weitere Plattformen
- [ ] **L** Linux/XIVLauncher: lokale EchokrauTTS-Installation (`launch_linux.sh`) mit **einer** Engine (XTTS oder Pocket) → `ready`, spricht; Spiel schließen beendet den Server (deckt Tester-Punkte `eli-linux-*` ab; Lazy-Install ist dort ebenfalls neu)
- [ ] **A** ⚠ **Offener Punkt: AMD-Test durch den Bekannten** (ROCm unter Linux oder `rocm_win`): `start-qwen-0.6b.sh` bzw. `.bat` → `ready`, `/tts` spricht. Zurückschicken lassen: NDJSON-Log, `/health`, ob die CUDA-Graph-Aufnahme unter HIP abbricht (`capture`/`hipGraph` im Log) und wie hoch der rtf ist. Ohne Ergebnis bleibt „AMD experimentell“ im Changelog stehen; es blockiert das Release nicht
- [ ] **A/W** GPU ohne passendes Backend (z. B. Intel/DirectML oder `gpu_backend=cpu` erzwingen) + Qwen → bricht **vor** dem Download mit klarer Meldung ab (Tester-Punkt `srv-qwen-no-gpu`)

### R5. Plugin-Themen ohne Wrapper-Bezug (Tester-Punkte, hier nur die Prüfart)
- [ ] **W** Spielstimme als Referenz: Ablauf laut Tester-Abschnitt „Game voice reference“; zusätzlich den Prozess-Monitor/Ordner beobachten: keine WAV-Datei aus Spielaudio auf der Platte (`gv-no-files`). Mit **Qwen** und **Pocket** prüfen, dass `/health inline_reference` `true` ist und der NPC mit Spielstimme spricht; mit MOSS (PyTorch) erscheint der Hinweis „nicht unterstützt“
- [ ] **W** Spielstimme, **erster Satz eines neuen NPCs** (Fix 2026-11-15, vorher sprach er mit einer Paketstimme): ohne vorher gebauten Index einen vertonten NPC zum ersten Mal ansprechen → schon die ERSTE Zeile hat die Spielstimme (Start höchstens ~4 s später). `dalamud.log`: `RankAsync: … quick stage X ms, total Y ms` — X notieren (Grundlage für die 4-s-Grenze); kein `not ready after 4 s` bei normalem NPC. Zwei neue NPCs direkt nacheinander: auch der zweite spricht sofort mit Spielstimme
- [ ] **W** Erzählerstimme wählen, Neustart, abwählen, gewählte Stimme deaktivieren (`narrator-*`)
- [ ] **W** Klick in ein Echokraut-Fenster während eines Dialogs schaltet nicht weiter, bei HUD-Größe 100 % **und** 150 % (`ui-click-no-advance`)
- [ ] **W** Stimmen-Dropdown im Dialog nach Hinzufügen/Entfernen einer Stimme und nach Änderung in der Sprachclip-Verwaltung (`play-dialog-voice-dropdown-*`)
- [ ] **W** Engine-Wechsel AllTalk ↔ EchokrauTTS beendet die alte (Task-Manager: kein zweiter Python-Prozess, VRAM frei) (`be-switch-stops-old`)
- [ ] **W** Langsame Quelle: Qwen/XTTS künstlich ausbremsen (zweites Spiel/Benchmark auf der GPU) → Zeile spielt am Stück statt zu stottern (`play-slow-gpu-no-glitch`)

### R6. Nach dem Taggen (je Schritt der Release-Reihenfolge)
- [ ] Nach Plugin 0.19.3.3: `RemoteUrls.json` um `gameVoiceAliasesUrl` und `gameVoiceActorSplitsUrl` (raw `main`, `Echokraut/Resources/…`) ergänzen → `dalamud.log` ohne „Remote … unavailable“
- [ ] Wrapper 0.0.2.0 getaggt → `raw.githubusercontent.com/RenNagasaki/Echokrautts/0.0.2.0/engines.json` liefert die Datei mit den Bewertungen; Docker-Release-Workflow grün
- [ ] ELI-1.3.2.0 hochgeladen → Zip-Name passt zu `installerUrl`; nach `RemoteUrls.json` (installerUrl/Version **und** echokrauTtsUrl/Version 0.0.2.0) lädt das Plugin den neuen Installer beim nächsten Start
- [ ] Plugin 0.19.3.3 aus dem Repository installiert (kein Dev-Build): Quick-Smoke + R2 Engine-Liste + ein Qwen-Satz

---

## 1. Load & Lifecycle
- [ ] Frischer Config (Config-JSON gelöscht) + eingeloggt → **FTU öffnet** (HandleStartup)
- [ ] Frischer Config + Spielstart am Login-Screen → **FTU öffnet nach dem Einloggen** (OnLogin)
- [ ] Plugin-Reload im eingeloggten Zustand → kein Crash
- [ ] Nach Versions-Bump (bestehender Config, FirstTime bereits durch) → **Changelog-Popup** erscheint einmal
- [ ] Commands: `/ek`, Toggle-Command, `/ekfirst`, Kurz-Alias
- [ ] Plugin-Unload → DB-Datei freigegeben/löschbar

## 2. First-Time-Wizard (`/ekfirst`)
- [ ] Engine-Selektor vorhanden, **EchokrauTTS default-hervorgehoben**; AllTalk↔EchokrauTTS wechselt Highlight
- [ ] Mode-Buttons (Local/Remote/Audio Files); `Next`-Gate sperrt bis gültig
- [ ] Step 1 zeigt **nur** die Sektionen der gewählten Engine
- [ ] Step 2 Summary korrekt; „Test connection" liefert Ergebnis
- [ ] Abschluss → Wizard kommt nicht erneut; Changelog stapelt sich nicht dahinter

## 3. Backend-Tab (pro Engine × Modus)
- [ ] Engine-Dropdown AllTalk ↔ EchokrauTTS
- [ ] Modus Local/Remote/None → richtige Sektion sichtbar; **überlebt Fenster-Close+Reopen** (beide Engines)
- [ ] EchokrauTTS: **Engine-Dropdown** (aus `engines.json`, mit Bewertung); engine-eigene Optionen (XTTS-FP16 nur mit GPU, Qwen, Pocket) erscheinen nur bei ihrer Engine; je Toggle → Local-Instanz **startet neu**
- [ ] Local: Install/Reinstall/Start/Stop → **kein Game-Freeze**; Status/Progress aktualisiert
- [ ] Remote: URL-Feld + „Test" → Ergebnis-Label ins richtige Feld
- [ ] Backend-Status-Anzeige (z. B. im Voice-Clip-Manager) spiegelt echte Erreichbarkeit (online/offline/„Audio Files Only")

## 4. Install-Flows (Local — nur wenn Install-Pfad im Release berührt)
- [ ] AllTalk-Local-Install läuft durch → `alltalk_tts/voices/` mit dem Stimmenpaket gefüllt
- [ ] EchokrauTTS-Local-Install: Wrapper-Download+Entpacken → Bootstrap install+serve → Ready; **Konsole lesbar** (kein Mojibake, Fortschritt nicht rot); `echokrautts/samples/` gefüllt (Stimmenpaket inkl. `.txt`-Transkripte)
- [ ] Engine-Wechsel (AllTalk↔EK / XTTS↔F5) kopiert Voices bzw. startet korrekt neu

## 5. Kern-Sprachausgabe
- [ ] Dialog (AddonTalk) vertont
- [ ] Battle-Talk vertont
- [ ] Spieler-Auswahlen + Cutscene-Auswahlen vertont
- [ ] Bubbles vertont (3D)
- [ ] Chat vertont je aktiviertem Kanal (3D, nur nahe Spieler hörbar)
- [ ] Retainer-Dialog vertont
- [ ] Auto-Advance schaltet nach der Zeile weiter; Cancel-on-Advance stoppt bei manuellem Weiterklicken
- [ ] In-Game-Voice-Lautstärke-Slider beeinflusst die Lautstärke

## 6. Voice-Management
- [ ] Voice Selection: Suche (Geschlecht/Volk/Name/Voice); NPC-Stimme neu zuweisen; **bleibt nach Reopen erhalten**
- [ ] Voice Clip Manager: großen NPC-Baum scrollen (**kein Crash**); Detail-Fenster öffnen; per-NPC Generate/Delete/Edit
- [ ] Phonetik-Korrekturen hinzufügen/ändern/löschen → wirkt bei Generierung
- [ ] „Reload voices"

## 7. Storage / Sync
- [ ] Lokales Speichern an → gleiche Zeile erneut anfragen → lädt von Disk (keine Neugenerierung)
- [ ] (falls genutzt) Google-Drive Up-/Download

## 8. Game Data Tools
- [ ] Dialog-Harvest läuft
- [ ] Stimmenpaket-Download (Progress-Bar, `.txt` neben jeder `.wav`, `LICENSE.txt`/`ATTRIBUTION.csv` vorhanden)

## 9. UI-Politur / Regression
- [ ] Alle Dropdowns öffnen/auswählen ohne Crash (Post-KTK)
- [ ] Slider, Tabs, Pagination bei 100+-Listen
- [ ] Hell- **und** Dunkel-Theme: Labels/Farben lesbar
- [ ] Fenster clampen am Bildschirmrand (≥50% sichtbar), wenn rausgezogen
- [ ] **Kein Freeze bei irgendeinem Button**

## 10. Lokalisierung
- [ ] DE rendert sauber; keine fehlenden Keys (englischer Fallback) in sichtbarer UI (ggf. FR/JP stichprobenartig)

---

## ⚠ Extra aufpassen (historisch gebrochen)
- **Native Dropdowns** nach KamiToolKit-Updates — v. a. das **Stimmen-Dropdown im Dialog-Toolbar** (mehrfacher Wechsel crashte).
- **Backend-Sektion-Sichtbarkeit** nach Config-Fenster Close+Reopen.
- **Plugin-Load im eingeloggten Zustand** („Not on main thread" → Load-Fehler).
- **DB-Datei-Lock** nach Unload (musste früher Spiel-Neustart).
- **Local-Install-Konsole**: Encoding-Mojibake + rot markierter Fortschritt.
- **Stop/Install-Buttons**: kurzer Game-Freeze (blockierendes I/O auf UI-Thread).
