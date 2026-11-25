# Recherche: Quest-Fortschritt lesen und Clip-Token → Patch (Plan §11.4)

Stand 2026-11-15. Ziel: für einen Sprecherwechsel mit Grenz-Token (`GameVoiceActorSplits.json`,
`boundaryPatches`) entscheiden, ob der Spieler diesen Story-Punkt erreicht hat, und daraus die
„späteste Epoche, deren Beginn der Spieler erreicht hat“ wählen.

**Geprüfte Quellen-Stände** (alle Zitate unten stammen aus genau diesen Commits):

| Quelle | Stand |
|---|---|
| aers/FFXIVClientStructs `main` | `80cb4a7b7e11a6702e2eb8839afd301a918c638e` |
| goatcorp/Dalamud `master` | `43b34118bcf7213e575ec39517d033191b1acf17` (= `~/DalamudRef/dev/commit_hash.txt`, also genau die Referenz, gegen die das Plugin baut) |
| xivdev/EXDSchema `latest` | `a1e24a0c72058353f30f23beb996792cdb926868` |
| xivapi/ffxiv-datamining `master` (CSV-Export der Spieldaten, en) | `52a72b38aa2f967cab36867f34cd9bee9debb88b` |
| ffxiv.consolegameswiki.com „Main Scenario Quests“ | abgerufen 2026-11-15 |

Alle Zahlen in den Tabellen sind **aus den Datamining-CSVs ausgewertet**, nicht aus dem Gedächtnis;
die Prüfschritte in §4 wiederholen das gegen die echten Spieldaten im Client.

---

## 0. Kernergebnisse

1. **Fortschritt lesen:** `QuestManager.IsQuestComplete(uint rowId)` (FFXIVClientStructs, statisch)
   bzw. Dalamuds **`IUnlockState.IsQuestCompleted(Quest row)`** (gibt es in der Referenz, gegen die
   das Plugin baut). Beide lesen das Bitfeld *aller* je abgeschlossenen Quests; die `uint`-Überladung
   nimmt die Row-Id (65536+) und maskiert selbst auf `& 0xFFFF`. Nur auf dem Framework-Thread rufen,
   erst nach dem Login gültig.
2. **Token-Bedeutung:** `vo_voiceman_XXYZZ` = **`XX` Erweiterung, `Y` Patch-Unterversion, `ZZ`
   Teil/Inhalt** — also `06100` = Patch 6.1 (MSQ), `06006` = 6.0 Kapitel-Blatt 6, `06505` = 6.55,
   `06101`/`06401`/`07010` = Raid/Nebeninhalt des Patches. Das Textblatt liegt passend unter
   `cut_scene/<XXY>/VoiceMan_<XXYZZ>`. **Das Beispiel-Token `06010` (Plan, `VoiceActorSplits`-Doku,
   Tests) ist falsch für „ab 6.1“ — richtig ist `06100`.**
3. **Patch → Quest:** Es gibt eine Excel-Tabelle, die genau das liefert: **`ScenarioTree`** (Row-Id =
   Quest-Row-Id, `Name` = `T_VER610_01_01` …). Daraus ergibt sich zu jedem Patch die erste MSQ-Quest.
   Eine Excel-Verbindung *Clip/VoiceMan → Quest* gibt es nicht (Quest → Cutscene schon, aber Cutscene
   → VoiceMan-Zeilen steht nur in der `.cutb`-Binärdatei, ungeprüft).
4. **Empfehlung:** „Patch P erreicht“ ⇔ `IsQuestComplete(Vorgänger der ersten MSQ von P)` ODER erste
   MSQ von P angenommen/abgeschlossen. Auswahl = größte Grenze, die erreicht ist; vor allen Grenzen →
   erste Epoche; Fortschritt nicht lesbar → neueste Epoche (bisheriges Verhalten). Höchsten erreichten
   Stand je Charakter (`ContentId`) merken, damit New Game+ und Ladephasen nicht zurückspringen.

---

## 1. Quest-Fortschritt im Plugin lesen

### 1.1 `QuestManager.IsQuestComplete` (FFXIVClientStructs)

Quelle: https://github.com/aers/FFXIVClientStructs/blob/80cb4a7b7e11a6702e2eb8839afd301a918c638e/FFXIVClientStructs/FFXIV/Client/Game/QuestManager.cs

```csharp
namespace FFXIVClientStructs.FFXIV.Client.Game;

[StructLayout(LayoutKind.Explicit, Size = 0x1092)]
public unsafe partial struct QuestManager {
    [StaticAddress("48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 84 24", 3)]
    public static partial QuestManager* Instance();

    [FieldOffset(0x10), FixedSizeArray] internal FixedSizeArray30<QuestWork> _normalQuests;          // angenommene Quests (max. 30)
    [FieldOffset(0x2E0), FixedSizeArray(isBitArray: true, bitCount: 751 * 8)]
    internal FixedSizeArray751<byte> _completedQuests;                                                // Bitfeld ALLER abgeschlossenen Quests

    [MemberFunction("E8 ?? ?? ?? ?? 41 88 84 2E")]
    public static partial bool IsQuestComplete(ushort questId);
    public static bool IsQuestComplete(uint questId) => IsQuestComplete((ushort)(questId & 0xFFFF));

    /// Get the current step in a specific quest. Will return 0 if the quest is not active
    /// (even if the quest has been completed prior).
    [MemberFunction("E8 ?? ?? ?? ?? 3A 43 06")]
    public static partial byte GetQuestSequence(ushort questId);
    public static byte GetQuestSequence(uint questId) => GetQuestSequence((ushort)(questId & 0xFFFF));

    [MemberFunction("45 33 C0 48 8D 41 18 66 39 10")]
    public partial bool IsQuestAccepted(ushort questId);          // Instanzmethode!
    public bool IsQuestAccepted(uint questId) => IsQuestAccepted((ushort)(questId & 0xFFFF));
}
```

