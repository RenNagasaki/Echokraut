# Test-Suite-Analyse Echokraut.Tests (2026-11-18)

Nur ein Bericht. Nichts wurde gelöscht oder geändert. Stand: Commit `280aaad`, macOS arm64 (x64-Testhost unter Rosetta).

## Messgrundlage
- Lauf: 1281 Fälle (1276 grün, 5 übersprungen = `[WindowsFact]`), **9 s Wandzeit**, 78 s Summe über alle Tests (die Tests laufen parallel).
- Coverage: `coverlet.console` 10.1.1 (instrumentiert statisch, csproj bleibt unverändert), eingeschlossen nur `[Echokraut]*`.
  `coverlet.collector` ist nicht referenziert, deshalb geht `--collect "XPlat Code Coverage"` ohne Paket-Änderung nicht.
- Coverage **je Testklasse** (102 Einzelläufe mit `--filter FullyQualifiedName~<Klasse>.`), nicht je Testmethode (das wären 1276 Läufe).
  Ausgewertet: Zeilen, die nur diese eine Klasse abdeckt („unique“), und ob die abgedeckten Zeilen eine Teilmenge einer anderen Klasse sind.

## Gesamt-Coverage
Echokraut.dll: **30,8 % Zeilen / 27,1 % Branches / 50,4 % Methoden**.
Die größten Lücken außerhalb von `Windows/` (Spiel-, Audio- und Prozess-Code, viel davon nur im Spiel prüfbar):

| ungedeckt | Datei |
|---:|---|
| 2710 / 2819 | Services/DialogHarvestService.cs |
| 1039 / 1039 | Services/Live3DAudioEngine.cs |
| 564 / 567 | Services/LuabParser.cs (reiner Parser, gut testbar) |
| 481 / 481 | Services/VoiceMessageProcessor.cs |
| 481 / 481 | Services/EchokrauTtsInstanceService.cs |
| 387 / 387 | Services/AlltalkInstanceService.cs |
| 286 / 455 | Helper/Functional/TalkTextHelper.cs |
| 272 / 316 | Services/VoiceClipManagerService.cs |
| 244 / 244 | Services/LineSubmissionService.cs |
| 166 / 173 | DataClasses/ReadSeekableStream.cs (rein, gut testbar) |
| 160 / 160 | Services/StoryProgressService.cs |

Hinweis: Wer Configuration/Loc anfasst, kommt automatisch auf ~1000 Zeilen (statische Initialisierung),
zum Beispiel ValidateInstallPathTests, QwenEngineTests und PocketEngineTests. Die Gesamtzahl „covered“ einer Klasse sagt deshalb wenig.

## Laufzeit
Kein Test ist von sich aus langsam. Die Ausreißer mit 3–4 s (`AudioPackageServiceTests.Export_…`,
`MigrateFromConfigTests.Characters_…`, `DatabaseServiceTests.FindCharacterIdsByAlias_…` usw.) sind
jeweils der **erste Test einer DB-Klasse**: Dort baut EF Core einmal sein Modell auf, und JIT-Kosten fallen an, während
parallel andere Klassen laufen. Die übrigen Tests derselben Klassen liegen im ms-Bereich. → Nichts zu tun.

Netzzeit (summiert): `RemoteUrlsReachabilityTests` 8,6 s, `LodestoneServiceIntegrationTests` 6,1 s.

## Kandidaten

### A. Live-Tests → `[Trait("Category","Live")]` (empfohlen)
| Klasse | Verhalten heute | Befund |
|---|---|---|
| `RemoteUrlsReachabilityTests` (6 Fakten) | geht immer ins Netz (HttpClient, 15 s Timeout); bei 403/429 kehrt `EchokrauTtsReleasesUrl_…` grün zurück | Abdeckung 50 Zeilen, **0 unique**. Prüft also keinen Code, sondern Remote-Zustand. Klarer Live-Kandidat. |
| `LodestoneServiceIntegrationTests` (4 Fakten) | Opt-in über `.env` (`LODESTONE_TEST_*`); ohne die Variablen kehren die Tests mit `return` **grün** zurück statt übersprungen | Auf diesem Mac liegt eine `.env` → die Tests liefen live und waren **grün**. Der WORKLOG-Punkt „Lodestone rot auf dem Mac“ ließ sich heute nicht nachstellen. |

