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
     `5ef13d2`, Projekt `echotools/echoscd` im privaten GitLab – Remote noch anzulegen),
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

## 11. Phase 6: Index im Voraus + Sprecherwechsel nach Story-Fortschritt (entschieden 2026-11-15)

**Anlass (Live-Test 2026-11-15, Papalymo/Yda):** Die erste Zeile eines neu getroffenen NPCs sprach mit
der Paketstimme, weil das Ranking erst beim Treffen läuft (8–12 s, davon ~4 s Dekodieren von 40 Clips,
Rest Whisper). Zwischenlösung im Code (nicht committet): erste Zeile wartet ≤ 4 s auf eine schnelle
Stufe aus den 10 neuesten Clips. **Bekannter Mangel dieser Zwischenlösung:** schnelle Stufe (10) und
Verfeinerung (40) können aus verschiedenen Sprecher-Epochen wählen → Stimmwechsel zwischen Zeile 1 und 2.
Außerdem ist `GameVoiceActorSplits.json` ausgeliefert **leer** → Sprecherwechsel werden heute nie erkannt.

**User-Entscheidungen:**
- **F4 neu: die Epoche, die zum Story-Fortschritt des Spielers passt** — nicht mehr „immer die neueste“
  („wenn wir das schon angehen, dann richtig“).
- **Kommt ins laufende Release** (Plugin 0.19.3.3 / EchokrauTTS 0.0.2.0).
- Index **im Voraus für alle vertonten NPCs**, bedient wie der Dialog-Export.

### 11.1 Index unabhängig vom Mapping
- Neue Tabelle `game_voice_index`: Sprache, **Sprecher-Token** (aus dem Text-Index, z. B. `PAPALYMO`),
  Epoche, `audio_base`, Rang, Score, `index_version`, erstellt. Ersetzt die Zeilen je `character_id`
  (die gingen bei jedem Mapping-Reset verloren: Papalymo heute als Charakter 7, 11, 15).
- Live: Sprecher → Tokens wie heute (`CandidateLines`: BaseId/Instanzen/Name → englischer Name → Token),
  dann Index-Lookup. Gilt damit auch für nie getroffene, nicht in der DB stehende NPCs.

### 11.2 Batch „Spielstimmen-Index für alle“
- Läuft über **alle Sprecher-Tokens des Text-Index** (nur vertonte NPCs), nicht über DB-NPCs.
- Bedienung wie Dialog-Export: Sprachwahl, Fortschritt mit Restzeit, abbrechbar, **fortsetzbar**
  (Tokens mit aktueller `index_version` überspringen), nur wenn EchokrauTTS bereit ist, über
  `IBatchModeService`.
- Tempo: Dekodieren parallel über CPU-Kerne; Whisper + Embedding seriell. Option „schnell ohne Whisper“.
  Schätzung ~1 000–1 500 Tokens/Sprache: mit Whisper ~1–2 h, ohne ~15–30 min — erster Lauf misst.
- Patch: neuer Spielstand → nur geänderte Tokens neu (Clip fehlt/Text geändert, §2.6), Hinweis mit Anzahl.
- Live-Pfad liest danach nur den Index; die Wartelogik bleibt Rückfall für Tokens ohne Eintrag.

### 11.3 Sprecherwechsel automatisch erkennen
- Wrapper: `POST /speaker-embedding` (Audio rein, Vektor raus, nichts gespeichert, wie `/asr-check`),
  mit dem Sprecher-Encoder der aktiven Engine (Qwen x-vector, XTTS speaker encoder) — keine neue
  Abhängigkeit; `/health speaker_embedding: true`. Ohne Endpunkt: Verhalten wie heute (eine Epoche).
- Plugin: Clips eines Tokens nach Patch sortieren, Vektoren gruppieren; Grenze, wo die Ähnlichkeit zur
  vorigen Gruppe deutlich fällt → **Epochen** mit Patch-Grenzen. Jede Epoche bekommt im Index eigene
  Clips (Rang je Epoche). Handgepflegte `game_voice_actor_splits.json` gewinnt; erkannte Grenzen als
  Vorschlag dorthin schreiben (zum Prüfen/Teilen).
- Schwellen sind Startwerte → in Tests mit synthetischen Vektoren absichern, live an bekannten Fällen prüfen.