- **ID-Form:** Die Spielfunktion nimmt die **`ushort`-ID ohne Offset** (= Row-Id − 0x10000). Die
  `uint`-Überladung nimmt die **Lumina-Row-Id** (65536+) und schneidet selbst mit `& 0xFFFF` ab —
  `70062 & 0xFFFF = 4526`. Dieselbe Zahl steht als Suffix im Quest-`Id`-Text (`AktKmh101_04526`),
  daran lässt sich das an echten Daten gegenprüfen. **Im Plugin immer die `uint`-Überladung mit
  `quest.RowId` benutzen**, nie selbst rechnen.
- **Gültigkeit:** Das Bitfeld `_completedQuests` (751 Byte ≈ 6008 Quests) enthält jede je
  abgeschlossene Quest, also auch alte — kein Journal-/Ansichtszustand. Es ist Teil der Spielerdaten,
  die beim Login geladen werden. Vor dem Login / im Titelbildschirm ist es leer bzw. ungültig →
  vorher `PlayerState.Instance()->IsLoaded` prüfen (genau das tut Dalamud, s. 1.3).
- **`IsQuestAccepted`** ist eine **Instanzmethode** (`QuestManager.Instance()->IsQuestAccepted(id)`),
  `IsQuestComplete` und `GetQuestSequence` sind statisch.
- **Threading:** `[MemberFunction]` ruft die native Spielfunktion direkt auf, die Spielspeicher ohne
  Sperre liest. FFXIVClientStructs garantiert keinerlei Thread-Sicherheit; Konvention in Dalamud ist,
  Spielzustand nur auf dem Framework-Thread zu lesen (`IFramework.RunOnFrameworkThread`, oder im
  `IFramework.Update`-Handler). Das Plugin nutzt beides schon (`Plugin.cs`). Für die Epochen-Wahl,
  die im Synthese-Pfad (Hintergrund-Thread) passiert: den Fortschritt **auf dem Framework-Thread
  ermitteln und als unveränderlichen Schnappschuss ablegen**, im Hintergrund nur den Schnappschuss
  lesen.

### 1.2 `UIState.IsUnlockLinkUnlockedOrQuestCompleted`

Quelle: https://github.com/aers/FFXIVClientStructs/blob/80cb4a7b7e11a6702e2eb8839afd301a918c638e/FFXIVClientStructs/FFXIV/Client/Game/UI/UIState.cs

```csharp
/// Checks to see if an unlock link is unlocked, or if the passed value is greater than 0x10000,
/// checks if the quest is completed.
/// <param name="minQuestProgression">If the quest is not complete, check for at least this level of
/// progress in the quest (using QuestManager.GetQuestSequence). If 0, the quest must have been completed.</param>
/// <param name="a4">... appears to be a flag to respect Unlock Flag 245 (ignore quest progression?)
/// for quest-based checks. Virtually always true in game code.</param>
[MemberFunction("E8 ?? ?? ?? ?? 44 22 F0")]
public partial bool IsUnlockLinkUnlockedOrQuestCompleted(uint unlockLinkOrQuestId, byte minQuestProgression = 0, bool a4 = true);

/// Check if the player has seen the cutscene before.
/// Only tracks skippable cutscenes (for that, check if WorkIndex is not 0 in CutsceneWorkIndex sheet).
/// <param name="cutsceneId"> RowId of the Cutscene </param>
[MemberFunction("E8 ?? ?? ?? ?? 33 D2 0F B6 CB 3A C3")]
public partial bool IsCutsceneSeen(uint cutsceneId);
```

- Hier ist die Quest-ID **mit** Offset (≥ 0x10000) gemeint — sonst wird sie als UnlockLink gelesen.
  Instanz: `UIState.Instance()->…`.
- Vorteil gegenüber `IsQuestComplete`: `minQuestProgression` erlaubt „mindestens Sequenz N der
  laufenden Quest“. Für uns unnötig (Patch-Grenze = ganze Quest).
- `a4` / „Unlock Flag 245“ ist undokumentiert; vermutlich der Schalter, mit dem das Spiel in
  Sonderzuständen (New Game+?) Quest-Fortschritt anders bewertet — **ungeklärt**, siehe 1.4.
- `IsCutsceneSeen` existiert, gilt aber nur für überspringbare Cutscenes (`CutsceneWorkIndex.WorkIndex
  != 0`) und braucht die Cutscene-Row, die wir zum Clip nicht kennen (§2.3) → als Hauptweg ungeeignet.

### 1.3 Dalamud-Service `IUnlockState`

Quellen:
https://github.com/goatcorp/Dalamud/blob/43b34118bcf7213e575ec39517d033191b1acf17/Dalamud/Plugin/Services/IUnlockState.cs ·
https://github.com/goatcorp/Dalamud/blob/43b34118bcf7213e575ec39517d033191b1acf17/Dalamud/Game/UnlockState/UnlockState.cs

Eingeführt im Commit „Add IUnlockState service“ (2025-11-08). In der lokalen Referenz
`~/DalamudRef/dev/Dalamud.xml` (gleicher Commit) vorhanden; das Plugin baut mit
`Dalamud.NET.Sdk/15.0.0` → nutzbar.