Zusätzlich: `LodestoneServiceIntegrationTests.DEBUG_RawHtmlFetch_ShowsWhatLodestoneReturns` hat **kein Assert**
(reine Diagnose-Ausgabe) → Kandidat zum Entfernen oder für `Skip`/ein Diagnose-Trait.
Das „return statt Skip“-Muster ließe sich durch ein `[LiveFact]` ersetzen, nach dem Vorbild von `WindowsOnlyAttributes.cs`
(Skip, wenn die Variablen fehlen). Dann sieht man im Bericht ehrlich „übersprungen“.
Danach `verify.js`/CI mit `--filter "Category!=Live"`.

### B. Echte Fehler im Testcode
1. **Doppelte Theory-Daten:** `GameVoiceConsentTests.IsGranted_OnlyForTheCurrentText`: `InlineData(0,false)` und
   `InlineData(CurrentVersion - 1,false)` sind bei `CurrentVersion = 1` identisch. xUnit meldet
   „Skipping test case with duplicate ID“, ein Fall läuft also stillschweigend nicht. Harmlos, aber Rauschen; z. B. `-1` statt `0` nehmen.
2. **CI ignoriert Testergebnisse:** `.gitlab-ci.yml:50` `dotnet test --no-build || true`. Rote Tests halten die
   Pipeline nie an. Kein Testcode-Problem, aber der wichtigste Befund für den Nutzen der Suite.

### C. Inhaltlich redundante / wertarme Tests (Löschkandidaten, Entscheidung beim User)
- `PhoneticCorrectionTests` (4): `Constructor_SetsFields` (Auto-Properties), `Contains_WorksInList` (testet `List<T>`,
  nicht Echokraut). Die Gleichheit ist in `StringKeyedComparableTests.PhoneticCorrection_Equals_StaysCaseInsensitive`
  schon abgedeckt; Coverage ⊆ StringKeyedComparableTests. → Datei könnte ganz entfallen oder in StringKeyedComparableTests aufgehen.
- Roundtrip/Default-Tests auf reinen Auto-Properties: `AlltalkDataTests.InstanceType_Roundtrip`,
  `AlltalkDataTests.InstanceType_DefaultsToNone`, `EchokrauTtsDataTests.CustomUrls_Roundtrip`,
  `…CustomUrls_DefaultToEmpty`, `…InstalledWrapperVersion_DefaultsToEmpty`, `…InstanceType_DefaultsToNone`.
  Beide Klassen haben 0 unique Zeilen. Die übrigen Tests darin (`BaseUrl_TrimEnd_…`, `TtsBackendArg_IsLowerCased`) haben echte Logik → behalten.

### D. „0 unique“, aber BEHALTEN (zur Einordnung)
Diese Klassen decken nichts ab, was nicht auch eine andere Klasse berührt. Die andere Klasse ist aber ein breiter Service-Test,
der die Zeilen nur durchläuft, ohne ihr Verhalten gezielt festzunageln. Die fokussierten Tests zeigen bei einem Fehler direkt auf die Ursache:
`WordErrorRateTests`, `SpeakerChangeDetectorTests`, `GameVoiceTextIndexTests`, `GameVoiceMatchSuggesterTests`,
`InlineReferenceAssemblerTests`, `PlayerNameTokenizerTests`, `NarratorVoicePolicyTests`, `VoicePackUrlResolverTests`,
`AudioPackageRulesTests`, `TtsPathsTests`, `NpcMapDataVoiceTests`, `RemoteUrlsJsonTests`.
(`GameVoiceReferenceServiceTests` allein deckt 1499 Zeilen ab, 800 davon unique.)