### 11.4 Epoche nach Story-Fortschritt wählen
- **Zu klären (Recherche zuerst):** wie der Fortschritt zuverlässig zu lesen ist (abgeschlossene
  Hauptszenario-Quests, z. B. `QuestManager.IsQuestComplete` über FFXIVClientStructs) und wie eine
  Clip-Epoche auf Fortschritt abgebildet wird (`audio_base` `vo_voiceman_XXYYY` → Patch/Quest-Bezug).
- Regel: die **späteste Epoche, deren Beginn der Story-Patch der Zeile erreicht**; vor der ersten
  bekannten Grenze → erste Epoche. Patch unbekannt → neueste (bisheriges Verhalten).
- **Story-Patch der Zeile, nicht der Höchststand des Spielers** (User-Entscheidung 2026-11-15 beim
  Live-Test): wer 6.0 im New Game+ nachspielt oder eine alte Nebenquest spät macht, hört in den
  vertonten Cutscenes die alte Stimme — die TTS-Zeilen dazwischen müssen dazu passen, sonst gibt es
  einen hörbaren Bruch. Reihenfolge: (1) angenommene Quest, deren Text die Zeile ist (Wortvergleich
  gegen das Quest-Textblatt), (2) angenommene Hauptquest, (3) höchster abgeschlossener Patch. Der
  frühere „nie absenken“-Speicher je Charakter ist entfallen.
- Fortschritt ändert sich im Spiel → Auswahl beim nächsten Satz neu (Index hat alle Epochen, kein Neu-Ranking).
- Gleiche Regel für den Live-Rückfall (schnelle Stufe nur innerhalb der gewählten Epoche → behebt den
  Mangel oben).

### 11.5 Index ↔ NPC zuordnen, wenn die Namen nicht passen (User-Hinweis 2026-11-15)
Sprecher-Tokens sind nicht immer namensgleich mit dem NPC (`MYSTERYVOICE` → G'raha Tia, `ICEHEART` →
Ysayle, `BLACKKNIGHT` → Golbez). Heute deckt das nur die kuratierte `Resources/GameVoiceAliases.json`
(+ lokale `game_voice_aliases.json`) ab — ohne Bedienung und ohne Überblick, was fehlt.
- **Bericht aus dem Batch** (wie `quest_alias_candidates.json` beim Dialog-Export), in
  `<localSaveLocation>/harvest/game_voice_unmatched_<lang>.json`:
  - Tokens ohne NPC: Token, Anzahl Clips, 3 Beispieltexte, **Vorschläge** (NPC-Namen nach Ähnlichkeit:
    normalisierter Teilstring/Levenshtein, NPCs aus denselben Quests/Cutscenes wie die Clips).
  - Tokens, die mehrere verschiedene Vollnamen beanspruchen (heute „gehört niemandem“, §2.2).
  - Live gesehene NPCs mit vertonten Zeilen-Kandidaten, aber ohne Token (aus dem Gesprächslog).
- **Ziel: das Matching gehört ins Repo, nicht zu jedem User** (User-Entscheidung 2026-11-15, „wie die
  Sprecherzuordnungen“). Gepflegt wird zentral, alle bekommen es automatisch — Muster wie
  `QuestNpcAliases.json` / `NpcRaces.json`:
  1. **Eingebettet** `Resources/GameVoiceAliases.json` (gibt es schon) — Stand beim Plugin-Release.
  2. **Remote** neu `RemoteUrls.gameVoiceAliasesUrl` → `raw.githubusercontent.com/RenNagasaki/Echokraut/
     main/Echokraut/Resources/GameVoiceAliases.json`, beim Start geholt, Fehler unkritisch (Rückfall auf
     eingebettet). So erreicht eine neue Zuordnung alle Nutzer **ohne Plugin-Release**, sobald sie auf
     `main` und im GitHub-Spiegel ist.
  3. **Lokal** `game_voice_aliases.json` gewinnt alles (eigene Korrekturen, Testen vor dem PR).
  Gleich für `GameVoiceActorSplits.json` (`gameVoiceActorSplitsUrl`): erkannte/korrigierte
  Sprechergrenzen (§11.3) werden genauso zentral ausgeliefert.
  Hinweis: `RemoteUrls.json` wird live von `main` gelesen — ein neues Feld stört alte Plugins nicht
  (unbekanntes Feld wird ignoriert), es muss aber erst NACH dem Plugin-Release benutzt werden.
