# EchokrautLocalInstaller

Standalone console app (`net10.0`, eigene Versionierung über `ELI-*`-Tags). Wird vom Plugin
gestartet und installiert bzw. betreibt das lokale TTS-Backend — entweder den neuen
**EchokrauTTS**-Wrapper oder das ältere **AllTalk**.

## Dateien

| Datei | Inhalt |
|---|---|
| `Program.cs` | Alles: Argument-Parsing, Downloads, Entpacken, Prozess-Start/-Stopp, Logging |
| `Constants.cs` | Ordnernamen, URLs, Build-Tools-Komponenten, uv-Pinning |
| `EchokrauTtsServeArgs.cs` | Getippte Sicht auf die Kommandozeilenargumente des `serve`/`update`-Modus |
| `GoogleDriveHelper.cs` | Umgeht die Bestätigungsseite bei großen Google-Drive-Downloads |

## Antivirus: dieses Binary steht unter Beobachtung

Das Release wird **automatisch vom Plugin heruntergeladen**. Schlägt Defender an, löscht er es
beim Nutzer — der Fehlalarm ist also ein Totalausfall, kein Schönheitsfehler. Am 01.08.2026 hat
Defender das Release-ZIP als `Trojan:Script/Wacatac.B!ml` entfernt (ML-Heuristik, kein
Signaturtreffer).

Der Auslöser war die Kette **„lädt Archiv aus dem Internet → entpackt → startet unsichtbare
PowerShell mit umgangener Execution Policy"**. Deshalb gilt hier:

- **Kein `powershell.exe` als Zwischenschritt**, und schon gar nicht mit `-ExecutionPolicy Bypass`
  oder `-WindowStyle Hidden` auf ein gerade entpacktes Skript. Der Windows-Pfad startet stattdessen
  `uv.exe` direkt — `EnsureUvAsync()` in `Program.cs` legt `<wrapperFolder>/.uv/uv.exe` an und macht
  damit in C#, was `bootstrap/install_win.ps1` (EchokrauTTS-Repo) für den Standalone-Fall tut.
  **Beide Seiten müssen synchron bleiben** (uv-Asset, `--no-project --python 3.11`).
- **Assembly-Metadaten vollständig halten** (`Company`, `Product`, `Description`, `Copyright`,
  `AssemblyTitle`, `FileVersion`) — ein leeres VERSIONINFO verschlechtert die Heuristik-Bewertung.
- **Nie `PublishSingleFile` mit Kompression, nie Obfuskation** — liest sich für den Scanner als
  Packer. Lose DLLs neben der exe sind die unauffällige Auslieferungsform (in der `.csproj`
  explizit abgeschaltet).
- Vor jedem Release das Asset durch VirusTotal schicken, erst dann taggen.

Noch offene Altlasten im Legacy-AllTalk-Pfad (gleiche Merkmalsfamilie, bisher nicht angefasst,
weil ohne AllTalk-Installation nicht testbar): `cmd.exe /C start "atsetup" /wait …` in der
Installationsroutine, das Durchreichen von conda-Befehlen über stdin an `cmd.exe` in
`StartInstance()` und `CallCMD()`.

Dauerhaft löst das Thema erst eine Code-Signatur (Azure Artifact/Trusted Signing) — SmartScreen-
Reputation sammelt sich pro Zertifikat, unsigniert startet jeder Build wieder bei null.

## Fallstricke

- Der Prozess wird vom Plugin gestartet **und beendet**; `ProcessExit` räumt Kind-Prozesse ab
  (`StopInstall`/`StopInstance`). Neue Kind-Prozesse dort mit abmelden.
- `updateechokrautts` erhält Nutzerdaten: `PRESERVEDECHOKRAUTTSFOLDERS` (`samples/`, `models/`)
  überleben ein Wrapper-Update, eine Neuinstallation nicht.
- `bootstrap.py` besitzt `.venv` allein. `uv run` deshalb **immer** mit `--no-project`, sonst
  legt uv aus der `pyproject.toml` des Wrapper-Ordners ein eigenes `.venv` an und überschreibt
  die gepinnte torch-Version.
- Log liegt neben der exe (`EchokrautLocalInstaller.log`), wird bei jedem Start fortgeschrieben.
