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
- [ ] EchokrauTTS: **XTTS/F5-Dropdown**; **FP16-Checkbox** (nur mit GPU sichtbar); je Toggle → Local-Instanz **startet neu**
- [ ] Local: Install/Reinstall/Start/Stop → **kein Game-Freeze**; Status/Progress aktualisiert
- [ ] Remote: URL-Feld + „Test" → Ergebnis-Label ins richtige Feld
- [ ] Backend-Status-Anzeige (z. B. im Voice-Clip-Manager) spiegelt echte Erreichbarkeit (online/offline/„Audio Files Only")

## 4. Install-Flows (Local — nur wenn Install-Pfad im Release berührt)
- [ ] AllTalk-Local-Install läuft durch → `alltalk_tts/voices/` mit Starter-Set gefüllt
- [ ] EchokrauTTS-Local-Install: Wrapper-Download+Extract → Bootstrap install+serve → Ready; **Konsole lesbar** (kein Mojibake, Fortschritt nicht rot); `echokrautts/samples/` gefüllt (**5 Samples/Stimme + `.txt`-Transkripte**)
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
- [ ] Voice-Starter-Set-Extraktion (Progress-Bar, 5 Samples/Stimme, `.txt` neben jeder `.wav`)

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