- **Pflege-Werkzeug (für dich als Maintainer, nicht Pflicht für Nutzer):** Fenster „Spielstimmen
  zuordnen“ in den Game-Data-Tools (neben dem Dialog-Export): lädt den Bericht, je Eintrag Token,
  Clip-Anzahl, Beispieltexte, Vorschläge oben; Aktionen „diesem NPC zuordnen“ · „keine Spielstimme“
  (`"Disabled": true`) · überspringen; optional „Probe anhören“ (nur im Speicher dekodiert, §2.3).
  Ergebnis landet zuerst in der lokalen Datei (sofort wirksam zum Prüfen), dann **„Für das Repo
  exportieren“**: schreibt die neuen Einträge sortiert, ohne Dubletten, im Format von
  `Resources/GameVoiceAliases.json` nach `harvest/game_voice_aliases_for_repo.json` → von Hand in
  die Repo-Datei übernehmen und committen (kein automatisches Schreiben ins Repo).
- **Je NPC im Stimmen-Fenster** bleibt nur ein kleiner Override für Nutzer („Keine Spielstimme“ bzw.
  anderes Token), gespeichert lokal — für Sonderfälle, bis die zentrale Datei nachzieht.
- **Wirkung:** Index ist je Token (§11.1) → Zuordnen braucht kein Neu-Ranking, nur LRU-Eintrag des NPCs
  verwerfen; der nächste Satz nutzt die neue Stimme. Manuelle Zuordnung schlägt die Mehrdeutigkeitsregel.
  Batch-Bericht beim nächsten Lauf berücksichtigt die zentrale Datei → was gematcht ist, verschwindet.
- Tests: Vorschlags-Heuristik (Teilstring/Levenshtein), Disabled-Eintrag, Reihenfolge eingebettet <
  remote < lokal, Remote-Fehler → eingebettet, Repo-Export (sortiert, dublettenfrei, Format wie
  Ressource), Lookup nach Zuordnung ohne Neu-Ranking.

### 11.6 Recherche: deutsche Sprecherwechsel (Auftrag vom User, 2026-11-15)
Liefert die **geprüften Grenzen** für `GameVoiceActorSplits.json` (zentral ausgeliefert, §11.5) und die
Prüffälle für die automatische Erkennung (§11.3) — die Erkennung muss diese Wechsel wiederfinden.
Auftrag wörtlich, nur der Abschnitt „Ausgabe“ ist auf die bestehende Logik angepasst:

