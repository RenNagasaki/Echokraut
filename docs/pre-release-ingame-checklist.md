# Echokraut — Pre-Release In-Game-Checkliste

Vor jedem Update-Release (Tag/GitHub-Release) einmal im Spiel durchgehen. Zwei Backends
(**AllTalk**, **EchokrauTTS**), je **Local / Remote / Audio Files Only**. Schwerpunkt: die
historisch fragilen Native-UI-Stellen + die eigentliche Sprachausgabe.

> Nicht jedes Release braucht alle Punkte — aber **Abschnitt ⚡ Quick-Smoke immer**, und alles
> was der aktuelle Release-Diff berührt. Die „Extra aufpassen"-Liste unten sind die Stellen, die
> in der Vergangenheit gebrochen sind.

---

## 0. Vor dem Spiel (Build/CI)
> `tools/release/preflight.sh` prüft die Punkte dieses Abschnitts (außer SonarQube) sowie R0/R6 in einem Lauf, nur lesend.
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
  - E2E `20261125-180742` (`ui:commands`): `/ek` öffnet und schließt die **Sprachclip-Verwaltung** (so registriert: „Opens the Voice Clip Manager“), `/ekconfig` das Config-Fenster, `/ekfirst` den Wizard, `/ekdata` die Game Data Tools. Punkt nennt fürs Config-Fenster den falschen Befehl → Text klären, dann abhaken
- [ ] Einen NPC ansprechen → Dialog wird vertont
- [ ] Stimme im Dialog-Toolbar-Dropdown **2–3× hintereinander** wechseln → **kein Crash**
- [ ] Config → Backend: Fenster schließen + wieder öffnen → **Backend-Sektion noch da** (beide Engines)
  - E2E `20261125-180742` (`ui:backend-reopen`): EchokrauTTS Local, Abschnitt aufgeklappt: 25 Labels nach Schließen + Öffnen identisch, aufgeklappt geblieben, nur die xtts-Optionen. Offen: AllTalk (Engine-Wechsel hat Seiteneffekte, B7)
- [ ] Plugin entladen → `echokraut.db` (+ `-wal`/`-shm`) **löschbar** ohne Spiel-Neustart

---

## 🎯 Release-Testplan: Plugin 0.19.3.3 · Installer ELI-1.3.3.0 · EchokrauTTS 0.0.2.0

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
  - E2E `20261125-133923` (Plugin-Weg, Test-Wrapper fd6a205): `f5_tts` vor f5 **nicht** in `.venv`, Root 20,8 → 22,5 GB beim Wechsel auf f5. Offen: Ordnergröße nach *nur* xtts (Lauf begann mit qwen) und `windows-test.sh` selbst
- [ ] **W** `tools/windows-test.sh qwen-0.6b qwen-1.7b` → `.venv-qwen` entsteht, `/health` meldet cuda, `/tts` liefert Ton. Erstes Wort hörbar nach ≈ 0,3 s (im NDJSON-Log `first` ansehen)
  - E2E `20261125-133923`: `.venv-qwen` da, `/health` cuda (1.7B + 0.6B), Ton. Erste 16 KB nach 1,2 s / 0,5 s (Plugin-Messung, nicht NDJSON `first`) → 0,3 s noch offen
- [x] **W** (2026-11-26 ✅ E2E `20261125-154801` beide Größen + `tools/e2e/wrapper-cli.sh` Lauf `wrapper-cli-20261126-101029`: `pocket6-fr` → `pocket.layers_fallback` auf Französisch, `/health` pocket/cpu, `/tts` fr 3,5 s Ton) `tools/windows-test.sh pocket pocket-6` → `/health` CPU, beide Modellgrößen sprechen. Mit `--language fr` + `pocket-6` → Rückfall auf 24 Layer mit lesbarer Meldung (`pocket.layers_fallback`)
  - E2E `20261125-154801` (Installer ELI-1.3.2.0): Pocket 24 und 6 Layer sprechen über das Plugin (erste 16 KB 2,2 s / 1,3 s); `--language fr`-Rückfall noch offen (B10)
  - B10 `wrapper-cli-20261126-101029`: „Pocket TTS n'a pas de modèle à 6 couches pour 'fr', utilisation de 24 couches“, danach `ready` nach 36 s und französischer `/tts`
- [x] **W** (2026-11-14 ✅ über eine **0.0.1.6**-Installation mit MOSS: Altmarker → `deps-classic`/`engine-{f5,xtts,moss}`/`model-*`, kein venv-Neubau, `ready` nach 44 s, Meldungen deutsch mit `key`) **Update auf eine bestehende 0.0.1.9-Installation** (der eigentliche Nutzerweg, die Tests decken nur die Marker-Logik): zuerst 0.0.1.9 mit `xtts` installieren und starten, dann den Stand 0.0.2.0 darüberlegen (`--keep`). Erwartet: **kein** venv-Neubau (torch bleibt), kein erneuter Download des XTTS-Modells; danach Wechsel auf `pocket` → installiert nur Pocket nach
- [x] **W** Antwort-Cache live: zweimal dieselbe `/tts`-Anfrage (gleicher Text, Stimme, `speed`) → Header `X-Cache: miss`, dann `hit` und die zweite Antwort kommt sofort · dieselbe mit `"no_cache": true` → `bypass`, neu erzeugt · zwei Anfragen **gleichzeitig** → eine `miss`, eine `shared`, und im Log steht nur **ein** `tts request start` · `/health` → `response_cache` mit Einträgen/MB. Web-UI (`/`) erzeugt bei jedem Klick neu (`bypass`)
  — 2026-11-25 E2E `20261125-171901` (Stage `wrapper-http:cache`, xtts cuda; ebenso `170203`, `171044`): miss → hit (gleiche Bytes) → `no_cache` bypass → hit, zwei gleichzeitig → miss + shared (gleiche Bytes), je Generierung genau ein `tts request start`, `/health` `response_cache` hits +2 / shared +1 / misses +2 / bypassed +1; die Testseite `/` schickt `no_cache: true` (Seiteninhalt, nicht geklickt). „Sofort“ belegt `repeat-hit` (erste Bytes 0 ms beim Treffer); `entries`/`mb` gelesen, nicht bewertet
