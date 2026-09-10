<!-- Generated 2026-06-04 by the solid-audit workflow (.claude/workflows/solid-audit.js): 36 agents, per-module fan-out + adversarial verify. Read-only audit; no code changed. -->

# Echokraut DRY/KISS/SOLID Audit — Synthesis Report

## 1. Executive Summary

**62 confirmed findings** across Services, DataClasses, Windows/Native, Helper/Functional, Enums, and Backends.

| Principle | Count |
|-----------|-------|
| DRY | 30 |
| KISS | 18 |
| SRP | 9 |
| DIP | 3 |
| ISP | 2 |

| Severity | Count |
|----------|-------|
| High | 11 |
| Medium | 30 |
| Low | 21 |

The dominant signal is **DRY** (nearly half of all findings), and it clusters into a handful of repeating structural patterns rather than scattered one-offs. Fixing the ~6 cross-cutting themes below resolves roughly 25 of the 30 DRY findings and several SRP/KISS items at once. The codebase also shows a recurring "copy a private helper into the next class instead of extracting it" habit in both `Windows/Native/` and `Services/` addon helpers — multiple files carry comments explicitly acknowledging the duplication.

---

## 2. Cross-Cutting Themes (highest leverage)

### Theme A — Native UI node factories & helpers duplicated across 6 windows (HIGH)
The same node-creation and state helpers are reimplemented per-window, with `NativeAlltalkBuilder.cs` even commenting "mirror NativeConfigWindow's factory methods."

- **`Button` / `Input` / `Label` / `Separator` factories** — `NativeConfigWindow.cs:1433`, `NativeGameDataToolsWindow.cs:731`, `NativeVoiceClipDetailWindow.cs:679`, `NativeVoiceClipManagerWindow.cs:991`, `NativeFirstTimeWindow.cs:534`, `NativeAlltalkBuilder.cs:268`.
- **`Dim()` / `SetVisible()`** — `NativeConfigWindow.cs:786`, `NativeFirstTimeWindow.cs:511`, `NativeNpcEditWindow.cs:317`, `NativeAlltalkBuilder.cs:270`, `NativeVoiceClipManagerWindow.cs:1000`.
- **`CreateCollapsibleSection()`** — verbatim copy in `NativeConfigWindow.cs:1460` and `NativeGameDataToolsWindow.cs:753`.
- **Hover-tint + tooltip wiring for `DynamicIconButtonNode`** — 6 copies across `NativeVoiceClipManagerWindow.cs:317`, `NativeGameDataToolsWindow.cs:312`, `NativeConfigWindow.cs:384`.
- **Auto-sized `TextButtonNode` measure-and-resize** — `NativeVoiceConfigWindow.cs` (lines 124, 185, 265) is the only sibling window *not* using a helper.

**Refactor:** Create one public static `NativeNodeFactory` (and/or `NativeNodeHelpers`) under `Helper/Functional/` or `Windows/Native/`. Move `Button`, `Label`, `Input`, `Check`, `Separator`, `HeaderLabel`, `Spacer`, `Dim`, `SetVisible`, `CreateCollapsibleSection`, `MakeButton`, and `WireIconButtonHover(node, normalTint, hoverTint)` there. Delete all per-window copies. This single extraction closes 5 DRY findings (4 of them HIGH/medium).

### Theme B — NPC identity resolution copied across 3 services (HIGH)
`RaceNameMap`, `ResolveRace`, `DetermineGender`, `IsWildRace`, `ResolveDisplayName` exist as full copies in `NpcAttributionRepairService.cs:26` and `DialogHarvestService`; `IsWildRace` appears a *third* time in `CharacterDataService`. An in-code comment dismisses it as "short enough" — it is ~90 lines.

**Refactor:** Extract a stateless `NpcIdentityResolver` under `Helper/Functional/` taking `IDataManager` + `IJsonDataService`. All three services delegate to it.

### Theme C — `ToString()`-based equality/comparison triad in 4 data classes (MEDIUM)
Identical `GetHashCode`/`Equals`/`CompareTo` block in `NpcMapData.cs:54`, `PhoneticCorrection.cs:24`, `EchokrautVoice`, and `BackendVoiceItem`.