> **Ziel:** Möglichst vollständige, belegte Liste aller FFXIV-Charaktere, deren **deutsche**
> Synchronstimme gewechselt hat, je mit: Charaktername (deutsch, ggf. englisch in Klammern), alter
> Sprecher + letzter Patch mit alter Stimme, neuer Sprecher + erster Patch mit neuer Stimme, ungefähres
> Datum (aus dem Patch-Release-Datum), Quelle(n) als URL, Sicherheit „bestätigt“ (mehrere Quellen /
> eindeutig) oder „unsicher“.
>
> **Vorgehen:**
> 1. Hauptquelle vollständig auswerten: https://spielestimmen.de/spiele/final-fantasy-xiv-online/ —
>    alle Einträge mit „bis zu …“ / „seit …“ in Haupt- und Nebenrollen; zusätzlich Charaktere mit
>    mehreren gelisteten Sprechern.
> 2. Gegenprüfen mit: Square-Enix-Forum (DE), „Deutscher FFXIV-Synchro-Cast“
>    https://forum.square-enix.com/ffxiv/threads/372307 und „Synchronsprecher Endwalker“
>    https://forum.square-enix.com/ffxiv/threads/468722; Interview Pamela Drobig
>    https://crystaluniverse.de/interviews/ffxiv-interview-mit-pamela-drobig/; IMDb-Besetzungslisten der
>    Erweiterungen (German version); ffxiv.consolegameswiki.com („Voiced by (DE)“); ffxiv.sevengamer.de;
>    Synchronkartei / Synchron-Forum, falls vorhanden.
> 3. Patch-Nummern mit offiziellen Release-Daten abgleichen (Lodestone / Wikis).
> 4. Neue Funde, die nicht in den Startdaten stehen, ergänzen und kennzeichnen.
>
> **Startdaten** (spielestimmen.de, sofern nicht anders angegeben — bitte verifizieren):
>
> | Charakter | Alte Stimme (bis) | Neue Stimme (ab) | ca. Zeitpunkt | Status |
> |---|---|---|---|---|
> | Urianger Augurelt | Alexander Wilz (bis 2.1) | Florian Hoffmann (ab 2.1) | ~Dez 2013 | bestätigt |
> | Alisaie Leveilleur | Annette Gunkel (bis 2.4) | Ann-Cathrin Schaible (ab 2.4) | ~Okt 2014 | bestätigt |
> | Minfilia Warde | Djuwita Müller (bis 2.5) | Ann Vielhaben (ab 3.1) | ~Ende 2015 | bestätigt |
> | Yda / Lyse Hext | Henrike Krügener (bis 3.5) | Paulina Weiner (ab 4.0) | ~Jun 2017 | bestätigt |
> | Matoya | Ingrid Mülleder (bis 3.5) | Ulrike Hübschmann (ab 4.1) | ~Okt 2017 | bestätigt |
> | Biggs | Gordon Piedesack (bis 4.5) | Thomas Dehler (ab 5.0) | ~Jul 2019 | bestätigt |
> | Hydaelyn (ab 6.0 Venat/Hydaelyn) | Sonngard Dressler (bis 5.5) | Antje von der Ahe (ab 6.0) | ~Dez 2021 | bestätigt |
> | Y'shtola Rhul | Annette Schreier (bis 6.1) | Birge Tetzner (ab 6.1) | ~Apr 2022 | bestätigt (auch Forum, Apr 2022) |
> | Brutalo Bomber | Andreas Wisniewski | David Hörning (ab 7.56) | 2026? | unsicher (evtl. zwei Auftritte, kein echter Wechsel) |
>
> Ohne Wechsel gelistet (zur Kontrolle): Alphinaud = Markus Haase, Thancred = Peter Lehn.
>
> **Regeln:** Keine Angaben aus dem Gedächtnis ohne Quelle; Unbelegtes als „unsicher“ markieren.
> Erstmal nur die deutsche Fassung — Englisch, Französisch und Japanisch später als eigene Aufgabe.

**Ausgabe (angepasst an die bestehende Logik):**
1. **Recherche-Dokument** `docs/research/game-voice-actor-splits-de.md` (im Plugin-Repo): die Tabelle
   wie oben plus Spalte **Grenz-Token**, Abschnitt „Offene Fragen“, Quellen je Zeile.
2. **Einträge im Format von `Echokraut/Resources/GameVoiceActorSplits.json`** (Schema `version: 2`,
   gelesen von `echoscd/EchoScd/VoiceActorSplits.cs`) — KEINE eigene JSON-Datei mit eigenem Schema:
   ```json
   { "npcName": "Y'shtola", "language": "DE", "boundaryPatches": ["06100"],
     "comment": "Annette Schreier -> Birge Tetzner ab 6.1 (~2022-04); bestätigt; <Quelle-URL>" }
   ```
   - `npcName` = Name **wie im deutschen Client angezeigt** (getrimmt, Groß/klein egal) — nicht der
     englische; mehrere Namen eines Charakters (Yda / Lyse) = je ein Eintrag.
   - `boundaryPatches` = **erster Patch der neuen Stimme als 5-stelliges Token** (drittes `_`-Segment
     der Audio-Basis, `vo_voiceman_06100_…` → `"06100"`; **Korrektur 2026-11-15:** `XXYZZ` = Erweiterung,
     Patch, Teil — 6.1 ist `06100`, `06010` wäre 6.0 Teil 10), streng aufsteigend; mehrere Wechsel = mehrere
     Grenzen. Patchnummer ≠ Token ist möglich → das Token **am Spielstand prüfen** (Text-Index: welche
     `vo_voiceman_XXYYY` hat der Sprecher, und ab welchem klingt die neue Stimme); nicht prüfbar →
     „unsicher“.
   - Datum, Sprecher, Status und Quelle stehen in `comment` (Freitext, wird ignoriert) und im
     Recherche-Dokument; Alter/neuer Sprecher sind für die Logik nicht nötig.
   - **Nur „bestätigt“ + geprüftes Token** kommt in die Repo-Datei; „unsicher“ bleibt im
     Recherche-Dokument (offene Fragen).