- [x] **W** Abbruch mit Refcount, live: zwei gleichzeitige Anfragen, eine per `/cancel/<X-Job-Id>` abbrechen → die andere läuft bis zum Ende · beide abbrechen → die GPU-Last fällt sofort ab (nvidia-smi), danach eine dritte identische Anfrage → `miss` (nichts Halbes gespeichert)
  — 2026-11-25 E2E `20261125-171901` (Stage `wrapper-http:cancel`, xtts cuda): Paar 1 — `/cancel` des ersten → der zweite bekommt den Clip bis zum Ende (2,7 MB, Generierung `done`, `tts request done`); Paar 2 — beide abgebrochen → Generierung `cancelled`, kein `tts request done`, `running` 0, GPU-Mittel 41 % → 19 % (nvidia-smi aus der Stage); dritte identische Anfrage → `miss`
- [x] **W** (2026-11-26 ✅ `de` E2E `20261125-154801`, `fr`/`ja` B10 `wrapper-cli-20261126-102448` auf dem Wrapper-Arbeitsstand mit dem `FatalError`-Fix: auch die fatalen Zeilen tragen `key`) NDJSON-Sprache: `--language de` / `fr` / `ja` → Fortschritts- und Statusmeldungen (Installation, Voice-Pack, Modell-Download) in dieser Sprache, jede Zeile hat ein `key`-Feld; Entwickler-Zeilen (`tts request start/done`, `backend selected`) bleiben englisch
  - E2E `20261125-154801` (Installer ELI-1.3.2.0), nur `de`: 397 NDJSON-Zeilen, 150 mit `key`, 29 deutsche Meldungen, 0 Mojibake im `EchokrautLocalInstaller.log`; `fr`/`ja` offen (B10)
  - B10 `wrapper-cli-20261126-101029`: `fr` und `ja` je 12 Texte mit `key`, alle in der Sprache, 0 Mojibake. **Offen:** fatale Bootstrap-Fehler (`FatalError`, z. B. `boot.unsupported_device`, `boot.model_failed`) kommen **ohne `key`** an (Wrapper-Befund, WORKLOG)
  - B10 `wrapper-cli-20261126-102448` (20/0, Wrapper mit Fix): `qwen-cpu` fatal mit `key` `boot.unsupported_device`, `pocket-ja` mit `boot.unsupported_language`, alle Texte in `de`/`ja`/`fr`

### R2. Installer ELI-1.3.3.0 + Plugin 0.19.3.3 (W, im Spiel)
> ELI-1.3.2.0 (Pre-Release 2026-11-25, `ce270cf`) wurde nie in `RemoteUrls.json` auf `main` genannt; ELI-1.3.3.0 = 1.3.2.0 + Fehlerdatei (`7ca20f0`). Die E2E-Belege mit 1.3.2.0 unten gelten für 1.3.3.0 weiter, nur die Fehlerdatei ist neu.
- [ ] **W** Plugin-Dev-Build + Installer 1.3.2.0 (lokaler Build) + Wrapper 0.0.2.0: Backend → EchokrauTTS → Engine-Liste zeigt die **Bewertung je Eintrag** in der Clientsprache (z. B. „Qwen 1.7B (Qualität 5/5, GPU ~5 GB, hohe Last)“), beste zuerst; DE, EN und JA durchschalten → keine abgeschnittenen Einträge
  - E2E `20261125-180742` (`ui:engine-dropdown`, DE-Client, Wrapper-Testzip): geöffnete Liste = 6 Einträge in Katalogreihenfolge mit Bewertung, keiner abgeschnitten, Auswahl „XTTS (Qualität 4/5, GPU ~3 GB, mittlere Last)“. Offen: EN und JA (Clientsprache im Spiel umstellen)
- [ ] **W** Wechsel XTTS → **Qwen 0.6B**: Installer-Konsole zeigt die Nachinstallation **auf Deutsch** und ohne Mojibake; Server wird `ready`; NPC spricht. `EchokrautLocalInstaller.log`: Zeile `ttsBackend=qwen-0.6b | xttsFp16=… | qwenTemperature=…` (kein `qwenXvecOnly` mehr)
  - E2E `20261125-154801` (Installer ELI-1.3.2.0): Mode-Zeile je Engine mit `ttsBackend`/`qwenTemperature`, kein `qwenXvecOnly`, 0 Mojibake, deutsche NDJSON-Texte, ready + Pipeline spielt (Reihenfolge dort qwen-1.7b → qwen-0.6b, nicht XTTS → 0.6B); die Konsole selbst nicht angesehen
  - ⚠ Mit **ELI-1.3.1.0** (Lauf `20261125-152750`, GitHub `main` nennt ihn noch) kommen die Werte zwar als Args an, die Mode-Zeile hat aber kein `qwenTemperature`/`pocket*`: der alte Installer reicht die Optionen nicht an den Wrapper weiter. Plugin 0.19.3.3 braucht ELI-1.3.2.0 im selben `RemoteUrls.json`-Push
- [ ] **W** Qwen-Option „ruhigere Betonung“ umschalten → Instanz startet neu, Log zeigt `--qwen-temperature 0.7`, die Zeile klingt hörbar anders; eine Checkbox „nur Sprecher-Embedding“ gibt es NICHT mehr (entfernt 2026-11-15)
  - E2E `20261125-154801` (Installer ELI-1.3.2.0): `SetQwenTemperature(0.7)` (Weg der Checkbox) → Neustart + ready 36 s, Log `qwenTemperature=0.7`, Generierung 0,5 s; zurück auf 0.9 ebenso. „Klingt anders“ = Hören, offen
