# Repo-Audit Echokraut — 2026-07-31

**Stand:** `main` @ `775a3c0`, 5 Commits vor `origin/main`, Working Tree mit **uncommitteten
Bug-5-Änderungen** (`AudioPlaybackService.cs`, `Live3DAudioEngine.cs`, `VoiceMessageQueueTests.cs`,
Changelog EN/DE, `Services/CLAUDE.md`). Build 0 Fehler, 569 Tests grün.

**Umfang:** Services, Windows/Native, DataClasses, Helper, Backends. Gelesen: `Live3DAudioEngine`,
`AudioPlaybackService`, `BackendService`, `VoiceMessageQueue`, `VoiceMessageProcessor`,
`EchokrauTtsBackend`, `AlltalkBackend`, `DialogState`, `NativeConfigWindow.OnUpdate`,
`NativeNodeFactory`, `Plugin.cs`, plus gezielte Greps über das ganze Repo.

**Dies ist ein Vorschlag, keine Umsetzung.** Nichts unten ist angefasst.

---

## 0. Zuerst zu klären

Die Bug-5-Änderungen liegen uncommittet im Working Tree. Alles unten setzt darauf auf. Reihenfolge
sollte sein: Bug 5 in-game testen → committen → dann dieser Plan. Sonst vermischen sich zwei
Änderungssätze in denselben Dateien (`AudioPlaybackService`, `Live3DAudioEngine`).

---

## 1. Echte Fehler (Phase 1 — würde ich zuerst machen)

### A1 · `VoiceMessageQueue._allEntries` wächst unbegrenzt
`Services/Queue/VoiceMessageQueue.cs:22`

`_allEntries[entry.Id] = entry` in `Enqueue`, aber **nirgends** ein `TryRemove` — nur
`Dispose()` räumt auf. Jede jemals gesprochene Zeile bleibt für die ganze Spielsitzung im
Dictionary, samt `VoiceMessage` → `Speaker` (`NpcMapData`) und `SpeakerFollowObj`
(**`IGameObject`-Referenz auf ein Spielobjekt**). Zusätzlich iterieren `CancelAll`,
`CancelBySource` und `GetStatistics` über diese stetig wachsende Menge — bei jedem
Dialog-Advance.

**Geplante Änderung:** Terminale Einträge aus `_allEntries` entfernen. Nicht sofort beim
Terminal-Übergang (`GetEntry`/`GetEntriesByState` würden Einträge verlieren, die die UI kurz
danach noch liest), sondern:
- In `MarkAsCompleted`/`MarkAsCancelled`/`MarkAsFailed` einen `CompletedAt`-Zeitstempel setzen.
- Eine `PruneTerminal(TimeSpan retention)` ergänzen, die terminale Einträge älter als ~30 s
  entfernt, aufgerufen aus `Enqueue` (amortisiert, kein Timer).
- `entry.Message.SpeakerObj`/`SpeakerFollowObj` beim Terminalwerden auf `null` setzen, damit die
  Spielobjekt-Referenz nicht am Eintrag hängt, solange er noch in der Retention ist.

**Tests:** `VoiceMessageQueueTests` — nach N abgeschlossenen Zeilen ist `_allEntries` beschränkt;
ein gerade abgeschlossener Eintrag ist via `GetEntry` noch erreichbar.

---

### A2 · `AudioPlaybackService._currentlyPlayingDictionary` — Leck + Thread-Race
`Services/AudioPlaybackService.cs:27`

Zwei Probleme in einem Feld:
1. **Leck:** `OnSourceEnded` macht `TryGetValue`, aber **kein `Remove`**. Nur der Skip-Pfad
   (Zeile 288) entfernt. Jede normal zu Ende gespielte Zeile bleibt drin — dieselbe Klasse wie A1.
2. **Race:** Es ist ein plain `Dictionary<,>`. Geschrieben wird aus dem `onStreamCreated`-Callback
   (Playback-Loop-Thread), gelesen/entfernt wird in `OnSourceEnded`, das über
   `ThreadPool.QueueUserWorkItem` aus dem BASS-Sync-Callback kommt
   (`Live3DAudioEngine.cs:868`). Gleichzeitiges Add + Read auf einem `Dictionary` kann in eine
   Endlosschleife oder korrupte Buckets laufen.

