# Plan: Spielstimme als Referenz + optionales privates Modell

Stand: 2026-11-09 · Status: **FREIGEGEBEN (2026-11-08, F1–F8 entschieden)** · Phasen 1–4 umgesetzt (nicht committet), nächste: Phase 5
Betrifft: `echokraut` (Plugin), `echokrautts` (Wrapper), `echokraut-tools` (Training)
Bezug: Commit `485eb43` (Extraktor entfernt), `5f876ee` (IsSpeech, VoiceActorSplits),
`echokraut-tools/docs/AUDIO-EXTRACTION.md`, `docs/claude/services/voice-pack.md` › Voice Pack.

Nicht Teil dieses Plans: Veröffentlichung von Modellen, Weitergabe von Audio, Rechtsberatung.

---

## 0. Ausgangslage und was sich gegenüber dem entfernten Extraktor ändert

Die harte Regel in `CLAUDE.md` („never extract audio from the game … must not come back in any
form“) wurde mit v0.19.3.0 eingeführt, Begründung dort nur „for legal reasons“. Dieser Plan hebt
sie **teilweise** auf. Damit die Entscheidung bewusst fällt, hier der ehrliche Vergleich:

| | Alter Extraktor (bis v0.19.2) | Dieser Plan |
|---|---|---|
| Spiel-Audio dekodiert | ja | **ja** (unverändert) |
| Audio auf Platte | ja, WAV-Starter-Set in `samples/` | nein (Plugin); Tools nur temporär fürs Training |
| Weitergabe möglich | ja, Dateien lagen offen herum | erschwert, nicht verhindert (Ausgabe-Clips, s. §6) |
| OGG-XOR-Entschlüsselung | ja (`ScdOggEntry`, eigener Code) | **nein**, OGG wird übersprungen |
| Stimme fremder NPCs verliehen | ja (Starter-Set als Pool) | **nein**, nur derselbe NPC |
| Standard | Teil der Installation | **AUS**, Opt-in |

Was der Plan **nicht** ändert: Es bleibt Stimmklonen echter Synchronsprecher ohne deren
Einwilligung. Das ist der Teil, den keine technische Schutzmaßnahme auflöst; er ist eine
Abwägung, die du triffst (siehe offene Frage F1).

## 1. Dalamud-Regeln (nachgelesen 2026-11-08)

Echokraut läuft über ein **eigenes Repo** (`RenNagasaki/MyDalamudPlugins`), nicht über das
offizielle D17-Repo. Die Regeln sind daher nicht bindend, aber der Maßstab, falls es je
eingereicht wird oder das Dalamud-Team sich äußert.

