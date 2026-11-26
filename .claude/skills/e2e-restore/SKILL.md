---
name: e2e-restore
description: Spielt nach einem destruktiven In-game-E2E-Lauf (Stufen backup/reset) die echte Echokraut-Konfiguration auf dem Gaming-PC zurück - live bei laufendem Spiel (Stufe restore, Selbst-Reload) oder per restore.ps1 bei geschlossenem Spiel. Nur auf ausdrücklichen Wunsch des Users auslösen.
disable-model-invocation: true
---

# E2E-Restore auf dem Gaming-PC

Spielt die echte Konfiguration zurück, die die E2E-Stufe `backup` gesichert hat (Plan
`docs/plans/ingame-e2e.md` › Report and restore). Läuft über ssh (`gaming-pc`), die Schale dort ist
PowerShell 5.1.

**Spiel läuft (Standard):** Live-Restore, kein Neustart.
```bash
tools/e2e/restore.sh --live
```
Schickt einen Auftrag nur mit der Opt-in-Stufe `restore` und folgt ihm bis `done`. Das Plugin prüft das
Backup, stoppt die E2E-Engine, lädt sich neu und spielt im Konstruktor die Sicherung zurück (Hashes vorher
und nachher geprüft). Ergebnis wörtlich melden: `restore pass … files restored` = erledigt; `skip` = lief
schon auf der echten Config; `fail` = Text melden und auf den Weg unten ausweichen (Spiel zu).

**Spiel ist zu (oder Live-Restore schlug fehl):**

1. Dem User sagen: Spiel, XIVLauncher und Dalamud müssen geschlossen sein. **Nie selbst einen Prozess
   beenden.** Wer schon „ist zu“ gesagt hat, braucht nicht erneut gefragt zu werden.
2. Ausführen (aus dem Repo-Wurzelverzeichnis):
   ```bash
   tools/e2e/restore.sh
   ```
   Es lädt `tools/e2e/*.ps1` nach `~\eke2e-tools\` hoch und startet `restore-latest.ps1 -NoPause`. Das
   Skript liest `pluginConfigs\Echokraut-e2e\state.json`. Restauriert wird nur, wenn dort
   `ConfigIsE2e = true` steht. Dann läuft `restore.ps1` aus `BackupDir`, das jede SHA-256 prüft und am
   Ende `state.json` löscht.
3. Ergebnis wörtlich melden:
   - Exit 0 mit „Restored N files“: erledigt.
   - „Nothing to restore“: das Plugin läuft schon auf der echten Konfiguration.
   - Exit 1: ein Prozess läuft noch.
   - Exit 2 oder 3: eine Backup-Datei fehlt oder weicht ab. **Nichts von Hand nachkopieren**, erst
     den User fragen.
4. Die Startmenü-Verknüpfung „Echokraut E2E Restore“ (gleiches Skript, wartet am Ende auf Enter) neu
   anlegen: `tools/e2e/restore.sh --install-shortcut`.
