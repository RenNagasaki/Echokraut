# FFXIV: Sprecherwechsel in der deutschen Fassung

Stand der Recherche: 15.11.2026 (Spielstand bis 7.58).

## Zweck

Die Liste sammelt belegte Wechsel der **deutschen** Synchronstimme von FFXIV-Charakteren. Sie dient
zwei Dingen:

1. **Grenzen für `Resources/GameVoiceActorSplits.json`:** Ab welchem Patch gehört eine Zeile eines
   Charakters zur neuen Stimme?
2. **Soll-Werte für die automatische Erkennung:** Die Erkennung soll genau diese Wechsel finden,
   und zwar an der richtigen Patchgrenze. Was sie zusätzlich findet, ist entweder ein neuer Fund
   oder ein Fehlalarm.

Maßgeblich ist die **Patchnummer**. Das Datum ist nur das offizielle Erscheinungsdatum dieses
Patches (siehe letzter Abschnitt). Die Spalte „Grenz-Token“ bleibt offen, sie wird an den
Spieldaten geprüft.

Hinweis zu den Quellen: Es gibt **keine offizielle Ankündigung** von Square Enix zu einem dieser
Wechsel. Alle Belege sind Fan-Quellen: spielestimmen.de, Forenbeiträge mit Angaben aus den
Abspännen, Fan-Wikis und ein Interview mit der Synchronregie (Pamela Drobig). IMDb und MobyGames
waren nicht abrufbar (HTTP 403), sie sind nur über Suchtreffer-Auszüge berücksichtigt. Das
Synchron-Forum und die Synchronkartei lieferten zu FFXIV nichts.

„bis zu … X“ und „seit … X“ nennen auf spielestimmen.de dieselbe Zahl. „bis 2.4“ heißt dort also
„letzte alte Zeilen vor 2.4“, nicht „einschließlich 2.4“.

## Haupttabelle

| Charakter | Anzeigename DE-Client | Alt (letzter Patch) | Neu (erster Patch) | Datum | Grenz-Token | Status | Quellen |
|---|---|---|---|---|---|---|---|
| Urianger Augurelt (Urianger Augurelt) | „Urianger“ (Wiki-Titel [S4]) | Alexander Wilz (2.0) | Florian Hoffmann (2.1) | 17.12.2013 | keine nötig: VoiceMan-Zeilen erst ab `02200`, alle neue Stimme | bestätigt | [S1] [S2] |
| Alisaie Leveilleur (Alisaie Leveilleur) | „Alisaie“ [S4] | Annette Gunkel (2.0 laut [S2] und [S4]. Ob sie in 2.1–2.3 noch Zeilen hatte, ist offen) | Ann-Cathrin Schaible (2.4) | 28.10.2014 | keine nötig: erste Zeilen `02401` (neue Stimme) | bestätigt | [S1] [S2] [S4] |
| Minfilia Warde (Minfilia Warde) | „Minfilia“ [S4] | Djuwita Müller („2.0–2.5“ [S2], „bis 2.5“ [S1]) | Ann Vielhaben (3.1) | 10.11.2015 | `03100` (eindeutig: keine Zeilen zwischen `02500` und `03200`); Messung uneindeutig → **nicht in der Datei** | Wechsel bestätigt, **Grenze unsicher** (2.55 und 3.0 offen) | [S1] [S2] |
| Yda / Lyse Hext (Yda / Lyse) | erst „Yda“, ab der Enthüllung „Lyse“ (cgw: Lyse ab 3.56) [S4] [S5] | Henrike Krügener (3.57 laut [S2], „bis 3.5“-Reihe laut [S1]) | Paulina Weiner (4.0) | 20.06.2017 | keine nötig: Token `YDA` bis `03500`, `LYSE` erst ab `04000` | bestätigt | [S1] [S2] [S5] |
| Matoya (Matoya) | „Matoya“ [S4] | Ingrid Mülleder (3.x, „bis 3.5“ [S1]) | Ulrike Hübschmann (4.1 laut [S1]. [S3] ordnet sie nur dem Block 4.1–4.55b zu) | 10.10.2017 | Messung widersprüchlich (`04500` andere Stimme, `05400` wieder alte) → **nicht in der Datei** | Wechsel bestätigt, Patch 4.1 nur eine Quelle | [S1] [S3] |
| **Solus zos Galvus / Emet-Selch** (Solus / Emet-Selch) — **neu** | „Solus“ bzw. „Emet-Selch“ (nicht am Client belegt) | Alexander Wolf (4.4 laut [S3]) | Andreas Hofer (5.0) | 02.07.2019 | keine nötig: getrennte Tokens `SOLUSZOSGALVUS` (`04400`/`04500`) und `EMETSELCH` (ab `05001`) | **unsicher** (eine Forenquelle plus IMDb-Suchtreffer, Grenze offen) | [S3] [S7] [S5] |
| Biggs (Biggs) | „Biggs“ [S4] | Gordon Piedesack („bis 4.5“ [S1]) | Thomas Dehler (5.0) | 02.07.2019 | `05000` (eindeutig: keine Zeilen zwischen `03007` und `05000`; Messung schwach, aber gleichgerichtet) | bestätigt | [S1] [S2] [S3] [S5] |
| Hydaelyn (Hydaelyn), ab 6.0 auch als Venat | „Hydaelyn“, ab 6.0 auch „Venat“ [S4] | Sonngard Dressler („bis 5.5“ [S1]) | Antje von der Ahe (6.0) | 07.12.2021 | `06000` (gemessen, klar: Abstand 0,36) | bestätigt | [S1] [S2] [S3] [S5] |
| Y'shtola Rhul (Y'shtola Rhul) | „Y'shtola“ [S4] | Annette Schreier (6.0x) | Birge Tetzner (6.1) | 12.04.2022 | `06100` (gemessen, sehr klar: Abstand 0,65) | bestätigt, aber **Sonderfall Dungeons** (siehe unten) | [S1] [S3] [S6] [S8] [S5] |
| Brutalo Bomber / Hector (Brute Bomber) | „Brutalo Bomber“ [S1] | Andreas Wisniewski (7.0x, M3 seit 7.01 [S5]) | David Hörning („seit Patch 7.56“ [S1]) | 08.09.2026 | keine: kein Token in den deutschen VoiceMan-Zeilen | **unsicher**, eher ein Eintragsfehler | [S1] [S5] |