- [x] **W** Wechsel auf **Qwen 1.7B** → `ready`, spricht; VRAM neben dem Spiel ≈ 5 GB (nvidia-smi)
  - E2E `20261125-133923`: ready, Pipeline spielt; VRAM-Spitze **+6,0 GiB** über dem Spiel allein (5,4 GiB), 0.6B +4,0, xtts +4,7, f5 +1,5, pocket/moss CPU
- [ ] **W** ⚠ **Offener Punkt: FPS-Einbruch mit Qwen.** Gleiche Stelle (z. B. Limsa, 30 s Dialog durchklicken) je mit XTTS, Qwen 0.6B und Qwen 1.7B, FPS-Anzeige an. Notieren: FPS im Leerlauf, minimal während der Erzeugung, GPU-Last. Ergebnis entscheidet, ob die `description`/`load` in `engines.json` **vor dem Taggen** schärfer formuliert werden müssen (z. B. „für Spieler mit Bildratenbegrenzung“)
  - E2E `20261125-133923`: **kein Einbruch messbar, aber das Spiel lief auf ~17 fps gedrosselt** (Fenster im Hintergrund): Leerlauf/Erzeugung qwen-1.7b 17/16,8 fps (ein 319-ms-Frame), qwen-0.6b 17,1/17, xtts 17/17; GPU-Mittel 42/48/25 %. Ohne Drossel wiederholen
- [ ] **W** Wechsel auf **Pocket TTS**: GPU-Last bleibt beim Spiel (Server auf CPU), Zeile auf Deutsch · „Modellgröße“ 24 → 6: neues Modell wird geladen, Neustart, schneller; bei 6 Layern ist Schritt 1 **nicht** auswählbar · Schritte 4 → 8 → Neustart, Log zeigt `--pocket-decode-steps 8` · zurück auf 24: die eigene Schrittzahl für 24 ist noch gesetzt
  - E2E `20261125-154801` (Installer ELI-1.3.2.0): Schritte 8 auf 24 Layern → Neustart, Log `pocketDecodeSteps=8`; 24 → 6 → Neustart, `pocketLayers=6 pocketDecodeSteps=4`, schneller (1,3 s statt 2,2 s erste Bytes); zurück auf 24 → `pocketDecodeSteps=8` (eigene Schritte erhalten). Dropdown-Inhalt („Schritt 1 bei 6 nicht wählbar“) nicht im Spiel geprüft
- [x] **W** Japanischer Client + Pocket → klare Meldung, dass es Japanisch nicht gibt, kein halber Download
  - B10 `wrapper-cli-20261126-101029` (Wrapper-Seite, `--language ja`): bricht mit `model.download_failed` auf Japanisch ab, kein Modell geschrieben. **Offen:** der Grund darin ist Deutsch („Pocket TTS hat kein Modell für Sprache 'ja' …“), und vorher hat Schritt `deps` die 6,3-GB-`.venv` installiert (Wrapper-Befunde, WORKLOG); der Text im Plugin braucht weiter einen JA-Client
  - B10 `wrapper-cli-20261126-102448` (Wrapper mit Fix): bricht nach 12 s **vor** `deps` mit `boot.unsupported_language` ab, ganz Japanisch („Pocket TTS には言語 'ja' のモデルがありません（利用可能: de, en, es, fr, it, pt）…“), keine venv verändert, kein Modell. Wrapper-Seite damit erledigt; **offen bleibt nur der JA-Client im Spiel** (wie die Meldung im Plugin/Installer ankommt)
  - Plugin (2026-11-26, per Tests mit gesetzter Client-Sprache, kein Spiellauf): vorher kam die Meldung nicht an (Installer endet nach Exit 1, sein Fenster schließt; Backend-Tab sprang stumm auf „Start“ zurück). Jetzt: Katalogfeld `languages` (Pocket ohne `ja`, fehlt es am Wrapper-Tag, kommt es aus dem eingebetteten Katalog) → Eintrag „Pocket TTS (in dieser Spielsprache nicht verfügbar)“ gesperrt, Install/Start ausgegraut, `Launch` startet den Installer gar nicht und setzt den lokalisierten Grund als Status. Tests: `TtsEngineAvailabilityTests` (Language_*, IsBlockedByLanguage, ShippedCatalog_*), `EchokrauTtsInstanceServiceTests.Install_PocketOnJapaneseClient_*` / `Install_EngineWithAModelForTheLanguage_*`, `TtsEngineCatalogServiceTests.RemoteCatalogWithoutLanguages_*`, Übersetzungen per `LocAssert`. Nicht gesehen: Darstellung der JP-Beschriftung in der Spielschrift