**Geplante Änderung:** → `ConcurrentDictionary<Guid, VoiceMessage>`, und in `OnSourceEnded` am
Ende `TryRemove(guid, out _)` (im `finally`, damit auch der Fehlerpfad räumt).

**Tests:** nicht sinnvoll unit-testbar (BASS). Änderung ist mechanisch und lokal.

---

### A3 · `PickVoice` vergibt für namenlose NPCs die erste Stimme der Liste
`Services/BackendService.cs:521-528`

```csharp
var npcName = npcData.Name ?? string.Empty;
for (var i = 0; i < voices.Count; i++)
    if (voices[i].VoiceName.Contains(npcName, StringComparison.OrdinalIgnoreCase))
        return voices[i];
```
`"beliebig".Contains("")` ist **true** → bei leerem NPC-Namen (Bubbles ohne auflösbaren Namen,
`???`-Sprecher vor Alias-Auflösung) trifft die Namensschleife sofort und liefert `voices[0]`.
Race/Gender/BodyType-Filter darunter wird nie erreicht. `IsVoiceFittingForNpc` (Zeile 455) hat den
Guard `!string.IsNullOrEmpty(npc.Name)` bereits — `PickVoice` nicht. Die beiden widersprechen sich.

**Geplante Änderung:** Namensschleife nur betreten, wenn `!string.IsNullOrWhiteSpace(npcName)`.

**Tests:** neuer `BackendServiceTests` (s. Phase 4) — leerer Name → Race/Gender-Match statt `[0]`.

---

### A4 · Blockierendes HTTP auf dem Framework-Thread (Frame-Freeze bis 5 s)
`Backends/EchokrauTtsBackend.cs:110-112`, `Backends/AlltalkBackend.cs:96`

Beide `GetAvailableVoices` sind synchron (`.GetAwaiter().GetResult()`, 5-s-Timeout-Client).
Aufrufkette: `NativeConfigWindow.OnUpdate` (Zeile 600-604, `_pendingRemapAfterConnect`)
→ `RefreshBackend` → `MapVoices` → `GetAvailableVoices`. `OnUpdate` läuft auf dem
Framework-Thread → bei unerreichbarem Backend friert das Spiel bis zum Timeout ein. Exakt die
Klasse, die 2026-07-05 für Stop/Install schon gefixt wurde (`Task.Run` in den Buildern).

**Geplante Änderung:** Nicht die Backends umbauen (das rippelt in `ITTSBackend`), sondern die
**Aufrufstelle**: `_pendingRemapAfterConnect` → `Task.Run(() => _backend.RefreshBackend())`, und in
`MapVoices` die anschließende UI-Benachrichtigung (`VoicesMapped`) über
`IFramework.RunOnFrameworkThread` zurückbouncen. Gleiche Prüfung für die anderen
`RefreshBackend`-Aufrufer (`OnInstanceReady`, `DatabaseWiped` — beide feuern bereits off-thread,
also nur verifizieren).

**Alternative, sauberer aber teurer:** `ITTSBackend.GetAvailableVoices` → `Task<List<string>?>`
und `MapVoices` async. Erwischt alle künftigen Aufrufer. Würde ich vorschlagen, wenn wir ohnehin
an `MapVoices` gehen (siehe C2) — sonst die billige Variante.

---

### A5 · `BackendService.ReloadService` blockiert per `.Result`
`Services/BackendService.cs:135` · `return _backend.ReloadService(reloadModel, eventId).Result;`

Gleiche Freeze-Gefahr wie A4, plus klassische Sync-over-Async-Deadlock-Falle.

**Ursprünglicher Vorschlag:** Signatur → `Task<bool> ReloadServiceAsync(...)`.
**Bei der Umsetzung verifiziert (wie im Plan angekündigt): `IBackendService.ReloadService` hat
NULL Aufrufer** im ganzen Repo — weder UI noch Tests noch Installer. Es ist toter Code.
→ **Umgesetzt als Entfernung** aus `IBackendService` + `BackendService` statt als Async-Umbau.
`ITTSBackend.ReloadService` + die beiden Backend-Implementierungen bleiben vorerst stehen
(AllTalks Reload-Endpoint ist real); sie fliegen mit **D2** in Phase 4 raus, wo die anderen
toten Interface-Methoden (`Cancel`/`Pause`/`Resume`) ohnehin drankommen.