```csharp
public interface IUnlockState : IDalamudService
{
    delegate void UnlockDelegate(RowRef rowRef);
    event UnlockDelegate? Unlock;                       // feuert, wenn etwas freigeschaltet wurde (auch Quests)

    bool IsQuestCompleted(Quest row);                   // Lumina.Excel.Sheets.Quest
    /// unlockLink: The unlock link id or quest id (quest ids in this case are over 65536).
    bool IsUnlockLinkUnlocked(uint unlockLink);
    bool IsUnlockLinkUnlocked(uint unlockLink, byte minimumQuestSequence);
    // … viele weitere Is…Unlocked
}

// Implementierung (UnlockState.cs):
private bool IsLoaded => CSPlayerState.Instance()->IsLoaded;
public bool IsQuestCompleted(Quest row)
{
    if (!this.IsLoaded) return false;
    return QuestManager.IsQuestComplete(row.RowId);
}
// Login → Debounce-Update; Logout → Cache geleert; AgentUpdateFlag.UnlocksUpdate → Update;
// das Update vergleicht u. a. UpdateUnlocksForSheet<Quest>() und feuert dann `Unlock`.
```

- **Inhaltlich identisch** mit `QuestManager.IsQuestComplete(row.RowId)` plus `IsLoaded`-Prüfung.
- **Achtung:** vor dem Login liefert es `false` — nicht unterscheidbar von „nicht abgeschlossen“.
  Für die Fallback-Regel („Fortschritt unbekannt → neueste Epoche“) braucht das Plugin also zusätzlich
  `IClientState.IsLoggedIn` bzw. `IPlayerState`/`PlayerState.IsLoaded`.
- **`Unlock`-Event** ist der saubere Auslöser zum Neuberechnen des Schnappschusses (zusätzlich
  `IClientState.Login`/`Logout`). Ob es zuverlässig für *jede* MSQ-Abgabe feuert, ist ungeprüft
  (§4 Schritt 6); Polling alle paar Sekunden im `Framework.Update` ist der billige Rückfall
  (wenige Bit-Abfragen).
- Empfehlung: **`IUnlockState` per Konstruktor injizieren** (passt zur DI-Regel, testbar über
  Interface), kein `unsafe` im Service; FFXIVClientStructs nur für `IsQuestAccepted`, falls gewünscht.

### 1.4 New Game+