**Refactor:** Introduce an abstract `StringKeyedDataClass` base (or static helper) parameterized by the string key; each class inherits/delegates.

### Theme D — `LogSourceConfig` reinvented and re-switched (HIGH)
`LogConfig.cs:9` hand-rolls 8× the 4-property `LogSourceConfig` that Echotools already ships for exactly this purpose. `NativeConfigWindow.Logs.cs:433` then has **8 parallel switch blocks** (4 getters + 4 setters) mapping `TextSource` to those fields.

**Refactor:** Delete `LogConfig.cs`; store 8 named `LogSourceConfig` properties (or `Dictionary<TextSource, LogSourceConfig>`) on `Configuration`. Replace the 8 switches with one `GetSourceConfig(TextSource)` accessor; each `GetShowDebug`/etc. becomes a one-line read. Closes 2 findings (both involving the same root cause).

### Theme E — Addon-helper logic duplicated across the 4 Talk/Bubble/SelectString helpers (MEDIUM)
- **Voice-line skip guard** (`nextIsVoice`/`timeNextVoice`/`NotifyNextIsVoice` + consume-and-expire) — `AddonBattleTalkHelper.cs:28`, `AddonBubbleHelper.cs:43`, `AddonTalkHelper.cs:42` (only the 500ms vs 1000ms timeout varies).
- **`GetAddonStrings` + `HandleSelectedString`** — line-for-line identical between `AddonSelectStringHelper.cs:81` and `AddonCutSceneSelectStringHelper.cs:74`.
- **Speaker resolution + `ProcessSpeechAsync` dispatch** — 4 copies (`AddonBattleTalkHelper.cs:132`, `AddonSelectStringHelper.cs:140`, `AddonCutSceneSelectStringHelper.cs:132`, `AddonTalkHelper.cs:240`), varying only by fallback literal (`""` vs `"PLAYER"`).

**Refactor:** Extract a `VoiceLineSkipGuard` value type (`Notify()` / `ConsumeAndCheck(timeoutMs)`), move `GetAddonStrings`/`HandleSelectedString` to a static `SelectStringHelper`, and add a shared `DispatchSpeech(..., fallbackSpeaker)` helper.

### Theme F — Shared "effective-player-id" and culture-mapping rules (HIGH/MEDIUM)
- **`hasPlayerPlaceholder ? LocalPlayerContentId : 0`** copied in 3 files, all cross-referencing each other in comments — `VoiceMessageProcessor.cs:614`, `VoiceClipManagerService.cs:59`, `LiveGenerationLogger.cs:32`. Extract `TalkTextHelper.GetEffectivePlayerId(bool, long)`.
- **`ClientLanguage → CultureInfo` switch** repeated 3× in `TalkTextHelper.cs` (`ReplaceDate:291`, `ReplaceTime:339`, `ReplaceIntWithVerbal:472`). Extract `GetCulture(ClientLanguage)`.

---

## 3. Most Impactful Per-File Findings (High / Medium)

### Backends
- **`AlltalkBackend.cs:40` [KISS/high]** — new `HttpClient`+`SocketsHttpHandler` allocated on *every* `GenerateAudioStreamFromVoice` call: textbook socket-exhaustion. A static `_httpClient` already exists showing the right pattern; add a second static streaming client.
- **`AlltalkBackend.cs:92` [KISS/medium]** — `GetStringAsync().GetAwaiter().GetResult()` blocks the framework thread; make `GetAvailableVoicesAsync`.
- **`AlltalkBackend.cs:115` [DRY/medium]** — `BaseUrl.TrimEnd('/') + path` inlined 3×; add `BuildUrl(path)`.

### Services — GoogleDriveSyncService (the heaviest single file)
- **`:29` [SRP/high]** — 619-line class mixes 5 concerns (sync loop, OAuth, recursive download, upload, legacy URL scraping). Split into `DriveAuthProvider`, `DriveSyncService`, `DriveUploadService`.
- **`:157` [KISS/high]** — public interface method `DownloadFolder` is `async void`, called fire-and-forget from `RunAsync:71`; the `catch` can never observe its exceptions. Change to `Task` and await.
- **`Secrets.cs:5` [DIP/high]** — `CLIENTID`/`CLIENTSECRET` are compiled-in committed constants. Inject via config or build-time substitution.
- **`:446` [DRY/medium]** — upsert block duplicated in `UploadFile`; extract `UpsertFileAsync(...)`.

