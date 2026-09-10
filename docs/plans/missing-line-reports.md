# Plan: Nutzer melden fehlende Zeilen (nach ekpack-Verteilung)

## Status

**Planung, kein Code.** Erstellt 2026-08-10 auf Wunsch des Users. Die Code-Aussagen unten sind am
Repo verifiziert (Datei + Zeile genannt); alles Übrige ist ausdrücklich als Annahme markiert.

## Ausgangslage

Sobald ein fertiges `*.ekpack` verteilt ist, laufen die meisten Empfänger im **None-Modus**: kein
eigener TTS-Backend, sie spielen ausschließlich die mitgelieferten WAVs. Fehlt eine Zeile, passiert
für sie schlicht *nichts* — der Dialog läuft stumm durch. Genau diese Fälle müssen zurückkommen,
sonst bleibt „der Extract ist komplett" eine Behauptung.

### Drei Lückenklassen, die NICHT dasselbe sind

| | Klasse | Ursache | Automatisch erkennbar? |
|---|---|---|---|
| **L1** | Zeile gar nicht in der DB | Harvest-Lücke (nicht gescannte Quelle, neuer Patch, Sprecher nicht auflösbar) | ja |
| **L2** | Zeile in der DB, aber kein Audio | Paket unvollständig / Datei fehlt | ja |
| **L3** | Audio da, aber falsch | falsche Zuordnung, falsche Stimme, falscher NPC | **nein** — braucht einen Knopf |

Die Unterscheidung ist der eigentliche Wert des Meldewegs: L1 fixe *ich* im Harvest, L2 in einem
neuen Paket, L3 über die Alias-Dateien. Ein Bericht, der die drei vermischt, ist kaum verwertbar.

## Was das Plugin im Moment des Fehlens bereits weiß (verifiziert)

- `VoiceMessageProcessor.TryLoadCachedAudio` (`VoiceMessageProcessor.cs:639`) setzt
  `voiceMessage.LoadedLocally = true`, wenn Audio gefunden wurde — aus der DB-Generationszeile oder
  über die Platten-Adoption. Bleibt es `false` **und** `Configuration.HasLiveGeneration` ist `false`
  (None-Modus), dann **hat der Nutzer garantiert nichts gehört**. Das ist das präziseste
  Fehlt-Signal, das es gibt, und es kostet nichts.
- Unterscheidung L1/L2 ohne neue DB-API: `_db.GetVoiceClipGeneration(voiceClipId, playerId)` ist
  bereits im selben Codepfad. Keine Generationszeile ⇒ Zeile war nicht im Paket (L1/L2), Zeile mit
  Pfad auf eine fehlende Datei ⇒ Paket beschädigt/unvollständig (L2).
- **Stabile Zeilen-ID gibt es schon**: `IAudioFileService.VoiceMessageToFileName(RemovePlayerNameInText(text))`
  (`IAudioFileService.cs:20-21`) ist der Hash, aus dem auch der WAV-Dateiname gebaut wird. Als
  Melde-ID verwendet, lässt sich ein Bericht **direkt gegen den Paketinhalt abgleichen** — keine neue
  Hash-Definition nötig, und Meldungen verschiedener Nutzer deduplizieren sich von selbst.
- **Spielernamen-Neutralisierung existiert**: `Helper/Functional/PlayerNameTokenizer` +
  `TalkTextHelper.ContainsPlayerPlaceholder`. Damit geht kein Charaktername in einen Bericht.
- **Vorbild für Berichtsdateien existiert**: der Harvest schreibt bereits
  `quest_alias_candidates.json`, `voice_name_suggestions_<lang>.json`,
  `scripted_alias_candidates.json` nach `<localSaveLocation>/harvest/`.
- **Discord-Kanal existiert**: `Constants.DISCORDURL` (`DataClasses/Constants.cs:14`).

## ⚠ Blocker, der VOR der Verteilung gelöst sein muss

