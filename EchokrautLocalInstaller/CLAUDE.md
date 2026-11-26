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
| `WrapperRestartPolicy.cs` | Neustart des Wrappers nach fatalem GPU-Fehler (Exit-Code 75), max. 3 in 15 min |
| `WrapperErrorFile.cs` | Abbruchgrund für das Plugin: `Error.EchokrauTTS.json` neben der exe |
| `WineLaunch.cs` | Linux: Datei-Protokoll zum nativen Wrapper, Pfadübersetzung über Wines `wine_get_unix_file_name` |
| `launch_linux.sh` | Linux: native Startrampe (EmbeddedResource, wird zur Laufzeit in `echokrautts/.linux-run/` geschrieben) |

Tests: `EchokrautLocalInstaller.Tests/` (xUnit, `InternalsVisibleTo`), läuft mit `node .claude/verify.js`.

## Linux = der Installer läuft in Wine

Unter Linux laufen Spiel, Plugin **und diese exe** in Wine (XIVLauncher); `isWindows=False` heißt
also „Windows-exe in Wine", nie „natives .NET". Wine startet ein Linux-Programm nur über
`start.exe /unix` und gibt **kein Prozess-Handle** zurück — `Process.Start("/bin/bash")` endet in
„No process is associated with this object" (so sah jeder Linux-Lauf bis ELI-1.3.0.0 aus).

Darum (seit ELI-1.3.1.0) der EchokrauTTS-Weg unter Linux:
- Download/Entpacken wie unter Windows (Datei-IO funktioniert in Wine).
- `RunEchokrauTtsUnderWine` schreibt `launch_linux.sh` + `args` nach `echokrautts/.linux-run/`
  und startet `start.exe /unix /bin/bash <script> <runFolder>` (Pfade per `WineLaunch.ToUnixPath`).
- Danach nur noch Dateien: `out.log` (wird mitgelesen → `HandleEchokrauTtsNdjson` → Ready-Datei),
  `pid`, `heartbeat` (exe berührt sie alle 2 s, solange das Spiel lebt; > 30 s alt → Skript beendet
  den Server), `stop` (Stopp-Wunsch), `exit` (Exit-Code, zuletzt geschrieben).
- **Kein `--parent-pid`** an den Wrapper: das wäre eine Wine-PID, sein Watchdog fände sie nicht
  und beendete sofort. Der Heartbeat ersetzt ihn (`BootstrapArguments(includeParentPid: false)`).
- Das Skript entfernt CRLF aus `install_linux.sh` (der Wrapper-Zip wird unter Windows gebaut).
- **AllTalk unter Linux ist bewusst gesperrt** (`AllTalkLinuxUnsupported`): Legacy, die alten
  `/bin/bash`-Zweige in `Install`/`StartInstance` sind dadurch unerreichbar.
- Ungetestet auf echter Linux-Hardware bis zum ersten Testerlauf (Checkliste `eli-linux-*`).

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

## Neustart nach fatalem GPU-Fehler (2026-11-12)

Nach `CUDA error: device-side assert triggered` ist der CUDA-Kontext des ganzen Wrapper-Prozesses
tot; der Wrapper beendet sich deshalb selbst mit **Exit-Code 75**
(`Constants.ECHOKRAUTTS_EXIT_RESTART_REQUIRED`, Gegenstück `EXIT_RESTART_REQUIRED` in
`echokrautts/wrapper/src/engine.py` — beide synchron halten). `StartEchokrauTts` läuft dafür in
einer Schleife über `RunEchokrauTtsOnWindows` / `RunEchokrauTtsUnderWine` (beide liefern den
Exit-Code) und fragt `WrapperRestartPolicy`:
- Neustart **nur** bei 75 und nur, wenn kein `StopInstance` lief (`StopRequested`) — jeder andere
  Code (0 Stopp, 1 Bootstrap-Fehler, Kill, unbekannt) ist endgültig.
- Höchstens `MaxRestarts` (3) in `Window` (15 min); ein Fehler, der bei jedem Start wiederkommt,
  endet sonst in einer Endlosschleife aus Modell-Ladezeiten.