---

### A6 · `PingBackendAsync` baut pro Probe einen neuen `HttpClient`
`Services/BackendService.cs:291` · `using var http = new HttpClient { … };`

Exakt der Socket-Exhaustion-Bug, der auf `refactor/solid-cleanups` (`1624395`) für
`AlltalkBackend` schon behoben wurde. Hier ungefixt. TTL von 30 s begrenzt die Frequenz, aber jede
Probe hinterlässt einen Socket im `TIME_WAIT`.

**Geplante Änderung:** `private static readonly HttpClient _probeClient` mit
`ReachabilityRequestTimeout`, analog zu den Backends.

---

### A7 · `GetAllCharacters()` ist die einzige ungecachte DB-Abfrage — und läuft pro Dialogzeile
`Services/VoiceMessageProcessor.cs:456`, `Services/DatabaseService.cs:597`

```csharp
var aliasChar = _db.GetAllCharacters().FirstOrDefault(c => c.Id == resolvedCharId.Value);
```
`GetAllCharacters()` ist **nicht** gecacht (anders als `GetNpcs`/`GetPlayers`/`GetVoices`/
`GetPhoneticCorrections`/`GetMutedBaseIds`): sie zieht unter `_writeLock` die **komplette**
`characters`-Tabelle per `AsNoTracking().ToList()` und wird hier benutzt, um **eine** Zeile über
den Primärschlüssel zu finden. Nach einem Harvest sind das zehntausende Zeilen — pro
alias-aufgelöster Dialogzeile, unter dem Lock, das der Harvest zum Schreiben braucht.

**Geplante Änderung:** `IDatabaseService.FindCharacterById(int id)` ergänzen
(`_context.Characters.AsNoTracking().FirstOrDefault(c => c.Id == id)`, oder direkt aus
`_cachedNpcs`/`_cachedPlayers` bedient) und an dieser Stelle verwenden. `GetAllCharacters()`
bleibt für die Batch-Aufrufer.

**Tests:** `DatabaseServiceTests` — `FindCharacterById` Treffer/Fehlschlag.

---

### A8 · `EchokrauTtsBackend._lastJobId` — falscher Job stornierbar
`Backends/EchokrauTtsBackend.cs:53, 204`

`StopGenerating` bricht immer den **zuletzt gesehenen** Job ab. Zwei Löcher:
- `RefreshBackend` erzeugt eine **neue** Backend-Instanz (`CreateActiveBackend`, Zeile 114) →
  `_lastJobId` ist `null` → `StopGenerating` ist ein stiller No-Op.
- Startet zwischen Cancel-Auslösung und `StopGenerating` schon die nächste Zeile, zeigt
  `_lastJobId` auf **die**, und wir stornieren die Zeile, die der Spieler hören will.
  (`BackendService._generatingEntry`-Guard verkleinert das Fenster, schließt es nicht.)

**Geplante Änderung:** Job-Id am `VoiceMessageEntry` mitführen statt im Backend-Feld:
`ITTSBackend.StopGenerating(EKEventId, string? jobId)`, `GenerateAudioStreamFromVoice` gibt die
Id über einen `out`/Callback heraus (analog zu `onStreamCreated` in `PlayStream`).
AllTalk ignoriert den Parameter (dessen Stop ist global).

---

### A9 · `.Result` in `CleanText` für Chat-Zeilen
`Services/VoiceMessageProcessor.cs:282` · `_languageDetection.GetTextLanguage(...).Result`

`ProcessSpeechAsync` ist bereits `async` — hier ist der blockierende Aufruf schlicht unnötig.

**Geplante Änderung:** `CleanText` → `async Task<string?> CleanTextAsync`, `ref`-Parameter
(`language`, `speaker`) durch ein kleines Rückgabe-Record ersetzen (`ref` + `async` geht nicht).

---

## 2. Robustheit / Log-Hygiene (Phase 2)

