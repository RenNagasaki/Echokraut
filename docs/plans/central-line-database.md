# Plan: Zentrale Zeilen-Datenbank (eigenes Projekt)

## Status

**Planung, kein Code.** Erstellt 2026-08-10 auf Wunsch des Users. Ersetzt den kollektiven Teil von
`missing-line-reports.md` — der Meldeweg über Issues/Discord wird damit zur Rückfallebene für die
Fälle, die eine Automatik nicht erkennen kann (falsche Zuordnung), siehe „Verhältnis zum anderen
Plan" unten.

Code-Aussagen sind am Repo verifiziert (Datei + Zeile); alles Übrige ist als Annahme markiert.

## Festgelegte Spezifikation (User, 2026-08-10)

1. Dialog startet ⇒ **automatische** Nachricht an den Server mit allen relevanten Infos.
2. Server prüft, ob bekannt; wenn nicht, Eintrag. Beides antwortet **200**.
3. **Kein UI**, läuft im Hintergrund.
4. **Keine Information über den Nutzer** — nur Zeile, Sprache, NPC.
5. Charaktername wird durch **Platzhalter** ersetzt.
6. Neue Zeilen gehen **erst in eine Zwischentabelle**, nach **X Anfragen** in die Haupttabelle.

Löst die heutige „eine Datei je Zeile im Google Drive"-Lösung ab.

## Entschieden: Variante A — Zufalls-GUID je Installation