### Services — VoiceMessageProcessor
- **`:402` [KISS/medium]** — `GetOrCreateNpcDataAsync` is a 105-line method with 10+ sequential concerns; the body-type + object-kind save block (479–503) is an obvious independent extraction.
- **`:508` [SRP/medium]** — `LogVoiceClip` does FFXIV map-coordinate math inline inside a DB-persistence method; move coordinate resolution to `ILuminaService`.

### Services — NpcDataService / INpcDataService
- **`NpcDataService.cs:15` [SRP/high]** — one class + 18-member interface owns NPC mapping, voice persistence, phonetic CRUD, and mute state. Split into `IVoiceMetadataService`, `IPhoneticCorrectionService`, `IMutedDialogueService`.
- **`INpcDataService.cs:18` [DIP/high]** — `GetAddCharacterMapData` takes `IBackendService` as a parameter, forcing every consumer + every test to hold/mock the backend. Move backend orchestration up into `VoiceMessageProcessor` (the sole caller).
- **`NpcDataService.cs:164` [DRY/medium]** — `MigrateOldData` duplicates the player+NPC migration loop in both branches; extract `MigrateList(list, resolveVoiceKey, label)`.

### Services — Live3DAudioEngine
- **`:177` [DRY/medium]** — `Sub`/`Scale`/`Norm` vector helpers reimplemented 3×; consolidate as private statics on the outer class.
- **`:270` [SRP/medium]** — ~1000-line nested `Source` class bundles WAV parsing, producer/consumer buffering, fade/pop-kill DSP, volume DSP, and 3D smoothing. Extract `WavHeaderParser` and an `AudioProcessing` helper.

### Services — other
- **`AlltalkInstanceService.cs:66` [DRY/medium]** — install-path guard tripled; extract `EnsureInstallPathValid`. Plus **`:320` [SRP/medium]** — `InstallCustomData` mutates lifecycle fields owned by Start/Stop.
- **`BackendService.cs:103` [SRP/medium]** — `MapVoices` + entity mappers are NPC-data concerns living in the backend service; move to `INpcDataService`. Plus **`:68` [DRY/medium]** can-connect/init duplication and **`:370` [KISS/low]** empty `Pause`/`Resume` stubs on the interface.
- **`JsonDataService.cs:97` [DRY/medium]** — 3 Load methods share the fetch/guard/deserialize/log skeleton; extract generic `FetchAndDeserialize<T>`.
- **`DatabaseService.cs:786` [DRY/low]** — `MuteInstance`/`UnmuteInstance` differ only by a boolean; extract `SetInstanceMute(id, bool)`.
- **`TextProcessingService.cs:19` [KISS/medium]** — 16 pure pass-throughs to `TalkTextHelper` (10 ignore both injected deps). Fold the logic in and delete the wrapper layer.
- **`ServiceContainer.cs:68` [KISS/medium]** — `Clear()` drains the container without disposing `IDisposable` services; `Dispose()` does. Remove `Clear()` or make it delegate.
- **`Queue/VoiceMessageQueue.cs:107` [DRY/medium]** — terminal-state teardown copy-pasted across `MarkAsCompleted`/`MarkAsCancelled`/`MarkAsFailed`; extract `TerminateEntry(...)`.
- **`Queue/IVoiceMessageQueue.cs:15` [ISP/medium]** — 4 monitoring methods used only by tests force both production consumers to depend on them; split reader/writer/monitor interfaces.

### Helper/Functional
- **`TalkTextHelper.cs:115` [DRY/medium]** — `ReadTextNode`/`ReadStringNode` share an identical parse-and-clean pipeline.
- **`VoiceExtractTextCleaner.cs:63`**, **`VoiceExtractFileNames.cs:74`**, **`RawPcmToWav.cs:50`**, **`Scd/ScdFile.cs:124` [all DRY/medium]** — lock-step male/female cleanup, separator-stripping loop, WAV-header writing, and Int16/32/64 read overloads respectively; each has a clean single-helper (or `BinaryPrimitives`) consolidation.