### B1 · `DialogState` — statischer Zustand (SonarQube S2696) + unsynchronisiertes `HashSet`
`Services/DialogState.cs`

Bekannter Backlog-Punkt. Zusätzlich neu gefunden: `SpeakersResolvedThisDialog` ist ein plain
`HashSet<int>`, beschrieben aus `VoiceMessageProcessor.GetOrCreateNpcDataAsync` (async, Threadpool)
und geleert aus `AddonTalkHelper.OnPostUpdate` (Framework-Thread). Gleiches Race wie A2.

**Geplante Änderung, zweistufig:**
- **Sofort, billig:** `SpeakersResolvedThisDialog` → `ConcurrentDictionary<int,byte>` hinter
  `Add`/`Contains`/`Clear`-Methoden. Behebt das Race ohne DI-Umbau.
- **Später, separat:** `IDialogStateService` + DI (S2696). Rippelt in `AddonTalkHelper`,
  `DialogTalkController`, `NativeWindowManager`, `Plugin.WireEvents`, `VoiceMessageProcessor`.
  **Eigener Commit, nicht mit den Bugfixes vermischen.**

### B2 · Nullability-Vertrag von `GetOrCreateNpcDataAsync` stimmt nicht
`VoiceMessageProcessor.cs:418` deklariert `Task<NpcMapData>` (non-nullable), Zeile 149 prüft aber
`if (npcData == null)`. Entweder ist der Check tot oder die Signatur lügt.

**Ursprünglicher Vorschlag:** Signatur → `Task<NpcMapData?>`, Check bleibt — mit der Annahme, der
`GetAddCharacterMapData`-Pfad könne nichts liefern.
**Bei der Umsetzung geprüft: die Annahme war falsch.** `NpcDataService.GetAddCharacterMapData`
(Zeilen 242-300) liefert auf **jedem** Pfad ein Objekt — findet es keine Zuordnung, fügt es `data`
hinzu und gibt genau das zurück. Die Signatur ist also korrekt, der Null-Check ist toter Code.
→ **Umgesetzt als Entfernung des Checks**, mit Kommentar warum er nicht feuern kann.

### B3 · Log-Level: routinemäßig unerreichbares Backend loggt `Error`
`EchokrauTtsBackend.cs:124`, `AlltalkBackend.cs:108,140,150,156,163`

`GetAvailableVoices`/`CheckReady` loggen Verbindungsfehler als `_log.Error`. Jeder
`Error`-Eintrag löst Dalamuds „Echokraut is creating errors"-Popup aus. Ein noch nicht
gestartetes lokales Backend beim Login ist der **Normalfall**, kein Nutzerproblem — die
Log-Level-Regel im Projekt-CLAUDE.md sagt dafür explizit `Warning`.

**Geplante Änderung:** Verbindungs-/Timeout-Fehler in beiden Backends auf `Warning`.
`CheckReady` gibt den Text ohnehin an die UI zurück, dort ist er sichtbar.

### B5 · `NpcDataService._mappedNpcs`/`_mappedPlayers` sind ungeschützte `List<>` (bei A4 gefunden)
`Services/NpcDataService.cs:23-27, 232-233`

**Neu, nicht Teil des ursprünglichen Audits — beim Umsetzen von A4 aufgefallen.**
`RefreshSelectables` iteriert beide Listen per `ForEach`, während `GetAddCharacterMapData`
(Dialogpfad, anderer Thread) `list.Add(...)` macht. Das ist ein `InvalidOperationException:
Collection was modified` im besten Fall.