**Das ekpack trägt keine Paket-Identität.** `AudioPackageManifest` (`DataClasses/AudioPackage.cs`)
hat `FormatVersion`, `CreatedBy`, `CreatedUtc`, `Language` — aber **keinen Paketnamen und keine
Paketversion**. Und `AudioPackageService` speichert beim Import **nichts** davon in der Konfiguration.

Folge: ein Bericht kann nicht sagen, *gegen welches Paket* getestet wurde. Bei mehreren Releases ist
er damit praktisch wertlos — „Zeile X fehlt" ohne „in Paket 1.2" erzeugt genau die Ratearbeit, die
der Meldeweg abschaffen soll.

**Zu tun, klein und vor dem ersten Release:**
1. `AudioPackageManifest` um `PackName` (z. B. `EK-Lines-EN`) und `PackVersion` (z. B. `1.0.0`)
   ergänzen — `FormatVersion` bleibt davon unberührt, das sind zwei verschiedene Dinge.
2. Beim Import in die `Configuration` schreiben (Liste importierter Pakete: Name, Version, Datum,
   Anzahl Einträge). Damit weiß die Installation dauerhaft, was sie hat.
3. Die Felder sind optional zu lesen (`""` bei Altpaketen), damit ein bereits verteiltes Paket nicht
   ungültig wird.

## Vorschlag

### 1. Erfassung — `IMissingLineReporter`

Neuer Service, ein einziger Aufrufpunkt in `ProcessSpeechAsync` direkt nach `TryLoadCachedAudio`:

```
wenn !LoadedLocally && !HasLiveGeneration:
    klasse = generationRow == null ? L1_oder_L2 : L2_datei_fehlt
    Record(id, sprache, sprecher, npcBaseId, tokenisierter Text, quelle, klasse)
```

- **Speicher**: `<localSaveLocation>/reports/missing_lines.json`, damit der Bericht Neustarts
  überlebt. Gedeckelt (Vorschlag 5.000 Einträge); **bei Überlauf wird die verworfene Zahl
  mitgeschrieben** — stille Kürzung würde einen Teilbericht wie einen vollständigen aussehen lassen
  (dieselbe Regel wie bei den Harvest-Zählern).
- **Entprellung** über die Zeilen-ID: dieselbe Zeile zehnmal gehört = ein Eintrag mit `Count: 10`.
  Die Häufigkeit ist für mich wertvoll (was fehlt vielen Leuten oft?).
- **L1 vs L2 sauber trennen** heißt zu wissen, ob die `voice_clips`-Zeile schon vorher existierte.
  `LogOrUpdateVoiceClip` legt sie beim Upsert eventuell gerade erst an. Sauberste Lösung: die
  Methode meldet zurück, ob sie **eingefügt oder aktualisiert** hat. Kleiner Eingriff, macht die
  Klassifikation aber erst ehrlich. *(Annahme: das ist ohne Nebenwirkung machbar — nicht verifiziert.)*

### 2. L3 (falsche Zuordnung) — nur manuell

Ein `Report`-Knopf je Zeile im `NativeVoiceClipDetailWindow`, dazu die Auswahl „falscher Sprecher /
falsche Stimme / Text passt nicht". Automatik ist hier unmöglich, und Raten wäre schlimmer als
nichts.

### 3. Berichtsartefakte — zwei, aus denselben Daten

- **`missing_lines.json`** — maschinenlesbar, versioniert wie `AudioPackageFormat`. Das ist, womit
  ich arbeite: direkt gegen Paketinhalt und Alias-Dateien abgleichbar.
- **Markdown-Zusammenfassung** — zum Einfügen in Discord oder ein GitHub-Issue. Nach NPC gruppiert,
  gedeckelt (z. B. 50 Zeilen) mit ausdrücklichem „und N weitere, siehe JSON". Kopf: Plugin-Version,
  Paketname + Paketversion, Client-Sprache, Zeitraum.

### 4. Datenschutz — harte Regeln

- Kein Charaktername, keine `content_id`, keine absoluten Pfade. Präzedenzfall:
  `AudioPackageRules.RelativeEntryPath` hält bewusst den Windows-Benutzernamen aus dem Manifest.