- [x] **W** (2026-11-26 ✅ im Spiel, DE-Client, Plugin-Dev-Build 7ca20f0 + lokal gebauter Installer 1.3.2.0+7ca20f0 + Wrapper 0.0.2.0, XTTS) **Abbruchgrund im Backend-Tab** (Installer mit `Error.EchokrauTTS.json`, ab dem nächsten ELI-Build): einen Start scheitern lassen, ohne die Client-Sprache umzustellen, z. B. Qwen mit `--gpu-backend cpu` bzw. auf einem PC ohne NVIDIA → statt stumm „Starting...“ → „Start“ steht rot unter dem Installationspfad „EchokrauTTS wurde beendet: <Text des Wrappers>“; `Error.EchokrauTTS.json` neben der exe enthält `key` + `message`. Danach Start mit gültiger Engine → Zeile verschwindet, Datei weg. Stop während „Starting...“ → **keine** rote Zeile
  - Unit-Tests (2026-11-26, kein Spiellauf): Installer `WrapperErrorFileTests` (echte Wrapper-Zeile → Datei mit key/message, andere Zeilen schreiben nichts, späterer Fehler ersetzt), Plugin `EchokrauTtsInstanceServiceTests.ReadLaunchError_*` / `ExitStatus_*` (Grund in jedem Modus, Start ohne Datei → Verweis aufs Log, absichtlicher Stopp → kein Fehler), `NativeEchokrauTtsBuilderTests.StatusLine_*`, Übersetzungen per `LocAssert`. Nicht gesehen: Zeile im Spiel (zweizeilig, Umbruch, Layout der Sektion)
  - Spiel 2026-11-26: Fehlstart per belegtem Port 8765 (Halteprozess bindet zwischen Entladen und Auto-Start, Wrapper meldet `server.port_in_use` fatal vor dem Modell-Laden; kein Sprachwechsel, kein PC ohne NVIDIA nötig). Rot unter dem Installationspfad „EchokrauTTS wurde beendet: Port 127.0.0.1:8765 ist schon belegt — läuft noch eine andere Wrapper-Instanz?“, Layout vom Tester bestätigt; `Error.EchokrauTTS.json` = UTF-8 ohne BOM, `key` `server.port_in_use` + deutsche `message`. Port frei → Start → Zeile weg, ready, Datei weg. Stop während „Starting...“ → keine rote Zeile. ⚠ Dabei im `dalamud.log` ein `[ERR] RunInstance: NullReferenceException` (Zeile 848: `StopInstance` setzt `_instanceProcess = null`, während die Warteschleife es liest; alt seit 2026-07-05, unsichtbar im Tab, WORKLOG)
- [ ] **W** Sprachclip-Verwaltung: eine Zeile zweimal hintereinander **„Neu erzeugen“** → beide Male eine frische Aufnahme (Wrapper-Log: zwei `tts request start`, `X-Cache: bypass`). Gegenprobe: dieselbe Zeile im Dialog zweimal innerhalb von 5 min anfragen (lokales Speichern **aus**) → beim zweiten Mal kommt der Ton ohne Wartezeit (Cache-Treffer)
  - E2E `20261125-171901` (Stage `wrapper-http`, xtts cuda): `IVoiceClipManagerService.GenerateForVoiceClip` (Aufruf des Buttons) zweimal auf einem Wegwerf-Clip → zwei `tts request start`, bypassed +2, kein Treffer. Gegenprobe über den Erzeugungspfad der Dialogzeilen (`IBackendService.GenerateVoice` ohne `NoCache`) zweimal: eine Generierung, Treffer, erste Bytes 359 ms → 0 ms. Offen: dieselbe Zeile wirklich im Talk-Dialog (braucht den NPC)
- [ ] **W** Schnell durch einen Dialog klicken (Cancel-on-Advance): keine hängende GPU-Last, nächste Zeile kommt normal (Refcount-Abbruch über das Plugin)
- [ ] **W** **Spielstimmen Phase 6 (Index für alle):** Wrapper mit **XTTS** (für die Erkennung) bzw. Qwen. Stimmen → „[+] Spielstimme als Referenz“ aufklappen → „Index für alle vertonten NPCs aufbauen“ (Button direkt nach dem Plugin-Neuladen aktiv, ohne erst eine Zeile zu sprechen), einmal **mit** Whisper, einmal **ohne** (frischer Lauf nach `game_voice_index` leeren): Laufzeit + Anzahl Tokens notieren (Schätzung 1–2 h / 15–30 min), Restzeit plausibel, Spiel bleibt flüssig. Abbrechen → erneut starten setzt fort („… aktuell“ steigt). Danach zeigt der Hinweis „Alle vertonten NPCs haben einen aktuellen Spielstimmen-Index“
  - E2E `20261125-203323` (`game-voice-index`, XTTS, DE): Abbrechen nach 5 Paaren → Neustart setzt fort (aktuell 3 → 8, andere Paare gerankt); schneller Lauf ohne Whisper 673 Tokens / 664 Paare in 655 s inkl. Sprecherwechsel-Erkennung, Spiel flüssig (max. Frame 70 ms bei ~17 fps im Hintergrund); mit Whisper nur 3 Paare angestoßen (Kennzeichen geprüft, nicht der volle Lauf). Offen: voller Whisper-Lauf, Button direkt nach Neuladen, Hinweistext — nach dem schnellen Lauf zählt der Hinweis weiter 667 Tokens als veraltet (Whisper verfügbar), Absicht klären
- [ ] **W** Nach dem Batch: nie getroffener vertonter NPC spricht **sofort** (ohne Wartezeit) mit Spielstimme; `harvest/game_voice_unmatched_de.json` existiert, Einträge plausibel (Token, Clips, Beispieltexte, Vorschläge)
  - E2E `20261125-203323` (`game-voice-index:unseen-npc`, `:full`): Thancred, von keinem Teil angesprochen, spricht sofort mit Spielstimme (420 ms, kein Ranking); `game_voice_unmatched_de.json` vollständig (Sprache DE, 354 ohne Zuordnung, 3 mehrdeutig, jeder Eintrag mit Token + Clips). Vorschläge inhaltlich nicht von Hand angesehen