### DataClasses
- **`EchokrautVoice.cs:23` [SRP/medium]** — `VoiceName` getter is a 40-line tokenizer; setter writes the *wrong* backing field (`voiceName` not `voiceNameShort`). Extract `VoiceNameHelper.ParseDisplayName`.
- **`ReadSeekableStream.cs:227` [KISS/high]** — `Length` returns the ring-buffer capacity, not stream length: a `Stream` contract violation. Delegate to underlying or throw `NotSupportedException`.
- **`Constants.cs:57` [DRY/medium]** — `RACENAMESLIST`/`GENDERNAMESLIST` are manual `.ToString()` mirrors; derive via `.Select(x => x.ToString()).ToArray()`.

### Windows / NativeWindowManager
- **`NativeWindowManager.cs:29` [DIP/medium]** — constructor takes the concrete `ServiceContainer` and calls `GetService<T>()` 12× (service-locator anti-pattern, violates the project's constructor-injection rule). Declare each service as an explicit ctor parameter.
- **`NativeConfigWindow.cs:29` [SRP/medium]** and **`NativeGameDataToolsWindow.cs:27` [SRP/medium]** — both windows bundle multiple independent operation state machines (delete-confirm/wipe; harvest/starter-set/repair). Extract per-operation controllers / partial files.

---

## 4. Appendix — All Confirmed Findings

- `Services/AddonBattleTalkHelper.cs:28` — [DRY/medium] Voice-line skip guard triplicated across three addon helpers
- `Services/AddonSelectStringHelper.cs:81` — [DRY/medium] `GetAddonStrings` body duplicated between the two SelectString helpers
- `Services/AddonBattleTalkHelper.cs:132` — [DRY/medium] Speaker resolution + `ProcessSpeechAsync` dispatch duplicated across four addon helpers
- `Services/AddonSelectStringHelper.cs:122` — [KISS/low] Log event started before the default-state guard in `HandleChange`
- `Services/AddonBubbleHelper.cs:188` — [SRP/low] `AddonBubbleHelper.Dispose` releases the global BASS audio engine
- `Services/AlltalkInstanceService.cs:66` — [DRY/medium] Repeated install-path validation guard across three public methods
- `Services/BackendService.cs:68` — [DRY/medium] Duplicated can-connect predicate and backend instantiation
- `Services/BackendService.cs:1` — [SRP/medium] BackendService carries voice-mapping/migration and entity converters
- `Services/BackendService.cs:370` — [KISS/low] Pause and Resume are empty stubs on IBackendService
- `Services/AlltalkInstanceService.cs:320` — [SRP/medium] InstallCustomData mixes installer workflow with instance-lifecycle state
- `Services/AudioFileService.cs:121` — [DRY/low] Google Drive filename recomputed instead of reusing filePath
- `Services/DatabaseService.cs:786` — [DRY/low] MuteInstance / UnmuteInstance are copy-paste duplicates
- `Services/GoogleDriveSyncService.cs:29` — [SRP/high] GoogleDriveSyncService carries five unrelated responsibilities
- `Services/GoogleDriveSyncService.cs:446` — [DRY/medium] Duplicate update-or-create Drive upload pattern in UploadFile
- `Services/GoogleDriveSyncService.cs:603` — [DRY/low] ExtractGoogleDriveFileId duplicates ExtractDriveFolderId and is dead code
- `Services/GoogleDriveSyncService.cs:157` — [KISS/high] DownloadFolder is declared async void on a public interface method
- `Services/GoogleDriveSyncService.cs:152` — [KISS/low] CreateDriveServicePkceAsync is a no-op wrapper that discards its result
- `Services/GoogleDriveSyncService.Secrets.cs:5` — [DIP/high] OAuth client credentials are hard-coded compiled-in constants
- `Services/JsonDataService.cs:97` — [DRY/medium] Fetch-deserialize-log pattern repeated across three Load methods
- `Services/Live3DAudioEngine.cs:177` — [DRY/medium] Vector3D math helpers duplicated across outer class and nested Source
- `Services/NpcAttributionRepairService.cs:26` — [DRY/high] Lumina race/gender/name resolution helpers are full copies of DialogHarvestService
- `Services/Live3DAudioEngine.cs:270` — [SRP/medium] Nested Source class carries WAV parsing, audio pipeline, 3D smoothing
- `Services/LipSyncHelper.cs:250` — [ISP/low] TryFindCharacter is public on the concrete class but absent from ILipSyncHelper
- `Services/Live3DAudioEngine.cs:996` — [KISS/low] Dead debug probe code left in production path inside PushAll
- `Services/NpcDataService.cs:15` — [SRP/high] NpcDataService carries four unrelated responsibilities
- `Services/INpcDataService.cs:18` — [DIP/high] GetAddCharacterMapData accepts IBackendService as a method parameter
- `Services/NpcDataService.cs:164` — [DRY/medium] MigrateOldData duplicates the player+NPC migration loop in both branches
- `Services/TextProcessingService.cs:19` — [KISS/medium] TextProcessingService is a pure pass-through to TalkTextHelper
- `Services/ServiceContainer.cs:68` — [KISS/medium] ServiceContainer.Clear() silently leaks all IDisposable services
- `Services/RemoteUrlService.cs:44` — [KISS/low] Blocking async HTTP call (.GetAwaiter().GetResult()) in constructor
- `Services/VoiceMessageProcessor.cs:614` — [DRY/medium] Effective-player-id rule copy-pasted from VoiceClipManagerService
- `Services/VoiceMessageProcessor.cs:508` — [SRP/medium] LogVoiceClip mixes map-coordinate math with DB persistence
- `Services/VoiceMessageProcessor.cs:402` — [KISS/medium] GetOrCreateNpcDataAsync accumulates too many sequential concerns
- `Services/Queue/IVoiceMessageQueue.cs:15` — [ISP/medium] IVoiceMessageQueue is a fat interface conflating operational and monitoring roles
- `Services/Queue/VoiceMessageQueue.cs:107` — [DRY/medium] Repeated terminal-state teardown across MarkAsCompleted/Cancelled/Failed
- `DataClasses/LogConfig.cs:9` — [DRY/high] 32-property LogConfig duplicates LogSourceConfig 8 times
- `DataClasses/Constants.cs:57` — [DRY/medium] RACELIST/RACENAMESLIST and GENDERLIST/GENDERNAMESLIST parallel data kept in sync manually
- `DataClasses/EchokrautVoice.cs:23` — [SRP/medium] VoiceName getter contains filename-parsing logic; setter writes wrong field
- `DataClasses/AlltalkVoices.cs:8` — [KISS/low] AlltalkVoices lives in namespace Echokraut instead of Echokraut.DataClasses
- `DataClasses/AlltalkData.cs:9` — [KISS/low] BaseUrl/ReloadModel/CustomModelUrl/CustomVoicesUrl are public mutable fields
- `DataClasses/NpcMapData.cs:54` — [DRY/medium] ToString-based equality/comparison triad duplicated across data classes
- `DataClasses/PhoneticCorrection.cs:24` — [DRY/medium] ToString-based equality/comparison triad (second instance)
- `DataClasses/ReadSeekableStream.cs:227` — [KISS/high] Length property returns seek-back buffer size, not stream length
- `DataClasses/ReadSeekableStream.cs:52` — [KISS/low] Commented-out dead code block left in Read()
- `DataClasses/ReadSeekableStream.cs:13` — [KISS/low] _seekBackBuffer field is unnecessarily public
- `DataClasses/VoiceMessage.cs:20` — [KISS/low] Commented-out property left in production class
- `Helper/Functional/TalkTextHelper.cs:291` — [DRY/high] ClientLanguage → CultureInfo switch block copy-pasted three times
- `Helper/Functional/TalkTextHelper.cs:115` — [DRY/medium] ReadTextNode and ReadStringNode share near-identical bodies
- `Helper/Functional/VoiceExtractTextCleaner.cs:63` — [DRY/medium] Post-split cleanup steps applied in parallel to male and female strings
- `Helper/Functional/VoiceExtractFileNames.cs:74` — [DRY/medium] NormalizeRace duplicates VoiceExtractKey.Normalize separator-stripping loop
- `Helper/Functional/RawPcmToWav.cs:50` — [DRY/medium] WAV header writing duplicated between WriteWavAsync and WriteWav
- `Helper/Functional/Scd/ScdFile.cs:124` — [DRY/medium] ReadInt16/32/64 duplicate the stackalloc-copy-reverse-convert pattern
- `Helper/Functional/Scd/ScdAdpcmEntry.cs:25` — [KISS/low] RIFF/WAV header assembled by manual byte-by-byte writes and raw Array.Copy offsets
- `Windows/Native/NativeConfigWindow.cs:786` — [DRY/high] Dim() and SetVisible() helpers duplicated across four windows
- `Windows/Native/NativeConfigWindow.cs:1433` — [DRY/high] Button()/Input()/Label()/Separator() node factories duplicated across five windows
- `Windows/Native/NativeConfigWindow.cs:1460` — [DRY/medium] CreateCollapsibleSection() duplicated verbatim in NativeGameDataToolsWindow
- `Windows/Native/NativeConfigWindow.Logs.cs:433` — [DRY/high] Eight parallel switch/pattern blocks for LogSourceConfig accessors
- `Windows/Native/NativeConfigWindow.cs:29` — [SRP/medium] NativeConfigWindow carries build/layout for five tabs plus per-frame sync for ~30 widgets
- `Windows/Native/NativeVoiceClipManagerWindow.cs:317` — [DRY/medium] Hover-tint + tooltip wiring repeated six times across three windows
- `Windows/Native/NativeGameDataToolsWindow.cs:27` — [SRP/medium] Owns UI and state machines for three independent operations
- `Windows/Native/NativeConfigWindow.cs:1277` — [DRY/low] Local-audio-path and Google-Drive inputs duplicated between Storage tab and None-mode section
- `Windows/Native/NativeVoiceConfigWindow.cs` — [DRY/medium] Auto-sized TextButtonNode creation inlined three times instead of using a helper
- `Windows/Native/NativeVoiceConfigWindow.cs` — [DRY/low] Collection toggle (add-if-absent/remove-if-present) duplicated for genders and races
- `Windows/NativeWindowManager.cs` — [DIP/medium] Constructor takes ServiceContainer (concrete service locator) instead of resolved interfaces
- `Enums/NpcRaces.cs:1` — [KISS/low] Six unused using directives on a plain enum
- `Enums/TTSBackends.cs:1` — [KISS/low] Five unused using directives on a plain enum
- `Enums/EventType.cs:8` — [KISS/low] #pragma suppression masking a trivially fixable doc-comment gap
- `Backends/AlltalkBackend.cs:115` — [DRY/medium] Repeated BaseUrl.TrimEnd('/') + path URL construction across three methods
- `Backends/AlltalkBackend.cs:40` — [KISS/high] New HttpClient/SocketsHttpHandler allocated on every GenerateAudioStreamFromVoice call
- `Backends/AlltalkBackend.cs:92` — [KISS/medium] Sync-over-async blocking call in GetAvailableVoices

---

## 5. Recommended Sequencing

1. **Quick wins (low risk, immediate):** remove dead code/unused usings (`ReadSeekableStream`, `VoiceMessage`, `Live3DAudioEngine` debug probe, `NpcRaces`/`TTSBackends`/`EventType` enums, `ExtractGoogleDriveFileId`); convert `AlltalkData` fields to properties; fix `AlltalkVoices` namespace; derive `Constants` name arrays.
2. **High-severity correctness:** `AlltalkBackend` static HttpClient (socket exhaustion), `GoogleDriveSyncService.DownloadFolder` async void, `ReadSeekableStream.Length` contract, `Secrets.cs` credentials.
3. **Theme extractions (biggest DRY payoff):** Theme A (`NativeNodeFactory`) and Theme D (`LogSourceConfig`) together close ~7 findings including 4 HIGH; then Themes B, C, E, F.
4. **SRP splits (larger refactors, do with tests):** `GoogleDriveSyncService`, `NpcDataService`, `Live3DAudioEngine.Source`, and the two large native windows.