### E. Struktur / Ablage
- **`GameVoicePhase6HelperTests.cs`** (43 Tests) enthält **9 Testklassen** (`StoryProgressTableTests`,
  `StoryContextResolverTests`, `GameVoiceEpochsTests`, `SpeakerChangeDetectorTests`, `GameVoiceMatchSuggesterTests`,
  `GameVoiceMappingServiceTests`, `GameVoiceSpeakerMapPhase6Tests`, `GameVoiceAssignmentSessionTests`,
  `GameVoiceNpcOverrideTests`). Das verstößt gegen „eine Testdatei je Klasse unter Test“, und der Name bezieht sich auf eine Plan-Phase statt auf eine Klasse.
  → aufteilen, je eine Datei mit dem Klassennamen. `GameVoiceSpeakerMapPhase6Tests` gehört nach `GameVoiceSpeakerMapTests.cs`.
  Weitere Dateien mit Phasen-Namen: keine.
- **`NodeStateTests`** testet `Echotools.UI.NodeState` (Submodul) und deckt in Echokraut.dll 0 Zeilen ab
  → gehört eher in die Tests des Echotools-Repos (dort anlegen, hier entfernen).

### F. Migrationstests: alle noch gültig
Jede getestete Migration existiert und wird aufgerufen:

| Test | Code | Aufruf |
|---|---|---|
| DatabaseMigrationTests (`EnsureSpeakerAliasTable`, `…VoiceKey`, `…WavFileName`, `…ReferenceSource`, `EnsureGameVoiceIndexTable`) | DatabaseService.cs | `RunSchemaMigrations` (v1→v8) |
| GameVoiceClipIsolationTests (`EnsureVoiceClipGenerationReferenceSource`) | DatabaseService.cs:243 | Kette v6→v7 |
| MigrateFromConfigTests | `IDatabaseService.MigrateFromConfig` | Plugin.cs:248 |
| BackfillAudioFilesTests | `BackfillAudioFiles` | Plugin.cs:256 |
| RetokenizePlayerNamesTests | `RetokenizePlayerNames` | NativeGameDataToolsWindow.cs:1032 |
| AlltalkDataMigrationTests | `AlltalkData.MigrateLegacyInstanceTypeFields` | Configuration.cs:266 |
| LogConfigMigrationTests | `Configuration.MigrateLegacyLogConfig` | Configuration.cs:156/271 |
| WrapperVersionMigrationTests | `MigrateWrapperVersionForExistingInstalls` | Configuration.cs:269 |

Offene Frage an den User (nicht aus dem Code entscheidbar): Ab welcher Mindestversion dürfen die Einmal-Migrationen
`MigrateFromConfig`/`BackfillAudioFiles` (Vor-DB-Configs) samt Tests wegfallen? Erst dann werden ihre Tests zu Kandidaten.

### G. Selbsttest-Tests (neu)
`Echokraut.Tests/SelfTest/` (8 Klassen) haben durchweg unique Zeilen (7–87). Keine Redundanz.
`SelfTestRunnerTests.OuterCancellation_IsNotCalledATimeout` dauert 1,2 s (echtes Warten); vertretbar.

## Reproduzieren
```bash
dotnet tool install coverlet.console --tool-path <tmp>/tools
B=Echokraut.Tests/bin/x64/Debug
<tmp>/tools/coverlet $B/Echokraut.Tests.dll --target dotnet \
  --targetargs "test $B/Echokraut.Tests.dll --nologo [--filter FullyQualifiedName~<Klasse>.]" \
  --include "[Echokraut]*" --format json -o <out>.json
dotnet test Echokraut.Tests/Echokraut.Tests.csproj -c Debug -p:Platform=x64 --no-build --logger "trx;LogFileName=<tmp>/run.trx"
```
(`DOTNET_ROOT_X64=~/.dotnet-x64` auf dem Mac. coverlet instrumentiert die DLL in `bin` und stellt sie danach wieder her.)