- [ ] **W** **Epoche nach Inhalt der Zeile:** `dalamud.log` → `Story progress: T_VER…` passt zum Charakter. Y'shtola in einer aktuellen Quest (nach 6.1) → neue Stimme (Birge Tetzner), Log `Game voice for 'Y'shtola': …:Post06100 — story T_VER… (LineQuest|AcceptedMainScenario …)`. **New Game+** Kapitel 6.0 starten, mit Y'shtola reden → alte Stimme (Annette Schreier), Log `…:Pre06100 — story T_VER600 …`. NG+ unterbrechen → wieder neue Stimme
- [ ] **W** Option „Sprecherwechsel erkennen“ mit XTTS → `harvest/game_voice_actor_splits_suggested_de.json` enthält Y'shtola `06100` und Hydaelyn `06000`, **keine** Alphinaud/Thancred-Grenze; der Index nutzt die Vorschläge NICHT
  - E2E `20261125-203323` (`game-voice-index:splits`): Datei enthält Y'shtola `06100` und Hydaelyn `06000`, keine Alphinaud-/Thancred-Grenze; neu vorgeschlagen Hythlodaeus `06004`, „Krieger der Dunkelheit“ `05000` (Name kommt mit `[p]`-Platzhalter an, WORKLOG). Offen: dass der Index die Vorschläge nicht nutzt, wurde nicht geprüft
- [ ] **W** Game Data Tools → „Spielstimmen zuordnen“: Bericht laden, einen Token zuordnen → der NPC spricht beim nächsten Satz mit der Spielstimme (kein Neu-Ranking im Log); „Für das Repo exportieren“ schreibt `harvest/game_voice_aliases_for_repo.json` im Format der Ressource
  - E2E `20261125-194150` + `20261125-203323` (`game-voice:alias`, Dienst-Aufrufe statt Fenster): Token gesperrt → Paketstimme, dem eigenen NPC zugeordnet → nächster Satz mit Spielstimme ohne Neu-Ranking; Export im Ressourcenformat (sortiert, ein Eintrag je Token, alle Ressourcen-Tokens drin). Offen: der Weg über das Fenster (Bericht laden, Knöpfe)
- [ ] **W** „Charakter bearbeiten“ → Feld Spielstimme `-` → NPC spricht mit Paketstimme; leeren → wieder Spielstimme
  - E2E `20261125-194150` + `20261125-203323` (`game-voice:character-field`, gleiche Feldlogik `GameVoiceNpcOverride` wie das Fenster): `-` → Paketstimme, eigenes Token → Spielstimme, leer → Spielstimme. Offen: Eingabe im Fenster selbst
- [x] **W** Changelog-Popup 0.19.3.3 erscheint einmal; Version im Fenstertitel
  - E2E `20261125-180742` (`ui:changelog`): `LastSeenChangelogVersion` = 0.19.3.2 → Reload → Popup öffnet sich von selbst, Titel „Neuerungen“ + „Echokraut v0.19.3.3“, nur v0.19.3.3 ungelesen, kein Wizard daneben; „Gelesen“ → Reload → weder Popup noch Wizard

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
- [x] **W** (2026-11-26 ✅ E2E-Lauf `20261126-093222` 22/0/0, Stage `wrapper-mixed`, DE-Client, Plugin-Build mit eingebetteter `RemoteUrls.json` = ELI-1.3.2.0/0.0.2.0, weil `main` noch 0.0.1.9 nennt: echtes Release 0.0.1.9 installiert; Qwen 1.7B/0.6B + Pocket mit „(ab Wrapper-Version 0.0.2.0)“ im Dropdown, Auswahl abgelehnt; erster Klick bietet 0.0.2.0 an, Beschriftung „Wrapper: 0.0.1.9 (neueste: 0.0.2.0)“; zweiter Klick aktualisiert in 52 s, 260 Stimmen bleiben, längster Frame 100 ms; danach Pocket gewählt und gestartet, pocket + qwen-0.6b sprechen in der Engine-Schleife) Plugin 0.19.3.3 + Wrapper **0.0.1.9**: Qwen/Pocket erscheinen ausgegraut mit „ab Version 0.0.2.0“; „Nach Updates suchen“ bietet 0.0.2.0 an, danach wählbar

- [x] **M/W** (2026-11-26 ✅ beide Richtungen im Spiel. Neues Plugin + Installer 1.3.1.0, belegter Port → `dalamud.log` „EchokrauTTS wurde beendet, bevor es bereit war. Der Grund steht in EchokrautLocalInstaller.log.“, im Installer-Log `RuntimeError: port already in use`. **Altes Plugin = Release-Binaries 0.19.3.2** (`latest.zip`, als Dev-Load) + Installer-Stand 7ca20f0 (= ELI-1.3.3.0 ohne Versionsnummer) + Wrapper 0.0.2.0: 9 Argumente (ohne Qwen/Pocket-Optionen) → XTTS ready in 60 s, kein Fehler im Log; belegter Port → Datei mit `key`/`message` geschrieben, altes Plugin meldet nur „Installer exited before … ready“; Port frei → Datei beim nächsten Start weg, ready. Qwen/Pocket im alten Plugin erst nach dem `main`-Push wählbar (Katalog am `main`-Tag), Argumentweg im Installer per Standardwerten. ⚠ 0.19.3.2 schreibt `schema_version = 5`; das neue Plugin läuft v6–v8 idempotent nach — DB danach zeilengleich mit der Sicherung (u. a. `game_voice_index` 1492)) Abbruchgrund, Mischstände: **neues Plugin + alter Installer** (1.3.1.0 / Pre-Release 1.3.2.0 ohne Fehlerdatei) → Start zeigt „… beendet, bevor es bereit war. Der Grund steht in EchokrautLocalInstaller.log.“ (belegt per `ExitStatus_PlainStartWithoutReason_*`, im Spiel offen). **Altes Plugin 0.19.3.2 + neuer Installer** → Datei wird geschrieben und ignoriert, Ready-Datei unverändert (Code: nur zusätzliche Datei, der nächste Start räumt sie weg; im Spiel offen). Wrapper 0.0.1.9 schickt kein `key` → nur `message` (Test `ErrorWithoutKey_*`)

### R4. Weitere Plattformen
- [ ] **L** Linux/XIVLauncher: lokale EchokrauTTS-Installation (`launch_linux.sh`) mit **einer** Engine (XTTS oder Pocket) → `ready`, spricht; Spiel schließen beendet den Server (deckt Tester-Punkte `eli-linux-*` ab; Lazy-Install ist dort ebenfalls neu)
- [ ] **A** ⚠ **Offener Punkt: AMD-Test durch den Bekannten** (ROCm unter Linux oder `rocm_win`): `start-qwen-0.6b.sh` bzw. `.bat` → `ready`, `/tts` spricht. Zurückschicken lassen: NDJSON-Log, `/health`, ob die CUDA-Graph-Aufnahme unter HIP abbricht (`capture`/`hipGraph` im Log) und wie hoch der rtf ist. Ohne Ergebnis bleibt „AMD experimentell“ im Changelog stehen; es blockiert das Release nicht
- [x] **A/W** (2026-11-26 ✅ `tools/e2e/wrapper-cli.sh` Lauf `wrapper-cli-20261126-101029`, `qwen-cpu`: `--gpu-backend cpu` + Qwen 0.6B → nach 12 s „Qwen 0.6B braucht eine NVIDIA-Grafikkarte … erkannt wurde 'cpu'. Wähle XTTS, F5 oder MOSS.“, keine `deps`-/`model`-Stufe, kein Modell, keine Qwen-venv; Intel/DirectML nicht geprüft) GPU ohne passendes Backend (z. B. Intel/DirectML oder `gpu_backend=cpu` erzwingen) + Qwen → bricht **vor** dem Download mit klarer Meldung ab (Tester-Punkt `srv-qwen-no-gpu`)

### R5. Plugin-Themen ohne Wrapper-Bezug (Tester-Punkte, hier nur die Prüfart)
- [ ] **W** Spielstimme als Referenz: Ablauf laut Tester-Abschnitt „Game voice reference“; zusätzlich den Prozess-Monitor/Ordner beobachten: keine WAV-Datei aus Spielaudio auf der Platte (`gv-no-files`). Mit **Qwen** und **Pocket** prüfen, dass `/health inline_reference` `true` ist und der NPC mit Spielstimme spricht; mit MOSS (PyTorch) erscheint der Hinweis „nicht unterstützt“
  - E2E `20261125-194150` (`game-voice:engine-*`): qwen-0.6b, pocket, moss, xtts melden `/health inline_reference` true → verfügbar, NPC spricht mit Spielstimme (erstes Audio 461–844 ms); in keinem Spielstimmen-Teil eine Audio-Datei unter Install-Root, `pluginConfigs` oder `%TEMP%` (Snapshot + Dateiwächter). **MOSS-Annahme veraltet:** das installierte MOSS (ONNX) kann In-Memory-Referenzen; „nicht unterstützt“ gilt nur für MOSS über PyTorch — nicht geprüft, Punkt anpassen
- [x] **W** Spielstimme, **erster Satz eines neuen NPCs** (Fix 2026-11-15, vorher sprach er mit einer Paketstimme): ohne vorher gebauten Index einen vertonten NPC zum ersten Mal ansprechen → schon die ERSTE Zeile hat die Spielstimme (Start höchstens ~4 s später). `dalamud.log`: `RankAsync: … quick stage X ms, total Y ms` — X notieren (Grundlage für die 4-s-Grenze); kein `not ready after 4 s` bei normalem NPC. Zwei neue NPCs direkt nacheinander: auch der zweite spricht sofort mit Spielstimme
  - E2E `20261125-194150` + `20261125-203323` (`game-voice:first-line`): Alisaie + Wuk Lamat ohne Index direkt nacheinander, beide erste Zeilen mit Spielstimme; quick stage 846–1225 ms, total 6,2–8,3 s (Whisper), erstes Audio 964–1406 ms
- [ ] **W** Erzählerstimme wählen, Neustart, abwählen, gewählte Stimme deaktivieren (`narrator-*`)
  - E2E `20261125-223048` (`scenarios:narrator`): wählen → Zeile ohne Sprecher nutzt sie, Plugin-Neustart (Selbst-Reload) → Wahl erhalten. Abwählen/Deaktivieren **nicht prüfbar**: das Stimmenpaket (260 WAVs) hat keine `Narrator.wav`, es gibt keinen Standard-Erzähler zum Zurückfallen (Befund, WORKLOG)
- [ ] **W** Klick in ein Echokraut-Fenster während eines Dialogs schaltet nicht weiter, bei HUD-Größe 100 % **und** 150 % (`ui-click-no-advance`)
- [ ] **W** Stimmen-Dropdown im Dialog nach Hinzufügen/Entfernen einer Stimme und nach Änderung in der Sprachclip-Verwaltung (`play-dialog-voice-dropdown-*`)
- [ ] **W** Engine-Wechsel AllTalk ↔ EchokrauTTS beendet die alte (Task-Manager: kein zweiter Python-Prozess, VRAM frei) (`be-switch-stops-old`)
  - E2E `20261125-223048` (`scenarios:alltalk-off`): EchokrauTTS → AllTalk stoppt EchokrauTTS (Prozess weg, Port zu, VRAM 9430 → 5705 MiB). Richtung AllTalk → EchokrauTTS mit laufendem AllTalk offen (AllTalk nicht im E2E-Lauf)
- [ ] **W** Langsame Quelle: Qwen/XTTS künstlich ausbremsen (zweites Spiel/Benchmark auf der GPU) → Zeile spielt am Stück statt zu stottern (`play-slow-gpu-no-glitch`)

### R6. Nach dem Taggen (je Schritt der Release-Reihenfolge)
- [ ] Nach Plugin 0.19.3.3: `RemoteUrls.json` um `gameVoiceAliasesUrl` und `gameVoiceActorSplitsUrl` (raw `main`, `Echokraut/Resources/…`) ergänzen → `dalamud.log` ohne „Remote … unavailable“
- [ ] Wrapper 0.0.2.0 getaggt → `raw.githubusercontent.com/RenNagasaki/Echokrautts/0.0.2.0/engines.json` liefert die Datei mit den Bewertungen; Docker-Release-Workflow grün
- [ ] ELI-1.3.3.0 hochgeladen (VirusTotal sauber) → Zip-Name passt zu `installerUrl`; nach `RemoteUrls.json` (installerUrl/Version **und** echokrauTtsUrl/Version 0.0.2.0) lädt das Plugin den neuen Installer beim nächsten Start
- [ ] Plugin 0.19.3.3 aus dem Repository installiert (kein Dev-Build): Quick-Smoke + R2 Engine-Liste + ein Qwen-Satz

---

## 1. Load & Lifecycle
- [x] Frischer Config (Config-JSON gelöscht) + eingeloggt → **FTU öffnet** (HandleStartup)
  - E2E `20261125-180742` (`first-time:auto-open`): nach dem Reset-Reload öffnete sich der Wizard von selbst
- [ ] Frischer Config + Spielstart am Login-Screen → **FTU öffnet nach dem Einloggen** (OnLogin)
- [ ] Plugin-Reload im eingeloggten Zustand → kein Crash
- [x] Nach Versions-Bump (bestehender Config, FirstTime bereits durch) → **Changelog-Popup** erscheint einmal
  - E2E `20261125-180742` (`ui:changelog`), wie R2
- [ ] Commands: `/ek`, Toggle-Command, `/ekfirst`, Kurz-Alias
  - E2E `20261125-180742`: `/ek` `/ekconfig` `/ekfirst` `/ekdata` öffnen + schließen ihr Fenster, die sieben `/ekt*`-Schalter kippen und zurück (`ui:commands`). **Fehler (`ui:alias`): `/echokraut` ist nicht registriert**, obwohl die Tester-Checkliste (`inst-cmd`) es verlangt
- [ ] Plugin-Unload → DB-Datei freigegeben/löschbar

## 2. First-Time-Wizard (`/ekfirst`)
- [ ] Engine-Selektor vorhanden, **EchokrauTTS default-hervorgehoben**; AllTalk↔EchokrauTTS wechselt Highlight
  - E2E `20261125-180742` (`ui:wizard`): Schritt 0 zeigt beide Engines; Hervorhebung (gedimmter Knopf) nicht geprüft
- [ ] Mode-Buttons (Local/Remote/Audio Files); `Next`-Gate sperrt bis gültig
- [x] Step 1 zeigt **nur** die Sektionen der gewählten Engine
  - E2E `20261125-180742` (`ui:wizard`): EchokrauTTS + Remote → Titel „Remote-Server“, „Testen“-Knopf da, kein Install-Knopf. AllTalk-Abschnitte nicht geprüft (Engine-Wechsel)
- [x] Step 2 Summary korrekt; „Test connection" liefert Ergebnis
  - E2E `20261125-180742` (`ui:wizard`): „Testen“ gegen die konfigurierte URL → „Verbindung erfolgreich“, Next frei, Zusammenfassung nennt EchokrauTTS
- [x] Abschluss → Wizard kommt nicht erneut; Changelog stapelt sich nicht dahinter
  - E2E `20261125-180742`: nach `first-time` und nach beiden Reloads von `ui:changelog` kein Wizard; nach dem Wizard kein Changelog

## 3. Backend-Tab (pro Engine × Modus)
- [ ] Engine-Dropdown AllTalk ↔ EchokrauTTS
- [ ] Modus Local/Remote/None → richtige Sektion sichtbar; **überlebt Fenster-Close+Reopen** (beide Engines)
  - E2E `20261125-180742` (`ui:backend-reopen`): Local zeigt die lokale, nicht die Remote-Kopfzeile, auch nach Close + Reopen; nur EchokrauTTS Local
- [ ] EchokrauTTS: **Engine-Dropdown** (aus `engines.json`, mit Bewertung); engine-eigene Optionen (XTTS-FP16 nur mit GPU, Qwen, Pocket) erscheinen nur bei ihrer Engine; je Toggle → Local-Instanz **startet neu**
  - E2E `20261125-180742`: Dropdown-Texte wie im Katalog (`ui:engine-dropdown`); bei xtts weder Pocket- noch Qwen-Optionen (`ui:backend-reopen`); Neustart je Toggle: B3
- [ ] Local: Install/Reinstall/Start/Stop → **kein Game-Freeze**; Status/Progress aktualisiert
  - E2E `20261125-133923`: Install (160 s), Stop, Start und 5 Engine-Wechsel samt Nachinstallation: längster Frame 98 ms (Grenze 1000 ms). Offen: Reinstall, Status-Anzeige
- [ ] Remote: URL-Feld + „Test" → Ergebnis-Label ins richtige Feld
- [ ] Backend-Status-Anzeige (z. B. im Voice-Clip-Manager) spiegelt echte Erreichbarkeit (online/offline/„Audio Files Only")

## 4. Install-Flows (Local — nur wenn Install-Pfad im Release berührt)
- [ ] AllTalk-Local-Install läuft durch → `alltalk_tts/voices/` mit dem Stimmenpaket gefüllt
- [ ] EchokrauTTS-Local-Install: Wrapper-Download+Entpacken → Bootstrap install+serve → Ready; **Konsole lesbar** (kein Mojibake, Fortschritt nicht rot); `echokrautts/samples/` gefüllt (Stimmenpaket inkl. `.txt`-Transkripte)
  - E2E `20261125-154801` (Installer ELI-1.3.2.0): `samples/` 260 `.wav`, alle mit `.txt`; Installer-Log ohne Mojibake (die Konsole selbst nicht angesehen)
- [ ] Engine-Wechsel (AllTalk↔EK / XTTS↔F5) kopiert Voices bzw. startet korrekt neu

## 5. Kern-Sprachausgabe
- [ ] Dialog (AddonTalk) vertont
- [ ] Battle-Talk vertont
- [ ] Spieler-Auswahlen + Cutscene-Auswahlen vertont
- [ ] Bubbles vertont (3D)
  - E2E `20261125-223048` (`scenarios:bubble`): übersprungen, am Standort keine Sprechblase in 2 min
- [ ] Chat vertont je aktiviertem Kanal (3D, nur nahe Spieler hörbar)
  - E2E `20261125-223048` (`scenarios:chat-channels`): Say, Shout, Yell, Tell, Party, Alliance, LS, CWLS, FC, Novice je mit Option an in der Queue, mit Option aus nicht (lokal gedruckt, eigene Figur). 3D/„nur nahe Spieler“ offen
- [ ] Retainer-Dialog vertont
- [ ] Auto-Advance schaltet nach der Zeile weiter; Cancel-on-Advance stoppt bei manuellem Weiterklicken
- [x] In-Game-Voice-Lautstärke-Slider beeinflusst die Lautstärke
  - E2E `20261125-223048` (`scenarios:volume`): Spiel-Schieber 100 → BASS-Kanal 1, 50 → 0.5 (Verhältnis 0.50), Schieber danach zurückgesetzt

## 6. Voice-Management
- [ ] Voice Selection: Suche (Geschlecht/Volk/Name/Voice); NPC-Stimme neu zuweisen; **bleibt nach Reopen erhalten**
  - E2E `20261125-223048` (`scenarios:voice-reassign`): neue Stimme über den Dienst gespeichert → nächste Zeile und die nach „Stimmen neu laden“ nutzen sie. Suche und Fenster-Reopen offen
- [ ] Voice Clip Manager: großen NPC-Baum scrollen (**kein Crash**); Detail-Fenster öffnen; per-NPC Generate/Delete/Edit
- [x] Phonetik-Korrekturen hinzufügen/ändern/löschen → wirkt bei Generierung
  - E2E `20261125-223048` (`scenarios:phonetics`): 'Stadt' → 'Zebrafant', geändert → 'Kolibrimaus', gelöscht → 'Stadt' im gesprochenen Text
- [x] „Reload voices"
  - E2E `20261125-223048` (`scenarios:reload-voices`): 260 Stimmen vorher und nachher, gleiche Schlüssel, keine doppelt

## 7. Storage / Sync
- [x] Lokales Speichern an → gleiche Zeile erneut anfragen → lädt von Disk (keine Neugenerierung)
  - E2E `20261125-223048` (`scenarios:local-save`): erste FC-Zeile erzeugt + WAV, zweite von der WAV geladen und abgespielt
- [ ] (falls genutzt) Google-Drive Up-/Download

## 8. Game Data Tools
- [x] Dialog-Harvest läuft
  - E2E `20261125-223048` (`scenarios:harvest`, nur Nicht-Quest-Dialoge): 27 247 Zeilen in 143 s (DE)
- [ ] Stimmenpaket-Download (Progress-Bar, `.txt` neben jeder `.wav`, `LICENSE.txt`/`ATTRIBUTION.csv` vorhanden)
  - E2E `20261125-154801` (Installer ELI-1.3.2.0): 260 `.wav` mit `.txt`, `LICENSE.txt` + `ATTRIBUTION.csv` vorhanden; Progress-Bar nicht angesehen

## 9. UI-Politur / Regression
- [ ] Alle Dropdowns öffnen/auswählen ohne Crash (Post-KTK)
  - E2E `20261125-180742` (`ui:sweep`): jedes Dropdown der eigenen Fenster geöffnet + geschlossen (Backend, Engine, Questtyp in VCM + GDT, Paketsprache), ohne Auswahl, kein Crash. Auswählen nicht (Seiteneffekte)
- [ ] Slider, Tabs, Pagination bei 100+-Listen
  - E2E `20261125-180742` (`ui:sweep`): alle Tabs + Untertabs, Vor/Zurück jeder Pagination, erste VCM-Zeile → Detailfenster; 56 Schritte, längster Frame 116 ms. Offen: Slider, Listen mit 100+ Einträgen (frische E2E-DB)
- [ ] Hell- **und** Dunkel-Theme: Labels/Farben lesbar
- [x] Fenster clampen am Bildschirmrand (≥50% sichtbar), wenn rausgezogen
  - E2E `20261125-180742` (`ui:clamp`): Config, VCM, Game Data Tools, Wizard je 80 % über rechts-unten und links-oben hinaus geschoben → jeweils ≥ 50 % je Achse sichtbar
- [ ] **Kein Freeze bei irgendeinem Button**
  - E2E `20261125-180742` (`ui:sweep`): Frame-Watchdog über den ganzen Sweep, max 116 ms (Grenze 1000 ms); nur seiteneffektfreie Knöpfe

## 10. Lokalisierung
- [ ] DE rendert sauber; keine fehlenden Keys (englischer Fallback) in sichtbarer UI (ggf. FR/JP stichprobenartig)
  - E2E `20261125-180742` (`ui:loc`, DE-Client): **4 englische Texte** – Log-Tabs „General“, „Dialogue“, „Bubbles“ (Labels ohne `Loc.S`) und „Done“ im Install-Balken des Wizards (Statustexte roh). Nur die Texte, die die übrigen Teile sahen

---

## ⚠ Extra aufpassen (historisch gebrochen)
- **Native Dropdowns** nach KamiToolKit-Updates — v. a. das **Stimmen-Dropdown im Dialog-Toolbar** (mehrfacher Wechsel crashte).
- **Backend-Sektion-Sichtbarkeit** nach Config-Fenster Close+Reopen.
- **Plugin-Load im eingeloggten Zustand** („Not on main thread" → Load-Fehler).
- **DB-Datei-Lock** nach Unload (musste früher Spiel-Neustart).
- **Local-Install-Konsole**: Encoding-Mojibake + rot markierter Fortschritt.
- **Stop/Install-Buttons**: kurzer Game-Freeze (blockierendes I/O auf UI-Thread).