Offizielles Handbuch (https://na.finalfantasyxiv.com/game_manual/newgame-plus/): „Events in the world
change to reflect your current chapter, but this does not revert your overall progress“; ein Kapitel
endet mit der letzten Quest, „all in-progress data is wiped clean“; Unterbrechen jederzeit möglich.
Wiki: https://ffxiv.consolegameswiki.com/wiki/New_Game%2B .

**Nicht belegt** ist, was `IsQuestComplete` *während* eines NG+-Kapitels für die Quests dieses
Kapitels liefert (das Spiel muss sie für das Journal als „offen“ behandeln; ob dafür das Bitfeld
getauscht oder nur der Journal-Zustand überlagert wird, steht in keiner der Quellen — der
undokumentierte `a4`-Schalter in 1.2 deutet auf eine Sonderbehandlung hin). Folgerung fürs Design:

- Den **höchsten je erreichten Grenz-Stand je Charakter** (`ContentId` aus `IPlayerState` bzw.
  `PlayerState.ContentId`) in der Plugin-Konfiguration merken und nie absenken. Dann kann NG+ nichts
  kaputtmachen, egal wie das Bitfeld sich verhält.
- **Umgesetzt 2026-11-15 (User-Entscheidung):** im NG+ die **Epoche des nachgespielten Kapitels** nehmen — über die angenommene Quest, nicht über das Bitfeld; der Höchststand-Speicher ist entfallen. Ursprünglich: optional später (die Cutscenes
  dort laufen ja mit den alten Clips). Das wäre eine eigene Entscheidung; die Regel aus §11.4 sagt
  „späteste erreichte Epoche“.

---

## 2. Clip-Token `XXYZZ` → Patch → Quest

### 2.1 Was das Token bedeutet (Beleg: Liste aller VoiceMan-Blätter)

Alle 98 VoiceMan-Textblätter im Datamining-Export
(https://github.com/xivapi/ffxiv-datamining/tree/52a72b38aa2f967cab36867f34cd9bee9debb88b/csv/en/cut_scene),
gruppiert nach Ordner `cut_scene/<ddd>/`:

```
022: 02200            023: 02300            024: 02400 02401      025: 02500
030: 03000…03008      031: 03100            032: 03200 03201      033: 03300   034: 03400 03401   035: 03500 03501
040: 04000…04006      041: 04100 04101      042: 04200 04201      043: 04300 04301   044: 04400 04401   045: 04500 04501 04505
050: 05000…05006      051: 05100 05101      052: 05200 05201      053: 05300 05301   054: 05400 05401   055: 05500 05505
060: 06000…06006      061: 06100 06101      062: 06200 06201      063: 06300 06301   064: 06400 06401   065: 06500 06501 06505
070: 07000…07005 07010   071: 07100 07101 07102 07120   072: 07200 07210 07220   073: 07300 07310 07320
074: 07400 07410      075: 07500 07505 07510 07515
```

Daraus und aus den Sprechern je Blatt (Zeilen gezählt):

| Muster | Bedeutung | Belege (häufigste Sprecher) |
|---|---|---|
| Ordner `ddd` = die ersten drei Ziffern des Tokens | `XX` Erweiterung (02 = ARR … 07 = DT), `Y` Patch-Unterversion | — |
| `XX0ZZ`, `ZZ` = 00…08 | x.0-Hauptgeschichte, in Kapitel-Blätter geteilt | 06004 VENAS/HERMES (Elpis), 06006 METEION; 07004 SPHENE |
| `XXY00`, `Y` = 1…5 | MSQ des Patches x.Y | **06100 YSHTOLA 143**, VARSHAHN 73 (6.1); 05100 CHAI-NUZZ; 07100 WUKLAMAT |
| `XX505` | **x.5 Teil 2** (x.55 / x.56) | 06505 WUKLAMAT/ERENVILLE (6.55); 05505 FOURCHENAULT/ARENVALD (5.55); 04505 AYMERIC/MYSTERYVOICE (4.56) |
| `XXY01`, `XXY02`, `XXY10`, `XXY15`, `XXY20` | Raids / Allianz-Raids / Nebeninhalt des Patches (wenige Zeilen, oft leer) | 06101 NALDTHAL (Aglaia, 6.1); 06401 THEMIS (Pandæmonium, 6.4); 07010/07210/07410 METEM (Arcadion); 05101 9S (YoRHa, 5.1); 02401 LOUISOIX (Coil, 2.4) |
| kein `020`/`021` | 2.0/2.1 haben keine VoiceMan-Blätter; deren Clips laufen nicht über dieses Schema → `TryExtractPatchToken` greift nicht, `ResolveEpoch` setzt `00000` (älteste Epoche) | — |

**Folgen:**
- Das Token ist **weder eine Patchnummer 1:1 noch eine laufende Nummer**, sondern `Patch(XX.Y)` +
  Teil `ZZ`. Ordinaler String-Vergleich sortiert trotzdem richtig nach Story-Zeit (02200 < … < 06006
  < 06100 < 06505 < 07000), deshalb funktioniert `VoiceActorSplits.EpochName` so wie er ist.
- **Grenz-Tokens sollten auf `XXY00` bzw. `XX505` normiert sein.** Das Beispiel `06010` in
  `GameVoiceActorSplits.json` (`_comment`), `VoiceActorSplitEntry.BoundaryPatches`-Doku, Plan §11.6
  und `EchoScd.Tests/VoiceActorSplitsTests.cs` sortiert zwar zufällig richtig (es gibt kein Blatt
  zwischen 06006 und 06100), bedeutet aber nach dem Schema „6.0, Teil 10“ — eine Abbildung Token →
  Patch → Quest würde daraus **6.0** machen und die neue Stimme zu früh wählen. Für „ab 6.1“ ist
  `06100` richtig.
- Nebeninhalt (`ZZ` ∉ {00, 05}) gehört zeitlich zu seinem Patch. Für die Epochen-Wahl ist nur die
  **Grenze** auf Quests abzubilden, nicht jeder Clip — der Clip wird weiter per Ordinalvergleich
  einer Epoche zugeordnet.

Abbildung Grenz-Token → Patch-Schlüssel:

```csharp
// "06100" → (6, 1, part2:false); "06505" → (6, 5, part2:true); "06006" → (6, 0, false)
static (int Major, int Minor, bool Part2) PatchOf(string token) =>
    (int.Parse(token[..2]), token[2] - '0', token[2] == '5' && int.Parse(token[3..]) >= 5);
```

### 2.2 Patch → erste MSQ-Quest: Sheet `ScenarioTree`

EXDSchema (https://github.com/xivdev/EXDSchema/blob/a1e24a0c72058353f30f23beb996792cdb926868/ScenarioTree.yml),
Spaltenreihenfolge = Feldreihenfolge:

```yaml
name: ScenarioTree
displayField: Name
fields:
  - name: Name            # Spalte 0, String: "T_VER610_01_01"
  - name: Addon           # 1, link Addon
  - name: QuestChapter    # 2, link QuestChapter (52 = 6.1)
  - name: Unknown0        # 3, aufsteigender Sortierschlüssel über die ganze MSQ (37860 … 37900 …)
  - name: Unknown1        # 4, Index innerhalb des Kapitels
  - name: Unknown2        # 5
  - name: Type            # 6, link ScenarioType
```

**Row-Id von `ScenarioTree` = Row-Id der MSQ-Quest** (1054 Zeilen, alle mit `T_VER…`). `T_VERabc`:
`a` Erweiterung, `b` Unterversion, `c` = 5/6 für x.55/x.56 (`555`, `655`, `756`); **2.55, 3.56, 4.56
sind dort NICHT getrennt** (in `250`/`350`/`450` enthalten) → deren erste Quest kommt aus der Wiki
(Tabelle unten, Spalte Name zeigt `T_VER250_01_09` usw.). Erste Quest eines Patches = Zeile mit
kleinstem `Unknown0`.

Ergebnis (ausgewertet aus `ScenarioTree.csv` + `Quest.csv`, Namen englisch; gegen die
consolegameswiki-Tabelle (https://ffxiv.consolegameswiki.com/wiki/Main_Scenario_Quests) abgeglichen:
**alle 36 Patch-Zeilen 2.1 … 7.56 stimmen überein**, inkl. 2.2 „Still Waters“; die x.0-Zeilen stammen nur
aus den Daten):

| Token | Patch | erste MSQ: Row-Id `Id` „Name“ | `PreviousQuest[0]` | `ScenarioTree.Name` |
|---|---|---|---|---|
| `02000` | 2.0 | 65575 `ManFst001_00039` „Coming to Gridania“ | 0 „—“ | `T_VER200_01_Gri_01` |
| `02100` | 2.1 | 66711 `GaiUse101_01175` „The Price of Principles“ | 70058 „The Ultimate Weapon“ | `T_VER210_01_01` |
| `02200` | 2.2 | 69415 `XxaUse201_03879` „Still Waters“ | 69414 „Build on the Stone“ | `T_VER220_01_01` |
| `02300` | 2.3 | 66978 `GaiUse301_01442` „The Great Divide“ | 66899 „Through the Maelstrom“ | `T_VER230_01_01` |
| `02400` | 2.4 | 65588 `GaiUse401_00052` „Traitor in the Midst“ | 66996 „Brave New Companions“ | `T_VER240_01_01` |
| `02500` | 2.5 | 65899 `GaiUse501_00363` „Good Intentions“ | 65625 „Let Us Cling Together“ | `T_VER250_01_01` |
| `02505` | 2.5 Teil 2 | 65906 `GaiUse601_00370` „Mask of Grief“ | 65965 „In Memory of Moenbryda“ | `T_VER250_01_09` |
| `03000` | 3.0 | 67116 `HeaVna101_01580` „Coming to Ishgard“ | 65964 „Before the Dawn“ | `T_VER300_01A_01` |
| `03100` | 3.1 | 67692 `HeaVnb101_02156` „An Uncertain Future“ | 67205 „Heavensward“ | `T_VER310_01_01` |
| `03200` | 3.2 | 67767 `HeaVnc101_02231` „As It Once Was“ | 67699 „As Goes Light, So Goes Darkness“ | `T_VER320_01_01` |
| `03300` | 3.3 | 67778 `HeaVnd101_02242` „The Man Within“ | 67777 „Causes and Costs“ | `T_VER330_01_01` |
| `03400` | 3.4 | 67877 `HeaVne101_02341` „Promises Kept“ | 67783 „Litany of Peace“ | `T_VER340_01_01` |
| `03500` | 3.5 | 67887 `HeaVnf101_02351` „Tidings from Gyr Abania“ | 67886 „An Ending to Mark a New Beginning“ | `T_VER350_01_01` |
| `03505` | 3.5 Teil 2 | 67892 `HeaVng101_02356` „The Obvious Solution“ | 67891 „Louisoix's Finest Student“ | `T_VER350_01_06` |
| `04000` | 4.0 | 67982 `StmBda101_02446` „Beyond the Great Wall“ | 67895 „The Far Edge of Fate“ | `T_VER400_01A_01` |
| `04100` | 4.1 | 68498 `StmBdb101_02962` „Arenvald's Adventure“ | 68089 „Stormblood“ | `T_VER410_01_01` |
| `04200` | 4.2 | 68558 `StmBdc101_03022` „Tidings from the East“ | 68508 „Return of the Bull“ | `T_VER420_01_01` |
| `04300` | 4.3 | 68606 `StmBdd101_03070` „Gosetsu and Tsuyu“ | 68565 „Rise of a New Sun“ | `T_VER430_01_01` |
| `04400` | 4.4 | 68679 `StmBde101_03143` „Sisterly Act“ | 68612 „Emissary of the Dawn“ | `T_VER440_01_01` |
| `04500` | 4.5 | 68715 `StmBdf101_03179` „Soul Searching“ | 68685 „Prelude in Violet“ | `T_VER450_01_01` |
| `04505` | 4.5 Teil 2 | 68720 `StmBdg101_03184` „A Brief Reprieve“ | 68719 „The Face of War“ | `T_VER450_01_06` |
| `05000` | 5.0 | 68815 `LucKma101_03279` „The Syrcus Trench“ | 68721 „A Requiem for Heroes“ | `T_VER500_01A_01` |
| `05100` | 5.1 | 69209 `LucKmg101_03673` „Shaken Resolve“ | 69190 „Shadowbringers“ | `T_VER510_01_01` |
| `05200` | 5.2 | 69297 `LucKmh101_03761` „Old Enemies, New Threats“ | 69218 „Vows of Virtue, Deeds of Cruelty“ | `T_VER520_01_01` |
| `05300` | 5.3 | 69307 `LucKmi101_03771` „In the Name of the Light“ | 69306 „Echoes of a Fallen Star“ | `T_VER530_01_01` |
| `05400` | 5.4 | 69543 `LucKmj101_04007` „Alisaie's Quest“ | 69318 „Reflections in Crystal“ | `T_VER540_01_01` |
| `05500` | 5.5 | 69594 `LucKmk101_04058` „Unto the Breach“ | 69552 „Futures Rewritten“ | `T_VER550_01_01` |
| `05505` | 5.5 Teil 2 | 69600 `LucKml101_04064` „The Company We Keep“ | 69599 „When the Dust Settles“ | `T_VER555_01_01` |
| `06000` | 6.0 | 69893 `AktKma101_04357` „The Next Ship to Sail“ | 69602 „Death Unto Dawn“ | `T_VER600_01_01` |
| `06100` | 6.1 | 70062 `AktKmh101_04526` „Newfound Adventure“ | 70000 „Endwalker“ | `T_VER610_01_01` |
| `06200` | 6.2 | 70128 `AktKmi101_04592` „In Search of Azdaja“ | 70071 „A Satrap's Duty“ | `T_VER620_01_01` |
| `06300` | 6.3 | 70206 `AktKmj101_04670` „Once More unto the Void“ | 70136 „Buried Memory“ | `T_VER630_01_01` |
| `06400` | 6.4 | 70271 `AktKmk101_04735` „Currying Flavor“ | 70214 „Gods Revel, Lands Tremble“ | `T_VER640_01_01` |
| `06500` | 6.5 | 70280 `AktKml101_04744` „Seeking the Light“ | 70279 „The Dark Throne“ | `T_VER650_01_01` |
| `06505` | 6.5 Teil 2 | 70287 `AktKmm101_04751` „When One Door Closes...“ | 70286 „Growing Light“ | `T_VER655_01_01` |
| `07000` | 7.0 | 70396 `KinGma101_04860` „A New World to Explore“ | 70289 „The Coming Dawn“ | `T_VER700_01_01` |
| `07100` | 7.1 | 70780 `KinGmg101_05244` „A Royal Invitation“ | 70495 „Dawntrail“ | `T_VER710_01_01` |
| `07200` | 7.2 | 70835 `KinGmh101_05299` „A Glimmer of the Past“ | 70786 „Crossroads“ | `T_VER720_01_01` |
| `07300` | 7.3 | 70900 `KinGmi101_05364` „Targeted Tragedy“ | 70842 „Seekers of Eternity“ | `T_VER730_01_01` |
| `07400` | 7.4 | 70962 `KinGmj101_05426` „With the Winds“ | 70909 „The Promise of Tomorrow“ | `T_VER740_01_01` |
| `07500` | 7.5 | 71006 `KinGmk101_05470` „In Fate's Footsteps“ | 70970 „Into the Mist“ | `T_VER750_01_01` |
| `07505` | 7.5 Teil 2 | 71011 `KinGml101_05475` „A Winter's Dream“ | 71010 „Trail to the Heavens“ | `T_VER756_01_01` |

Spalte `Token` = normiertes Grenz-Token. „Patch P erreicht“ prüft man am sinnvollsten über die
Spalte `PreviousQuest[0]` (s. §3): sobald die abgeschlossen ist, kann der Spieler die erste Quest von
P annehmen und deren Cutscenes sehen.

### 2.3 Gibt es eine Excel-Kette Quest → Cutscene → VoiceMan?

- **Quest → Cutscene: ja.** `Quest.QuestParams[i].ScriptInstruction` (String) / `.ScriptArg` (uint).
  Instruktionen, die mit `CUT` beginnen (`CUT_SCENE_01`, `CUT_SCENE_N_01`, `CUTSCENE_01`,
  `NCUT_EVENT_<QUEST>_01` …; `LCUT_ACTOR0`, `LCUT_POS0` etc. sind KEINE Cutscene-Rows), tragen als Arg
  die **Row-Id im Sheet `Cutscene`**. Beispiel 70062 „Newfound Adventure“:
  `CUT_SCENE_01=2810 → Cutscene[2810].Path = "ex4/aktkmh/aktkmh10110/aktkmh10110"`,
  ebenso `CUT_SCENE_02=2811`, `CUT_SCENE_03=2812`. 70495 „Dawntrail“:
  `NCUT_EVENT_KINGMF114_01=3402 → ex5/kinact/kinact06240/kinact06240`.
  EXDSchema `Cutscene.yml`: Feld `Path` (Spalte 0) + `Unknown0…8`.
  Datei im Spiel: `cut/<Path>.cutb` (Pfad-Präfix `cut/` und Endung `.cutb` = Annahme, §4 Schritt 8).
- **Cutscene → VoiceMan: nicht in Excel.** Keines der Sheets `Cutscene`, `CutsceneName`,
  `CutsceneWorkIndex`, `CutSceneIncompQuest` (nur `Quest`-Link) verweist auf ein Textblatt oder
  einen Text-Key. Die Zuordnung, welche `TEXT_VOICEMAN_…`-Zeilen eine Cutscene spricht, steckt in der
  Cutscene-Binärdatei. Ob die `.cutb` die Text-Keys als lesbaren String enthält, ist **ungeprüft**;
  wenn ja, ließe sich zu jedem Clip die exakte Quest finden (Quest → Cutscene-Row → `.cutb` → Keys).
  Das wäre genauer als die Patch-Grenze, wird aber für §11.4 nicht gebraucht (Sprecherwechsel liegen
  auf Patch-Grenzen).
- **Andere Spalten, die NICHT taugen:**
  - `Quest.Expansion` (link `ExVersion`) ist nur die Erweiterung (6.x alle = 4), keine Patch-Auflösung.
  - `Quest.JournalGenre` (link `JournalGenre`, MSQ = Rows 1…15, `JournalGenre.JournalCategory` → 
    `JournalCategory.JournalSection = 0`) gruppiert gröber als Patches: 1 „Seventh Umbral Era“,
    2 „Seventh Astral Era“, 3 „Heavensward“, 4 „Dragonsong War“, 5 „Post-Dragonsong War“,
    6 „Stormblood“, 7 „Post-Ala Mhigan Liberation“, 8 „Shadowbringers“, 9 „The Voyage Home“,
    10 „Dark Reprise“, 11 „Endwalker“, 12 „Newfound Adventure“ (6.1–6.55), 13 „Dawntrail“,
    14 „The New Dawn“ (7.1–7.3), 15 „Winter's Prelude“ (7.4–7.56). Nützlich nur als Plausibilitäts-
    prüfung („ist das überhaupt eine MSQ?“).
  - Das Präfix von `Quest.Id` (`AktKmh…` = 6.1, `AktKmi…` = 6.2, …) passt ab 3.1 sauber zu den Patches,
    in ARR aber nicht (2.2 beginnt mit `XxaUse201`, der Rest ist `GaiUse2xx`) → `ScenarioTree` ist
    die zuverlässigere Quelle.
  - `CompleteJournalCategory.FirstQuest/LastQuest` verweisen auf `CompleteJournal`, nicht auf `Quest`.

---

## 3. Empfehlung fürs Plugin

### 3.1 Daten (einmal je Sprache unabhängig — Quests sind sprachneutral)

1. **Grenz-Tabelle zur Laufzeit aus `ScenarioTree` bauen** (kein Pflegeaufwand bei neuen Patches):
   - Alle Rows lesen, `Name` mit `^T_VER(\d)(\d)(\d)_` parsen → Schlüssel (Major, Minor, Part2 =
     Ziffer3 ∈ {5,6}).
   - Je Schlüssel die Row mit kleinstem `Unknown0` = erste Quest; `Quest[first].PreviousQuest[0]` =
     „Tor“-Quest.
   - **Sonderfälle 2.55 / 3.56 / 4.56** (in `T_VER250/350/450` verschmolzen) als kleine eingebettete
     Tabelle: `02505 → 65906`, `03505 → 67892`, `04505 → 68720` (erste Quest; Tor =
     `PreviousQuest[0]`).
   - Als Absicherung zusätzlich die Tabelle aus §2.2 eingebettet ausliefern; weicht die Laufzeit-
     Auswertung davon ab → Warnung ins Log, eingebettet gewinnt nicht automatisch (neue Patches sollen
     ohne Release funktionieren).
2. **Grenz-Token → Tor-Quest:** `PatchOf(token)` (§2.1) → Tabelle. Unbekannter Patch (z. B. Token aus
   der Zukunft, Daten noch nicht im Client) → „nicht erreicht“.

### 3.2 Fortschritt (Framework-Thread, Schnappschuss)

```csharp
// In einem Service mit IUnlockState, IClientState, IPlayerState, IDataManager (Konstruktor-DI).
// Läuft auf dem Framework-Thread: bei Login, bei IUnlockState.Unlock, sonst alle ~5 s.
bool Reached(PatchBoundary b)
{
    var quests = _data.GetExcelSheet<Quest>();
    if (b.GateQuestId != 0 && _unlock.IsQuestCompleted(quests.GetRow(b.GateQuestId))) return true;
    if (_unlock.IsQuestCompleted(quests.GetRow(b.FirstQuestId))) return true;           // Absicherung
    unsafe { var qm = QuestManager.Instance();                                          // optional: angenommen
             return qm != null && qm->IsQuestAccepted(b.FirstQuestId); }
}
// Schnappschuss: höchster erreichter Patch-Schlüssel (Story-Reihenfolge ist monoton, also genügt
// eine Binärsuche über die sortierte Grenz-Tabelle). Persistiert je ContentId: max(alt, neu).
```

- `IsLoaded`/`IsLoggedIn` falsch → Schnappschuss „unbekannt“ (nicht „nichts erreicht“).
- Die Story ist linear: „P erreicht“ ⇒ „alle früheren Patches erreicht“. Deshalb reicht als Zustand
  **ein einziger Wert** (höchster erreichter Patch), und eine Prüfung braucht nur O(log n) Abfragen.

### 3.3 Epochen-Wahl (Hintergrund-Thread, nur Schnappschuss lesen)

Für NPC + Sprache mit Grenzen `b1 < … < bn` (normierte Tokens):

1. Fortschritt **unbekannt** → **neueste Epoche** (bisheriges Verhalten, Plan F4).
2. Sonst `k` = größtes i mit `Patch(bi) ≤ erreichter Patch` →
   - kein solches i → **erste Epoche** (`Pre{b1}`),
   - sonst Epoche von `bk` (`Post{b1}` bzw. `From{bk}`, Namen wie `VoiceActorSplits.EpochName`).
3. Grenz-Token ohne Abbildung (unbekannter Patch, nicht normiert) → diese Grenze gilt als „nicht
   erreicht“ und es gibt eine Warnung beim Laden der Split-Datei.
4. Fortschritt ändert sich → nur der Schnappschuss wechselt; der nächste Satz wählt neu (Index hat
   alle Epochen).

Umsetzungshinweis: `VoiceActorSplits` (EchoScd, ohne Spielabhängigkeit) bekommt eine reine Funktion
`EpochForProgress(npc, lang, Func<string, bool?> reached)` o. ä.; die Spiel-Abfrage bleibt im Plugin
hinter einem Interface (`IStoryProgress`), damit die Auswahl mit Fakes testbar ist.

### 3.4 Fallbacks

| Lage | Verhalten |
|---|---|
| nicht eingeloggt / `PlayerState.IsLoaded == false` | unbekannt → neueste Epoche |
| `IUnlockState`-Aufruf wirft / Sheet fehlt | unbekannt → neueste Epoche, einmal loggen |
| New Game+ | ~~gespeicherter Höchststand je `ContentId` gilt (nie absenken)~~ → seit 2026-11-15: Patch der angenommenen Quest gilt (Plan §11.4) |
| Grenze in einem Patch, den der Client nicht kennt | „nicht erreicht“ |
| Clip ohne Token (2.0/2.1, anderes Schema) | wie bisher älteste Epoche (`00000`) |

---

## 4. Prüfschritte an echten Spieldaten

Alles im Client per Debug-Befehl/Log, Charakter mit bekanntem Stand. IDs aus §2.2.

1. **Sheet-Liste:** `IDataManager.Excel.SheetNames` nach `cut_scene/` filtern; prüfen, dass jeder
   Name `cut_scene/<ddd>/VoiceMan_<ddd><ZZ>` ist (Ordner = erste drei Token-Ziffern) und die Liste der
   in §2.1 entspricht (neuere Patches können hinzukommen; ein Blatt `0601x`… würde die Normierung
   widerlegen).
2. **Token-Semantik:** In `VoiceMan_06505` und `VoiceMan_06100` die ersten Zeilen lesen (Spalte 0
   Key, Spalte 1 Text) — 06505 muss die 6.55-Szenen (Wuk Lamat, Erenville) enthalten, 06100 die
   6.1-Szenen (Tataru-Brief, Y'shtola/Varshahn). Für die deutsche Y'shtola: ein Clip aus `06006` und
   einer aus `06100` anhören → alte bzw. neue Stimme.
3. **ScenarioTree:** `GetExcelSheet<ScenarioTree>()` (bzw. `RawRow`, Spalte 0 als String, Spalte 3
   als uint): Row 70062 → `Name == "T_VER610_01_01"`; Row 71014 → `"T_VER756_01_04"`; alle Rows
   haben ein `T_VER`-Präfix; `Unknown0` ist über die Patch-Reihenfolge aufsteigend. Prüfen, ob die
   Lumina-Property tatsächlich `Name`/`Unknown0` heißt (Lumina.Excel wird aus EXDSchema generiert).
4. **Quest-Zeilen:** `Quest[70062].Id == "AktKmh101_04526"` (4526 = 70062 − 65536),
   `Quest[70062].PreviousQuest[0].RowId == 70000`, `Quest[70062].JournalGenre.RowId == 12`.
5. **Fortschritt:** auf einem Charakter mit abgeschlossenem 6.1 loggen:
   `QuestManager.IsQuestComplete(70000u)`, `IsQuestComplete((ushort)4526)`,
   `IUnlockState.IsQuestCompleted(Quest[70062])`, `UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(70062)`
   → alle `true`; dieselben für 71014 (7.56, sofern nicht gespielt) → `false`. Direkt nach dem Login
   (erster Framework-Tick) und im Titelbildschirm wiederholen → Login-Zeitpunkt der Gültigkeit.
6. **Event:** eine MSQ-Quest abgeben und loggen, ob `IUnlockState.Unlock` mit `RowRef<Quest>`
   feuert.
7. **New Game+:** NG+-Kapitel 6.0 starten, Werte aus Schritt 5 erneut loggen; danach unterbrechen und
   wieder loggen. Ergebnis hier nachtragen (offene Frage 1.4).
8. **(Optional) `.cutb`:** `IDataManager.GetFile("cut/ex4/aktkmh/aktkmh10110/aktkmh10110.cutb")`
   laden und nach ASCII `TEXT_VOICEMAN_06100` durchsuchen → entscheidet, ob eine exakte Clip→Quest-
   Zuordnung möglich ist (§2.3).

---

## 5. Offene Punkte

- Verhalten von `IsQuestComplete` im New Game+ (Prüfschritt 7).
- Lesbare Text-Keys in `.cutb` (Prüfschritt 8) — nur für eine spätere, feinere Zuordnung.
- Bedeutung von `ZZ` = 10/15/20 im 7.x-Bereich ist nur aus Sprechern abgeleitet (Raids); für die
  Grenzen egal, weil Grenzen auf `XXY00`/`XX505` normiert werden.
- 2.0/2.1-Cutscene-Stimmen liegen nicht in VoiceMan-Blättern; ein Sprecherwechsel innerhalb von ARR
  (z. B. Urianger „ab 2.1“, Plan §11.6) lässt sich mit diesem Schema nur grob abbilden (alles vor
  `02200` = älteste Epoche). Für eine Grenze `02100` existiert kein Clip, die Grenze trennt also
  praktisch nichts.
- Die Beispiel-/Test-Token `06010` in `GameVoiceActorSplits.json` (`_comment`),
  `echoscd/EchoScd/VoiceActorSplits.cs` (Doku) und `echoscd/EchoScd.Tests/VoiceActorSplitsTests.cs`
  auf `06100` umstellen, wenn §11.4 umgesetzt wird (hier bewusst nicht geändert).

## 6. Quellen

- FFXIVClientStructs `QuestManager.cs`: https://github.com/aers/FFXIVClientStructs/blob/80cb4a7b7e11a6702e2eb8839afd301a918c638e/FFXIVClientStructs/FFXIV/Client/Game/QuestManager.cs
- FFXIVClientStructs `UIState.cs`: https://github.com/aers/FFXIVClientStructs/blob/80cb4a7b7e11a6702e2eb8839afd301a918c638e/FFXIVClientStructs/FFXIV/Client/Game/UI/UIState.cs
- FFXIVClientStructs `PlayerState.cs` (`IsLoaded` @0x00, `ContentId` @0x68): https://github.com/aers/FFXIVClientStructs/blob/80cb4a7b7e11a6702e2eb8839afd301a918c638e/FFXIVClientStructs/FFXIV/Client/Game/UI/PlayerState.cs
- FFXIVClientStructs `AgentScenarioTree.cs` (UI-Agent mit aktueller MSQ-ID; nur UI-Zustand, als Quelle nicht empfohlen): https://github.com/aers/FFXIVClientStructs/blob/80cb4a7b7e11a6702e2eb8839afd301a918c638e/FFXIVClientStructs/FFXIV/Client/UI/Agent/AgentScenarioTree.cs
- Dalamud `IUnlockState`: https://github.com/goatcorp/Dalamud/blob/43b34118bcf7213e575ec39517d033191b1acf17/Dalamud/Plugin/Services/IUnlockState.cs
- Dalamud `UnlockState` (Implementierung): https://github.com/goatcorp/Dalamud/blob/43b34118bcf7213e575ec39517d033191b1acf17/Dalamud/Game/UnlockState/UnlockState.cs
- EXDSchema `Quest.yml`, `ScenarioTree.yml`, `Cutscene.yml`, `JournalGenre.yml`, `CutSceneIncompQuest.yml`, `CompleteJournalCategory.yml`: https://github.com/xivdev/EXDSchema/tree/a1e24a0c72058353f30f23beb996792cdb926868
- Lumina.Excel (Sheets aus EXDSchema generiert): https://github.com/NotAdam/Lumina.Excel
- ffxiv-datamining CSV (`Quest.csv`, `ScenarioTree.csv`, `Cutscene.csv`, `JournalGenre.csv`, `JournalCategory.csv`, `cut_scene/*/VoiceMan_*.csv`): https://github.com/xivapi/ffxiv-datamining/tree/52a72b38aa2f967cab36867f34cd9bee9debb88b/csv/en
- consolegameswiki, MSQ je Patch: https://ffxiv.consolegameswiki.com/wiki/Main_Scenario_Quests
- New Game+: https://na.finalfantasyxiv.com/game_manual/newgame-plus/ · https://ffxiv.consolegameswiki.com/wiki/New_Game%2B