3. Kurze Zusammenfassung im Chat: neu, korrigiert, noch unsicher.

**Verwendung:** Die Epochen speisen §11.4 (Auswahl nach Story-Fortschritt; Patch-Grenze → Fortschritt
„hat Patch X erreicht“) und sind die Soll-Werte für die Erkennung aus §11.3 (Test: jede bestätigte
Grenze wird gefunden; Alphinaud/Thancred ergeben **keine** Grenze).

### 11.7 Reihenfolge, Tests, Doku
1. 11.4-Recherche (Fortschritt lesbar? Abbildung Clip → Patch) und 11.6 (deutsche Sprecherwechsel,
   unabhängig, kann parallel laufen) — bestimmen die Grenzen und die Soll-Werte für 11.3.
2. 11.1 Tabelle + Lookup (Migration, alte `game_voice_references` verwerfen → einmal neu ranken).
3. 11.2 Batch + UI (Backend-Tab, neben „Index aufbauen“, der dann diesen Batch startet).
4. 11.3 Wrapper-Endpunkt (Tests mit Fake-Encoder) + Gruppierung im Plugin.
5. 11.4 Auswahl nach Fortschritt.
6. 11.5 Remote-Laden der Alias-/Split-Dateien, Bericht, Pflege-Fenster + Repo-Export, kleiner
   Nutzer-Override (Bericht entsteht im Batch aus 3.; UI lokalisiert DE/EN/FR/JA). `RemoteUrls.json` erst
   nach dem Plugin-Release um die neuen URLs ergänzen (Release-Reihenfolge im Testplan nachziehen).
7. `IndexVersion` erhöhen, Release-Testplan (R2) + Tester-Checkliste, Changelog DE/EN, Doku
   (`docs/claude/services/game-voice-and-packages.md`, Wrapper `docs/claude/http-api.md`).

### 11.8 Umsetzung und Messbefunde (2026-11-15)
**Umgesetzt** (Plugin, Wrapper; `node .claude/verify.js` in beiden Repos grün):
- 11.1 `game_voice_index` (Schema v8, je Sprache + Token + Epoche, `lines_hash`), alte Tabelle verworfen.
- 11.2 Batch über alle Tokens des Text-Index: fortsetzbar, abbrechbar, Restzeit, „schnell ohne Whisper“,
  paralleles Dekodieren, Hinweis „N Sprecher ohne aktuellen Index“. Live nur noch Rückfall.
- 11.3 Wrapper `POST /speaker-embedding` + `/health speaker_embedding/speaker_encoder` (Qwen x-vector,
  XTTS-Sprecher-Encoder; F5/MOSS/Pocket: 501). Plugin `SpeakerChangeDetector`.
- 11.4 `StoryProgressService` (`ScenarioTree` + `IUnlockState`), Epochenwahl `GameVoiceEpochs`; die
  schnelle Stufe arbeitet jetzt innerhalb der gewählten Epoche (Mangel der Zwischenlösung behoben).
- 11.5 `GameVoiceMappingService` (eingebettet < remote < lokal), Bericht `game_voice_unmatched_<lang>.json`,
  Abschnitt „Spielstimmen zuordnen“ in den Game Data Tools mit Repo-Export, Nutzer-Override im NPC-Fenster.
- 11.6 Recherche `docs/research/game-voice-actor-splits-de.md`; Datei: Y'shtola `06100`, Hydaelyn
  `06000`, Biggs `05000` (DE).

**Abweichungen vom Plan (Befund, nicht Geschmack):**
- **Erkannte Grenzen werden NICHT automatisch angewendet**, nur als Vorschlag geschrieben
  (`harvest/game_voice_actor_splits_suggested_<lang>.json`). Messung 978 deutsche Clips, 11 Sprecher:
  Qwens x-vector trennt nicht (Y'shtola 0,41 gegen Fehlalarm 0,39), der XTTS-Encoder knapp (0,65 /
  0,36 gegen 0,28). Rohe Vektoren liegen alle bei ~0,98 Kosinus, erst die Zentrierung macht sie
  brauchbar; der jeweils neueste Patch wirkt immer „anders“ (Aufnahmesession). Zuverlässig ist die
  handgepflegte, zentral ausgelieferte Datei — die Erkennung ist Werkzeug für die Pflege.