- Das Plugin merkt davon nichts: sein Prozess ist der Installer, der weiterläuft; die Ready-Datei
  bleibt liegen, der neue Wrapper schreibt sie beim `ready` erneut. Während des Neuladens (~1 min)
  scheitern Anfragen am nicht erreichbaren Port.
- Braucht ein **Installer-Release** (`ELI-*`) und ein Wrapper-Release, um beim Nutzer anzukommen.

## Fallstricke

- Der Prozess wird vom Plugin gestartet **und beendet**; `ProcessExit` räumt Kind-Prozesse ab
  (`StopInstall`/`StopInstance`). Neue Kind-Prozesse dort mit abmelden. `Process.HasExited` wirft
  bei nie gestarteten Prozessen — immer über `IsRunning(process)` prüfen.
- `updateechokrautts` erhält Nutzerdaten: `PRESERVEDECHOKRAUTTSFOLDERS` (`samples/`, `models/`)
  überleben ein Wrapper-Update, eine Neuinstallation nicht.
- `bootstrap.py` besitzt `.venv` allein. `uv run` deshalb **immer** mit `--no-project`, sonst
  legt uv aus der `pyproject.toml` des Wrapper-Ordners ein eigenes `.venv` an und überschreibt
  die gepinnte torch-Version.
- Log liegt neben der exe (`EchokrautLocalInstaller.log`), wird bei jedem Start fortgeschrieben.

## Qwen-Optionen (ELI-1.3.2.0, 2026-11-13)
- `EchokrauTtsServeArgs` hat ein optionales Positionsargument mehr: `qwenTemperature` (`args[9]`, Custom-Data
  `args[11]`). Leer = nicht weitergeben → der Wrapper behält seinen eigenen Default (älteres Plugin schickt es
  nicht). Weitergereicht als `--qwen-temperature`; wirkungslos für andere Engines. (`qwenXvecOnly` stand bis
  2026-11-15 davor und ist samt Slot entfernt — die folgenden Positionen sind deshalb um eins vorgerückt;
  geht, weil ELI-1.3.2.0 und Plugin 0.19.3.3 nie veröffentlicht wurden. Der Wrapper ignoriert ein
  verirrtes `--qwen-xvec-only` per `parse_known_args`.) Braucht Wrapper 0.0.2.0 (Engines `qwen-0.6b`/`qwen-1.7b`).
- Dazu `pocketDecodeSteps` (`args[10]` / Custom-Data `args[12]`) → `--pocket-decode-steps`, gleiche Regel (leer = nicht weitergeben).
- Dazu `pocketLayers` (`args[11]` / Custom-Data `args[13]`) → `--pocket-layers` (24/6), gleiche Regel. Das Plugin schickt für 6 Layer nie Schritt 1 (getrennte Werte je Größe); der Wrapper hebt 1 bei 6 Layern zusätzlich auf 4.

## Abbruchgrund ans Plugin (2026-11-26)
- `HandleEchokrauTtsNdjson` schreibt beim Wrapper-Ereignis `error` `Error.EchokrauTTS.json` neben die exe
  (`WrapperErrorFile`, `{"key","message"}`; `message` kommt schon in der Spielsprache, `key` fehlt bei
  Wrapper 0.0.1.9 → leer). Der letzte Fehler gewinnt. Eine Ausnahme in `StartEchokrauTts` schreibt ihren
  Text mit leerem `key` (nach `StopInstance`).
- Gelöscht wird sie beim Beginn von `StartEchokrauTts` und vom Plugin (vor jedem Start, bei Stop).
  **Nicht in `StopInstance`:** `ProcessExit` → `Dispose` → `StopInstance` läuft bei jedem Ende des
  Installers, die Datei wäre weg, bevor das Plugin sie liest. Dateiname im Plugin gespiegelt
  (`EchokrauTtsInstanceService.ErrorFileName`).
- Kommt mit **ELI-1.3.3.0** an (1.3.2.0 war ein Pre-Release ohne die Datei, nie in `RemoteUrls.json`
  auf `main`); ältere Installer schreiben keine Datei, das Plugin
  verweist dann aufs Log.
