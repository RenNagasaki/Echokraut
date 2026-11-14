# Machbarkeit: Stimmpakete für Deutsch, Französisch und Japanisch

## Status

**Recherche, kein Code.** Erstellt 2026-08-11 auf Wunsch des Users („kannst du gucken, ob du in der
Lage bist, analog zum bisherigen Voicepack Versionen in DE/FR/JA zu finden/erstellen — mit denselben
Kriterien? Das englische hat einen harten Dialekt in Deutsch").

Was am Code/an Datenblättern geprüft ist, steht mit Quelle. Was ich **nicht** verifizieren konnte,
ist als solches markiert.

## Kurzantwort

**Deutsch: ja, gut machbar. Französisch: machbar, aber schwächere Quellenlage. Japanisch: machbar,
aber nur zur Hälfte der Sprecherzahl und unter einer anderen Lizenz.**

⚠ **Wichtiger als die Stimmen: das Plugin kann derzeit gar keine sprachspezifischen Pakete
unterscheiden.** Siehe „Der eigentliche Blocker" — das Beschaffen der Audios ist die leichtere
Hälfte.

## Was „dieselben Kriterien" konkret heißt (aus dem Code gelesen)

Aus `VoicePack/build_adults.py` und `VoicePack/verify_pack.py`:

| Kriterium | Wert |
|---|---|
| Sprecher | **100 je Geschlecht** (`WANT_PER_GENDER = 100`) ⇒ 200 Erwachsene |
| Clips je Stimme | **6** (`CLIPS_PER_VOICE = 6`) |
| Rauschabstand | **≥ 30 dB** (`MIN_SNR_DB = 30.0`) |
| Mindestdauer | **≥ 2,0 s**, Übersteuerung ≤ 0,1 % |
| Ausgabeformat | **24 kHz, mono, PCM 16** + gleichnamige `.txt` mit Transkript |
| Quelle | LibriTTS-R, **CC BY 4.0**, 24 kHz |
| Kinder | 60 Stück, aus **erwachsenen Frauenstimmen** per Pitch-/Formant-Shift erzeugt |

## Befund je Sprache

| Sprache | Bester Kandidat | Lizenz | Rate | Sprecher | Urteil |
|---|---|---|---|---|---|
| **DE** | HUI-Audio-Corpus-German | **CC0** | 44,1 kHz | 122 | **gut** |
| DE (ergänzend) | MLS German | CC BY 4.0 | **16 kHz** | 176 (81m/95w) | Rate zu niedrig |
| DE (auffüllen) | Common Voice de | **CC0** | 48 kHz MP3 | sehr viele | Qualität streut |
| **FR** | MLS French | CC BY 4.0 | **16 kHz** | 142 (62m/80w) | Rate zu niedrig |
| FR (auffüllen) | Common Voice fr | **CC0** | 48 kHz MP3 | viele | Qualität streut |
| **JA** | JVS | **CC BY-SA 4.0** | 24 kHz | **100 gesamt** | Lizenz + Zahl |
| JA (auffüllen) | Common Voice ja | **CC0** | 48 kHz MP3 | deutlich weniger als de/fr | dünn |

### Deutsch — der klare Fall

**HUI-Audio-Corpus-German**: 122 Sprecher, 326 Stunden, **CC0**, **44,1 kHz**, aus LibriVox
aufbereitet, ausdrücklich für TTS gebaut (saubere Audio-Text-Ausrichtung). CC0 ist sogar *freier* als
die heutige CC BY 4.0 — keine Namensnennungspflicht. 44,1 kHz heißt **herunterrechnen** auf 24 kHz,
nicht hochrechnen; das ist der qualitativ richtige Weg.

122 < 200. Aufgefüllt wird aus **Common Voice de** (CC0, sehr groß), wobei der `MIN_SNR_DB = 30`-Filter
die eigentliche Arbeit macht — Common Voice ist Crowd-Material mit stark schwankender Aufnahmequalität.

⚠ **Überschneidungsgefahr**: HUI **und** MLS German stammen beide aus LibriVox. Dieselben Vorleser
können in beiden vorkommen. Falls beide Quellen genutzt werden, muss über den Vorlesernamen
dedupliziert werden — sonst stehen zwei „verschiedene" Stimmen im Paket, die dieselbe Person sind.
Genau diese Falle gab es beim englischen Paket schon einmal in anderer Form (200 Erwachsene waren
bereits vergeben, deshalb blieben für die Kinder nur 58 Sprecher übrig).

### Französisch — machbar, aber ohne Glanzstück

Kein französisches Gegenstück zu HUI gefunden. **MLS French** ist lizenzsauber (CC BY 4.0) und hat
142 Sprecher, liegt aber bei **16 kHz** — für eine Referenzstimme, die geklont wird, ist das ein
echter Nachteil: die fehlenden oberen Frequenzen kommen durch Hochrechnen nicht zurück, und
LibriTTS-R ist ja gerade deshalb 24 kHz.

Realistisch: **Common Voice fr** (CC0, 48 kHz) als Hauptquelle mit strengem Filter, MLS fr als
Ergänzung, wenn 16 kHz akzeptabel ist.

### Japanisch — der Problemfall

**JVS**: 100 Sprecher, 30 Stunden, gezielt für Sprachsynthese gebaut, **kommerzielle Nutzung
ausdrücklich erlaubt**, 24 kHz (48 kHz/24 Bit auf Anfrage für kommerzielle Nutzung). Technisch ideal.

Zwei Haken:

1. ⚠ **Lizenz CC BY-SA 4.0 — „ShareAlike".** Das heutige Paket ist CC BY 4.0. ShareAlike verlangt,
   dass **Bearbeitungen** unter derselben Lizenz weitergegeben werden — und die Kinderstimmen sind
   per Pitch-Shift genau das: Bearbeitungen. Eine bloße Sammlung verschiedener Lizenzen in einem Zip
   ist in der Regel unproblematisch („mere aggregation"), die abgeleiteten Kinderstimmen sind es
   nicht. **Das ist eine Entscheidung, die du treffen musst, keine technische Frage.**
2. **100 Sprecher gesamt**, nicht je Geschlecht — also etwa die Hälfte des Ziels. Common Voice ja ist
   deutlich kleiner als de/fr und füllt die Lücke nur teilweise.

## ⚠ Der eigentliche Blocker: das Plugin kennt keine Sprache bei Stimmen

Am Code geprüft — und das ist der Grund, warum die Beschaffung die leichtere Hälfte ist:

- **`EchokrautVoice` hat kein Sprachfeld.** Es gibt `IsAdultVoice`, `IsChildVoice`, `IsElderVoice`,
  `UseAsRandom`, erlaubte Rassen und Geschlechter — **keine Sprache**.
- **Die Dateinamen-Grammatik hat keine Sprachkomponente**: `Gender_RacePool[-BodyType]_NPCnnn.wav`.
  `BackendService.MapVoices` leitet daraus Geschlecht, Rassen und Alter ab, sonst nichts.
- **`voicePackUrl` in `RemoteUrls.json` ist EIN String**, kein Wörterbuch.

⇒ Vier Sprachpakete lassen sich heute weder herunterladen noch auseinanderhalten. Vorhandenes
Vorbild direkt daneben: **`voiceNameUrls` ist bereits ein Wörterbuch je Sprache** — dieselbe Form auf
`voicePackUrl` anzuwenden, ist der naheliegende Weg.

Zu klären ist außerdem, was beim Sprachwechsel passieren soll: Der lokale EchokrauTTS-Wrapper **lädt
ein Modell je Lauf** und lehnt eine abweichende Sprache je Anfrage ab (deshalb wird `language` im
`/tts` bewusst weggelassen, `EchokrauTtsBackend.cs:29-30,133`). AllTalk dagegen bekommt die Sprache
je Anfrage mit (`AlltalkBackend.cs:57`). Ein sprachspezifisches Paket muss also mindestens zum
Client-Sprachstand passen — und die NPC-Stimmzuordnungen hängen an Dateinamen, die sich beim Wechsel
ändern würden.

## Kinderstimmen: kein neues Beschaffungsproblem

Die 60 Kinderstimmen wurden nicht gefunden, sondern **erzeugt** — aus erwachsenen Frauenstimmen per
Praat `Change gender...` (Formant-Ratio + Ziel-Pitch + Range-Faktor). Dasselbe Verfahren funktioniert
unverändert auf deutschen, französischen und japanischen Frauenstimmen und **erbt deren Akzent
automatisch**. Für die Kinder muss also nichts zusätzlich beschafft werden — `build_children.py` und
`score_children.py` sind wiederverwendbar, nur die Quellenliste wechselt.

## Empfohlener Weg

1. **Deutsch zuerst** — beste Quellenlage, und es ist die Sprache, deren Akzentproblem gemeldet wurde.
   HUI (CC0, 44,1 kHz) als Kern, Auffüllung aus Common Voice de, Dedup gegen LibriVox-Doppelgänger.
2. **Vorher oder parallel: die Sprachdimension im Plugin.** Ohne sie ist ein deutsches Paket nicht
   ausspielbar. Das ist der kritische Pfad, nicht das Audio.
3. **Französisch danach**, Common-Voice-getrieben.
4. **Japanisch zuletzt** und erst nach deiner Lizenzentscheidung zu CC BY-SA.

Grobe Schätzung, sobald die Quellen feststehen: **je Sprache 1–2 Tage** Bauzeit (die Pipeline
existiert), plus **1–2 Tage** für die Sprachdimension im Plugin.

## Was ich NICHT verifiziert habe

- **Genaue Sprecherzahlen und Stundenzahlen von Common Voice je Sprache** — die Übersichtsseite gab
  sie nicht her. Dass de/fr groß und ja deutlich kleiner ist, ist eine begründete Annahme, keine
  Messung.
- **Geschlechterverteilung in JVS** (grob hälftig erwartet) und in HUI.
- **Wie viele Sprecher den `SNR ≥ 30 dB`-Filter je Quelle überstehen.** Beim englischen Paket war
  genau das die Engstelle — bei Common Voice erwarte ich eine deutlich höhere Ausfallrate als bei
  LibriTTS-R. **Das ist die Zahl, die über Machbarkeit entscheidet, und sie lässt sich nur durch
  einen Probelauf ermitteln.**
- Ob HUI und MLS German tatsächlich Sprecher teilen (nur als Risiko hergeleitet, nicht geprüft).

## Offene Fragen an dich

1. **CC BY-SA für Japanisch akzeptabel?** Wenn nein, fällt JVS weg und Japanisch wird deutlich
   dünner.
2. **Ein Paket je Sprache oder ein großes gemischtes?** Getrennt ist kleiner im Download und
   erlaubt, nur die eigene Sprache zu ziehen — verlangt aber die Sprachdimension im Plugin.
3. **Sollen die deutschen Stimmen die englischen ersetzen oder ergänzen?** Ersetzen ändert bei
   bestehenden Nutzern die Stimmen aller NPCs; ergänzen verdoppelt die Auswahl und braucht eine
   Filterregel.
4. **Ist 24 kHz zu halten?** Falls ja, scheiden MLS de/fr als Hauptquelle aus (16 kHz).

## Quellen

- MLS: <https://www.openslr.org/94/> · <https://arxiv.org/abs/2012.03411>
- HUI-Audio-Corpus-German: <https://arxiv.org/abs/2106.06309> · <https://github.com/iisys-hof/HUI-Audio-Corpus-German>
- JVS: <https://arxiv.org/abs/1908.06248> · <https://sites.google.com/site/shinnosuketakamichi/research-topics/jvs_corpus>
- Common Voice: <https://commonvoice.mozilla.org/en/datasets>

## ⚠ KORREKTUR meiner eigenen Lizenzangabe (2026-08-11, nach der Entscheidung „nur Deutsch")

**Ich hatte HUI oben mit „CC0" in die Tabelle geschrieben. Das war nicht belegt.** Die Angabe stammte
aus einer Suchmaschinen-Zusammenfassung, nicht aus der Quelle. Nachgeprüft:

| geprüft | Aussage zur Datenlizenz |
|---|---|
| Downloadseite `opendata.iisys.de` | **keine** |
| Paper-Abstract (arXiv 2106.06309) | **keine** (das CC-BY-SA-Symbol dort gilt dem *Paper*) |
| GitHub-README | **keine** |
| GitHub-Repo-Lizenz | Apache-2.0 — das ist der **Code** der Pipeline, nicht die Audios |

⇒ **HUI selbst nennt nirgends eine Datenlizenz.** Für ein öffentlich verteiltes Paket, dessen ganzer
Zweck nachprüfbare Lizenzlage ist, wäre das ein Blocker.

**Die Grundlage kommt stattdessen von der Quelle: LibriVox.** LibriVox veröffentlicht alle Aufnahmen
als Public-Domain-Widmung, ausdrücklich auch für kommerzielle Nutzung und **ohne**
Namensnennungspflicht. Das ist tragfähiger als CC0 es wäre — und es ist derselbe Boden, auf dem das
englische Paket schon steht (`attribution_adults.json` nennt „Google LLC (LibriTTS-R); LibriVox
volunteer readers").

⚠ **Zwei Einschränkungen, die ich nicht kleinreden will** (und ich bin kein Jurist):
1. LibriVox sagt „public domain **in den USA**, nicht zwangsläufig in anderen Ländern". Für eine
   Verteilung aus Deutschland heraus ist zusätzlich relevant, ob die **Vorlagen** (die vorgelesenen
   Bücher) auch hier gemeinfrei sind — deutsche Schutzfrist ist 70 Jahre nach Tod des Urhebers.
2. Eine echte „Public-Domain-Widmung" kennt das deutsche Recht nicht; CC0 ist dort der übliche Ersatz.

**Praktische Folge für den Aufbau:** HUI als *bequeme Vorverarbeitung* von LibriVox behandeln, aber
die Herkunft je Sprecher bis zur LibriVox-Aufnahme zurückverfolgen und **so** attributieren — nicht
„HUI, CC0" schreiben, sondern Vorleser + LibriVox-Quelle, wie es das englische Paket bereits tut.

## Deutsch: konkreter Stand (2026-08-11)

**Entscheidung des Users: erstmal nur Deutsch.**

Gemessene Fakten zum Bezug:

- Quelle: `opendata.iisys.de/opendata/Datasets/HUI-Audio-Corpus-German/dataset_clean/others_Clean.zip`
- **14,8 GB**, `Accept-Ranges: bytes` ⇒ **fortsetzbar und jederzeit abbrechbar**
- Die fünf großen Einzelsprecher (Bernd Ungerer, Hokuspokus, Friedrich, Karlsson, Eva K) liegen
  separat; die **~117 übrigen Sprecher stecken alle in `others_Clean.zip`** — das ist das Archiv,
  auf das es für Stimmenvielfalt ankommt. Die fünf großen sind für ein Paket mit 200 verschiedenen
  Stimmen fast wertlos (viel Material, aber nur fünf Stimmen).
- „Clean" = engere Qualitätsauswahl derselben Sprecher (15 GB statt 23 GB).
- Alles 44,1 kHz ⇒ **herunterrechnen** auf die geforderten 24 kHz.

⚠ **Plattenplatz ist knapp: 57 GB frei auf `F:` (89 % belegt).** Zip 15 GB + entpackt geschätzt
20–25 GB = 35–40 GB. Es geht, aber ohne Reserve. **Deshalb: Download läuft, Entpacken NICHT** — das
entscheidest du, wenn du wieder da bist. Der Download liegt in
`F:/Git-Repositories/Dalamud/VoicePack/stage_de/` (kein git-Repo, also keine Gefahr, 15 GB zu
committen).

⚠ **Offene technische Frage: HUI liefert offenbar keine Geschlechtsangabe je Sprecher.** Das Paket
braucht aber 100 männliche und 100 weibliche Stimmen. Lösbar ohne neue Quelle: **das Geschlecht über
die gemessene Grundfrequenz ableiten** — genau das Verfahren, das beim englischen Paket schon für die
Kinderauswahl benutzt wurde (F0-Schwelle statt Korpus-Metadaten, weil die Metadaten unzuverlässig
waren). `score_children.py` und `audiokit.py` sind dafür wiederverwendbar.

**Die entscheidende Zahl bleibt ungemessen:** wie viele der ~117 Sprecher nach `SNR ≥ 30 dB`,
`≥ 2 s`, `Clipping ≤ 0,1 %` und **je Geschlecht** übrig bleiben. Erst der Probelauf auf dem
entpackten Archiv sagt, ob 100/100 erreichbar sind oder ob aus Common Voice aufgefüllt werden muss.

## PROBELAUF GEMESSEN (2026-08-11) — die entscheidende Zahl liegt vor

Download vollstaendig (14,8 GB, Byte-genau). **Ohne Entpacken** gemessen: das Zip-Inhaltsverzeichnis
gibt die Sprecherliste her, und einzelne Mitglieder lassen sich direkt aus dem Archiv lesen. Skript:
`VoicePack/probe_de.py`, Rohdaten `VoicePack/probe_de.json`. Gemessen wurde mit **derselben**
`audiokit.quality()`-Funktion, die auch das englische Paket benutzt — die Zahlen sind also
vergleichbar und nicht neu erfunden.

**Archivstruktur:** `<Vorleser>/<Buch>/wavs/*.wav` + `metadata.csv`, 24.244 WAVs, 117 Ordner auf
oberster Ebene, davon **113 mit Audio**. Die Ordnernamen sind **echte LibriVox-Vorlesernamen**
(`Alexandra_Bogensperger`, `Ramona_Deininger-Schnabel`, `Julia_Niedermaier`, …) — genau das, was der
Attributionsweg über LibriVox braucht.

### Ergebnis

| | |
|---|---|
| Sprecher mit Audio | **113** |
| davon Geschlecht (aus gemessener F0) | **63 weiblich / 50 männlich** |
| **bestehen das Qualitätstor** | **96** (53 w / 43 m) |
| Ziel des englischen Pakets | 100 **je** Geschlecht |

### ⚠ Die Qualität ist NICHT das Problem — die Sprecherzahl ist es

Das hatte ich anders erwartet. Die SNR-Schwelle bindet **überhaupt nicht**:

| Schwelle | gesamt | weiblich | männlich |
|---|---|---|---|
| SNR ≥ 30 | 96 | 53 | 43 |
| SNR ≥ 20 | **96** | **53** | **43** |

Von 30 auf 20 dB gelockert ändert sich **nichts**. Nur 2 Sprecher scheitern überhaupt am Rauschabstand
(knappste Ausfälle: 29,5 dB). **Alle übrigen 17 Ausfälle scheitern an „weniger als 6 Clips"** — die
Sprecher sind schlicht mit zu wenig Material vertreten.

⇒ HUI ist audio-seitig hervorragend (es ist ein kuratierter TTS-Korpus, kein Crowd-Material). Der
Engpass ist **die Zahl verschiedener Vorleser**, und daran ändert kein Filter etwas.

### Was das für Deutsch bedeutet

**96 nutzbare Stimmen statt 200.** Vier Wege, aufsteigend nach Aufwand:

1. **Ziel für Deutsch senken.** `WANT_PER_GENDER = 100` ist ein **Parameter des Bauskripts, keine
   Anforderung des Plugins** — `BackendService.MapVoices` nimmt, was da ist. 96 verschiedene deutsche
   Stimmen sind viel Vielfalt; NPCs wiederholen sich nur etwas häufiger. **Billigster Weg, sofort
   machbar.**
2. **Die fünf grossen Einzelsprecher dazunehmen** (Bernd Ungerer, Friedrich, Karlsson = m;
   Eva K, Hokuspokus = w) ⇒ etwa **46 m / 55 w**. Kostet weitere 5–22 GB Download je Sprecher für
   **+5 Stimmen** — schlechtes Verhältnis, aber es sind die saubersten Aufnahmen des Korpus.
3. **Common Voice de** zum Auffüllen. Dort würde der SNR-Filter dann tatsächlich binden (Crowd-Material),
   und es ist ein grosser separater Download.
4. **MLS German** (176 Sprecher) — aber 16 kHz **und** hohe Überschneidungsgefahr: MLS ist ebenfalls
   LibriVox-basiert, die Vorleser dürften sich stark mit HUI decken. Der reale Zugewinn ist deutlich
   kleiner als 176 nahelegt.

### ⚠ Zusätzlicher Engpass: die Kinderstimmen

Die 60 Kinder des englischen Pakets stammen aus **Frauenstimmen, die NICHT als Erwachsene verwendet
wurden** (deshalb war dort die Beschaffung so mühsam). Deutsch hat insgesamt **63 Frauen**. Entweder
- die Kinder werden aus denselben Frauen erzeugt, die schon als Erwachsene im Paket sind (Stimmen
  klingen dann verwandt), oder
- der Frauenpool wird geteilt (z. B. 40 erwachsene Frauen + 23 Kinder), oder
- es gibt weniger als 60 Kinder.

**Das ist eine Gestaltungsentscheidung, keine technische Grenze** — aber sie muss getroffen werden.

### Nächster Schritt

Entpacken ist weiterhin **nicht** passiert (57 GB frei, 35–40 GB nötig). Für den eigentlichen Bau
reicht es, die ausgewählten ~96 Sprecher **gezielt aus dem Zip** zu lesen, statt alles zu entpacken —
genau so, wie der Probelauf es schon macht. Damit bleibt der Plattenbedarf im niedrigen einstelligen
GB-Bereich.