- **Epoche nach dem Story-Patch der Zeile statt nach dem Höchststand** (Live-Test 2026-11-15, User):
  NG+ und spät nachgeholte alte Quests müssen zur Stimme ihrer Cutscenes passen. `StoryContextResolver`
  (Zeile → angenommene Quest per Wortvergleich → angenommene MSQ → abgeschlossener Stand),
  `StoryProgressTable.KeyForQuest`; der „nie absenken“-Speicher `GameVoiceStoryProgress` ist entfernt.
  Log je Zeile mit Sprecherwechsel: `Game voice for '…': <Token:Epoche> — story <Patch> (<Quelle>)`.
  Live nachgeschärft: Zeilen VOR der Questannahme (Sequenz 0, NG+-Kapitelstart) fanden keine
  angenommene Quest → Suche über die Texte ALLER Quests (Wort-Tripel, `LineQuestNotAccepted`),
  NG+ „Aufbruch zu neuen Horizonten“ → `Pre06100` bestätigt. Patch einer Nebenquest = wo sie
  freigeschaltet wird (Vorgänger-Kette zur Hauptquest; eine 6.2-Nebenquest nach dem 6.0-Finale zählt
  als 6.0, User-Entscheidung), Quest-ID nur ohne Kette.
- **Gleicher Vorname, ein Name ist exakt das Token** (Live-Befund Estinien, User): bisher „mehrdeutig“
  → keine Spielstimme. Jetzt besitzt der exakte Name das Token, die längeren Namen („Estinien
  Wyrmblood“) sprechen mit. Betrifft im Datenbestand auch Nero und Sphene.
- „Probe anhören“ im Pflege-Fenster fehlt (Abspielen aus dem Speicher wäre ein eigener Pfad).
- Epoche je Token, nicht je NPC: ein NPC mit mehreren Tokens wählt je Token; Splits werden unter dem
  Token oder einem Client-/Englischnamen des Besitzers gefunden.

**Live-Befund 2026-11-15 (Estinien, Qwen 1.7B):** die vorläufige Referenz der schnellen Stufe (ohne
Whisper, 2 Clips/12,3 s) klang SEHR anders als die endgültige (1 Clip/7,9 s) — der erste Dialog eines
neuen NPC wirkt wie ein Rückfall. Nach dem Batch tritt die schnelle Stufe nur noch für unbekannte NPCs
auf; ob sie strenger filtern muss, nach dem Batch-Test entscheiden.

**Live-Test R2 am Windows-PC (2026-11-15, RTX 5090, DE-Client, Qwen 1.7B T 0,7):**
- **Batch mit Whisper:** 673 Tokens, 667 bewertet, 547 mit Spielstimme, 357 ohne Zuordnung
  (`harvest/game_voice_unmatched_de.json`), **30:31 min** (Schätzung war 1–2 h). Der Lauf **ohne
  Whisper wurde nicht gemessen** (User-Entscheidung): die Option spart gegenüber 30 min wenig und wählt
  schlechter (genau das ist die schnelle Stufe, die live „SEHR anders" klang). Vorschlag: Option
  streichen, sobald auch XTTS mit Whisper vertretbar lief.
- **Stimmwechsel mitten in der Zeile** = Qwen sampelt jeden Satz-Chunk unabhängig (255 Zeichen → 2
  Chunks). Wrapper: `QwenWorker.max_chars_per_chunk = 1000`, eine Zeile = ein Sampling-Lauf.
- **Streuung je Erzeugung, nicht Referenz:** dieselbe Referenz (identischer `inline:`-Hash) klang mal
  richtig, mal „komplett falsch". Messung über `/speaker-embedding` (zentriert): kalt nach Neustart
  0,67–0,75, warm 0,66–0,72 → **kein Anlaufeffekt**. Blinder Hörtest Estinien, je 8 Erzeugungen:
  T 0,7 + 1 Clip (7,9 s, Stand bis dahin) **2/8 schlecht**; T 0,5 + 1 Clip, T 0,5 + 2 Clips, T 0,7 + 2
  Clips (15,1 s) **alle gut**. Zwei Clips scheiterten nur an der 15-s-Grenze (7,9 + 0,3 + 6,9 s).
  → **Grenze je Engine** (User-Entscheidung A): Worker deklariert `max_inline_ref_seconds` (Qwen 20 s,
  sonst `inline_ref_max_seconds` 15 s — F5s Textbudget ist `22 - ref_s`), `/health
  inline_ref_max_seconds`, das Plugin baut die Referenz danach (ältere Wrapper: 15 s). Live: Estinien 2
  Clips, „klappt verdammt gut".
- **Antwort-Cache als Plugin-Option** (Hörtests brauchen frische Erzeugungen): Checkbox → Installer-Arg
  → `--response-cache-minutes 0`, angehakt bleibt `config.json` des Wrappers maßgeblich.
- **Sprecherwechsel-Erkennung mit Qwen** (Batch oben): Vorschläge Amon `06005`, G'raha Tia (Token
  `mysteryvoice`) `05000`; Y'shtola/Hydaelyn **nicht** erkannt — deckt sich mit der Messung oben.