- **Plugin Restrictions** (https://dalamud.dev/plugin-publishing/restrictions):
  - „Dalamud plugins should enhance the experience, not radically alter it.“
  - „Certain plugins may be subject to certain additional constraints or review based on their
    featureset, potential impact to the ecosystem, and other factors.“
  - „I don't want to cause harm to the game, its community or Square Enix.“ /
    „I want to minimize the risk of Square Enix taking action and taking away the things we built“
  - Zu Spiel-Assets, Extraktion oder KI-Stimmen steht dort **nichts**.
- **AI Usage Policy** (https://dalamud.dev/plugin-publishing/ai-policy) – regelt KI beim
  *Erstellen* des Plugins und KI-Assets:
  - „Disclose any AI-generated assets in your plugin's description“
  - „AI-generated audio (music, sounds) should be toggle-able or replaceable by user selections
    where possible“
  - „Entirely AI-generated submissions will be auto-rejected; undisclosed AI use or repeated
    violations will result in a ban.“
  - Generative KI-*Funktionen* im Plugin, Stimmklonen, Modelltraining: **nicht geregelt**.
- **Acceptable Use Policy** (goatcorp-Wiki, gilt für das offizielle Repo): Zusicherung, dass
  Einreichungen „your own original work and lawfully submitted“ und „provided with the necessary
  consent of any third party“ sind. Keine Aussage zu Spieldaten.

Folgerung für den Plan: „abschaltbar/ersetzbar“ ist durch Standard AUS + Voice-Pack als Standard
erfüllt; die KI-Nutzung gehört in die Plugin-Beschreibung. Die eigentliche Frage (Square Enix,
Sprecherrechte) beantworten die Dalamud-Regeln **nicht**. Separat lesenswert, hier bewusst nicht
bewertet: die „FINAL FANTASY XIV Materials Usage License“ von Square Enix.

## 2. Teil A – Laufzeit-Referenz im Plugin

### 2.1 Ablauf je unvertonter Zeile
1. Zeile kommt in die Queue, Sprecher-NPC ist aufgelöst, Feature ist an und die Sperren (§5) sind frei.
2. `IGameVoiceReferenceService.TryGetReference(npc, language, epoch)` →
   - Index-Treffer in der DB (gespeicherte Clip-IDs) → Clips nachladen (2.3);
   - kein Index → Kandidaten suchen, ranken (Teil C), Top-1–3 in den Index schreiben;
   - keine Kandidaten → Fallback Teil B.
3. Referenz-PCM + Referenztext (der **Spieltext** des Clips, nicht ASR) an EchokrauTTS (2.4).
4. PCM-Puffer nach der Anfrage verwerfen; ein kleiner In-Memory-LRU (z. B. 20 NPCs) spart das
   wiederholte Dekodieren in einem Gespräch. Nie auf Platte.

### 2.2 Kandidaten finden („vertonte Zeile desselben NPCs“)
- Quelle: die Cutscene-Textsheets, die der Dialog-Harvest schon liest. Ihr TextKey
  `TEXT_VOICEMAN_06006_000010_YSHTOLA` liefert über `VoiceExtractKey.TryParse` Sprecher-Token und
  `audioFileBase`; `VoiceExtractKey.Resolve` (exakter Voll- oder Vorname, Fix aus 5f876ee) ordnet
  ihn dem NPC zu.
- Pfad: `VoiceScdPaths.Build(audioBase, lang)` → erste existierende Datei via `IDataManager.FileExists`.
- Einmaliger Text-Index `Sprecher-Token → [audioBase, Text, Patch-Token]` beim ersten Gebrauch,
  im Speicher gehalten. Liest nur Text, kein Audio.
- Nur Zeilen in der **Client-Sprache** (die Referenz muss die Sprache der Ausgabe sprechen).

### 2.3 Dekodieren nur im Speicher
- `IDataManager.GetFile(path).Data` → `byte[]` → eigener, schlanker SCD-Leser:
  Header + Eintragstabelle lesen, **Codec-Feld prüfen, bevor Eintragsdaten angefasst werden**.
  - MS-ADPCM (0x0C) → Dekoder → `float[]`/`short[]` PCM, Resampling auf 24 kHz mono im Speicher.
  - OGG (0x06) und alles andere → Eintrag überspringen, Zähler fürs Log. **Kein XOR-Code**,
    kein Lesen der OGG-Nutzdaten.
- Wiederverwendbar aus `485eb43^`: `MsAdpcmDecoder`, `WavResampler`, Header-Teile von `ScdFile`.
  **Nicht** übernehmen: `ScdOggEntry`, `ScdOggCryptType`, `PcmWavWriter`, Datei-Ausgabe.
- Neuer Ort: `Helper/Functional/GameAudio/` (rein funktional, ohne DI, gut testbar mit
  synthetischen SCD-Byte-Puffern statt echter Spieldaten in den Tests).

### 2.4 Wrapper-Schnittstelle (echokrautts)
Heute nimmt `POST /tts` nur einen **Dateinamen** (`"sample": "anna_de.wav"`) aus `samples/`.
Ohne Änderung ginge das nur über eine Datei auf Platte, also braucht es:
- `POST /tts` mit Inline-Referenz: Multipart oder JSON mit `ref_audio` (base64 PCM 24 kHz mono)
  + `ref_text`. Der Wrapper hält sie **nur im RAM** (kein Schreiben nach `samples/`, kein
  Temp-File; F5/XTTS-Aufrufe, die einen Pfad wollen, über In-Memory-Wrapper oder
  `SpooledTemporaryFile` mit `max_size` > Clipgröße – prüfen je Engine).
- Konditionierungs-Cache (XTTS-Latents, F5-Ref-Mel) im RAM, Schlüssel = Hash der Clip-IDs.
- `asr_for_missing_ref_text` bleibt für Inline-Referenzen aus, weil der Spieltext da ist;
  ASR wird nur zur Prüfung genutzt (Teil C), und der Transkript-**Datei**-Cache in
  `samples.py` darf für Inline-Referenzen nicht greifen.
- Neuer Endpunkt `POST /asr-check` (Audio rein, Text raus, nichts gespeichert) für Teil C.
- `/health` meldet zusätzlich `inline_reference: true`, damit das Plugin die Sperre (§5)
  prüfen kann. Der Wrapper darf weiter auf `0.0.0.0` binden (jede Instanz ist erlaubt, §5.3).
- **AllTalk wird nicht unterstützt**: seine API kennt nur Dateinamen, das hieße Audio auf Platte.

### 2.5 Sprecherwechsel (Patch-Epochen)
- `VoiceActorSplits` + `VoiceActorSplitsFile` aus 5f876ee zurückholen (reiner Text/JSON-Code),
  Schlüssel künftig `NPC-Name + Sprache` statt Voice-Key. Liefert ausgeliefert leer, lokale
  Überschreibung wie damals.
- Auswahl: Kandidaten nur aus **einer** Epoche, nie gemischt. Welche Epoche, siehe F4.

### 2.6 Gespeichert wird nur der Index
Neue Tabelle (Schema v14 → umgesetzt als **v6** `game_voice_references`) `GameVoiceReference`:
`CharacterId, Language, Epoch, AudioBase (1–3 Zeilen, Rang), Score, IndexVersion, CreatedUtc`.
Keine Audiobytes, keine Merkmale, aus denen sich Audio rekonstruieren ließe.
`IndexVersion` steigt, wenn sich Ranking/Filter ändern → alter Index wird neu bewertet.
Nach Spielpatch: Index-Zeile, deren Pfad nicht mehr existiert, wird verworfen und neu gesucht.

## 3. Teil B – Fallback ohne vertonten Clip
- Unverändert der heutige Weg: Voice-Pack-Stimme über `Gender_RacePool[-BodyType]`
  (`BackendService.PickVoice` / `FitsNpcData`).
- **Keine** Spielstimme eines anderen NPCs als Ersatz, auch nicht „ähnlich klingend“ oder
  gleiche Rasse. Technisch: `IGameVoiceReferenceService` liefert nur für den exakten Namen oder
  einen Eintrag der Alias-Liste (F2).
- Pro NPC sichtbar, welche Quelle aktiv ist (Voice Clip Manager: Symbol/Tooltip „Spielstimme“
  vs. „Voice-Pack“), und pro NPC abschaltbar.

## 4. Teil C – Qualitätsfilter: Ranking statt harter Grenzen
Harte Ausschlüsse nur für Offensichtliches; alles andere geht in einen Score, die besten 1–3
gewinnen. Ein NPC mit nur schlechten Clips bekommt den besten davon, solange er über einer
Mindestschwelle liegt; darunter Fallback B.

| Stufe | Kriterium | Art |
|---|---|---|
| Text | `VoiceExtractTextCleaner.IsSpeech` (5f876ee): Lachen, Ausrufe, Lautmalerei, `(…)`-Hinweise; Sprachgrenzen EN/FR 4 Wörter, DE 3, JP 8 Zeichen | hart |
| Dauer | 3–15 s; Optimum 6–10 s | hart außerhalb, sonst Score |
| Tempo | Wörter (JP: Zeichen) pro Sekunde nahe Sprach-Median | Score |
| Signal | Lautheit (LUFS/RMS), Übersteuerung (Anteil Samples ≥ 0,99), Stilleanteil, Tonhöhenspitzen (Schreie), Rauschanteil/Effekte (Hochband-Anteil, spektrale Flachheit, Steigung aus `WavEffectGate.cs`) | Score |
| ASR | Whisper im Wrapper (`/asr-check`), Wortfehlerrate gegen Spieltext; hohe WER = Effekt/Hall/Mehrstimmigkeit | Score, teuer → nur für Top-5 je NPC |

- `WavEffectGate` arbeitet heute mit NAudio auf Dateipfaden; für das Plugin auf `float[]`
  portieren (ohne NAudio). Gleiche Schwellen als Startwerte, danach an echten Clips justieren.
- Diversität: bei 2–3 Clips Dauer streuen (`VoiceExtractSampleSelector.PickDiverse`, 5f876ee).
- Ranking: Batch „Index aufbauen“ + optional sofort beim Gesprächsstart für den aktuellen NPC
  (F5). Bis ein Index da ist, spricht das Voice-Pack (kein Warten im Dialog).

## 5. Schutzgeländer (gelten für A–C)
1. **Standard AUS.** Eigener Schalter in den Einstellungen, eigener Config-Wert, nicht Teil
   von „Installieren“.
2. **Opt-in-Dialog** beim Einschalten, lokalisiert EN/DE/FR/JP, muss aktiv bestätigt werden,
   Bestätigung mit Textversion gespeichert (Textänderung → erneut fragen). Inhalt (Entwurf):
   - Nutzt Sprachaufnahmen aus **deiner eigenen** FFXIV-Installation, nur im Arbeitsspeicher.
   - Ergebnis ist eine KI-Nachahmung echter Synchronsprecher; nicht mit Square Enix verbunden.
   - **Erzeugte Ausgabe nicht veröffentlichen, streamen, aufnehmen oder weitergeben.**
   - Nur für den privaten Gebrauch; jederzeit abschaltbar, Voice-Pack bleibt Standard.
3. **Jede eigene EchokrauTTS-Instanz, egal wo gehostet; Verantwortung beim User** (F8,
   entschieden 2026-11-08: „Überall, nur Hinweis“).
   - Erlaubt für `InstanceType` `Local` und `Remote`, ohne Adressprüfung.
   - Opt-in-Text und Einstellungsfenster sagen **deutlich**: „Nur mit deiner eigenen, privaten
     Instanz verwenden – nicht mit Servern anderer Personen oder geteilten/öffentlichen
     Servern.“ Bei `Remote` steht der Hinweis zusätzlich direkt beim Schalter.
   - **Automatischer Fallback aufs Voice-Pack** (kein Fehler, kein Abschalten, einmalige Info in
     Log und Einstellungen) nur noch, wenn der Server die Funktion technisch nicht kann:
     `/health` ohne `inline_reference: true` (alter Wrapper) oder Backend AllTalk.
   - Bewusst in Kauf genommen: Das Plugin verhindert nicht, dass jemand einen fremden Server
     einträgt; Referenz-Audio geht dann über dessen Verbindung (bei `http://` unverschlüsselt).
4. **OGG nie anfassen** (2.3). Test, der sicherstellt, dass der Leser bei OGG die Nutzdaten nicht
   liest (Leser auf einem Puffer, dessen OGG-Bereich bei Zugriff wirft).
5. **Voice-Pack bleibt Standard** und Fallback; Abschalten des Features stellt sofort um.

## 6. Ausgabe-Clips (Lücke, die der Plan schließen muss)
„Kein Audio auf Platte“ betrifft die Referenz. Die **erzeugten** Zeilen landen aber heute im
Voice-Clip-Speicher und können über `AudioPackageService` (`*.ekpack`) exportiert und über
Google-Drive-Sync geteilt werden – also genau die Weitergabe, die ausgeschlossen sein soll.
- `VoiceClipEntity` bekommt `ReferenceSource` (`VoicePack` / `GameVoice`).
- `GameVoice`-Clips: vom `.ekpack`-Export und vom Drive-Sync ausgeschlossen (Hinweis im UI),
  nicht in die Community-Datenbank-Meldungen mit Audio (heute nur Text – beibehalten).
- Feature aus → Option „mit Spielstimme erzeugte Clips löschen“ (Standard: ja).
- Offen: ob die erzeugten Clips überhaupt gecacht werden dürfen oder jedes Mal neu erzeugt
  werden (F3).

## 7. Teil D – Privates Modell (nur echokraut-tools)
- **Training nur in echokraut-tools**, nur nach eigenem Opt-in (gleicher Zustimmungstext + Zusatz
  „Modell nicht weitergeben“). Das Plugin trainiert nie.
- Voraussetzung: der offene Worklog-Punkt „OGG-XOR-Pfad vermeiden“ – SaintCoinachs
  `new ScdFile()` dekodiert im Konstruktor **alle** Einträge inkl. OGG-XOR. Tools braucht
  denselben ADPCM-only-Leser wie das Plugin (geteilter Quellcode, siehe F6).
- Trainingsdaten (WAV + `metadata_train/eval.csv`) liegen zwangsläufig auf Platte, aber nur in
  einem Arbeitsordner, der **nach Trainingsende automatisch gelöscht** wird – auch bei Abbruch
  (beim nächsten Start: verwaiste Arbeitsordner melden und löschen).
- Modell-Metadaten `echokraut_model.json` neben den Gewichten:
  `{ "source": "game-data-private", "notice": "Privat, keine Weitergabe …", "created", "tool_version", "language" }`
  (Hinweis zusätzlich als `NOTICE.txt`).
- Plugin:
  - `CustomModelUrl` (Installation per URL) bleibt für frei lizenzierte Modelle; enthält das
    entpackte Archiv `echokraut_model.json` mit `source: game-data-private` → Installation
    abbrechen + Meldung.
  - Neu: „Lokales Modell laden“ (Ordnerpfad) → kopiert/verlinkt nach `models/echokraut_custom`;
    nur dieser Weg akzeptiert `game-data-private`, und nur mit erteiltem Opt-in (§5.2) und
    eigener Instanz (§5.3).
  - Ehrliche Grenze: Ein Modell ohne Metadaten ist nicht als „aus Spieldaten“ erkennbar. Die
    Prüfung schützt vor Versehen, nicht vor Absicht.
- `docs/AUDIO-EXTRACTION.md` in Tools bei jeder Änderung am Extraktionspfad nachziehen.

## 8. Umsetzung in Phasen (jeweils mit Tests, `node .claude/verify.js` grün)
1. **CLAUDE.md-Regel ersetzen** (erst nach Freigabe, Textvorschlag §10) + Changelog-Eintrag.
2. **Funktionaler Kern**: SCD-Leser (ADPCM-only), MS-ADPCM-Dekoder, Resampler, Signalmetriken,
   IsSpeech, VoiceActorSplits – alles ohne DI, mit synthetischen Testdaten.
   - ✅ 2026-11-08 Repo `../echoscd` (statt `Helper/Functional/GameAudio/`, gemäß F6): SCD-Leser,
     MS-ADPCM-Dekoder, Resampler, `ScdVoiceDecoder`, 58 xUnit-Tests inkl. OGG-Nie-lesen-Wächter.
     Noch nicht eingebunden (kein Submodul), noch nicht committet.
   - ✅ 2026-11-08 ebenfalls in `echoscd`: `SignalAnalyzer` (WavEffectGate-Port, gleiche Schwellen),
     `SpeechTextFilter.IsSpeech`, `SampleSelector.PickDiverse`, `VoiceActorSplits` (Schlüssel
     NPC-Name + Sprache, JSON liest der Aufrufer), 149 Tests. Begründung für echoscd statt Plugin:
     Plugin und Tools brauchen beide dieselbe Auswahl (kein Doppelcode).
3. **Wrapper**: Inline-Referenz, `/asr-check`, `inline_reference` in `/health`, Tests in `wrapper/tests`.
   - ✅ 2026-11-08 in `echokrautts` (nicht committet, live/GPU ungetestet): `/tts` mit `ref_audio` +
     `ref_text` (Pflicht) + `ref_ids` (Cache-Schlüssel), `/asr-check`, `/health` `inline_reference` +
     `asr_check`; 44 neue Tests. Je Engine: F5 umgeht `F5TTS.infer` (schreibt sonst eine Temp-WAV),
     XTTS rechnet Latents aus Tensoren, MOSS-ONNX kodiert im Speicher. **MOSS-PyTorch kann es nicht**
     (Vendor nimmt nur Pfade) → `inline_reference: false`, Plugin fällt dort aufs Voice-Pack zurück (§5.3).
4. **Plugin-Service** `IGameVoiceReferenceService` + DB v14 + Backend-Erweiterung
   `EchokrauTtsBackend` (Inline-Referenz) + Sperrlogik.
   - ✅ 2026-11-09 umgesetzt (nicht committet, ingame ungetestet): `echoscd` als Submodul (Erst-Commit
     `5ef13d2`, URL `gitlab.echotools.cloud/echotools/echoscd` – Remote noch anzulegen),
     `GameVoiceReferenceService` + `GameVoiceDataSource` + Helfer in `Helper/Functional/GameVoice/`,
     Inline-Referenz/`/asr-check`/`/health`-Flags im `EchokrauTtsBackend`, Rückfall aufs Voice-Pack bei
     Ablehnung, `BatchOperation.GameVoiceIndex`. **DB-Version ist v6, nicht v14** (die Migrationskette
     wurde beim Rewrite auf v1 zurückgesetzt; `CurrentSchemaVersion` stand bei 5). `GameVoiceAliases.json`
     = alte `VoiceExtractAliases.json` (36 Einträge). Config `GameVoiceReferenceEnabled` (aus) +
     `GameVoiceRankOnConversationStart` (an), noch ohne UI. **Offen für Phase 5:** §6 komplett
     (`ReferenceSource`-Markierung, Export-/Sync-Ausschluss) – bis dahin darf der Schalter nicht in die UI.
     Bewertungsschwellen (Tempo-Median, Gewichte, `MinScore`) sind Startwerte → Phase 8 an echten Clips.
5. **UI + Opt-in + Lokalisierung** (EN/DE/FR/JP), Voice-Clip-Manager-Kennzeichnung, Export-/Sync-Ausschluss.
   - ✅ 2026-11-09 umgesetzt (nicht committet, ingame ungetestet): §6 als **Schema v7**
     `voice_clip_generations.reference_source` (an der Generation statt am Clip – sie beschreibt die
     Datei), gesetzt nur bei angenommener Referenz; Ausschluss aus `.ekpack`-Export und Drive-Upload;
     abgeschaltet → Cache-Treffer mit Spielstimme wird neu erzeugt, Dialog „Clips löschen“ (Standard
     zuerst). Backend-Tab-Abschnitt mit Opt-in-Dialog (`GameVoiceConsent`, Textversion 1, Hash-Test),
     Remote-Hinweis, Status aus `Availability`, „Index aufbauen“ mit Fortschritt; VCM-Präfix
     „[Spielstimme]“; Changelog v0.19.3.3 + Checkliste (`gv-*`).
     **Offen:** pro NPC abschaltbar (§3), README/pluginmaster-Hinweis (F7).
6. **Tools**: ADPCM-only-Leser statt SaintCoinach-`ScdFile`, Opt-in, Auto-Löschen, Metadaten.
7. **Plugin**: lokales Modell laden, URL-Install-Prüfung.
8. Test-Checkliste (`Testing/checklist.json`) + ingame-Test mit echten Daten (Codec-Verteilung
   `cut/*/sound/*.scd` messen – offener Worklog-Punkt).

## 9. Entscheidungen (abgestimmt 2026-11-08)
- **F1** Teil A **und** D wie geplant (Zero-Shot im Plugin per Opt-in + privates Modell aus Tools).
- **F2** Exakter Name **plus gepflegte Alias-Liste**: `GameVoiceAliases.json` (eingebettet +
  lokale Überschreibung, Format wie die alte `VoiceExtractAliases.json` aus `485eb43^`) ordnet
  Namensvarianten („???“, Verkleidung, Titel) demselben Charakter zu. Nur Text, gilt als „derselbe
  NPC“; alles ohne Eintrag → Voice-Pack.
- **F3** Erzeugte Clips **cachen, nie exportieren** (§6 wie beschrieben).
- **F4** **Immer die neueste Epoche** (aktueller Sprecher).
- **F5** **Einmaliger Batch** „Index aufbauen“ (Button, Fortschrittsbalken, über
  `IBatchModeService` wie andere Bulk-Läufe) **plus Option** „Beim Gesprächsstart den aktuellen
  NPC sofort ranken“ (Standard an), falls er noch keinen Index hat. Ohne Index → Voice-Pack.
- **F6** **Neues kleines Repo** (netstandard2.0-Bibliothek, Arbeitsname `echoscd`, AGPL-3.0),
  eingebunden als Submodul in Plugin und Tools; eigene Tests dort.
- **F7** **Ja**: Hinweis in README und pluginmaster-Beschreibung (KI-Sprachausgabe, optional
  Spielstimme als Referenz, standardmäßig aus).
- **F8** **Jeder Server erlaubt, nur deutlicher Hinweis** „nur eigene private Instanz“ (§5.3).
  Fallback aufs Voice-Pack nur, wenn der Server die Inline-Referenz nicht kann. Ersetzt die
  frühere Vorgabe „nur LAN, sonst Voice-Pack“.

## 10. Ersatztext für die CLAUDE.md-Regel (angewendet 2026-11-08)

> ## Hard constraint: game audio only in memory, only opt-in, only local
> Echokraut never writes, converts to a file, or redistributes audio from the user's FFXIV
> installation. The only permitted use is `IGameVoiceReferenceService` (plan:
> `docs/plans/game-voice-reference.md`): with the user's explicit opt-in and the user's own
> EchokrauTTS instance (local or remote; the UI states it must be their own private one), voiced
> MS-ADPCM clips of **the same NPC** (exact name or alias list) are decoded in memory and passed
> as a zero-shot reference. OGG entries are skipped without reading their payload — no decryption code
> (XOR or otherwise) may exist in this repo. Only clip ids are persisted. Clips generated from a
> game reference are excluded from `.ekpack` export and Drive sync. The voice pack stays the
> default and the fallback. Anything beyond this (files on disk, other NPCs, AllTalk,
> training in the plugin) is still forbidden.