- Text wird durch `PlayerNameTokenizer` geschickt, bevor er in den Bericht geht.
- **Nichts verlässt den Rechner ohne Knopfdruck.** Sammeln passiert lokal, Senden ist immer eine
  bewusste Handlung.

### 5. Einreichungsweg — bewusst ohne Server

Passend zur bestehenden Linie der Checklisten-Seite („kein Server, keine Anmeldung, die Seite
verschickt nichts"):

- **A — GitHub-Issue-Vorlage** (`.github/ISSUE_TEMPLATE/missing-lines.yml`): Plugin legt den Bericht
  in die Zwischenablage und öffnet die Issue-Seite; der Nutzer fügt ein und hängt bei Bedarf die JSON
  an. *(Vorbefüllen per URL scheidet aus — Längenbegrenzung.)*
- **B — Discord** über `Constants.DISCORDURL`, gleiches Markdown.
- **C — Automatischer Upload**: **nicht empfohlen** für den Start. Braucht Server, Moderation,
  Spam-Schutz und eine Einwilligung; der Nutzen entsteht erst bei großem Volumen.

**Empfehlung: A + B zuerst.** C nur, wenn das Volumen es rechtfertigt.

### 6. Rückweg zu den Nutzern

- **L1/L3** münden in `quest_npc_aliases.json` bzw. Harvest-Korrekturen. Die Alias-Datei wird bereits
  **remote** geladen (`RemoteUrlsData.QuestNpcAliasesUrl`) — Korrekturen erreichen die Nutzer also
  **ohne Plugin-Update**. Das ist der schnellste vorhandene Hebel.
- **L2** braucht ein neues Paket-Release.

### 7. Bedienung

Neue Sektion „Report missing lines" im `NativeGameDataToolsWindow` (dort liegen die
Daten-Werkzeuge): Zähler je Klasse, „Bericht erzeugen", „Discord öffnen" / „Issue öffnen",
„Gemeldetes verwerfen". Alle Loc-Strings mit DE/FR/JP.

## Phasen

| Phase | Inhalt | Aufwand (Schätzung) |
|---|---|---|
| **0** | Paket-Identität in Manifest + Import (**Blocker**) | 0,5 Tag |
| **1** | Erfassung + JSON-Datei + Entprellung | 1 Tag |
| **2** | Markdown-Bericht + UI-Sektion + Loc | 1 Tag |
| **3** | Issue-Vorlage + Kurzanleitung für Melder | 0,5 Tag |
| **4** | L3-Knopf im Detailfenster | 0,5 Tag |

Phase 0 muss **vor** dem ersten Paket-Release stehen, alles andere kann danach kommen.

## Bewusst verworfen

- **Automatischer Upload ohne Zustimmung** — Vertrauensbruch, und rechtlich unnötig heikel.
- **Audio mitschicken** — der Bericht braucht nur Text-IDs; Audio wäre groß und brächte nichts.
- **Eigene Hash-Definition für die Melde-ID** — der WAV-Dateiname-Hash existiert und verbindet
  Bericht und Paketinhalt direkt.
- **Nur L2 melden** („fehlt halt Audio") — dann bleibt die Harvest-Lücke L1 für immer unsichtbar,
  und genau die war das Thema dieser Session.

## Offene Fragen an den User

1. **Sammeln standardmäßig an oder aus?** Ich tendiere zu **an, aber rein lokal** — Senden bleibt
   ein Knopfdruck. Ohne Vorab-Sammlung meldet fast niemand, weil der Moment längst vorbei ist.
2. **GitHub-Issue oder Discord als Hauptweg?** Issues sind kuratierbar und durchsuchbar, Discord hat
   die niedrigere Hemmschwelle. Beide zu bauen kostet fast nichts extra.
3. **Paketname/-version**: welches Schema? Vorschlag `EK-Lines-<LANG>` + semantische Version.
4. Soll der Bericht die **Häufigkeit** je Zeile enthalten? (Ich halte das für den wertvollsten Teil —
   es zeigt, was viele betrifft.)