### Wichtiger Sonderfall Y'shtola: Dungeon-Zeilen wurden nachträglich getauscht

Ein Forumsbeitrag vom 05.10.2022 [S3, Seite 4, Beitrag #10] berichtet: Am Patch-Tag von 6.1 waren in
den Dungeons (Vertraute bzw. Unterstützte Instanzen) **noch Annette Schreiers Aufnahmen** zu hören.
**Seit 6.2** spricht in **allen** Dungeons die neue Stimme, auch in den alten.

Für die Splits heißt das:

- Die alte Stimme steckt in den heutigen Spieldaten vermutlich nur noch in Zwischensequenzen und
  Quests bis 6.0x.
- Die Dungeon- und Kampf-Sprachzeilen alter Inhalte gehören wahrscheinlich schon zur neuen Stimme.
- Eine reine Patchgrenze „ab 6.1“ würde diese Zeilen falsch einordnen.

Der Beleg ist ein einzelner Spielerbericht. Am Spielstand prüfen.

## Prüfung am Spielstand (2026-11-15)

Grundlage: deutscher Client (PC), alle `cut_scene/*/VoiceMan_*`-Zeilen (27 141), Tokens je Sprecher. Dazu
je Patch bis zu 4 Clips (3–15 s), im Speicher dekodiert, mit `POST /speaker-embedding` des Wrappers
vermessen (XTTS-Encoder `xtts-v2-speaker`; Qwens x-vector trennte nicht sauber). Die Vektoren sind
auf den Mittelwert aller Clips zentriert. Bewertet werden Patch-Mittelwerte, der eigene Patch zählt
nicht mit (Verfahren und Schwellen: `SpeakerChangeDetector`).

- **Token-Format:** `XXYZZ` = Erweiterung `XX`, Patch `Y`, Teil `ZZ`. `06100` ist 6.1, `06006` ist
  6.0 Teil 6, `06505` ist 6.55. Das Beispiel `06010` aus dem Plan wäre 6.0 Teil 10, **nicht** 6.1.
- **Y'shtola:** Grenze `06100`, Abstand 0,65 (30 Patches davor, 13 danach). Der größte Fehlalarm
  aller Kontrollfälle lag bei 0,28 (Alphinaud, nur der letzte Patch).
- **Hydaelyn:** Grenze `06000`, Abstand 0,36. Auf Clip-Ebene ist die Ähnlichkeit über die Grenze
  0,3–0,5, innerhalb 0,6–0,8.
- **Biggs:** nur 7 Clips in 3 Patches (`03003`, `03007`, `05000`). Über die Grenze 0,4–0,7, innerhalb
  0,6–0,8: schwach, aber in der erwarteten Richtung. Die Grenze `05000` ist eindeutig, weil es
  dazwischen keine Zeilen gibt.
- **Minfilia:** Die Clips zerfallen in vier Gruppen: `02200`/`02300`, `02400`/`02500`, `03200`/`03400`
  und `05001`/`05003`. Die letzte Gruppe ist Ryne, die in 5.0 anfangs „Minfilia“ heißt. Dass sich
  2.4/2.5 von 2.2/2.3 abhebt, passt zu keiner Quelle. → Erst anhören, dann eintragen. Vorschlag zum
  Prüfen: `["03100", "05000"]`, wobei `05000` die Ryne-Epoche ist.
- **Matoya:** `04500` klingt deutlich anders als alles andere (0,3), `05400` aber wieder wie 3.x
  (0,7). Das passt nicht zu „ab 4.1 neu“. → offen.
- **Kontrollfälle Alphinaud und Thancred:** keine Grenze über der Schwelle.

**In `Echokraut/Resources/GameVoiceActorSplits.json` übernommen** (bestätigt + Token geprüft):
Y'shtola `06100`, Hydaelyn `06000`, Biggs `05000`.

## Neue Funde (nicht in den Startdaten)

1. **Solus zos Galvus / Emet-Selch:** Die Liste vom 14.02.2023 [S3, Seite 5, Beitrag #1] führt
   „Solus/Emet-Selch: Alexander Wolf (4.4) Andreas Hofer (ab 5.0)“. Laut Suchtreffer-Auszug führt
   IMDb Alexander Wolf als deutsche Stimme von „Solus zos Galvus“ (2017, also die
   Stormblood-Besetzung) [S7]. Die Seite selbst war nicht abrufbar. spielestimmen.de kennt nur
   Andreas Hofer („Solus zos Galvus / Emet-Selch“), ebenso consolegameswiki (Emet-Selch, DE
   Andreas Hofer). Offen ist, wer die Emet-Selch-Szenen in 4.5/4.56 spricht. Die letzte alte
   Zeile liegt also bei 4.4 oder 4.5x.
2. **Y'shtola, Dungeon-Sonderfall:** siehe oben. In den Startdaten fehlte er.
3. **Arcadion-Kämpfer mit zwei Sprechern auf spielestimmen.de:** Neben Brutalo Bomber zeigen zwei
   weitere Kämpfer dasselbe Muster. Ein deutscher Eintrag ohne Patch steht neben einem englisch
   benannten Eintrag „seit Patch 7.56“ mit anderem Sprecher:
   - Honey B. Lovely: „Suzie Summ Honigsüß“ = Sarah Alles-Shahkarami (cgw bestätigt [S5]) gegen
     „Honey B. Lovery seit 7.56“ = Sophia Längert [S1].
   - Wicked Thunder: „Tosender Donner / Eutrope“ = Christina Wöllner (cgw bestätigt [S5]) gegen
     „Wicked Thunder seit 7.56“ = Lisa Braun [S1].

   Bei anderen Kämpfern, etwa Black Cat, Sugar Riot, Deep Blue, Red Hot, Howling Blade und The
   Tyrant, ist der Sprecher in beiden Einträgen derselbe. Die „seit 7.56“-Marke steht dort auch an
   Figuren, die schon in 7.0/7.2 auftraten. Sie bezeichnet also eher den Stand der eingereichten
   Daten als einen Auftritt in 7.56.

   Auffällig: Alle drei „neuen“ Sprecher sprechen bereits andere Arcadion-Figuren. Lisa Braun spricht
   Vamp Fatale, Sophia Längert Sugar Riot und David Hörning Howling Blade. Das spricht für vertauschte
   Zeilen beim Eintragen, ist aber nicht belegt. Status aller drei: **unsicher**. Die Patchnotes zu
   7.56 [S9] nennen keine neuen Arcadion-Szenen.
4. **Hinweis zum Abgleich der Stimmen:** Die alte Stimme eines Charakters ist oft zugleich die Stimme
   eines anderen Charakters. Für die Erkennung ist das wichtig, weil Clusterings sonst Figuren
   vermischen:
   - Annette Gunkel (Alisaie alt) spricht auch Tataru und Kan-E-Senna.
   - Ann Vielhaben (Minfilia neu) spricht auch Lamimi.
   - Florian Hoffmann (Urianger neu) spricht auch Magnai, Glynard, Halric und Kanilokka.
   - Gordon Piedesack (Biggs alt) spricht auch Raubahn.
   - Thomas Dehler (Biggs neu) spricht auch Gaius und Bismarck.
   - Antje von der Ahe (Hydaelyn neu) spricht auch Nanamo.
   - Annette Schreier (Y'shtola alt) spricht auch Meriel.
   - Paulina Weiner (Lyse neu) spricht auch Puddingway.

   Alles laut [S1] und [S3].

## Korrekturen gegenüber den Startdaten

- **Yda/Lyse:** Die letzte alte Stimme ist **3.57**, nicht bloß „3.5“ [S2]. In der
  spielestimmen-Notation steht „3.5“ für die ganze 3.5x-Reihe. Ab 3.56 heißt sie im Spiel „Lyse“
  (cgw: Lyse-Seite „Patch 3.56“), vorher „Yda“.
- **Alisaie:** Die alte Stimme ist nur für **2.0** belegt [S2] [S4]. „bis 2.4“ auf spielestimmen.de
  heißt „vor 2.4“. Ob sie in 2.1–2.3 Zeilen hatte, ist offen.
- **Urianger:** Die alte Stimme gilt nur für **2.0**, die Grenze 2.1 ist eindeutig. Die Startdaten
  sind korrekt, nur „bis 2.1“ heißt „bis einschließlich 2.0x“.
- **Minfilia:** Die neue Stimme ist erst ab **3.1** belegt, nicht ab 2.55 oder 3.0. Das Datum
  „~Ende 2015“ wird damit genauer: 10.11.2015.
- **Matoya:** Den Patch 4.1 nennt nur spielestimmen.de. Das Forum [S3] sagt nur „irgendwo in
  4.1–4.55b, mit neuer Stimme“. Der Status sinkt deshalb für die Patchgrenze von „bestätigt“ auf
  „Wechsel bestätigt, Grenze eine Quelle“. Nebenbei: Auf Englisch und Japanisch wechselte Matoya
  erst zwischen Heavensward und Shadowbringers [S5]. Die deutsche Grenze liegt also anders.
- **Hydaelyn:** In 6.0 heißt sie auch „Venat“. Beide Namen haben dieselbe neue Stimme (spielestimmen:
  „Venat / Hydaelyn seit 6.0“, cgw Venat: DE Antje von der Ahe).
- **Y'shtola:** Der Wechsel betrifft wegen des Dungeon-Sonderfalls nicht alle Zeilen ab 6.1 gleich
  (siehe oben).
- **Brutalo Bomber:** Wahrscheinlich ist das kein echter Wechsel, sondern ein Eintragsfehler auf
  spielestimmen.de (siehe „Neue Funde“, Punkt 3). Der Auftritt mit Wisniewski ist M3 (7.01). Der
  zweite Auftritt als „Brute Abombinator“ ist M7 (7.2) [S5]. Falls es doch zwei Stimmen gibt, wäre
  7.2 die naheliegendere Grenze als 7.56.

## Offene Fragen

1. **Minfilia 2.55 und 3.0:** Wer spricht Minfilia in 2.55–2.57 (Bankett/„Before the Fall“) und in
   3.0? Die Quellen sagen nur „2.0–2.5“ bzw. „bis 2.5“ (alt) und „seit 3.1“ (neu).
2. **Alisaie 2.1–2.3:** Gibt es dort Zeilen, und von wem?
3. **Emet-Selch 4.5/4.56:** Spricht Alexander Wolf noch die Emet-Selch-Szenen am Ende von
   Stormblood, oder schon Andreas Hofer? Der Wechsel selbst ist nur einmal (Forum) und über einen
   IMDb-Suchtreffer belegt.
4. **Y'shtola, „Birge Tetzner (as of 7.56)“ auf consolegameswiki:** Das Wiki schreibt
   „Birge Tetzner (as of 7.56) / Annette Schreier“. Nach dem Schema der Seite (wie bei Tataru FR:
   „Lou Viguier (as of 7.56) / Kelly Marrot“) hieße das „neu ab 7.56“. Das widerspricht allen
   anderen Quellen (6.1), vermutlich ein Fehler im Wiki. Ob 7.56 alte Y'shtola-Zeilen neu vertont
   hat, ist unbelegt. Am Spielstand prüfen.
5. **Y'shtola-Dungeonzeilen:** Wurden mit 6.2 wirklich alle Dungeon- und Kampfzeilen alter Inhalte
   ersetzt (nur ein Spielerbericht)? Ebenfalls ungeklärt: die Ausrufe in Vertrauten bzw.
   Unterstützten Instanzen der ARR–ShB-Dungeons.
6. **Wedge und „Grauer Felsen“ (Hoary Boulder):** In der Abspann-Liste [S3, Seite 5] stehen sie mit
   „*“ = „zum ersten Mal in den Credits **oder** neue Stimme“: Wedge (Markus Haase) in 5.0,
   Grauer Felsen (Andreas Sparberg) in 6.0. Beide Figuren gibt es seit 2.0. Ein Stimmwechsel ist
   nicht belegt, aber möglich, falls sie vorher vertonte Zeilen hatten.
7. **Thancred:** Mehrere Spieler vermuteten 2022 auch für Thancred eine neue Stimme. Andere
   widersprachen ([S3] Seite 2). Kein Beleg für einen Wechsel, siehe Kontrollfälle.
8. **Hilda:** Ein Beitrag von 2018 fragt, ob Szenen nach 3.0 nachvertont wurden ([S3], Seite 1,
   Beitrag #7). Reine Vermutung.
9. **M'naago:** spielestimmen.de schreibt „in der Erweiterung »Stormblood«“ zu Rieke Werner. Das
   könnte heißen, dass für andere Auftritte (3.5, später) jemand anderes geführt wird. Nicht belegt,
   cgw nennt nur Rieke Werner.
10. **Anzeigenamen:** Die Namen in der Spalte „Anzeigename DE-Client“ stammen aus den Seitentiteln
    des deutschen Fan-Wikis (sevengamer) bzw. von spielestimmen.de, nicht direkt aus dem Client.
    Achtung, **Namensfalle:** In 5.0 heißt Ryne anfangs ebenfalls „Minfilia“. Ihre Stimme ist Anne
    Düe [S1] [S5]. Das ist kein Stimmwechsel von Minfilia Warde, wird bei einem Split über den
    Anzeigenamen aber so aussehen.

## Kontrollfälle ohne Wechsel

| Charakter | Sprecher | Belege |
|---|---|---|
| Alphinaud Leveilleur („Alphinaud“) | Markus Haase, durchgehend | [S1] [S2] [S5] |
| Thancred Waters („Thancred“) | Peter Lehn, durchgehend. Spieler-Vermutungen 2022 (6.1) wurden im selben Thread widersprochen | [S1] [S2] [S5] [S3 S.2] |
| Tataru Taru | Annette Gunkel, durchgehend (in der französischen Fassung gab es mit 7.56 einen Wechsel, in der deutschen nicht) | [S1] [S2] [S5] |
| Krile | Jennifer Weiß | [S1] [S3] [S5] |
| G'raha Tia / Kristallexarch | Marcus Off | [S1] [S3] [S5] |
| Elidibus (auch Themis) | Rainer Doering | [S1] [S3] [S5] |

## Patch-Release-Daten (verwendet)

Quelle: consolegameswiki, Seite „Patches“ (https://ffxiv.consolegameswiki.com/wiki/Patches),
abgerufen am 15.11.2026. Es sind die globalen Release-Tage. Für 7.56 bestätigt durch die
Patchnotes-Berichterstattung vom 08.09.2026 [S9]. Die Lodestone-Einzelmeldungen wurden nicht geprüft.

| Patch | Datum | Patch | Datum |
|---|---|---|---|
| 2.0 | 27.08.2013 | 4.4 | 18.09.2018 |
| 2.1 | 17.12.2013 | 4.5 / 4.56 | 08.01.2019 / 25.03.2019 |
| 2.4 | 28.10.2014 | 5.0 | 02.07.2019 |
| 2.5 / 2.55 | 20.01.2015 / 30.03.2015 | 5.5 / 5.55 | 13.04.2021 / 24.05.2021 |
| 3.0 | 23.06.2015 | 6.0 | 07.12.2021 |
| 3.1 | 10.11.2015 | 6.1 | 12.04.2022 |
| 3.5 | 17.01.2017 | 6.2 | 23.08.2022 |
| 3.56 / 3.57 | 27.03.2017 / 17.04.2017 | 6.55 | 16.01.2024 |
| 4.0 | 20.06.2017 | 7.0 / 7.01 | 02.07.2024 / 16.07.2024 |
| 4.1 | 10.10.2017 | 7.2 | 25.03.2025 |
| | | 7.5 / 7.56 | 28.04.2026 / 08.09.2026 |

## Quellen

- [S1] spielestimmen.de, Final Fantasy XIV Online: https://spielestimmen.de/spiele/final-fantasy-xiv-online/
  (vollständig ausgewertet, alle „bis zu“/„seit“-Einträge). Personenseiten:
  https://spielestimmen.de/personen/andreas-wisniewski/, https://spielestimmen.de/personen/david-hoerning/,
  https://spielestimmen.de/personen/annette-schreier/, https://spielestimmen.de/personen/birge-tetzner/,
  https://spielestimmen.de/personen/andreas-hofer/
- [S2] Square-Enix-Forum DE, „Deutscher FFXIV-Synchro-Cast“, Beitrag #1 (06.08.2018, Liste aus den
  Abspännen): https://forum.square-enix.com/ffxiv/threads/372307
- [S3] Derselbe Thread, Folgeseiten: https://forum.square-enix.com/ffxiv/threads/372307/page2 bis
  `/page5`. Seite 4 #10 (05.10.2022): Dungeon-Sonderfall Y'shtola. Seite 5 #1–#2 (14.02.2023): Liste
  4.1–6.0 mit „*“-Markierungen. Seite 5 #3 (17.01.2024): Birge Tetzner als neue Y'shtola laut
  6.55-Abspann.
- [S4] ffxiv.sevengamer.de (deutsches Fan-Wiki), Seitentitel und Infobox Alisaie
  („Annette Gunkel (2.0), Ann Cathrin Schaible (2.4 +)“): https://ffxiv.sevengamer.de/wiki/Alisaie,
  außerdem /wiki/Y%27shtola, /wiki/Urianger, /wiki/Minfilia, /wiki/Yda, /wiki/Lyse, /wiki/Matoya,
  /wiki/Biggs, /wiki/Hydaelyn, /wiki/Venat, /wiki/Emet-Selch
- [S5] ffxiv.consolegameswiki.com, „Voiced by (DE)“: Y'shtola_Rhul, Alisaie_Leveilleur,
  Urianger_Augurelt, Yda, Lyse, Matoya, Biggs, Venat, Emet-Selch, Ryne, Brute_Bomber, Wicked_Thunder,
  Honey_B._Lovely, Thancred_Waters, Alphinaud_Leveilleur, Tataru_Taru (z. B.
  https://ffxiv.consolegameswiki.com/wiki/Y%27shtola_Rhul). Minfilia und Kan-E-Senna haben dort
  keinen DE-Eintrag.
- [S6] Square-Enix-Forum DE, „Synchronsprecher Endwalker“ (31.07.2022: „Y'shtola hat mit 6.1 eine
  neue Stimme bekommen“): https://forum.square-enix.com/ffxiv/threads/468722
- [S7] IMDb, Alexander Wolf (nur Suchtreffer-Auszug, Seite selbst HTTP 403):
  https://imdb.com/name/nm3197978
- [S8] Crystal Universe, Interview mit Pamela Drobig (21.02.2024): Neubesetzung von Y'shtola per
  neuem Casting, erste Szene in 6.1:
  https://crystaluniverse.de/interviews/ffxiv-interview-mit-pamela-drobig/
- [S9] Patch 7.56, Patchnotes-Bericht vom 08.09.2026: https://mein-mmo.de/final-fantasy-xiv-dawntrail-patch-7-56/2/