**Wichtig:** die Race ist **nicht** durch A4 entstanden. `RefreshBackend` → `MapVoices` →
`RefreshSelectables` läuft bereits heute off-thread — aus `Task.Run(RefreshBackend)` im
`BackendService`-Konstruktor und aus dem `OnInstanceReady`-Event. A4 fügt nur weitere
Aufrufstellen mit demselben (schon vorhandenen) Muster hinzu. Der Kommentar an der
`_pendingRemapAfterConnect`-Stelle („defer to the main thread … fires VoicesMapped/UI events")
suggerierte eine Thread-Garantie, die es nie gab: `OnVoicesMapped`/`OnCharacterMapped` setzen
nur `bool`-Flags, der Node-Aufbau passiert ohnehin in `OnUpdate`.

**⚠ Beim Angehen gestoppt — ein naives Lock baut einen Deadlock.** Befund:

`DatabaseService` feuerte `VoiceClipLogged` **innerhalb** von `lock (_writeLock)`
(`LogVoiceClip`, `LogOrUpdateVoiceClip`; `WipeAll` machte es bereits richtig). An diesem Event
hängt `NpcDataService.LoadFromDatabase`. Ein `_mapLock` um die Listen ergäbe damit:

| | hält | will |
|---|---|---|
| Thread A | `_writeLock` (LogOrUpdateVoiceClip) → Event → LoadFromDatabase | `_mapLock` |
| Thread B | `_mapLock` (GetAddCharacterMapData) → `_db.SaveCharacter` | `_writeLock` |

→ vollständiger Spiel-Freeze, deutlich schlimmer als die Race.

**Bereits umgesetzt (Voraussetzung, sicher für sich):** beide Events aus dem `_writeLock`
herausgezogen, mit Begründung am Event dokumentiert. Der Zyklus ist damit gebrochen.

**Noch offen — der eigentliche B5-Kern.** Drei Teile, die zusammen gehören:
1. `MappedNpcs`/`MappedPlayers` → `IReadOnlyList<NpcMapData>` + Snapshot. **Wichtig: über den
   Typ, nicht per Konvention** — gibt die Property weiter eine `List<>` heraus und man ersetzt
   sie nur durch eine Kopie, werden die **8 externen Mutationsstellen still wirkungslos**
   (`List.Remove` compiliert weiter, trifft aber die Kopie). Mit `IReadOnlyList` bricht der
   Compiler an jeder Stelle → keine stille Regression.
2. Externe Mutationen auf Service-Methoden umbiegen. Sinnvoll: `RemoveCharacter(data)` pflegt
   den Cache gleich mit — alle 8 Stellen machen ohnehin `RemoveCharacter(x)` **und**
   `MappedXxx.Remove(x)` direkt hintereinander. Plus ein `ClearMappedCaches()` für den
   Wipe-Pfad (`NativeConfigWindow:569`).
3. Erst dann die Synchronisierung. **Ein Lock allein reicht nicht:** `GetAddCharacterMapData`
   ist ein Read-Modify-Write (find → add) mit DB-Aufrufen mittendrin. Das Lock darf keinen
   `_db.*`-Aufruf umschließen (sonst wieder Lock-Ordnung), also braucht die Operation entweder
   eine echte Atomaritätsstrategie oder Copy-on-Write plus eine Regel, was bei einem Konflikt
   gewinnt. **Das ist der Teil, der Nachdenken braucht, nicht Tippen.**

Aufwand realistisch: eigener, konzentrierter Arbeitsblock mit In-Game-Test. Nicht nebenbei.

### B6 · „Clear mapped players" wirft nach dem ersten Eintrag (bei B5 gefunden)
`Windows/Native/NativeConfigWindow.cs:896`

`foreach (var p in _npcData.MappedPlayers) { … _npcData.MappedPlayers.Remove(p); }` — Mutation
der Liste, über die gerade iteriert wird → `InvalidOperationException: Collection was modified`
beim ersten Entfernen. Der Button löschte also genau **einen** Spieler und brach dann ab. Die
Schwester-Buttons („Clear mapped NPCs" / „Clear mapped bubbles") iterieren über eine
`FindAll(...)`-Kopie und waren nie betroffen — deshalb fiel es nur hier auf.
**✅ Umgesetzt:** `.ToList()` vor der Schleife.

### B4 · Leere Catch-Blöcke (S2486)
~60 in `DialogHarvestService`, 12 in `Plugin.Dispose`, 4 in `Live3DAudioEngine.StopAndDispose`.
Unterschiedlich zu bewerten:
- **Plugin.Dispose / StopAndDispose:** bewusst und richtig (Teardown darf nicht abbrechen).
  Vorschlag: `catch { /* teardown: keep going */ }` kommentieren, nicht ändern.
- **DialogHarvestService (Lumina-Spaltensondierung, Zeilen ~230-500):** verschluckt
  Schema-Drift stumm. Wenn Square eine Spalte verschiebt, harvestet der Nutzer stillschweigend
  leere Ergebnisse. → siehe D3.

---

## 3. Performance (Phase 3)

### C1 · `Dim()` schreibt jeden Frame Alpha auf ~40+ Nodes
`Windows/Native/NativeNodeFactory.cs:53` · `if (node != null) node.Alpha = enabled ? 1.0f : 0.4f;`

`NativeConfigWindow.OnUpdate` ruft `Dim` unbedingt für ~40 Nodes pro Frame auf (Zeilen 606-660),
weitere Fenster analog. Der Setter macht `SetAlpha((byte)(value*255))` → nativer ATK-Schreibzugriff
jeden Frame. Das verstößt gegen die **eigene** Regel in `Dalamud/CLAUDE.md`
(„Don't set alpha/multiply every frame … Setting AddNodeFlags/RemoveNodeFlags every frame can
crash the game").

**Geplante Änderung:** `Dim` idempotent machen —
`var a = enabled ? 1.0f : 0.4f; if (Math.Abs(node.Alpha - a) > 0.001f) node.Alpha = a;`
Ein Zeilen-Fix an einer Stelle, wirkt für alle Fenster. Analog `SetVisible`
(`if (node.IsVisible != visible)`).

### C2 · `MapVoices` fragt `GetVoices()` dreimal ab und baut die Liste zweimal
`BackendService.cs:151, 179, 210` + Mapping-Allokation je Aufruf. Kein Bug (Cache), aber unnötig.
**Geplante Änderung:** einmal lesen, nach dem Insert-Block einmal neu lesen, Ergebnis
weiterverwenden.

### C3 · `PhoneticCorrection`-Liste wird pro Zeile neu materialisiert
`VoiceMessageProcessor.cs:291-292`: `_db.GetPhoneticCorrections().Select(…).ToList()` pro
gesprochener Zeile. Quelle ist gecacht, die Projektion nicht.
**Geplante Änderung:** projizierte Liste in `DatabaseService` mitcachen (neben `_cachedPhonetics`,
in `RefreshCaches` befüllt) oder `ITextProcessingService.ReplacePhonetics` direkt die
Entity-Liste nehmen lassen.

---

## 4. Struktur (Phase 4 — optional, größer)

### D1 · `VoiceEntityToEchokrautVoice` doppelt implementiert
`BackendService.cs:668` und `NpcDataService.cs:484`. Klassische DRY-Verletzung; die beiden können
auseinanderlaufen (`BackendService` mappt zusätzlich `Note`).
**Geplante Änderung:** nach `Helper/Functional/VoiceEntityMapper` (statisch, rein, testbar),
beide Aufrufer darauf. `EchokrautVoiceToEntity` gleich mit.

### D2 · `IBackendService.Cancel/Pause/Resume` sind leere No-Ops
`BackendService.cs:397-413` — drei Methoden mit Kommentar „Handled by AudioPlaybackService".
Tote Interface-Fläche (ISP).
**Geplante Änderung:** aus `IBackendService` entfernen; Aufrufer prüfen und auf
`IAudioPlaybackService` umbiegen.

### D3 · `DialogHarvestService` (3969 Zeilen) — gezielter Teil-Extrakt statt Rundum-Split
Ein voller SRP-Split ist mir hier zu riskant fürs Verhältnis. Konkret extrahieren würde ich nur
den Lumina-Spalten-Sondierblock (Zeilen ~225-500, der Cluster aus ~30 identischen
`try { row.ReadUInt32Column(n) } catch { }`) in ein `Helper/Functional/LuminaColumnProbe`:
`TryReadIds(row, params int[] columns)`. Gewinn: eine Stelle, unit-testbar, und ein Zähler
für „Spalte fehlt" → **eine** Warnung am Ende des Harvests statt 30 stumme Catches (behebt den
DialogHarvest-Teil von B4).

### D4 · `Live3DAudioEngine.StartCore` — Cognitive Complexity 37/15
Bekannter Backlog-Punkt, weiterhin gültig (die Bug-1/5-Fixes haben eher noch Zweige ergänzt).
**Geplante Änderung:** drei private Methoden herausziehen — `ResolveInputFormat()` (der
`_autoDetectWav`-Block, Zeilen 422-482), `CreateAndConfigureChannel()` (490-534),
`StartWorkersAndPlay()` (536-543). Rein mechanisch, verhaltenserhaltend.

### D5 · Debug-Logging auf Info-Level im Audio-Hotpath
`Live3DAudioEngine.cs:1142` loggt für **jede** Zeile 64 Bytes Audio als Hex-String
(`FIRST PUTDATA BYTES`), Zeilen 438/500 loggen `WAV seek parse ok` / `FMT DECIDED` auf `Info`.
Das war Diagnose für die Streaming-Bugs.
**Geplante Änderung:** alle drei auf `Debug`; den Hex-Dump ganz raus oder hinter ein
Debug-Level-Gate (der String wird gebaut, bevor irgendwer ihn filtert).

---

## 5. Testlücken

Es gibt **keine** `BackendServiceTests`, `VoiceMessageProcessorTests`, `AudioPlaybackServiceTests`
— das sind die drei logikdichtesten Services und genau die, die die letzten vier Bugfixes
angefasst haben. 48 Testdateien decken drumherum ab (Queue, Helper, DB, Backends-Parsing).

**Geplante Änderung:** mit Phase 1 ein `BackendServiceTests` anlegen und die reinen
Entscheidungsmethoden abdecken — `PickVoice` (inkl. A3), `IsVoiceFittingForNpc`,
`EnsureFittingVoice`, `IsBackendAvailable`/`CanConnectActive` über die drei `InstanceType`-Werte.
Die brauchen nur `IDatabaseService`+`INpcDataService`-Mocks, kein BASS und kein Dalamud.

---

## 6. Vorgeschlagene Reihenfolge

| Phase | Inhalt | Dateien | Risiko |
|---|---|---|---|
| **0** | Bug 5 in-game testen + committen | (bestehende WIP) | — |
| **1** | A1-A3, A6, A7 + `BackendServiceTests` | 6 | niedrig, testbar |
| **1b** | A4, A5, A8, A9 (Threading/Async) | 5 | mittel — in-game-Test nötig |
| **2** | B1 (nur `ConcurrentDictionary`), B2, B3 | 5 | niedrig |
| **3** | C1, C2, C3 | 4 | niedrig, C1 in-game sichtbar |
| **4** | D1, D2, D4, D5 | 6 | niedrig, verhaltenserhaltend |
| **5** | D3 (`LuminaColumnProbe`), B4-Rest | 2 | mittel — Harvest-Regression |
| **später** | `IDialogStateService` (S2696, voll) | ~8 | eigener Commit |

Phase 1 und 1b berühren beide `BackendService.cs` und `AudioPlaybackService.cs` — sinnvoll in
einem Feature-Branch (`fix/audit-2026-07-31`), da >3 Dateien.

Changelog-Bullets pro Commit: A1/A2 (Speicherleck bei langen Sitzungen), A3 (falsche Stimme für
namenlose Sprecher), A4/A5 (Freeze beim Verbindungstest), B3 (kein Fehler-Popup mehr ohne
Backend), C1 (UI-Last). Der Rest ist intern → `(no changelog: internal refactor)`.

---

## 7. Bewusst NICHT vorgeschlagen

- **`NpcDataService`-SRP-Split** — Begründung im Handoff (§3) unverändert gültig.
- **`DialogHarvestService`-Vollsplit** — 3969 Zeilen, kaum Testabdeckung, hohe
  Regressionsgefahr für ein rein strukturelles Ziel. Nur D3 als Teil-Extrakt.
- **`.Result` in Installer-/Provisioning-Pfaden** (`LocalInstallerProvisioner`,
  `*InstanceService`, `RemoteUrlService`, `JsonDataService`) — die laufen bereits off-thread
  bzw. beim Start; kein Freeze-Pfad gefunden. Nur die vier oben genannten (`A4`, `A5`, `A9`,
  `BackendService:135`) hängen an Framework-Thread-Aufrufern.
- **GoogleDrive-Secrets** — False Positive, im Handoff (§3) geklärt.