- **Sprecherwechsel-Erkennung mit XTTS** (Batch mit Whisper, Index aktuell → nur Erkennung, **7:11 min**):
  Y'shtola `06100` (Abstand 0,61, 30|13 Patches) und Hydaelyn `06000` (0,35) erkannt, **keine**
  Alphinaud/Thancred-Grenze — Erwartung erfüllt. Zusätzlich Hythlodaeus `06004` (0,79, nur 3|3 Patches)
  und „Krieger der Dunkelheit" `05000` (0,32) → anhören. Befund: der NpcName trägt noch den Platzhalter
  („Krieger[p] der Dunkelheit"). Der `harvest`-Ordner folgt „lokal speichern" (an: `LocalSaves\harvest`,
  aus: `<Installationsordner>\harvest`) — beim Suchen der Berichte beachten.
- **UI-Befunde:** (1) nach einem Plugin-Neuladen bleibt der Spielstimmen-Status auf „prüfe den
  Server…" und der Index-Button gesperrt, bis eine Zeile gesprochen wird; (2) mit einem weiteren
  Bedienelement im Backend-Tab werden die letzten Checkboxen/Buttons des Spielstimmen-Bereichs nicht
  mehr gezeichnet (Gegenprobe: Cache-Checkbox entfernt → wieder da). Behoben: Spielstimmen-Bereich als
  ausklappbarer Bereich oben im Tab „Stimmen“ (User-Entscheidung, kein eigener Tab); der Status fragt
  `/health` selbst, sobald er „unbekannt“ ist.
- **Test 1 bestanden:** ein nie getroffener NPC spricht sofort mit Spielstimme.
- **Nach jedem Engine-Wechsel erst „fremde“ Stimme** (Tataru, G'raha): laut Log gingen die ersten
  Zeilen OHNE Referenz raus, also Stimmenpaket. Zwei Ursachen: (1) die Probe galt weiter für die alte
  Engine — Qwen 20 s an Pocket → `400`, Zeile fällt aufs Paket; (2) ein `/health` während des Neustarts
  (Verbindung verweigert) wurde als „kein Spielstimmen-Support“ gespeichert und erst nach `ProbeTtl`
  (5 min) neu gefragt — 17:10–17:14 sprach jede Zeile aus dem Paket, auch nach dem Wechsel zu Qwen.
  Behoben: `OnInstanceReady` → Probe und Referenz-Cache verwerfen, sofort neu fragen; nicht
  erreichbar = `Unknown` (nächste Zeile fragt wieder), nicht `ServerUnsupported`.
- Offen außerhalb dieses Tests: Qwen-Betonung (teils kein Satzende, „Ruinen" → „Ruh-Inen").

**Live-Test im Spiel abgeschlossen (User, 2026-11-16).** **Offen:**
`RemoteUrls.json` nach dem Plugin-Release um `gameVoiceAliasesUrl`/`gameVoiceActorSplitsUrl` ergänzen,
Minfilia (4 Klanggruppen, Ryne heißt in 5.0 auch „Minfilia“) und Matoya (`04500` anders, `05400` wieder
alt) anhören und entscheiden; EN/FR/JA-Recherche.