Punkt 6 („nach X Anfragen befördern") braucht unterscheidbare Absender, sonst befördert **ein**
Absender mit X POSTs beliebigen Text und die Zwischentabelle wäre Verzögerung statt Schutz. **Der
User hat Variante A gewählt** (2026-08-10):

- **`installId` = einmalig erzeugte Zufalls-GUID**, in der Plugin-Konfiguration abgelegt.
- Aus **nichts** abgeleitet: nicht aus Charakter, Account, Hardware, IP oder Spielpfad. Sie enthält
  keine Aussage über die Person und ist nicht rückführbar.
- **Einziger Zweck:** eine Stimme je Installation je Zeile (`UNIQUE(pending_id, install_id)`).
- Nicht mit Spielzeit, Welt oder Ähnlichem verknüpft gespeichert.

Damit bedeutet X **X verschiedene Installationen** — die Bedingung dafür, dass Punkt 6 überhaupt
schützt.

Für die **Ratenbegrenzung** kommt zusätzlich die IP zum Einsatz — flüchtig, nicht mit Zeilen
verknüpft gespeichert. Ohne irgendeine Begrenzung ist der Endpunkt sonst ein offenes Scheunentor.

## Zwei Befunde vorab, die die Ausgangslage verändern

### 1. Der Google-Drive-Upload ist bereits totes Holz

`IGoogleDriveSyncService.UploadVoiceLine` hat **keinen einzigen Aufrufer** im Plugin — die einzigen
Vorkommen sind die Deklaration (`IGoogleDriveSyncService.cs:15`), die Weiterleitung
(`GoogleDriveSyncService.cs:94`) und die Implementierung (`DriveUploadService.cs:34`). Es gibt also
**nichts zu migrieren**: das neue System ist grüne Wiese, und der alte Pfad kann ersatzlos raus.

⚠ **Aber nur der Upload-Pfad.** `IGoogleDriveSyncService` wird weiterhin von `Plugin.cs`,
`AlltalkInstanceService` und `AudioFileService` injiziert, und `DriveLinkHelper` wandelt
Drive-Freigabelinks in Direktdownloads (für Custom-Model/Voices-URLs). Der **Download**-Teil bleibt.

### 2. Die Zeilen-Identität existiert schon dreifach

`VoiceLine.GetFileName` (`DataClasses/VoiceLine.cs`) baut heute
`{Language}_{Gender}_{Race}_{Name}_{VoiceMessageToFileName(RemovePlayerNameInText(Text))}.json`.
Derselbe Hash ist auch der **WAV-Dateiname** und die Melde-ID aus dem Paket-Plan. Wenn der Server
denselben Schlüssel verwendet, hängen zentrale DB, `*.ekpack` und lokale DB **ohne Übersetzungsschicht**
zusammen. Das ist die wichtigste Einzelentscheidung des Entwurfs.

⚠ Voraussetzung: **vor** dem Hashen tokenisieren (`Helper/Functional/PlayerNameTokenizer`), sonst
trägt jeder Nutzer seinen Charakternamen in den Schlüssel und dieselbe Zeile zerfällt in tausend
Varianten. `RemovePlayerNameInText` normalisiert bereits auf `<PLAYERNAME>`.

## Der harte Teil: ein offener Schreib-Endpunkt wird vergiftet

Das ist das zentrale Risiko, nicht die Technik. Ein unauthentifizierter Endpunkt, der beliebigen Text
einem beliebigen NPC zuordnet, wird früher oder später mit Müll oder Beleidigungen gefüllt — und weil
die DB später Sprachausgabe für alle speist, ist der Schaden hoch und öffentlich.

Ein API-Schlüssel im Plugin hilft **nicht**: er steht in der DLL und ist in Minuten extrahiert. Er
hält Zufallsverkehr ab, keinen Angreifer. Das sollte man wissen, bevor man sich darauf verlässt.

**Vorschlag: Konsens statt Vertrauen — dreistufig.**

1. **Serverseitige Plausibilitätsprüfung.** Der Server hält denselben aus den Spieldateien
   extrahierten Bestand, den der Harvest erzeugt (~232.000 Quest- + 26.790 Cutscene- + ~29.000
   Skript-Zeilen). Eine gemeldete Zeile, deren Hash dort vorkommt, ist sofort vertrauenswürdig —
   das ist der Großteil. Zusätzlich: Längen- und Zeichensatzgrenzen je Sprache, NPC-Name muss aus dem
   bekannten Namensindex stammen.
2. **Quarantäne für alles Unbekannte.** Neue Zeilen landen in `pending`, nicht im öffentlichen
   Bestand. Genau diese Zeilen sind der eigentliche Gewinn — sie zeigen die Harvest-Lücken.
3. **Beförderung durch Übereinstimmung.** `pending` → öffentlich, sobald **N verschiedene
   Installationen** exakt denselben `(sprache, npc, texthash)` melden (Vorschlag N=3). Ein Einzelner
   kann damit nichts einschleusen, ohne N Installationen zu fälschen. Das passt exakt zum
   Wunsch „kein Feedback, läuft einfach mit" — der Konsens entsteht von selbst.

Dazu Ratenbegrenzung je Installations-ID und je IP, plus eine Sperrliste.

## Datenschutz — nicht verhandelbar, weil es automatisch läuft

Der Client sendet ohne Zutun des Nutzers. Damit gilt:

- **Kein Charaktername, keine `content_id`, keine Pfade.** Text wird tokenisiert, bevor er den
  Rechner verlässt. Präzedenzfall im Repo: `AudioPackageRules.RelativeEntryPath` hält bewusst den
  Windows-Benutzernamen aus dem Manifest.
- **Installations-ID = zufällige GUID**, aus nichts abgeleitet. Zweck ausschließlich: Konsenszählung
  und Ratenbegrenzung.
- **IP nur flüchtig** für die Ratenbegrenzung, nicht mit Zeilen verknüpft gespeichert.
### Einwilligung — entschieden (User, 2026-08-10)

- **Ein Schalter, zwei Orte, ein Konfigurationswert** (z. B. `Configuration.ShareLinesWithCommunityDb`,
  Standard **`true`**):
  - **Ersteinrichtungsassistent** (`NativeFirstTimeWindow`) — mit einem erklärenden Satz daneben.
  - **Settings → General** (`NativeConfigWindow.BuildGeneralPanel`) — derselbe Wert, damit man ihn
    jederzeit wiederfindet.
- **Kein UI im Betrieb**, wie gewünscht: keine Meldungen, keine Fortschrittsanzeige, keine Fehler im
  Spiel.
- **Changelog erklärt es ausdrücklich**: Standard an, und dass **keine Nutzerdaten** übertragen
  werden.

⚠ **Wichtiges Detail, das leicht übersehen wird: Bestandsnutzer sehen den Assistenten nie.**
Ein neuer Konfigurationswert mit Standard `true` schaltet sich bei jedem bestehenden Nutzer still
ein — die Ersteinrichtung läuft bei denen nicht noch einmal. Der einzige Kanal, der sie zuverlässig
erreicht, ist **der Changelog-Popup**, der nach jedem Update automatisch aufgeht
(`IChangelogService` + `NativeChangelogWindow`, gesteuert aus `Plugin.HandleStartup`/`OnLogin`).

⇒ **Der Changelog-Eintrag ist für Bestandsnutzer nicht Doku, sondern die Aufklärung selbst.** Er muss
deshalb in der Liste weit oben stehen und den Schalter benennen, nicht nur das Feature loben.

### Entwurf für den Changelog-Eintrag

**EN**
```
[Community] Echokraut helps complete the dialogue database - and you can turn it off
  When you talk to an NPC, Echokraut now reports that dialogue line to a central
  database, so lines missing from the plugin's own data can be found and added.
  This is ON by default and runs quietly in the background.

  - Only three things are sent: the dialogue line, its language, and which NPC
    says it. Your character name is replaced by a placeholder before sending.
  - Nothing about you is sent - no character name, no world, no account, no
    play time, no file paths.
  - You can switch it off any time under Settings -> General.
```

**DE** (ASCII-Umschrift wie im Rest der Datei)
```
[Gemeinschaft] Echokraut vervollstaendigt die Dialog-Datenbank - abschaltbar
  Wenn du mit einem NPC sprichst, meldet Echokraut diese Dialogzeile jetzt an eine
  zentrale Datenbank, damit Zeilen gefunden werden koennen, die in den Daten des
  Plugins fehlen. Das ist standardmaessig AN und laeuft still im Hintergrund.

  - Gesendet werden nur drei Dinge: die Dialogzeile, ihre Sprache und welcher NPC
    sie spricht. Dein Charaktername wird vorher durch einen Platzhalter ersetzt.
  - Ueber dich wird nichts gesendet - kein Charaktername, keine Welt, kein Account,
    keine Spielzeit, keine Dateipfade.
  - Du kannst es jederzeit unter Einstellungen -> Allgemein abschalten.
```

## Adresse (User, 2026-08-10)

**`https://echolines.echotools.cloud`**

| | |
|---|---|
| Live-Pfad | `POST https://echolines.echotools.cloud/v1/lines` |
| Harvest-Pfad | `POST https://echolines.echotools.cloud/v1/lines/import` |
| Statusprobe | `GET https://echolines.echotools.cloud/healthz` |
| Admin-Oberfläche | `https://echolines.echotools.cloud/admin` |

### Die API ist bereits live (gemessen 2026-08-10)

Der Dienst läuft. Vom User genannt und von mir nachgeprüft:

| Endpunkt | Zweck | GET | POST |
|---|---|---|---|
| `/healthz` | Statusprobe | **200** `{"status":"ok"}` | 405 |
| `/v1/lines` | **Live-Pfad** — was der Spieler gerade hört | 405 | **200** |
| `/v1/lines/import` | **Harvest-Pfad** — der aus den Spieldateien extrahierte Bestand | 405 | **200** |

`POST /v1/lines` mit leerem Objekt antwortet `{}` bei 200 — die „immer 200, keine Rückmeldung"-Semantik
aus Punkt 2 ist also bereits umgesetzt. Davor steht Cloudflare, HSTS ist gesetzt.

⚠ **Nicht geprüft und bewusst nicht geprüft:** das genaue Anfrage-Schema. Ein POST mit echten
Beispielzeilen würde in die **Produktivdatenbank** schreiben. Das unten skizzierte Schema ist
weiterhin mein Entwurf und muss gegen die tatsächliche Implementierung abgeglichen werden, bevor der
Client gebaut wird.

⇒ `/healthz` ersetzt das von mir vorgeschlagene `/v1/health`: **das ist die Adresse, die der
Reachability-Test verwenden soll** — sie prüft die Erreichbarkeit, ohne Zeilen zu schreiben.

## Zwei Schreibpfade, zwei Bedeutungen — das ist der eigentliche Gewinn

Der `import`-Pfad kam später dazu und verändert den Nutzen des Ganzen erheblich. Die beiden Pfade
tragen **unterschiedliche Aussagen**, und der Server sollte sie deshalb unterscheiden (Spalte
`origin`, Werte `live` / `harvest`):

| Pfad | Aussage | Gewonnen aus |
|---|---|---|
| `/v1/lines` | „diese Zeile ist im Spiel tatsächlich vorgekommen" | Dialog beim Spieler |
| `/v1/lines/import` | „diese Zeile steht in den Spieldateien" | lokaler Harvest |

Erst zusammen beantworten sie die Vollständigkeitsfrage **in beide Richtungen**:

- **live gesehen, aber nicht im Harvest** ⇒ Extraktionslücke. Genau die Frage, um die sich diese
  ganze Session gedreht hat — ab dann beantwortet sie sich laufend von selbst.
- **im Harvest, aber nie live gesehen** ⇒ Inhalt, den die Extraktion findet, den aber niemand
  erreicht. Entweder sehr selten oder tot. Bisher gar nicht feststellbar.

### ⚠ Der Harvest-Pfad braucht andere Mengenregeln als der Live-Pfad

Ein vollständiger Harvest sind **~290.000 Zeilen** (232.000 Quest + 26.790 Cutscene + ~29.000
Skript, gemessen). Das ist eine andere Größenordnung als die paar hundert Zeilen einer Spielstunde:

- **Zerlegen ist Pflicht**, nicht Optimierung. Vorschlag: Blöcke von 1.000–5.000 Zeilen,
  fortsetzbar, mit Pause dazwischen — kein Versand während des Spielens.
- **Der Client muss sich merken, dass er es schon getan hat**, samt Spielversion. Ohne das schickt
  jede Installation bei jedem Start 290.000 Zeilen. Vorschlag: gesendeter Stand = (Spiel-Patch,
  Harvest-Version, Sprache); erneut senden nur nach einem Spiel-Patch oder einer geänderten
  Harvest-Logik.
- **Auslösen nur bewusst**: nach einem abgeschlossenen Harvest, nicht automatisch beim Start.
- **Konsens funktioniert hier besonders gut**: Harvest-Ergebnisse sind deterministisch aus den
  Spieldateien abgeleitet — drei Installationen auf demselben Patch erzeugen exakt dieselben Hashes.
  X=3 ist damit schnell erreicht und trotzdem aussagekräftig.

### Cloudflare davor

- Ratenbegrenzung und Bot-Schutz können teilweise dort liegen statt in der Anwendung.
- Der Client muss **429 und 5xx still schlucken** und später erneut versuchen — niemals eine Meldung
  im Spiel (Punkt 3). Bei einem Import-Block heißt das: Block wiederholen, nicht den ganzen Import
  verwerfen.

### Die URL gehört in `RemoteUrls.json`, nicht in den Code

Das Plugin hat mit `IRemoteUrlService` + `Resources/RemoteUrls.json` bereits den Mechanismus, mit dem
Adressen **ohne neues Plugin-Release** geändert werden können — inklusive Rückfall auf die
eingebettete Kopie, wenn der Abruf scheitert. Ein hart einkompilierter Endpunkt bedeutet dagegen:
zieht der Dienst um, sind alle installierten Plugins bis zum nächsten Release blind.

⇒ Neues Feld, z. B. `echolinesUrl`, plus der übliche Merge-Fallback in `RemoteUrlsData`.

⚠ **Aber erst eintragen, wenn der Endpunkt antwortet.** `RemoteUrlsReachabilityTests` ruft die in
`RemoteUrls.json` hinterlegten Adressen **wirklich** ab und erwartet Erfolg (`AlltalkUrl`,
`InstallerUrl`, `MsBuildToolsUrl`, `NpcRacesUrl`, … in einer festen Liste). Ein Eintrag, der noch
404 liefert, färbt den Testlauf rot. Zwei saubere Wege:

1. Feld erst mit dem Deployment ergänzen — am einfachsten.
2. Oder den Test für dieses Feld tolerant machen, so wie er es beim GitHub-Releases-Abruf mit
   403/429 schon tut (Ratenbegrenzung sagt nichts über die Richtigkeit der URL aus).

**`GET /healthz` ist die Adresse für den Reachability-Test** — sie existiert bereits und antwortet
`{"status":"ok"}`. **Ein Test, der `POST /v1/lines` aufruft, würde in die Produktivdatenbank
schreiben** — also nicht machen.

## Schnittstelle

Ein Endpunkt, gestapelt, immer 200 — wie gewünscht:

```
POST /v1/lines
{
  "installId": "<guid>",          // NUR bei Variante A; entfällt bei B/C
  "pluginVersion": "0.19.3.1",
  "lines": [
    { "lang": "de", "npc": "Alphinaud Leveilleur", "gender": "Male",
      "race": "Elezen", "npcBaseId": 1000123,
      "text": "Nun denn, -PlayerFirstName-, ...",   // Platzhalter, NIE der echte Name
      "hash": "<VoiceMessageToFileName-Hash>",
      "source": "AddonTalk" }
  ]
}
→ 200 {}   (immer, auch wenn alles schon bekannt war)
```

**Was NICHT im Payload steht** (Punkt 4): kein Charaktername, keine Welt, keine `content_id`, keine
Spielzeit, keine Pfade, keine Hardware-Kennung. `pluginVersion` ist drin, weil ein Formatfehler sonst
nicht zuzuordnen ist — falls auch das zu viel ist, kann es in einen HTTP-Header wandern oder ganz
entfallen.

- **Stapel statt Einzelrequest.** Eine Zeile je Request wäre bei mehreren hundert Zeilen pro
  Spielstunde verschwenderisch und bricht bei jedem Netzwerkhänger. Der Client puffert und sendet
  alle ~60 s oder ab 50 Zeilen.
- **Client-seitige Unterdrückung ist der größte Hebel:** eine lokal persistierte Menge bereits
  gesendeter Hashes. Nach dem ersten Durchspielen sendet ein Nutzer fast nichts mehr. Ohne das
  schickt jeder Nutzer bei jedem Dialog erneut dieselben Zeilen.
- `400` nur bei kaputtem oder zu großem Payload, `429` bei Ratenbegrenzung — der Client ignoriert
  beides und versucht es später erneut. **Nie eine Fehlermeldung im Spiel.**
- Versionierter Pfad (`/v1/`), damit ein Formatwechsel alte Clients nicht bricht.

## Datenmodell (Skizze)

Zwei Tabellen, genau wie in Punkt 6 verlangt — Zwischentabelle und Haupttabelle:

```
-- ZWISCHENTABELLE: alles Neue landet hier zuerst
pending_lines   id, lang, npc_name, gender, race, npc_base_id, text, text_hash,
                first_seen_utc, last_seen_utc, hit_count
                UNIQUE(lang, npc_name, text_hash)

-- Stimmen je Zwischeneintrag (nur Variante A/C)
pending_votes   pending_id, voter_key, seen_utc
                UNIQUE(pending_id, voter_key)   -- eine Stimme je Installation/Quelle

-- HAUPTTABELLE: befördert ab X
lines           id, lang, npc_name, gender, race, npc_base_id, text, text_hash,
                first_seen_utc, promoted_utc, source_count
                UNIQUE(lang, npc_name, text_hash)
```

**Beförderung:** `hit_count` (Variante B) bzw. `COUNT(pending_votes)` (A/C) erreicht X ⇒ Zeile wandert
nach `lines`, Zwischeneintrag wird entfernt. `UNIQUE(pending_id, voter_key)` ist bei A/C die ganze
Konsenslogik — mehr braucht es nicht.

**Beim Eintreffen einer Zeile:** existiert sie in `lines` ⇒ nichts tun (der „schon bekannt"-Fall aus
Punkt 2). Sonst `pending_lines` anlegen oder Zähler/Stimme erhöhen und ggf. befördern. In allen
Fällen 200.

⚠ **`pending_lines` braucht eine Verfallsregel**, sonst wächst sie unbegrenzt mit Einzelmeldungen,
die X nie erreichen. Vorschlag: Einträge ohne neue Stimme seit 12 Monaten werden verworfen — aber
**vorher exportiert**, denn genau diese Einträge sind das interessante Material für Phase 4 (was
kennt der Harvest nicht?).

## Web-UI (Admin) — entschieden

Der Server bekommt eine kleine Weboberfläche für den Betreiber. Drei Aufgaben:

### 1. Manuelles Freigeben unterhalb von X

Zeilen, die X=3 nie erreichen (Randinhalte, alte Saison-Events, seltene NPCs), müssen von Hand
befördert werden können. Liste über `pending_lines`, sortier- und filterbar nach Sprache, NPC,
Stimmenzahl, Alter; je Eintrag **Freigeben** / **Ablehnen**, dazu Mehrfachauswahl.

Abgelehnte Zeilen brauchen einen `rejected`-Zustand statt einer Löschung — sonst meldet der nächste
Client sie sofort wieder und sie stehen erneut in der Liste.

### 2. Massenimport der bisherigen Google-Drive-Dateien

⚠ **Präzisierung zum Format** (am Code geprüft, `DriveUploadService.cs:34-60`): die Dateien heißen
**`.json`**, nicht `.txt` — nur der MIME-Typ beim Upload war `text/plain`. Inhalt ist
`JsonSerializer.Serialize(voiceLine)`, also ein Objekt mit `Gender`, `Race`, `Language`, `Name`,
`Text`. Der Dateiname ist
`{Language}_{Gender}_{Race}_{Name}_{VoiceMessageToFileName(RemovePlayerNameInText(Text))}.json`.

⚠ **Und ein Fallstrick, der den Import sonst still verfälscht:** `Gender`, `Race` und `Language` sind
Enums und wurden von `System.Text.Json` **als Zahlen** serialisiert (kein `JsonStringEnumConverter`
im Pfad). Ändert sich eine Enum-Reihenfolge, zeigen alte Zahlen auf falsche Werte. **Der Dateiname
enthält dieselben Angaben als Klartext** (`Gender.ToString()` usw.) — der Importer sollte deshalb
**den Dateinamen als führend** behandeln und aus dem JSON nur `Text` übernehmen. Bei Abweichung
zwischen beiden: Eintrag in einen Konfliktbericht, nicht raten.

Bedienung: Mehrfachauswahl im Datei-Dialog (viele Dateien auf einmal) oder ein Zip. Ergebnis als
Bericht — importiert / schon bekannt / fehlerhaft, mit Beispielen. Import geht **direkt in `lines`**
(Haupttabelle), nicht in die Quarantäne: das Material stammt vom Betreiber selbst.

### 3. Einblick für Phase 4

Ansicht „gemeldet, aber im Harvest unbekannt" — der eigentliche Nutzen des ganzen Aufbaus.

### ⚠ Damit kommt Authentifizierung ins Spiel

Bis hierhin war der Dienst schreibend-anonym und ansonsten harmlos. **Eine Admin-Oberfläche im
offenen Netz ohne Anmeldung wäre der größte Einzelfehler des Entwurfs** — wer sie findet, kann
beliebige Zeilen freigeben und damit genau die Vergiftung auslösen, gegen die die Quarantäne gebaut
wurde.

Minimum: eigener Pfad (`/admin`), Anmeldung mit einem einzigen Betreiberkonto, Passwort-Hash aus einer
Umgebungsvariablen/Secret, Cookie-Session, keine Registrierung, Ratenbegrenzung auf dem Login. Kein
Nutzerverwaltungs-System — es gibt genau einen Benutzer.

## Technik

- **ASP.NET Core, containerisiert.** Vorbild im Nachbarprojekt: `Echocrowd.Server` (ASP.NET, EF Core,
  `Models`/`Services`/`Migrations`/`Middleware`) — gleicher Autor, gleicher Stack.
- **Datenbank: PostgreSQL** (entschieden). EF Core mit `Npgsql`; Migrationen wie im Nachbarprojekt.
  Datenbank als eigener Container, Volume für die Daten, Zugangsdaten aus Secrets.
- **Repo-Name: `echolines`** (entschieden), unter `gitlab.echotools.cloud/echotools/echolines`,
  GitHub-Spiegel `RenNagasaki/Echolines`.
- **Eigene `Testing/checklist.json`** — Server-Komponenten ohne eigene Tag-Struktur hängen sonst als
  zusätzliche `component` an der Plugin-Checkliste (Regelwerk im übergeordneten `CLAUDE.md`).

### Bau und Auslieferung (entschieden, analog Echokraut)

- **GitHub Action baut das Docker-Image.** Auslöser: Push auf den Hauptbranch und Tags. Image nach
  GHCR (`ghcr.io/rennagasaki/echolines`), getaggt mit Commit-SHA **und** Release-Tag — ein
  `latest`-only-Image macht Rückrollen unmöglich.
- **GitLab CI analog zu `Echokraut/.gitlab-ci.yml`**, dessen Aufbau übernommen wird:
  - Stages `security` → `analysis` → `mirror`
  - `gitleaks` (Secret-Scan, `alpine:latest`)
  - `sonarqube` (`mcr.microsoft.com/dotnet/sdk:10.0`, `dotnet-sonarscanner`, eigener
    `SONAR_PROJECT_KEY`)
  - `mirror-to-github` nur auf dem Hauptbranch, **additiv** mit `git push --all --force` +
    `git push --tags` — **nie `--mirror`**: das löscht auf GitHub Tags, die dort über die Release-UI
    entstanden sind, und zerstört die zugehörigen Releases.
  - ⚠ Die GitHub-Zieladresse wird **fest eingetragen** (`RenNagasaki/Echolines`), nicht aus
    `${GITHUB_USER}` gebaut — der CI-Anmeldebenutzer ist nicht der Repo-Eigentümer, das hat bei
    Echokraut schon einmal auf das falsche Repo gezeigt und 403 geliefert.
- **Reihenfolge der beiden CI-Systeme:** GitLab ist der Ursprung, GitHub der Spiegel. Die
  GitHub-Action läuft also auf dem gespiegelten Stand. Das ist der einfachste Weg, hat aber eine
  Folge: **ein Bau startet erst, nachdem der Spiegel-Job durch ist.** Wer das nicht will, baut das
  Image stattdessen in der GitLab-CI — dann ist die GitHub-Action überflüssig. *(Offene Frage 1
  unten.)*

## Client-Seite (Plugin)

### Umfang: JEDE unvertonte Zeile, ausnahmslos (User, 2026-08-10)

Gemeldet wird **jede Dialogzeile, die das Spiel nicht selbst vertont** — **unabhängig davon**, ob
lokal Audio existiert, ob die Zeile in der lokalen DB steht, ob sie im `*.ekpack` enthalten war oder
ob der Nutzer den NPC stummgeschaltet hat. Nur „was mir fehlt" zu melden wäre wertlos: die zentrale
DB soll den vollständigen Bestand abbilden, nicht die Lücken einer einzelnen Installation.

**Vertonte Zeilen kommen dabei von selbst nicht vor**, ohne dass man dafür etwas prüfen müsste:
`AddonTalkHelper.HandleChange` verlässt sich bei einer vertonten Zeile früh (`voiceNext` ⇒
„Skipping voice-acted line") und ruft den Prozessor gar nicht erst auf. Was `ProcessSpeechAsync`
erreicht, ist damit per Konstruktion unvertont.

### ⚠ Der Aufrufpunkt ist NICHT `LogVoiceClip` — dort gehen Zeilen verloren

Ursprünglich hatte ich `VoiceMessageProcessor.LogVoiceClip` vorgeschlagen. **Das ist falsch.** Am
Code geprüft (`VoiceMessageProcessor.cs`): `LogVoiceClip` steht in Zeile 235, **hinter vier Early
Returns**:

| Zeile | Abbruch | greift im None-Modus? |
|---|---|---|
| 193 | NPC stummgeschaltet (`IsNpcMuted`) | **ja** — keine `HasLiveGeneration`-Bedingung |
| 207 | keine Stimme gesetzt | nein (nur bei Live-Generierung) |
| 214 | Stimmen-Lautstärke 0 | nein (nur bei Live-Generierung) |
| 226 | Endlautstärke ≤ 0 | **ja** — keine Bedingung |

⚠ **Der Kommentar direkt über `LogVoiceClip` behauptet „regardless of mute/volume state" — das
stimmt nicht.** Ein stummgeschalteter NPC oder eine Endlautstärke von 0 lässt die Zeile fallen,
bevor sie überhaupt geloggt wird. (Nebenbefund: damit fehlen diese Zeilen auch der **lokalen** DB —
ein eigenständiger Altfehler, hier nur notiert, nicht Teil dieses Plans.)

⇒ **Der Meldeaufruf gehört an Schritt 3.5, `VoiceMessageProcessor.cs:187`**, direkt nachdem der
NPC aufgelöst und `voiceMessage` gebaut ist und `DialogState.CurrentVoiceMessage` gesetzt wird —
**vor** allen Stumm-, Stimmen- und Lautstärkeprüfungen. Dort liegen NPC, Sprache, Geschlecht, Rasse
und Text bereits vor, und die Zeile ist bereits als sprechbar erkannt (die Textprüfung sitzt
davor, Zeile ~150).

Kleiner Restfall, bewusst in Kauf genommen: **Schritt 1** (`IsBackendAvailable`, Zeile ~122) bricht
ab, wenn ein Nutzer *Local* ohne Installation oder *Remote* ohne URL eingestellt hat. Im
**None-Modus liefert `IsBackendAvailable()` true** (`BackendService.cs:227`) — Paket-Nutzer, also die
wichtigste Gruppe, sind nicht betroffen. Nur fehlkonfigurierte Installationen melden nichts.

**Auch nicht auflösbare Sprecher werden gemeldet.** Zeilen mit `???` oder einem Fakenamen sind
sogar die wertvollsten — genau sie zeigen die Zuordnungslücken. Gesendet wird dann der angezeigte
Sprechername.

### Versandtakt Live-Pfad — entschieden (User, 2026-08-11)

**Alle 60 Sekunden gehen alle noch nicht gesendeten Zeilen raus**, gesammelt in einer Anfrage.

- ⚠ **„Unbekannt" heißt hier ausschließlich „von dieser Installation noch nicht gesendet".** Nicht
  „nicht in der lokalen DB", nicht „kein Audio vorhanden" — das war die Präzisierung von gestern und
  ist hier die entscheidende Lesart.
- **Streuung ±10 s** auf das Intervall. Ohne das schlagen nach einer Server- oder Netzstörung alle
  Clients gleichzeitig wieder auf.
- **Vorzeitiges Senden ab ~200 gepufferten Zeilen** — nicht wegen der Geschwindigkeit, sondern um die
  Anfragegröße zu begrenzen.
- **429 und 5xx**: still schlucken, Backoff verdoppeln, Zeilen im Puffer lassen. Nie eine Meldung im
  Spiel.

### ⚠ Nicht Geschwindigkeit ist das Risiko, sondern Verlust

Ob eine Zeile jetzt oder in 60 Sekunden ankommt, ist für die Datenbank ohne Bedeutung — niemand
wartet darauf. Was wirklich weh tut: **ein reiner Speicherpuffer verliert bei jedem Sitzungsende die
letzte Minute.** Absturz, Plugin-Neuladen, Ausloggen, Spiel beenden — jedes Mal weg. In einem
modifizierten Spiel endet eine Sitzung öfter unsanft, als einem lieb ist, und die letzte Minute vor
einem Absturz ist ausgerechnet die mit dem frischen Inhalt.

Deshalb, unabhängig vom Intervall:

- **Puffer liegt auf der Platte**, nicht nur im Speicher (kleine Datei unter `<localSaveLocation>`).
  Beim Start wird gelesen, was liegen geblieben ist, und mitgeschickt.
- **Beim Entladen ein letzter Versuch**, aber ⚠ **ohne zu blockieren**: `Plugin.Dispose` läuft auf
  dem Framework-Thread, und dort auf einen HTTP-Aufruf zu warten hängt das Spiel beim Ausloggen auf
  (im Repo als Regel notiert: kein `Thread.Sleep` in `Plugin.Dispose`). Was nicht mehr rausgeht,
  bleibt in der Datei und geht beim nächsten Start mit.
- **Erst nach bestätigtem 200 als gesendet markieren** — sonst frisst ein Timeout die Zeilen
  genauso zuverlässig wie ein Absturz.

Mit persistentem Puffer ist das Intervall dann fast beliebig: 60 s ist ein guter Kompromiss aus
wenigen Anfragen und kurzem Nachlauf.

### Harvest-Pfad — bestätigt (User, 2026-08-11)

`/v1/lines/import` wird **ausschließlich nach einem abgeschlossenen Harvest** ausgelöst, nie beim
Start und nie im Hintergrund während des Spielens. Regeln wie oben: blockweise, fortsetzbar,
gemerkt an (Spiel-Patch, Harvest-Version, Sprache).

### Übrige Regeln

- Neuer `ILineSubmissionService`: puffert, dedupliziert lokal, sendet im Hintergrund.
  **Nie auf dem Frame-Thread** — im Repo mehrfach schmerzhaft gelernt (`Windows/CLAUDE.md`
  → „Backend calls must not run on the frame thread").
- **Die lokale „schon gesendet"-Menge bedeutet NICHT „lokal bekannt".** Sie verhindert nur, dass
  dieselbe Installation dieselbe Zeile mehrfach schickt. Ob die Zeile lokal in der DB steht oder
  Audio hat, spielt für den Versand **keine** Rolle.
- **Statischer `HttpClient`**, kein `new` je Aufruf. Im Repo zweimal als Socket-Erschöpfung
  aufgetreten (`BackendService`-Probe, AllTalk-Streaming-Client).
- Der alte Upload-Pfad (`UploadVoiceLine`, `DriveUploadService`) fliegt raus; `DriveLinkHelper` und
  der Download-Teil bleiben.

## Phasen

| Phase | Inhalt | Aufwand (Schätzung) |
|---|---|---|
| **1** | Server-Grundgerüst: Projekt, Postgres-Modell, `POST /v1/lines`, Ratenbegrenzung, Docker + beide CI-Systeme | 2,5 Tage |
| **2** | Bestand aus dem Harvest einspielen (Plausibilitätsprüfung + Quarantänelogik, Beförderung ab X=3) | 1 Tag |
| **3** | Client: `ILineSubmissionService`, lokale Unterdrückung, Stapelversand, `installId`, Schalter an beiden Stellen, Changelog | 1,5 Tage |
| **4** | Web-UI: Admin-Login, Freigabe-/Ablehnliste, Phase-4-Ansicht | 2 Tage |
| **5** | Web-UI: Massenimport der Drive-Dateien (Dateiname führend, Konfliktbericht) | 1 Tag |
| **6** | Auswertung „welche gemeldeten Zeilen kennt der Harvest nicht?" | 1 Tag |
| **7** | Alten Drive-Upload aus dem Plugin entfernen | 0,5 Tag |

Summe grob **9,5 Tage**. Phase 5 kann vorgezogen werden, wenn der Drive-Bestand zuerst gerettet
werden soll — sie hängt nur an Phase 1 und dem Admin-Login aus Phase 4.

**Phase 4 ist der Zweck der Übung.** Sie beantwortet dauerhaft und automatisch die Frage, an der
diese Session gearbeitet hat: was fehlt dem Extract noch?

## Naheliegende Erweiterung (bewusst NICHT eingeplant)

Dieselbe DB kann später auch **ausliefern** — Zeilen und Zuordnungen abrufbar machen und damit die
`*.ekpack`-Verteilung ergänzen oder ersetzen. Das ist ein eigener Entwurf (Auslieferung, Caching,
Urheberrecht an den generierten Audios) und gehört nicht in diesen Plan. Erwähnt, damit das
Datenmodell es nicht verbaut — deshalb `status` und `first_seen_utc` von Anfang an.

## Verhältnis zum anderen Plan

`missing-line-reports.md` deckt drei Klassen ab: **L1** Zeile fehlt in der DB, **L2** Audio fehlt,
**L3** Zuordnung falsch. Die zentrale DB erledigt **L1 vollautomatisch und laufend** — dafür braucht
es dann keinen Melde-Knopf mehr. **L2 und L3 bleiben** beim Meldeweg: dass eine Zuordnung falsch ist,
kann nur ein Mensch feststellen, und welches Paket welche Audios enthält, weiß der Server nicht.

Empfehlung: den Paket-Plan auf L2/L3 eindampfen, sobald dieser hier steht.

## Repo-Beschreibung (für GitLab/GitHub)

Englisch, weil GitHub der öffentliche Spiegel ist und die Nutzerschaft international — dieselbe
Begründung wie bei der Tester-Checkliste.

**Kurzbeschreibung (das einzeilige Feld):**

```
Community-fed database of FINAL FANTASY XIV dialogue lines in all four game languages,
collecting what Echokraut's own extraction misses. No user data is ever sent or stored.
```

**Kürzere Fassung, falls das Feld eng ist:**

```
Community-fed database of FFXIV dialogue lines in all four languages — the missing-line
backstop for Echokraut. No user data.
```

**Deutsch, falls irgendwo gebraucht:**

```
Von der Gemeinschaft gespeiste Datenbank der FFXIV-Dialogzeilen in allen vier Spielsprachen —
sie fängt auf, was Echokrauts eigene Extraktion nicht findet. Es werden keine Nutzerdaten übertragen.
```

**Einleitung für die README:**

```
Echolines collects FINAL FANTASY XIV dialogue lines reported by Echokraut installations and
keeps them in one place, in all four game languages.

Echokraut extracts dialogue from the game's own data files, but that extraction is never quite
complete: speakers that cannot be resolved, content added by a patch, sheet families nobody has
looked at yet. Echolines closes that gap from the other end — every line a player actually sees
is reported once, and what the extraction does not know shows up here.

Three things are sent per line: the text, its language, and which NPC speaks it. The player's
character name is replaced by a placeholder before sending. Nothing about the player is
transmitted or stored: no character name, no world, no account, no play time, no file paths.
Reporting is on by default and can be switched off in Echokraut under Settings -> General.

New lines land in a staging table first and are only promoted once three separate installations
have reported them, so a single source cannot inject anything.
```

## Offene Fragen an den User

1. **Image-Bau in der GitHub-Action (auf dem Spiegel) oder direkt in der GitLab-CI?** Ersteres ist so
   gewünscht, kostet aber die Spiegel-Verzögerung vor jedem Bau.
2. **Admin-Login**: reicht ein einzelnes Betreiberkonto mit Passwort aus einem Secret, oder soll es
   über etwas Vorhandenes laufen?
3. **Aufbewahrung in `pending_lines`**: 12 Monate ohne neue Stimme vorgeschlagen — bei manueller
   Freigabe über die Web-UI vielleicht länger sinnvoll.

## Entschieden (User, 2026-08-10)

- **Variante A** — Zufalls-GUID je Installation als `installId`.
- **X = 3** für die automatische Beförderung, **plus manuelle Freigabe** in der Web-UI für alles
  darunter.
- **Schalter mit Standard „an"** im Ersteinrichtungsassistenten **und** unter Settings → General,
  ein gemeinsamer Konfigurationswert.
- **Changelog erklärt ausdrücklich**: Standard an, keine Nutzerdaten. Entwurf EN/DE steht oben.
- **PostgreSQL**.
- **`npc_base_id` wird mitgespeichert.**
- **Repo-Name `echolines`.**
- **Web-UI** mit manueller Freigabe, Massenimport der Drive-Dateien und Phase-4-Ansicht.
- **Docker-Image über GitHub Action**, GitLab-CI mit GitHub-Spiegel analog Echokraut.
- **Adresse: `https://echolines.echotools.cloud`** — `/v1/lines` (live), `/v1/lines/import` (Harvest),
  `/healthz` (Status), `/admin` (Verwaltung). **API ist live.** URL über
  `RemoteUrls.json` beziehen, nicht hart einkompilieren — aber erst eintragen, wenn der Endpunkt
  antwortet, sonst färbt `RemoteUrlsReachabilityTests` rot.
- **Umfang: JEDE unvertonte Zeile wird gemeldet**, nicht nur die lokal fehlenden. Aufrufpunkt
  deshalb `VoiceMessageProcessor.cs:187` (Schritt 3.5) statt `LogVoiceClip`.
- **Live-Pfad: alle 60 s (±10 s Streuung) alle noch nicht gesendeten Zeilen**, vorzeitig ab ~200
  gepufferten. Puffer **auf der Platte**, damit ein Absturz nicht die letzte Minute frisst; als
  gesendet gilt erst, was mit 200 bestätigt wurde.
- **Harvest-Pfad nur nach einem abgeschlossenen Harvest**, blockweise und gemerkt an
  (Spiel-Patch, Harvest-Version, Sprache).
