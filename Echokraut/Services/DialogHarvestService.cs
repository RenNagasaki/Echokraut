using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using System.Text;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace Echokraut.Services;

public class DialogHarvestService : IDialogHarvestService
{
    private readonly IDataManager _dataManager;
    private readonly IJsonDataService _jsonData;
    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IDatabaseService _db;
    private readonly IBackendService _backend;
    private readonly INpcDataService _npcData;
    private readonly IRemoteUrlService _remoteUrls;

    private static readonly string[] LangCodes = { "en", "de", "ja", "fr" };
    private static readonly Dalamud.Game.ClientLanguage[] LangValues =
    {
        Dalamud.Game.ClientLanguage.English,
        Dalamud.Game.ClientLanguage.German,
        Dalamud.Game.ClientLanguage.Japanese,
        Dalamud.Game.ClientLanguage.French
    };

    /// <summary>
    /// Manual NPC name alias → ENpcResident ID for cutscene NPCs that can't be resolved
    /// through Lua bytecode analysis (dialog played by native cutscene system, not Lua Talk()).
    /// </summary>
    private static readonly Dictionary<string, uint> CutsceneNpcAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "FLAMEMARSHALROAILLE", 1010038 },
    };

    private static readonly HashSet<string> PlaceholderTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "0", "leer", "未使用", "inutilisé", "unused", "dummy", "test",
        "none", "null", "n/a", "-", "---", "...", "placeholder"
    };

    public bool IsRunning { get; private set; }
    public event Action<string>? ProgressChanged;
    public event Action<int, int>? ProgressCountChanged;

    private int _phaseTotal = 1;
    private int _phaseCurrent;

    private CancellationTokenSource? _internalCts;
    private Task? _runningTask;
    private readonly object _runLock = new();
    private bool _disposed;

    public DialogHarvestService(
        IDataManager dataManager,
        IJsonDataService jsonData,
        ILogService log,
        Configuration config,
        IDatabaseService db,
        IBackendService backend,
        INpcDataService npcData,
        IRemoteUrlService remoteUrls)
    {
        _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));
    }

    public async Task RunAsync(Dalamud.Game.ClientLanguage language, CancellationToken ct, int? questTypeFilter = null)
    {
        if (_disposed || IsRunning) return;
        IsRunning = true;

        var _baseId = _log.Start(nameof(RunAsync), TextSource.None);
        var eventId = new EKEventId(_baseId.Id, _baseId.TextSource);

        CancellationTokenSource linkedCts;
        lock (_runLock)
        {
            _internalCts?.Dispose();
            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts = _internalCts;
        }
        var linkedCt = linkedCts.Token;

        try
        {
            Task task;
            lock (_runLock) { _runningTask = Task.Run(() => DoHarvest(language, linkedCt, eventId, questTypeFilter), linkedCt); task = _runningTask; }
            await task;
        }
        catch (OperationCanceledException)
        {
            _log.Info(nameof(RunAsync), "Harvest cancelled by user.", eventId);
            BeginPhase(Loc.S("Cancelled."), 1);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(RunAsync), $"Harvest failed: {ex}", eventId);
            BeginPhase(string.Format(Loc.S("Error: {0}"), ex.Message), 1);
        }
        finally
        {
            IsRunning = false;
            lock (_runLock) { _runningTask = null; }
            _log.End(nameof(RunAsync), eventId);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Task? task;
        CancellationTokenSource? cts;
        lock (_runLock) { task = _runningTask; cts = _internalCts; }
        try { cts?.Cancel(); } catch { }
        if (task != null)
        {
            try { task.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
        try { cts?.Dispose(); } catch { }
    }

    private void DoHarvest(Dalamud.Game.ClientLanguage language, CancellationToken ct, EKEventId eventId, int? questTypeFilter = null)
    {
        // Filter semantics:
        //   null     → harvest everything (default)
        //   0 (None) → only non-quest dialog (DefaultTalk/Balloon/etc.) — skip quest scan + persist
        //   1..6     → only quests whose ClassifyQuest matches — skip non-quest persist
        var harvestNonQuest = questTypeFilter is null or 0;
        var harvestQuests = questTypeFilter is null or > 0;
        // Step 1: Load dialog sheets in all languages (LoadDialogSheet drives its own phase progress)
        var defaultTalkTexts = LoadDialogSheet<DefaultTalk>("DefaultTalk", GetDefaultTalkTexts, ct, eventId);
        ct.ThrowIfCancellationRequested();

        // Balloon (Bubbles), ContentTalk and NpcYell (BattleTalks) are intentionally NOT harvested:
        // their NPC attribution proved unreliable (cf. Alphinaud false-positive bubble bug, no static
        // mapping for modern dungeon BattleTalks). They get captured live via AddonBubbleHelper /
        // AddonBattleTalkHelper at runtime instead. Loading them as empty makes all downstream
        // matching/scanning paths no-op without breaking the structure.
        var balloonTexts = new Dictionary<uint, Dictionary<string, List<string>>>();
        var contentTalkTexts = new Dictionary<uint, Dictionary<string, List<string>>>();
        var npcYellTexts = new Dictionary<uint, Dictionary<string, List<string>>>();

        var instanceTexts = LoadDialogSheet<InstanceContentTextData>("InstanceContentTextData", GetSingleTextSheet, ct, eventId);
        ct.ThrowIfCancellationRequested();

        var publicContentTexts = LoadDialogSheet<PublicContentTextData>("PublicContentTextData",
            (row, lang) => [ExtractTextWithPlayerName(row.TextData, lang) ?? ""], ct, eventId);
        ct.ThrowIfCancellationRequested();

        var gimmickTalkTexts = LoadDialogSheet<GimmickTalk>("GimmickTalk",
            (row, lang) => [ExtractTextWithPlayerName(row.Message, lang) ?? ""], ct, eventId);
        ct.ThrowIfCancellationRequested();

        var partyContentTexts = LoadDialogSheet<PartyContentTextData>("PartyContentTextData",
            (row, lang) => [ExtractTextWithPlayerName(row.Data, lang) ?? ""], ct, eventId);
        ct.ThrowIfCancellationRequested();

        var massiveTexts = LoadDialogSheet<MassivePcContentTextData>("MassivePcContentTextData", GetSingleTextSheet, ct, eventId);
        ct.ThrowIfCancellationRequested();

        var goldSaucerTexts = LoadDialogSheet<GoldSaucerTextData>("GoldSaucerTextData", GetSingleTextSheet, ct, eventId);
        ct.ThrowIfCancellationRequested();

        var allDialogSheets = new Dictionary<string, Dictionary<uint, Dictionary<string, List<string>>>>
        {
            ["DefaultTalk"] = defaultTalkTexts,
            ["Balloon"] = balloonTexts,
            ["InstanceContentTextData"] = instanceTexts,
            ["ContentTalk"] = contentTalkTexts,
            ["NpcYell"] = npcYellTexts,
            ["PublicContentTextData"] = publicContentTexts,
            ["GimmickTalk"] = gimmickTalkTexts,
            ["PartyContentTextData"] = partyContentTexts,
            ["MassivePcContentTextData"] = massiveTexts,
            ["GoldSaucerTextData"] = goldSaucerTexts,
        };

        // Step 2: Load NPC names in all languages (ENpcResident + BNpcName)
        BeginPhase(Loc.S("Loading NPC names..."), 1);
        var npcNames = LoadNpcNames(ct, eventId);
        EndPhase();
        ct.ThrowIfCancellationRequested();

        BeginPhase(Loc.S("Loading BNpc names..."), 1);
        var bnpcNames = LoadBNpcNames(ct, eventId);
        EndPhase();
        ct.ThrowIfCancellationRequested();

        // Step 3: Load ENpcBase for race/gender
        // Build multi-hop DefaultTalk lookup: intermediate sheet row ID → set of DefaultTalk IDs.
        // ENpcData references GilShop/Warp/FateShop/etc. which then link to DefaultTalk.
        BeginPhase(Loc.S("Building DefaultTalk chain lookup..."), 1);
        var intermediateToDefaultTalk = new Dictionary<uint, HashSet<uint>>();

        // GilShop: AcceptTalk (col 3) + FailTalk (col 4) → DefaultTalk
        try
        {
            var gilShopRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "GilShop");
            if (gilShopRaw != null)
            {
                foreach (var row in gilShopRaw)
                {
                    var ids = new HashSet<uint>();
                    try { var v = row.ReadUInt32Column(3); if (v != 0) ids.Add(v); } catch { }
                    try { var v = row.ReadUInt32Column(4); if (v != 0) ids.Add(v); } catch { }
                    if (ids.Count > 0) intermediateToDefaultTalk[row.RowId] = ids;
                }
            }
        }
        catch { }

        // Warp: ConditionSuccessEvent (col 2) + ConditionFailEvent (col 3) + ConfirmEvent (col 4) → DefaultTalk
        try
        {
            var warpRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "Warp");
            if (warpRaw != null)
            {
                foreach (var row in warpRaw)
                {
                    var ids = new HashSet<uint>();
                    try { var v = row.ReadUInt32Column(2); if (v != 0) ids.Add(v); } catch { }
                    try { var v = row.ReadUInt32Column(3); if (v != 0) ids.Add(v); } catch { }
                    try { var v = row.ReadUInt32Column(4); if (v != 0) ids.Add(v); } catch { }
                    if (ids.Count > 0)
                    {
                        if (!intermediateToDefaultTalk.TryGetValue(row.RowId, out var existing))
                            intermediateToDefaultTalk[row.RowId] = ids;
                        else
                            foreach (var id in ids) existing.Add(id);
                    }
                }
            }
        }
        catch { }

        // FateShop: DefaultTalk[0..9] (col 3-12) → DefaultTalk
        try
        {
            var fateShopRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "FateShop");
            if (fateShopRaw != null)
            {
                foreach (var row in fateShopRaw)
                {
                    var ids = new HashSet<uint>();
                    for (var i = 0; i < 10; i++)
                    {
                        try { var v = row.ReadUInt32Column(3 + i); if (v != 0) ids.Add(v); } catch { }
                    }
                    if (ids.Count > 0)
                    {
                        if (!intermediateToDefaultTalk.TryGetValue(row.RowId, out var existing))
                            intermediateToDefaultTalk[row.RowId] = ids;
                        else
                            foreach (var id in ids) existing.Add(id);
                    }
                }
            }
        }
        catch { }

        // SpecialShop: CompleteText (col 2043) + NotCompleteText (col 2044) → DefaultTalk
        try
        {
            var ssRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "SpecialShop");
            if (ssRaw != null)
            {
                foreach (var row in ssRaw)
                {
                    var ids = new HashSet<uint>();
                    try { var v = row.ReadUInt32Column(2043); if (v != 0) ids.Add(v); } catch { }
                    try { var v = row.ReadUInt32Column(2044); if (v != 0) ids.Add(v); } catch { }
                    if (ids.Count > 0)
                    {
                        if (!intermediateToDefaultTalk.TryGetValue(row.RowId, out var existing))
                            intermediateToDefaultTalk[row.RowId] = ids;
                        else
                            foreach (var id in ids) existing.Add(id);
                    }
                }
            }
        }
        catch { }

        // TripleTriad: DefaultTalk{Challenge/Unavailable/NPCWin/Draw/PCWin} (cols 20-24) → DefaultTalk
        try
        {
            var ttRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "TripleTriad");
            if (ttRaw != null)
            {
                foreach (var row in ttRaw)
                {
                    var ids = new HashSet<uint>();
                    for (var i = 20; i <= 24; i++)
                    {
                        try { var v = row.ReadUInt32Column(i); if (v != 0) ids.Add(v); } catch { }
                    }
                    if (ids.Count > 0)
                    {
                        if (!intermediateToDefaultTalk.TryGetValue(row.RowId, out var existing))
                            intermediateToDefaultTalk[row.RowId] = ids;
                        else
                            foreach (var id in ids) existing.Add(id);
                    }
                }
            }
        }
        catch { }

        // PreHandler: AcceptMessage (col 4) + DenyMessage (col 5) → DefaultTalk
        try
        {
            var phRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "PreHandler");
            if (phRaw != null)
            {
                foreach (var row in phRaw)
                {
                    var ids = new HashSet<uint>();
                    try { var v = row.ReadUInt32Column(4); if (v != 0) ids.Add(v); } catch { }
                    try { var v = row.ReadUInt32Column(5); if (v != 0) ids.Add(v); } catch { }
                    if (ids.Count > 0)
                    {
                        if (!intermediateToDefaultTalk.TryGetValue(row.RowId, out var existing))
                            intermediateToDefaultTalk[row.RowId] = ids;
                        else
                            foreach (var id in ids) existing.Add(id);
                    }
                }
            }
        }
        catch { }

        // SwitchTalk → SwitchTalkVariation (sub-rows) → DefaultTalk
        // ENpcData references SwitchTalk row IDs. SwitchTalkVariation uses the SAME row IDs
        // as sub-row keys, with DefaultTalk in each sub-row.
        // SwitchTalkVariation: col 3 = DefaultTalk (based on xivapi schema)
        BeginPhase(Loc.S("Loading SwitchTalkVariation..."), 1);
        var stvCount = 0;
        try
        {
            // SwitchTalkVariation is a sub-row sheet — try sub-row API first
            var stvSheet = _dataManager.GetSubrowExcelSheet<RawSubrow>(Dalamud.Game.ClientLanguage.English, "SwitchTalkVariation");
            if (stvSheet != null)
            {
                foreach (var rowCollection in stvSheet)
                {
                    foreach (var subrow in rowCollection)
                    {
                        try
                        {
                            var dtId = subrow.ReadUInt32Column(3);
                            if (dtId == 0) continue;

                            if (!intermediateToDefaultTalk.TryGetValue(subrow.RowId, out var ids))
                            {
                                ids = new HashSet<uint>();
                                intermediateToDefaultTalk[subrow.RowId] = ids;
                            }
                            ids.Add(dtId);
                            stvCount++;
                        }
                        catch { }
                    }
                }
                _log.Debug(nameof(DoHarvest), $"SwitchTalkVariation: loaded {stvCount} sub-row entries", eventId);
            }
            else
            {
                // Fallback: try regular sheet access
                var stvRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "SwitchTalkVariation");
                if (stvRaw != null)
                {
                    foreach (var row in stvRaw)
                    {
                        try
                        {
                            var dtId = row.ReadUInt32Column(3);
                            if (dtId == 0) continue;

                            if (!intermediateToDefaultTalk.TryGetValue(row.RowId, out var ids))
                            {
                                ids = new HashSet<uint>();
                                intermediateToDefaultTalk[row.RowId] = ids;
                            }
                            ids.Add(dtId);
                            stvCount++;
                        }
                        catch { }
                    }
                    _log.Debug(nameof(DoHarvest), $"SwitchTalkVariation (flat): loaded {stvCount} entries", eventId);
                }
                else
                {
                    _log.Debug(nameof(DoHarvest), "SwitchTalkVariation: sheet not found", eventId);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(nameof(DoHarvest), $"SwitchTalkVariation error: {ex.Message}", eventId);
        }

        _log.Debug(nameof(DoHarvest),
            $"Built multi-hop lookup: {intermediateToDefaultTalk.Count} intermediate entries", eventId);

        EndPhase();
        BeginPhase(Loc.S("Loading NPC data..."), 1);
        var npcBaseSheet = _dataManager.GetExcelSheet<ENpcBase>()!;
        var npcBaseRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "ENpcBase");
        EndPhase();

        // Balloon-specific lookups (Behavior chain, ENpcBase col 105, LGB scan) are skipped
        // when the Balloon sheet wasn't loaded — see comment at the top of DoHarvest.
        var behaviorToBalloon = new Dictionary<uint, HashSet<uint>>();
        var npcBalloonIds = new Dictionary<uint, HashSet<uint>>();
        if (balloonTexts.Count > 0)
        {
            BeginPhase(Loc.S("Building Balloon lookup..."), 1);
            var behaviorBalloonCount = 0;
            try
            {
                var behaviorSheet = _dataManager.GetSubrowExcelSheet<RawSubrow>(
                    Dalamud.Game.ClientLanguage.English, "Behavior");
                if (behaviorSheet != null)
                {
                    foreach (var rowCollection in behaviorSheet)
                    {
                        foreach (var subrow in rowCollection)
                        {
                            try
                            {
                                var bId = subrow.ReadUInt32Column(8);
                                if (bId == 0) continue;
                                if (!behaviorToBalloon.TryGetValue(subrow.RowId, out var ids))
                                {
                                    ids = new HashSet<uint>();
                                    behaviorToBalloon[subrow.RowId] = ids;
                                }
                                ids.Add(bId);
                                behaviorBalloonCount++;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            if (npcBaseRaw != null)
            {
                foreach (var rawRow in npcBaseRaw)
                {
                    var ids = new HashSet<uint>();
                    try { var v = rawRow.ReadUInt32Column(105); if (v != 0) ids.Add(v); } catch { }
                    try
                    {
                        var behaviorId = rawRow.ReadUInt32Column(64);
                        if (behaviorId != 0 && behaviorToBalloon.TryGetValue(behaviorId, out var bIds))
                            foreach (var bid in bIds) ids.Add(bid);
                    }
                    catch { }
                    if (ids.Count > 0) npcBalloonIds[rawRow.RowId] = ids;
                }
            }
        }

        var matchedDialogIds = new Dictionary<string, HashSet<uint>>();
        foreach (var sheetName in allDialogSheets.Keys)
            matchedDialogIds[sheetName] = new HashSet<uint>();

        // LGB Balloon scan is skipped entirely when Balloon harvest is disabled.
        // The scan iterates every territory's planevent.lgb (1000+ files) — pure waste otherwise.
        var lgbBalloonToNpc = new Dictionary<uint, uint>(); // Balloon ID → ENpcBase ID
        if (balloonTexts.Count > 0)
        {
            EndPhase();
            var ttSheetCount = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.Count ?? 1;
            BeginPhase(Loc.S("Scanning LGB territory files..."), ttSheetCount);
            var lgbTerritoriesScanned = 0;
            var lgbEntriesTotal = 0;
            var lgbBoundsMapped = 0;
            var balloonSheetIds = new HashSet<uint>(balloonTexts.Keys);

            var lgbFileCache = new List<(byte[] data, List<LgbParser.ENpcEntry> entries)>();
            try
            {
                var ttSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
                if (ttSheet != null)
                {
                    foreach (var territory in ttSheet)
                    {
                        ct.ThrowIfCancellationRequested();
                        var bgPath = territory.Bg.ExtractText();
                    if (string.IsNullOrEmpty(bgPath)) continue;

                    var lastSlash = bgPath.LastIndexOf('/');
                    var bgDir = lastSlash >= 0 ? bgPath[..lastSlash] : bgPath;

                    foreach (var lgbName in new[] { "planevent.lgb", "planmap.lgb", "planlive.lgb", "planner.lgb" })
                    {
                        var lgbFile = _dataManager.GetFile($"bg/{bgDir}/{lgbName}");
                        if (lgbFile == null) continue;

                        var data = lgbFile.Data;
                        var entries = LgbParser.ParseENpcEntries(data);
                        lgbEntriesTotal += entries.Count;

                        if (entries.Count > 0)
                            lgbFileCache.Add((data, entries));

                        // Resolve Balloon IDs from EXD chains (ENpcBase direct + Behavior)
                        foreach (var entry in entries)
                        {
                            if (npcBalloonIds.TryGetValue(entry.BaseId, out var balloonIds))
                            {
                                foreach (var bId in balloonIds)
                                {
                                    if (balloonSheetIds.Contains(bId))
                                        lgbBalloonToNpc.TryAdd(bId, entry.BaseId);
                                }
                            }

                            if (entry.BehaviorId != 0 && behaviorToBalloon.TryGetValue(entry.BehaviorId, out var behaviorBalloons))
                            {
                                foreach (var bId in behaviorBalloons)
                                {
                                    if (balloonSheetIds.Contains(bId))
                                        lgbBalloonToNpc.TryAdd(bId, entry.BaseId);
                                }
                            }
                        }

                        // Pass 1: scan within each entry's own bounds (accurate attribution)
                        var byteScanResults = LgbParser.ScanEntriesForNearbyIds(
                            data, entries, balloonSheetIds);
                        foreach (var (balloonId, npcId) in byteScanResults)
                        {
                            if (lgbBalloonToNpc.TryAdd(balloonId, npcId))
                                lgbBoundsMapped++;
                        }
                    }

                    lgbTerritoriesScanned++;
                    ReportPhaseProgress(lgbTerritoriesScanned);
                }
            }
        }
            catch (Exception ex)
            {
                _log.Debug(nameof(DoHarvest), $"LGB scan error: {ex.Message}", eventId);
            }

            lgbFileCache.Clear(); // free memory
            _log.Info(nameof(DoHarvest),
                $"LGB Balloon scan: {lgbBalloonToNpc.Count} unique Balloon IDs mapped " +
                $"({lgbBoundsMapped} within bounds). " +
                $"{lgbEntriesTotal} ENpc entries across {lgbTerritoriesScanned} territories", eventId);
        }

        var linkedDialogs = new List<LinkedDialog>();
        var npcCount = 0;
        var npcBaseTotal = npcBaseSheet.Count;

        // Build appearance → named NPC lookup for pass 2 (unnamed NPC resolution)
        // Key: "race_gender_face_hair" → (npcId, names, raceStr, gender)
        var appearanceToNamedNpc = new Dictionary<string, (uint npcId, Dictionary<string, string> names, string raceStr, Genders gender)>();

        // Pass 1: Scan named NPCs
        BeginPhase(Loc.S("Processing named NPCs..."), npcBaseTotal);
        foreach (var npcBase in npcBaseSheet)
        {
            ct.ThrowIfCancellationRequested();
            npcCount++;
            ReportPhaseProgress(npcCount);

            var npcId = npcBase.RowId;

            if (!npcNames.TryGetValue(npcId, out var names))
                continue;

            if (names.Values.All(string.IsNullOrEmpty))
                continue;

            var raceStr = GetRaceString(npcBase);
            var race = ParseNpcRace(raceStr);
            var gender = NpcIdentityHelper.DetermineGender(npcBase, race, _jsonData.ModelGenderMap);

            // Build appearance key for named NPC lookup
            var appearanceKey = $"{npcBase.Race.RowId}_{npcBase.Gender}_{npcBase.Face}_{npcBase.HairStyle}";
            appearanceToNamedNpc.TryAdd(appearanceKey, (npcId, names, raceStr, gender));

            var dataRefs = GetENpcDataValues(npcBase);
            foreach (var dataRef in dataRefs)
            {
                if (dataRef == 0) continue;

                // Direct match: ENpcData value IS a DefaultTalk/Balloon row ID
                foreach (var (sheetName, dialogEntries) in allDialogSheets)
                {
                    if (!dialogEntries.TryGetValue(dataRef, out var textsByLang))
                        continue;

                    foreach (var texts in FlattenTexts(textsByLang))
                    {
                        if (texts.Values.All(string.IsNullOrEmpty))
                            continue;

                        linkedDialogs.Add(new LinkedDialog
                        {
                            NpcId = npcId,
                            NpcName = names,
                            Race = raceStr,
                            Gender = gender.ToString(),
                            Sheet = sheetName,
                            DialogId = dataRef,
                            MatchSource = DialogMatchSource.Direct.ToString(),
                            Texts = texts
                        });
                    }

                    matchedDialogIds[sheetName].Add(dataRef);
                }

                // Multi-hop: ENpcData → GilShop/Warp/FateShop → DefaultTalk
                if (intermediateToDefaultTalk.TryGetValue(dataRef, out var chainedIds))
                {
                    foreach (var chainedId in chainedIds)
                    {
                        if (!allDialogSheets["DefaultTalk"].TryGetValue(chainedId, out var chainedTexts))
                            continue;
                        if (matchedDialogIds["DefaultTalk"].Contains(chainedId))
                            continue;

                        foreach (var texts in FlattenTexts(chainedTexts))
                        {
                            if (texts.Values.All(string.IsNullOrEmpty))
                                continue;

                            linkedDialogs.Add(new LinkedDialog
                            {
                                NpcId = npcId,
                                NpcName = names,
                                Race = raceStr,
                                Gender = gender.ToString(),
                                Sheet = "DefaultTalk",
                                DialogId = chainedId,
                                MatchSource = DialogMatchSource.Direct.ToString(),
                                Texts = texts
                            });
                        }

                        matchedDialogIds["DefaultTalk"].Add(chainedId);
                    }
                }
            }

            // Read dedicated Balloon fields from pre-built lookup (col 105 + 107).
            if (npcBalloonIds.TryGetValue(npcId, out var balloonIds))
            foreach (var balloonId in balloonIds)
            if (!matchedDialogIds["Balloon"].Contains(balloonId)
                && allDialogSheets["Balloon"].TryGetValue(balloonId, out var bTextsByLang))
            {
                // Build a simple lang → text entry from the first (only) text field
                var balloonEntry = new Dictionary<string, string>();
                foreach (var (lang, textList) in bTextsByLang)
                {
                    if (textList.Count > 0 && !string.IsNullOrEmpty(textList[0]))
                        balloonEntry[lang] = textList[0];
                }

                if (balloonEntry.Count > 0)
                {
                    linkedDialogs.Add(new LinkedDialog
                    {
                        NpcId = npcId,
                        NpcName = names,
                        Race = raceStr,
                        Gender = gender.ToString(),
                        Sheet = "Balloon",
                        DialogId = balloonId,
                        MatchSource = DialogMatchSource.Direct.ToString(),
                        Texts = balloonEntry
                    });
                    matchedDialogIds["Balloon"].Add(balloonId);
                }
            }
        }

        ct.ThrowIfCancellationRequested();

        EndPhase();
        // Pass 2: Scan unnamed NPCs for dialog not yet matched.
        // Resolve names by matching appearance (race+gender+face+hair) to named NPCs.
        BeginPhase(Loc.S("Processing unnamed NPCs..."), npcBaseTotal);
        npcCount = 0;
        var pass2UnnamedWithDialog = 0;
        var pass2AppearanceMatched = 0;
        var pass2NewDialogIds = 0;
        foreach (var npcBase in npcBaseSheet)
        {
            ct.ThrowIfCancellationRequested();
            npcCount++;
            ReportPhaseProgress(npcCount);

            var npcId = npcBase.RowId;

            // Only process unnamed NPCs (skip ones already processed in pass 1)
            if (npcNames.TryGetValue(npcId, out var existingNames)
                && existingNames.Values.Any(n => !string.IsNullOrEmpty(n)))
                continue;

            var dataRefs = GetENpcDataValues(npcBase);
            var hasNewDialog = false;

            foreach (var dataRef in dataRefs)
            {
                if (dataRef == 0) continue;
                foreach (var sheetName in allDialogSheets.Keys)
                {
                    if (allDialogSheets[sheetName].ContainsKey(dataRef) && !matchedDialogIds[sheetName].Contains(dataRef))
                    {
                        hasNewDialog = true;
                        break;
                    }
                }
                if (hasNewDialog) break;
            }

            if (!hasNewDialog) continue;
            pass2UnnamedWithDialog++;

            // Try to find a named NPC with matching appearance
            var appearanceKey = $"{npcBase.Race.RowId}_{npcBase.Gender}_{npcBase.Face}_{npcBase.HairStyle}";
            if (!appearanceToNamedNpc.TryGetValue(appearanceKey, out var namedNpc))
                continue; // No appearance match — skip (can't resolve name)
            pass2AppearanceMatched++;

            var raceStr = namedNpc.raceStr;
            var gender = namedNpc.gender;
            var names = namedNpc.names;

            foreach (var dataRef in dataRefs)
            {
                if (dataRef == 0) continue;

                foreach (var (sheetName, dialogEntries) in allDialogSheets)
                {
                    if (matchedDialogIds[sheetName].Contains(dataRef)) continue;
                    if (!dialogEntries.TryGetValue(dataRef, out var textsByLang)) continue;

                    foreach (var texts in FlattenTexts(textsByLang))
                    {
                        if (texts.Values.All(string.IsNullOrEmpty))
                            continue;

                        linkedDialogs.Add(new LinkedDialog
                        {
                            NpcId = namedNpc.npcId,
                            NpcName = names,
                            Race = raceStr,
                            Gender = gender.ToString(),
                            Sheet = sheetName,
                            DialogId = dataRef,
                            MatchSource = DialogMatchSource.Direct.ToString(),
                            Texts = texts
                        });
                    }

                    matchedDialogIds[sheetName].Add(dataRef);
                }
            }
        }

        ct.ThrowIfCancellationRequested();

        // Balloon diagnostics + unnamed-NPC appearance match are skipped when Balloon harvest is off.
        if (balloonTexts.Count > 0)
        {
            var balloonFieldNonZero = 0;
            var balloonFieldMatched = 0;
            if (npcBaseRaw != null)
            {
                foreach (var nb in npcBaseSheet)
                {
                    if (!npcNames.TryGetValue(nb.RowId, out var nn) || nn.Values.All(string.IsNullOrEmpty))
                        continue;
                    var rr3 = npcBaseRaw.GetRowOrDefault(nb.RowId);
                    if (rr3 is not { } raw3) continue;
                    try
                    {
                        var bVal = raw3.ReadUInt32Column(105);
                        if (bVal != 0)
                        {
                            balloonFieldNonZero++;
                            if (balloonTexts.ContainsKey(bVal))
                                balloonFieldMatched++;
                        }
                    }
                    catch { }
                }
            }
            var uniqueBalloonIdsInSheet = npcBalloonIds
                .Where(kvp => npcNames.TryGetValue(kvp.Key, out var n) && n.Values.Any(v => !string.IsNullOrEmpty(v)))
                .SelectMany(kvp => kvp.Value)
                .Where(bId => balloonTexts.ContainsKey(bId))
                .Distinct()
                .Count();
            var uniqueBalloonIdsNotMatched = npcBalloonIds
                .Where(kvp => npcNames.TryGetValue(kvp.Key, out var n) && n.Values.Any(v => !string.IsNullOrEmpty(v)))
                .SelectMany(kvp => kvp.Value)
                .Where(bId => balloonTexts.ContainsKey(bId) && !matchedDialogIds["Balloon"].Contains(bId))
                .Distinct()
                .Count();

            _log.Info(nameof(DoHarvest),
                $"Balloon: {balloonFieldNonZero} NPCs non-zero, {balloonFieldMatched} NPCs match sheet, " +
                $"{uniqueBalloonIdsInSheet} unique IDs in sheet, {uniqueBalloonIdsNotMatched} unique not yet matched. " +
                $"matchedDialogIds Balloon={matchedDialogIds["Balloon"].Count}, DT={matchedDialogIds["DefaultTalk"].Count}", eventId);

            if (npcBaseRaw != null)
            {
                foreach (var npcBase2 in npcBaseSheet)
                {
                    var nid = npcBase2.RowId;
                    if (npcNames.TryGetValue(nid, out var n2) && n2.Values.Any(v => !string.IsNullOrEmpty(v)))
                        continue;

                    var rr2 = npcBaseRaw.GetRowOrDefault(nid);
                    if (rr2 is not { } raw2) continue;

                    try
                    {
                        var bId = raw2.ReadUInt32Column(105);
                        if (bId != 0 && !matchedDialogIds["Balloon"].Contains(bId)
                            && balloonTexts.ContainsKey(bId))
                        {
                            var ak = $"{npcBase2.Race.RowId}_{npcBase2.Gender}_{npcBase2.Face}_{npcBase2.HairStyle}";
                            if (appearanceToNamedNpc.TryGetValue(ak, out var namedNpc2))
                            {
                                foreach (var texts in FlattenTexts(balloonTexts[bId]))
                                {
                                    if (texts.Values.All(string.IsNullOrEmpty)) continue;
                                    linkedDialogs.Add(new LinkedDialog
                                    {
                                        NpcId = namedNpc2.npcId,
                                        NpcName = namedNpc2.names,
                                        Race = namedNpc2.raceStr,
                                        Gender = namedNpc2.gender.ToString(),
                                        Sheet = "Balloon",
                                        DialogId = bId,
                                        MatchSource = DialogMatchSource.Direct.ToString(),
                                        Texts = texts
                                    });
                                }
                                matchedDialogIds["Balloon"].Add(bId);
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        _log.Info(nameof(DoHarvest),
            $"Pass 2: {pass2UnnamedWithDialog} unnamed NPCs with new dialog, " +
            $"{pass2AppearanceMatched} appearance matched", eventId);

        EndPhase();
        // Diagnostic: check where unmatched DefaultTalk IDs live
        BeginPhase(Loc.S("Diagnosing unmatched DefaultTalk..."), 1);
        var unmatchedDtIds = new HashSet<uint>(
            defaultTalkTexts.Keys.Where(k => !matchedDialogIds["DefaultTalk"].Contains(k)));
        var foundInENpcData = 0;
        var foundInEventHandler = 0;
        var foundViaArrayEventHandler = 0;

        // Build ArrayEventHandler → DefaultTalk lookup
        var aehToDefaultTalk = new Dictionary<uint, HashSet<uint>>();
        try
        {
            var aehRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "ArrayEventHandler");
            if (aehRaw != null)
            {
                foreach (var row in aehRaw)
                {
                    var ids = new HashSet<uint>();
                    for (var i = 0; i < 16; i++)
                    {
                        try
                        {
                            var v = row.ReadUInt32Column(i);
                            if (v != 0 && unmatchedDtIds.Contains(v)) ids.Add(v);
                        }
                        catch { }
                    }
                    if (ids.Count > 0) aehToDefaultTalk[row.RowId] = ids;
                }
            }
        }
        catch { }

        foreach (var npcBase in npcBaseSheet)
        {
            ct.ThrowIfCancellationRequested();
            var dataRefs = GetENpcDataValues(npcBase);
            foreach (var dataRef in dataRefs)
            {
                if (dataRef != 0 && unmatchedDtIds.Contains(dataRef))
                    foundInENpcData++;
            }
            // Check EventHandler (first field, index 0 in raw data)
            // ENpcBase raw: col 0 = EventHandler, col 1 = Important, col 2-33 = ENpcData[0..31]
            try
            {
                var ehVal = npcBase.ENpcData[0].RowId; // This is ENpcData[0], not EventHandler
                // We need raw access for EventHandler. Let's check via the data refs approach.
            }
            catch { }

            // Check if any ENpcData references an ArrayEventHandler with unmatched DefaultTalk
            foreach (var dataRef in dataRefs)
            {
                if (dataRef != 0 && aehToDefaultTalk.ContainsKey(dataRef))
                    foundViaArrayEventHandler++;
            }
        }

        // Check SwitchTalkVariation: maps base DefaultTalk → replacement DefaultTalk on quest completion
        // SwitchTalkVariation.RowId = base DefaultTalk ID, SwitchTalkVariation.DefaultTalk (col 3) = replacement
        var foundViaSwitchTalk = 0;
        var switchTalkReplacementToBase = new Dictionary<uint, uint>(); // replacement DT → base DT
        try
        {
            var stvRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "SwitchTalkVariation");
            if (stvRaw != null)
            {
                foreach (var row in stvRaw)
                {
                    try
                    {
                        var replacementDt = row.ReadUInt32Column(3);
                        if (replacementDt != 0 && unmatchedDtIds.Contains(replacementDt))
                        {
                            switchTalkReplacementToBase[replacementDt] = row.RowId;
                            foundViaSwitchTalk++;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // Check CustomTalk: ScriptArg[0..29] (col 33-62) may contain DefaultTalk IDs
        var customTalkToDefaultTalk = new Dictionary<uint, HashSet<uint>>();
        var foundViaCustomTalk = 0;
        try
        {
            var ctRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "CustomTalk");
            if (ctRaw != null)
            {
                foreach (var row in ctRaw)
                {
                    var ids = new HashSet<uint>();
                    for (var i = 0; i < 30; i++)
                    {
                        try
                        {
                            var v = row.ReadUInt32Column(33 + i);
                            if (v != 0 && unmatchedDtIds.Contains(v)) ids.Add(v);
                        }
                        catch { }
                    }
                    if (ids.Count > 0) customTalkToDefaultTalk[row.RowId] = ids;
                }
                foundViaCustomTalk = customTalkToDefaultTalk.Values.SelectMany(s => s).Distinct().Count();
            }
        }
        catch { }

        _log.Info(nameof(DoHarvest),
            $"Diagnostic: {unmatchedDtIds.Count} unmatched DefaultTalk, " +
            $"{foundViaCustomTalk} via CustomTalk, " +
            $"{foundInENpcData} in ENpcData, " +
            $"{foundViaArrayEventHandler} via AEH, " +
            $"{foundViaSwitchTalk} via STV", eventId);
        EndPhase();
        _log.Info(nameof(DoHarvest), $"Diag: {unmatchedDtIds.Count} unmatched, {foundViaCustomTalk} CustomTalk, {foundViaSwitchTalk} STV, {foundInENpcData} ENpcData", eventId);
        _log.Info(nameof(DoHarvest), $"Pass 2: {pass2UnnamedWithDialog} unnamed with dialog, {pass2AppearanceMatched} matched", eventId);

        // Pass 3: Link Balloon entries via LGB planevent data
        var lgbLinked = 0;
        foreach (var (balloonId, npcId) in lgbBalloonToNpc)
        {
            if (matchedDialogIds["Balloon"].Contains(balloonId)) continue;
            if (!allDialogSheets["Balloon"].TryGetValue(balloonId, out var bTextsByLang)) continue;

            var npcBase = npcBaseSheet.GetRowOrDefault(npcId);
            if (npcBase == null) continue;

            var raceStr = GetRaceString(npcBase.Value);
            var race = ParseNpcRace(raceStr);
            var gender = NpcIdentityHelper.DetermineGender(npcBase.Value, race, _jsonData.ModelGenderMap);

            // Try to get NPC name (might be unnamed)
            var lgbNames = npcNames.TryGetValue(npcId, out var nn) && nn.Values.Any(v => !string.IsNullOrEmpty(v))
                ? nn
                : new Dictionary<string, string> { { "en", "" } };

            var balloonEntry = new Dictionary<string, string>();
            foreach (var (lang, textList) in bTextsByLang)
            {
                if (textList.Count > 0 && !string.IsNullOrEmpty(textList[0]))
                    balloonEntry[lang] = textList[0];
            }

            if (balloonEntry.Count > 0)
            {
                linkedDialogs.Add(new LinkedDialog
                {
                    NpcId = npcId,
                    NpcName = lgbNames,
                    Race = raceStr,
                    Gender = gender.ToString(),
                    Sheet = "Balloon",
                    DialogId = balloonId,
                    MatchSource = DialogMatchSource.Direct.ToString(),
                    Texts = balloonEntry
                });
                matchedDialogIds["Balloon"].Add(balloonId);
                lgbLinked++;
            }
        }
        _log.Info(nameof(DoHarvest), $"Pass 3 (LGB Balloon): {lgbLinked} new Balloon entries linked", eventId);

        // Step 4: Collect unmatched dialog entries
        BeginPhase(Loc.S("Collecting unmatched dialogs..."), 1);
        var unmatchedDialogs = new List<UnmatchedDialog>();

        foreach (var (sheetName, dialogEntries) in allDialogSheets)
        {
            var matched = matchedDialogIds[sheetName];
            foreach (var (dialogId, textsByLang) in dialogEntries)
            {
                if (matched.Contains(dialogId)) continue;

                foreach (var texts in FlattenTexts(textsByLang))
                {
                    if (texts.Values.All(string.IsNullOrEmpty))
                        continue;

                    unmatchedDialogs.Add(new UnmatchedDialog
                    {
                        Sheet = sheetName,
                        DialogId = dialogId,
                        Texts = texts
                    });
                }
            }
        }

        // Step 4b: Build NPC → zone name lookup from Level sheet
        EndPhase();
        ct.ThrowIfCancellationRequested();
        var (npcTerritories, npcZoneNames) = BuildNpcTerritoryLookup(language, ct, eventId);

        // Step 5: Harvest quest dialogs (HarvestQuestDialogs drives its own phase)
        ct.ThrowIfCancellationRequested();
        List<LinkedQuestDialog> linkedQuests;
        List<UnmatchedQuestDialog> unmatchedQuests;
        List<ParenCandidateEntry> parenCandidates;
        if (harvestQuests)
        {
            (linkedQuests, unmatchedQuests, parenCandidates) = HarvestQuestDialogs(npcNames, bnpcNames, npcBaseSheet, npcTerritories, npcZoneNames, language, ct, eventId, questTypeFilter);
        }
        else
        {
            _log.Info(nameof(DoHarvest), "Quest dialog scan skipped — quest type filter set to 'Non-Quest Dialog' only.", eventId);
            linkedQuests = new List<LinkedQuestDialog>();
            unmatchedQuests = new List<UnmatchedQuestDialog>();
            parenCandidates = new List<ParenCandidateEntry>();
        }

        // Step 5b: Harvest cut_scene/* unvoiced dialogs. Gated on harvestNonQuest because
        // we tag them as QuestType.None (no .cutb parsing → can't tell which quest the
        // cutscene belongs to). Filter "All" and "Non-Quest Dialog" pick them up; specific
        // quest-type filters skip them.
        if (harvestNonQuest)
        {
            ct.ThrowIfCancellationRequested();
            var cutsceneDialogs = HarvestCutsceneDialogs(npcNames, npcBaseSheet, language, ct, eventId);
            linkedDialogs.AddRange(cutsceneDialogs);
        }

        // Log unmapped ModelChara IDs before persist (so it's visible early)
        if (_unmappedModels.Count > 0)
        {
            var lines = _unmappedModels
                .OrderByDescending(kvp => kvp.Value.Count)
                .Select(kvp => $"ModelChara={kvp.Key} ({kvp.Value.Count}x): {string.Join(", ", kvp.Value.Take(5))}");
            _log.Warning(nameof(DoHarvest),
                $"Unmapped ModelChara IDs (race=Unknown, need NpcRaces.json entry): {_unmappedModels.Count} model IDs\n" +
                string.Join("\n", lines), eventId);
            _unmappedModels.Clear();
        }

        // Step 6: Persist linked dialogs to database
        // Suppress events for the entire persist phase to prevent concurrent DbContext access.
        // NpcDataService.LoadFromDatabase skips while SuppressEvents is true.
        // BulkMode disables per-Upsert cache refreshes — we refresh once at the end.
        int persisted = 0;
        int persistedQuest = 0;
        _db.SuppressEvents = true;
        _db.BulkMode = true;
        try
        {
            if (harvestNonQuest)
            {
                ct.ThrowIfCancellationRequested();
                persisted = PersistLinkedDialogs(linkedDialogs, npcBaseSheet, language, npcZoneNames, ct, eventId);
            }
            else
            {
                _log.Info(nameof(DoHarvest), "Non-quest dialog persist skipped — quest type filter restricts to a single quest type.", eventId);
            }

            if (harvestQuests)
            {
                ct.ThrowIfCancellationRequested();
                persistedQuest = PersistLinkedQuestDialogs(linkedQuests, npcBaseSheet, language, npcZoneNames, ct, eventId);
            }
        }
        finally
        {
            _db.BulkMode = false;
            _db.RefreshCaches();
            _db.SuppressEvents = false;
            _db.NotifyVoiceClipLogged();
        }

        // Write unmatched dialogs to JSON (no NPC link — can't go in DB)
        ct.ThrowIfCancellationRequested();
        if (unmatchedDialogs.Count > 0 || unmatchedQuests.Count > 0)
        {
            var baseDir = _config.SaveToLocal ? _config.LocalSaveLocation : @"C:\alltalk_tts";
            var outputDir = Path.Combine(baseDir, "harvest");
            Directory.CreateDirectory(outputDir);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

            // Split unmatched dialogs by category
            var coreSheets = new HashSet<string> { "DefaultTalk", "Balloon" };
            var unmatchedCore = unmatchedDialogs.Where(d => coreSheets.Contains(d.Sheet)).ToList();
            var unmatchedBattleTalks = unmatchedDialogs.Where(d => !coreSheets.Contains(d.Sheet)).ToList();

            if (unmatchedCore.Count > 0)
                File.WriteAllText(Path.Combine(outputDir, "unmatched_dialogs.json"),
                    JsonSerializer.Serialize(unmatchedCore, jsonOptions));

            if (unmatchedBattleTalks.Count > 0)
                File.WriteAllText(Path.Combine(outputDir, "unmatched_battletalks.json"),
                    JsonSerializer.Serialize(unmatchedBattleTalks, jsonOptions));

            if (unmatchedQuests.Count > 0)
                File.WriteAllText(Path.Combine(outputDir, "unmatched_quest_dialogs.json"),
                    JsonSerializer.Serialize(unmatchedQuests, jsonOptions));

            // Multi-candidate paren-prefix entries — user resolves these manually by copying
            // entries into <configDir>/quest_npc_aliases.json with the chosen npcId/npcName.
            if (parenCandidates.Count > 0)
                File.WriteAllText(Path.Combine(outputDir, "quest_alias_candidates.json"),
                    JsonSerializer.Serialize(parenCandidates, jsonOptions));
        }

        // Voice-name suggestion files: harvest detects (-Fakename-) prefixes on lines that
        // DID resolve to an NPC and emits one VoiceMap-shaped entry per (NPC, language).
        // Output is directly mergeable into VoiceNames{LANG}.json so the user can grow that
        // file from real game data instead of curating each entry by hand.
        ct.ThrowIfCancellationRequested();
        EmitVoiceNameSuggestions(linkedDialogs, linkedQuests, eventId);

        var linkedDtCount = linkedDialogs.Count(d => d.Sheet == "DefaultTalk");
        var linkedBalloonCount = linkedDialogs.Count(d => d.Sheet == "Balloon");
        var linkedBattleTalkCount = linkedDialogs.Count(d => d.Sheet != "DefaultTalk" && d.Sheet != "Balloon");
        var unmatchedDtCount = unmatchedDialogs.Count(d => d.Sheet == "DefaultTalk");
        var unmatchedBalloonCount = unmatchedDialogs.Count(d => d.Sheet == "Balloon");
        var unmatchedBattleTalkCount = unmatchedDialogs.Count(d => d.Sheet != "DefaultTalk" && d.Sheet != "Balloon");
        var msg = $"Done: {persisted + persistedQuest} persisted to DB " +
                  $"({linkedDialogs.Count} linked: {linkedDtCount} DT, {linkedBalloonCount} Balloon, {linkedBattleTalkCount} BattleTalk, " +
                  $"{linkedQuests.Count} quest), " +
                  $"{unmatchedDialogs.Count} unmatched ({unmatchedDtCount} DT, {unmatchedBalloonCount} Balloon, {unmatchedBattleTalkCount} BattleTalk), " +
                  $"{unmatchedQuests.Count} quest unmatched";
        _log.Info(nameof(DoHarvest), msg, eventId);
        BeginPhase(string.Format(Loc.S("Done: {0} dialogs persisted"), persisted + persistedQuest), 1);
    }

    private static readonly Dictionary<Dalamud.Game.ClientLanguage, string> LangToCode = new()
    {
        { Dalamud.Game.ClientLanguage.English, "en" },
        { Dalamud.Game.ClientLanguage.German, "de" },
        { Dalamud.Game.ClientLanguage.Japanese, "ja" },
        { Dalamud.Game.ClientLanguage.French, "fr" },
    };

    private int PersistLinkedDialogs(
        List<LinkedDialog> dialogs,
        ExcelSheet<ENpcBase> npcBaseSheet,
        Dalamud.Game.ClientLanguage language,
        Dictionary<uint, string> npcZoneNames,
        CancellationToken ct,
        EKEventId eventId)
    {
        var langCode = LangToCode.GetValueOrDefault(language, "en");
        var persisted = 0;

        // Pre-process: for each NPC name, find the best gender/race (prefer non-Unknown/None).
        // This deduplicates NPCs that appear as both (Male, Elezen) and (None, Unknown).
        var bestIdentity = new Dictionary<string, (string gender, string race, string raceStr)>();
        foreach (var dialog in dialogs)
        {
            if (!dialog.NpcName.TryGetValue(langCode, out var name) || string.IsNullOrEmpty(name)) continue;
            var resolvedName = _jsonData.GetNpcName(name).Trim();
            if (string.IsNullOrEmpty(resolvedName)) continue;

            if (!bestIdentity.TryGetValue(resolvedName, out var current))
            {
                bestIdentity[resolvedName] = (dialog.Gender, dialog.Race, dialog.Race);
            }
            else
            {
                // Prefer real gender/race over None/Unknown
                var curGender = Enum.TryParse<Genders>(current.gender, true, out var cg) ? cg : Genders.None;
                var newGender = Enum.TryParse<Genders>(dialog.Gender, true, out var ng) ? ng : Genders.None;
                var curRace = Enum.TryParse<NpcRaces>(current.race, true, out var cr) ? cr : NpcRaces.Unknown;
                var newRace = Enum.TryParse<NpcRaces>(dialog.Race, true, out var nr) ? nr : NpcRaces.Unknown;

                var upgrade = false;
                if (curRace == NpcRaces.Unknown && newRace != NpcRaces.Unknown) upgrade = true;
                else if (curGender == Genders.None && newGender != Genders.None && curRace == newRace) upgrade = true;

                if (upgrade)
                    bestIdentity[resolvedName] = (dialog.Gender, dialog.Race, dialog.Race);
            }
        }

        // ── Phase 1: Create all unique characters and assign voices ──
        var characterCache = new Dictionary<string, (CharacterEntity character, CharacterContextEntity context)>();
        var dialogNpcNames = new Dictionary<int, (string npcName, Genders gender, NpcRaces race, string raceStr)>(); // dialog index → resolved identity

        // Hoist the voice list once — converting EF entities to EchokrautVoice on every NPC was the main allocation hot-spot.
        var voicesForHarvest = _npcData.GetEchokrautVoices();

        BeginPhase(Loc.S("Linking dialogs to characters..."), Math.Max(1, dialogs.Count));
        for (var i = 0; i < dialogs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            ReportPhaseProgress(i);
            var dialog = dialogs[i];

            if (!dialog.Texts.TryGetValue(langCode, out var text) || string.IsNullOrWhiteSpace(text))
                continue;

            var gender = Enum.TryParse<Genders>(dialog.Gender, true, out var g) ? g : Genders.None;
            _npcDePronoun.TryGetValue(dialog.NpcId, out var dePronoun);
            var resolvedNames = ResolveGenderTags(dialog.NpcName, gender, dePronoun);
            if (!resolvedNames.TryGetValue(langCode, out var npcName) || string.IsNullOrWhiteSpace(npcName))
                continue;

            npcName = _jsonData.GetNpcName(npcName).Trim();
            npcName = NormalizeNpcName(npcName);

            if (bestIdentity.TryGetValue(npcName, out var best))
            {
                gender = Enum.TryParse<Genders>(best.gender, true, out var bg) ? bg : gender;
                dialog.Race = best.race;
            }

            var race = Enum.TryParse<NpcRaces>(dialog.Race, true, out var r) ? r : NpcRaces.Unknown;
            dialogNpcNames[i] = (npcName, gender, race, dialog.Race);
            var charKey = $"{npcName}|{(int)gender}|{(int)race}|{(int)language}";

            if (!characterCache.ContainsKey(charKey))
            {
                var bodyType = BodyType.Adult;
                try
                {
                    var npcBase = npcBaseSheet.GetRow(dialog.NpcId);
                    bodyType = npcBase.BodyType switch { 4 => BodyType.Child, 3 => BodyType.Elder, _ => BodyType.Adult };
                }
                catch { }

                // Resolve voice up-front (pure, no DB writes, no logging) so we can persist it
                // in the same UpsertCharacter call instead of issuing a second round-trip.
                var npcData = new NpcMapData(ObjectKind.BattleNpc)
                {
                    Name = npcName, Race = race, RaceStr = dialog.Race,
                    Gender = gender, BodyType = bodyType, Language = language,
                };
                var pickedVoice = _backend.PickVoice(npcData, voicesForHarvest);

                var character = _db.UpsertCharacter(new CharacterEntity
                {
                    Name = npcName,
                    Race = (int)race,
                    RaceStr = dialog.Race,
                    Gender = (int)gender,
                    BodyType = (int)bodyType,
                    Language = (int)language,
                    ObjectKind = (int)ObjectKind.BattleNpc,
                    VoiceKey = pickedVoice?.BackendVoice ?? string.Empty,
                });

                // EnsureContext (not Upsert) so re-runs of the harvest don't reset user-tuned
                // IsEnabled / Volume back to defaults on characters that already exist.
                var context = _db.EnsureContext(character.Id, dialog.Sheet == "Balloon" ? "bubble" : "npc");

                characterCache[charKey] = (character, context);
            }
        }

        EndPhase();
        BeginPhase(Loc.S("Saving voice clips to database..."), Math.Max(1, dialogs.Count));

        // ─��� Phase 2: Persist voice clips and instances ──
        for (var i = 0; i < dialogs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var dialog = dialogs[i];

            if (!dialogNpcNames.TryGetValue(i, out var resolved))
                continue;

            var charKey = $"{resolved.npcName}|{(int)resolved.gender}|{(int)resolved.race}|{(int)language}";
            if (!characterCache.TryGetValue(charKey, out var cached))
                continue;

            if (!dialog.Texts.TryGetValue(langCode, out var text) || string.IsNullOrWhiteSpace(text))
                continue;

            npcZoneNames.TryGetValue(dialog.NpcId, out var zoneName);
            _db.GetOrCreateInstance(cached.character.Id, dialog.NpcId, zoneName ?? "");

            var textSource = dialog.Sheet == "Balloon" ? TextSource.AddonBubble : TextSource.AddonTalk;
            _db.LogOrUpdateVoiceClip(new VoiceClipEntity
            {
                CharacterId = cached.character.Id,
                NpcBaseId = (long)dialog.NpcId,
                Timestamp = DateTime.UtcNow,
                TextSource = (int)textSource,
                Language = (int)language,
                VoiceKey = cached.character.VoiceKey,
                OriginalText = text,
                CleanedText = text,
                SavedToDisk = false,
                BodyType = cached.character.BodyType,
                HasPlayerPlaceholder = TalkTextHelper.ContainsPlayerPlaceholder(text),
                QuestType = (int)dialog.QuestType,
            });

            // Speaker-alias capture: if the text starts with the FFXIV speaker hint
            // "(-Fakename-)" AND the fakename differs from the NPC's own name in this
            // language, record the alias on the character row. Includes anonymous markers
            // like "???" — those still tell us which characters CAN appear as "???" in
            // a cutscene, and the runtime resolves the actual speaker via physical
            // presence (speaker.BaseId match) + already-spoken tracking. Runs in bulk
            // mode → SaveChanges is deferred to the persist flush.
            var aliasMatch = ParenSpeakerCaptureRegex.Match(text);
            if (aliasMatch.Success)
            {
                var fake = aliasMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(fake)
                    && !string.Equals(fake, resolved.npcName, StringComparison.OrdinalIgnoreCase))
                {
                    _db.UpsertSpeakerAlias(cached.character.Id, (int)language, fake);
                }
            }

            persisted++;
            ReportPhaseProgress(persisted);
            if (persisted % 500 == 0)
            {
                // Flush accumulated changes in one transaction, then clear tracker
                _db.FlushChanges();
                _db.ClearChangeTracker();
            }
        }

        // Flush remaining changes
        _db.FlushChanges();

        EndPhase();
        return persisted;
    }

    private int PersistLinkedQuestDialogs(
        List<LinkedQuestDialog> dialogs,
        ExcelSheet<ENpcBase> npcBaseSheet,
        Dalamud.Game.ClientLanguage language,
        Dictionary<uint, string> npcZoneNames,
        CancellationToken ct,
        EKEventId eventId)
    {
        // Reuse the same logic as regular dialogs — LinkedQuestDialog has the same NPC fields
        var converted = dialogs.Select(q => new LinkedDialog
        {
            NpcId = q.NpcId,
            NpcName = q.NpcName,
            Race = q.Race,
            Gender = q.Gender,
            Sheet = "DefaultTalk",
            DialogId = 0,
            MatchSource = q.MatchSource,
            QuestType = q.QuestType,
            Texts = q.Texts
        }).ToList();

        return PersistLinkedDialogs(converted, npcBaseSheet, language, npcZoneNames, ct, eventId);
    }

    /// <summary>
    /// Load a dialog sheet in all 4 languages. Returns dialogId → lang → list of text strings.
    /// </summary>
    private Dictionary<uint, Dictionary<string, List<string>>> LoadDialogSheet<T>(
        string sheetName,
        Func<T, string, List<string>> textExtractor,
        CancellationToken ct,
        EKEventId eventId) where T : struct, Lumina.Excel.IExcelRow<T>
    {
        var result = new Dictionary<uint, Dictionary<string, List<string>>>();

        // Total = sum of row counts across all 4 languages so the bar reflects real work.
        var totalRows = 0;
        for (var li = 0; li < LangValues.Length; li++)
        {
            var s = _dataManager.GetExcelSheet<T>(LangValues[li]);
            if (s != null) totalRows += s.Count;
        }
        BeginPhase(string.Format(Loc.S("Scanning {0} for dialogs..."), sheetName), totalRows);
        var processed = 0;

        for (var langIdx = 0; langIdx < LangCodes.Length; langIdx++)
        {
            ct.ThrowIfCancellationRequested();
            var langCode = LangCodes[langIdx];
            var lang = LangValues[langIdx];
            var sheet = _dataManager.GetExcelSheet<T>(lang);
            if (sheet == null) continue;

            var count = 0;
            foreach (var row in sheet)
            {
                count++;
                processed++;
                ReportPhaseProgress(processed);

                var texts = textExtractor(row, langCode);
                if (texts.Count == 0) continue;

                if (!result.TryGetValue(row.RowId, out var langDict))
                {
                    langDict = new Dictionary<string, List<string>>();
                    result[row.RowId] = langDict;
                }

                langDict[langCode] = texts;
            }

            _log.Debug(nameof(LoadDialogSheet), $"Loaded {count} {sheetName} rows for {langCode}", eventId);
        }

        EndPhase();
        return result;
    }

    private static List<string> GetDefaultTalkTexts(DefaultTalk row, string langCode)
    {
        var texts = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var text = ExtractTextWithPlayerName(row.Text[i], langCode);
            texts.Add(text ?? "");
        }
        return texts;
    }

    private static List<string> GetBalloonTexts(Balloon row, string langCode)
    {
        var text = ExtractTextWithPlayerName(row.Dialogue, langCode);
        return [text ?? ""];
    }

    /// <summary>
    /// Generic extractor for sheets with a single .Text property
    /// (InstanceContentTextData, ContentTalk, NpcYell, MassivePcContentTextData, GoldSaucerTextData).
    /// </summary>
    private static List<string> GetSingleTextSheet<T>(T row, string langCode) where T : struct
    {
        // Use dynamic dispatch — all these sheets have a .Text property returning ReadOnlySeString
        var text = ExtractTextWithPlayerName(((dynamic)row).Text, langCode);
        return [text ?? ""];
    }

    /// <summary>
    /// Flatten per-language text lists into individual text entries (one per text field index).
    /// E.g., DefaultTalk has 3 text fields → produces up to 3 separate Dictionary entries.
    /// </summary>
    private static List<Dictionary<string, string>> FlattenTexts(Dictionary<string, List<string>> textsByLang)
    {
        var maxFields = textsByLang.Values.Max(l => l.Count);
        var result = new List<Dictionary<string, string>>();

        for (var fieldIdx = 0; fieldIdx < maxFields; fieldIdx++)
        {
            var entry = new Dictionary<string, string>();
            foreach (var (lang, texts) in textsByLang)
                entry[lang] = fieldIdx < texts.Count ? texts[fieldIdx] : "";

            // Skip single-character texts (placeholders, noise)
            if (entry.Values.All(v => v.Length <= 1))
                continue;

            // Skip developer placeholders ("0", "Leer", "未使用", etc.)
            if (entry.Values.Where(v => !string.IsNullOrEmpty(v)).Any(v => PlaceholderTexts.Contains(v.Trim())))
                continue;

            // Skip entries where all languages have identical text (system strings, untranslated IDs)
            var nonEmpty = entry.Values.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
            if (nonEmpty.Count == 1)
                continue;

            // Skip entries with large length discrepancy between languages (likely partial/bad data)
            var lengths = entry.Values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v.Length).ToList();
            if (lengths.Count >= 2)
            {
                var shortest = lengths.Min();
                var longest = lengths.Max();
                if (shortest > 0 && longest > shortest * 5)
                    continue;
            }

            result.Add(entry);
        }

        return result;
    }

    private Dictionary<uint, Dictionary<string, string>> LoadNpcNames(CancellationToken ct, EKEventId eventId)
    {
        var result = new Dictionary<uint, Dictionary<string, string>>();

        for (var langIdx = 0; langIdx < LangCodes.Length; langIdx++)
        {
            ct.ThrowIfCancellationRequested();
            var langCode = LangCodes[langIdx];
            var lang = LangValues[langIdx];
            var sheet = _dataManager.GetExcelSheet<ENpcResident>(lang);
            if (sheet == null) continue;

            foreach (var row in sheet)
            {
                var name = row.Singular.ExtractText();
                if (!result.TryGetValue(row.RowId, out var nameDict))
                {
                    nameDict = new Dictionary<string, string>();
                    result[row.RowId] = nameDict;
                }
                nameDict[langCode] = name?.Trim() ?? "";

                // Store German Article value for gender tag resolution
                if (lang == Dalamud.Game.ClientLanguage.German)
                    _npcDePronoun[row.RowId] = row.Pronoun;
            }

            _log.Debug(nameof(LoadNpcNames), $"Loaded ENpcResident names for {langCode}", eventId);
        }

        return result;
    }

    private Dictionary<uint, Dictionary<string, string>> LoadBNpcNames(CancellationToken ct, EKEventId eventId)
    {
        var result = new Dictionary<uint, Dictionary<string, string>>();

        for (var langIdx = 0; langIdx < LangCodes.Length; langIdx++)
        {
            ct.ThrowIfCancellationRequested();
            var langCode = LangCodes[langIdx];
            var lang = LangValues[langIdx];
            var sheet = _dataManager.GetExcelSheet<BNpcName>(lang);
            if (sheet == null) continue;

            foreach (var row in sheet)
            {
                var name = row.Singular.ExtractText();
                if (string.IsNullOrEmpty(name)) continue;

                if (!result.TryGetValue(row.RowId, out var nameDict))
                {
                    nameDict = new Dictionary<string, string>();
                    result[row.RowId] = nameDict;
                }
                nameDict[langCode] = name.Trim();
            }

            _log.Debug(nameof(LoadBNpcNames), $"Loaded BNpcName names for {langCode}", eventId);
        }

        return result;
    }

    // Collect unmapped ModelChara IDs during harvest for diagnostic output
    private readonly Dictionary<int, HashSet<string>> _unmappedModels = new();
    // German Pronoun value per NPC ID (from ENpcResident). 1 = feminine noun (sie).
    private readonly Dictionary<uint, sbyte> _npcDePronoun = new();

    private static QuestType ClassifyQuest(Quest quest)
    {
        // Beast tribe quests
        if (quest.BeastTribe.RowId != 0) return QuestType.BeastTribe;

        // Repeatable quests (dailies, weeklies, leves)
        if (quest.IsRepeatable) return QuestType.Repeatable;

        // Classify by EventIconType
        var iconType = quest.EventIconType.RowId;
        return iconType switch
        {
            3 => QuestType.MSQ,         // Meteor icon
            8 => QuestType.Unlock,      // Blue + icon (unlock & class/job)
            10 => QuestType.Unlock,     // Also blue + variant
            2 => QuestType.Event,       // Seasonal event icon
            1 => QuestType.SideQuest,   // Normal ! icon
            _ => QuestType.SideQuest,   // Default to side quest for unknown types
        };
    }

    private string GetRaceString(ENpcBase npcBase)
    {
        try
        {
            var raceRowId = npcBase.Race.RowId;
            if (raceRowId != 0)
            {
                // Playable race — read from English Race sheet
                var enRaceSheet = _dataManager.GetExcelSheet<Race>(Dalamud.Game.ClientLanguage.English);
                var enRace = enRaceSheet?.GetRowOrDefault(raceRowId);
                if (enRace != null)
                {
                    var raceName = enRace.Value.Masculine.ExtractText();
                    return NpcIdentityHelper.CanonicalRaceName(raceName);
                }
            }

            // Beast tribe fallback — use ModelChara → ModelsToRaceMap
            var modelChara = (int)npcBase.ModelChara.RowId;
            if (modelChara != 0 && _jsonData.ModelsToRaceMap.TryGetValue(modelChara, out var beastRace))
                return beastRace.ToString();

            // Track unmapped models for diagnostic
            if (modelChara != 0)
            {
                if (!_unmappedModels.TryGetValue(modelChara, out var names))
                {
                    names = new HashSet<string>();
                    _unmappedModels[modelChara] = names;
                }
                var enName = _dataManager.GetExcelSheet<ENpcResident>(Dalamud.Game.ClientLanguage.English)?
                    .GetRowOrDefault(npcBase.RowId)?.Singular.ExtractText() ?? $"ENpcBase#{npcBase.RowId}";
                names.Add(enName);
            }

            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static NpcRaces ParseNpcRace(string raceStr)
    {
        return Enum.TryParse<NpcRaces>(raceStr, true, out var race) ? race : NpcRaces.Unknown;
    }

    /// <summary>
    /// Resolves FFXIV grammatical gender tags in NPC names.
    /// German: [a] = adjective ending (er/e), [p] = profession suffix (""/in)
    /// French: [a] = adjective ending (""/"e"), [p] = profession suffix (""/e)
    /// Applied per-language; languages without known tags pass through unchanged.
    /// Any remaining [x] tags are stripped as fallback.
    /// </summary>
    /// <summary>
    /// Resolve German [a]/[p] and French [a]/[p] gender tags in NPC names.
    /// The <paramref name="dePronoun"/> value from ENpcResident.Pronoun (German sheet) controls
    /// whether [p] adds "in": Pronoun 1 = sie (feminine noun) → [p] is empty since the name
    /// is already feminine. Pronoun 0 = er (masculine) → [p] adds "in" for feminine NPC gender.
    /// </summary>
    /// <summary>
    /// Capitalize the first character so harvest stems like "stille Druidin" — German
    /// adjective declensions land lowercase after [a]→"e" substitution — match the runtime
    /// title-cased display name "Stille Druidin". Without this, a single NPC could end up
    /// as two distinct character rows (the case-insensitive unique index added in v12 catches
    /// existing collisions; this prevents the harvester from creating new ones).
    /// </summary>
    internal static string NormalizeNpcName(string name)
        => Echokraut.Helper.Functional.NpcNameNormalizer.Capitalize(name);

    private static Dictionary<string, string> ResolveGenderTags(Dictionary<string, string> names, Genders gender, int dePronoun = 0)
    {
        var resolved = new Dictionary<string, string>(names.Count);
        var isFemale = gender == Genders.Female;
        foreach (var (lang, name) in names)
        {
            // Helper resolves [a]/[p] tags + strips unknown bracket tags + capitalizes;
            // we strip the trailing capitalization here because callers expect the raw
            // resolved form (existing harvest pipeline applies NormalizeNpcName separately).
            var resolvedName = Echokraut.Helper.Functional.NpcNameNormalizer.Resolve(name, lang, isFemale, dePronoun);
            resolved[lang] = resolvedName;
        }
        return resolved;
    }


    private static List<uint> GetENpcDataValues(ENpcBase npcBase)
    {
        var values = new List<uint>(32);
        for (var i = 0; i < 32; i++)
        {
            var val = npcBase.ENpcData[i].RowId;
            if (val != 0)
                values.Add(val);
        }
        return values;
    }

    /// <summary>
    /// Bundle of Lua-side data for a single quest: the textKey → NPC ID mapping resolved by the
    /// existing 5-priority bytecode analysis, the raw Talk() calls (in extraction order, grouped
    /// by FunctionIndex), and the ACTOR name → NPC ID table from QuestParams. The latter two are
    /// used by the silent-actor paren-prefix heuristic in the harvest main loop.
    /// </summary>
    internal record LuaQuestMapping(
        Dictionary<string, uint> TextKeyToNpcId,
        List<LuabParser.TalkCall> TalkCalls,
        Dictionary<string, uint> ActorNameToNpcId);

    /// <summary>
    /// Parse the quest's Lua script to build a textKey → ENpcBase ID mapping.
    /// Uses QuestParams ACTOR definitions + Lua bytecode analysis.
    /// </summary>
    private LuaQuestMapping BuildLuaTextKeyMapping(Quest quest, string questId, string subdir, EKEventId eventId)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var emptyTalkCalls = new List<LuabParser.TalkCall>();
        var emptyActorMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Build ACTOR → NPC ID mapping and scene → NPC ID mapping from QuestParams.
            // QuestParams has two patterns:
            //   "ACTOR0" = npcId          → ACTOR name to NPC ID
            //   "SEQ_0_ACTOR0" = sceneNum → ACTOR triggers scene number (direct scene mapping)
            var actorNameToNpcId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            var sceneNumToActorName = new Dictionary<int, string>(); // scene number → "ACTOR0"

            for (var i = 0; i < quest.QuestParams.Count; i++)
            {
                var param = quest.QuestParams[i];
                var instruction = param.ScriptInstruction.ExtractText();
                if (string.IsNullOrEmpty(instruction)) continue;

                if (instruction.StartsWith("ACTOR", StringComparison.OrdinalIgnoreCase)
                    && !instruction.Contains("_"))
                {
                    // Pure ACTOR entry: "ACTOR0" = npcId
                    var npcId = (uint)param.ScriptArg;
                    if (npcId > 0)
                        actorNameToNpcId[instruction] = npcId;
                }
                else if (Regex.IsMatch(instruction, @"^SEQ_\d+_ACTOR\d+$", RegexOptions.IgnoreCase))
                {
                    // Scene mapping entry: "SEQ_0_ACTOR0" = sceneNumber
                    var actorMatch = Regex.Match(instruction, @"(ACTOR\d+)$", RegexOptions.IgnoreCase);
                    if (actorMatch.Success)
                    {
                        var sceneNum = (int)param.ScriptArg;
                        sceneNumToActorName[sceneNum] = actorMatch.Value.ToUpperInvariant();
                    }
                }
            }

            if (actorNameToNpcId.Count == 0)
                return new LuaQuestMapping(result, emptyTalkCalls, emptyActorMap);

            // Load the Lua script file
            var scriptPath = $"game_script/quest/{subdir}/{questId}.luab";
            var file = _dataManager.GetFile(scriptPath);
            if (file == null)
                return new LuaQuestMapping(result, emptyTalkCalls, actorNameToNpcId);

            // Parse bytecode — extracts Talk() calls, dispatch mapping, and scene name→funcIndex
            var parser = new LuabParser(file.Data);
            var (talkCalls, sceneActorMap, sceneNameToFuncIndex) = parser.ParseWithDispatch();

            // Build scene→NPC mapping from QuestParams SEQ_N_ACTORN entries.
            // "SEQ_0_ACTOR0" = 0 means ACTOR0 triggers OnScene00000.
            // Use CLOSURE+SETGLOBAL analysis to get the correct funcIndex for each scene name.
            var questParamSceneMap = new Dictionary<int, uint>(); // funcIndex → NPC ID
            foreach (var (sceneNum, actorName) in sceneNumToActorName)
            {
                var sceneName = $"OnScene{sceneNum:D5}";
                if (sceneNameToFuncIndex.TryGetValue(sceneName, out var funcIndex)
                    && actorNameToNpcId.TryGetValue(actorName, out var npcId))
                {
                    questParamSceneMap[funcIndex] = npcId;
                }
            }

            // Build ConditionType-based scene→NPC mapping as fallback
            // Uses sceneNameToFuncIndex for correct scene→funcIndex resolution
            var conditionTypeMap = BuildConditionTypeSceneMap(quest.RowId, sceneNameToFuncIndex);
            var conditionTypeNpcMap = new Dictionary<int, uint>();
            foreach (var (funcIdx, npcId) in conditionTypeMap)
                conditionTypeNpcMap[funcIdx] = npcId;

            // Build ACTOR index → NPC ID mapping (sorted by ACTOR number)
            var sortedActors = actorNameToNpcId
                .OrderBy(kvp =>
                {
                    var numPart = kvp.Key.AsSpan(5); // skip "ACTOR"
                    return int.TryParse(numPart, out var n) ? n : 999;
                })
                .ToList();

            // Group Talk calls by function to determine register patterns
            var callsByFunction = talkCalls.GroupBy(c => c.FunctionIndex).ToList();

            foreach (var group in callsByFunction)
            {
                var funcIndex = group.Key;
                var distinctRegisters = group.Select(c => c.ActorRegister).Distinct().ToList();
                var isMultiSpeaker = distinctRegisters.Count > 1;

                foreach (var call in group)
                {
                    uint? npcId = null;

                    // Priority 1: QuestParams scene mapping (SEQ_N_ACTORN = sceneNum)
                    // Most reliable — explicit per-quest data from the Quest sheet.
                    if (questParamSceneMap.TryGetValue(funcIndex, out var qpNpcId))
                    {
                        npcId = qpNpcId;
                    }

                    // Priority 2: Lua debug info (parameter names like "actor0")
                    if (npcId == null
                        && call.ActorName != null
                        && call.ActorName.StartsWith("actor", StringComparison.OrdinalIgnoreCase)
                        && actorNameToNpcId.TryGetValue(call.ActorName, out var directId))
                    {
                        npcId = directId;
                    }

                    // Priority 3: Dispatch bytecode analysis (scene function → ACTOR)
                    if (npcId == null
                        && sceneActorMap.TryGetValue(funcIndex, out var dispatchActorName)
                        && actorNameToNpcId.TryGetValue(dispatchActorName, out var dispatchNpcId))
                    {
                        npcId = dispatchNpcId;
                    }

                    // Priority 4: ConditionType mapping (Quest sheet NPC→scene data)
                    if (npcId == null
                        && conditionTypeNpcMap.TryGetValue(funcIndex, out var ctNpcId))
                    {
                        npcId = ctNpcId;
                    }

                    // Priority 5: Register-based heuristic (only for unambiguous cases)
                    if (npcId == null)
                    {
                        if (sortedActors.Count == 1)
                        {
                            npcId = sortedActors[0].Value;
                        }
                        else if (isMultiSpeaker)
                        {
                            var actorIdx = Math.Max(0, call.ActorRegister - 1);
                            if (actorIdx < sortedActors.Count)
                                npcId = sortedActors[actorIdx].Value;
                        }
                    }

                    if (npcId != null)
                        result[call.TextKey] = npcId.Value;
                }
            }

            return new LuaQuestMapping(result, talkCalls, actorNameToNpcId);
        }
        catch (Exception ex)
        {
            _log.Debug(nameof(BuildLuaTextKeyMapping), $"Failed to parse Lua script for {questId}: {ex.Message}", eventId);
        }

        return new LuaQuestMapping(result, emptyTalkCalls, emptyActorMap);
    }

    // ── Quest NPC aliases: embedded ← remote ← user-local (priority-1 override) ──
    /// <summary>
    /// Build the (QuestId, NpcNameKey) → NPC ID mapping from up to three sources, layered:
    /// embedded resource <c>QuestNpcAliases.json</c> (always), remote URL via
    /// <see cref="IRemoteUrlService"/> (if reachable), and local user file
    /// <c>&lt;localSaveLocation&gt;/harvest/quest_npc_aliases.json</c> (if present). Later
    /// sources override earlier ones for the same (QuestId, NpcNameKey).
    /// <para>
    /// Each entry resolves to an NPC by explicit <c>NpcId</c> (wins) or by <c>NpcName</c>
    /// (cross-language, case-insensitive, with normalization: strip spaces/apostrophes/
    /// hyphens, uppercase). Ambiguous or unknown names log a warning and the entry is
    /// skipped — user must set <c>NpcId</c> for those.
    /// </para>
    /// </summary>
    private Dictionary<(uint questId, string npcNameKey), uint> LoadUserAliases(
        Dictionary<uint, Dictionary<string, string>> npcNames,
        Dictionary<uint, Dictionary<string, string>> bnpcNames,
        EKEventId eventId)
    {
        var result = new Dictionary<(uint, string), uint>();

        // Build name index once for resolving NpcName entries.
        var nameToIds = BuildAliasNameIndex(npcNames, bnpcNames);

        // 1) Embedded fallback (always available)
        var embeddedFile = LoadAliasFileFromEmbedded(eventId);
        ApplyAliasEntries(embeddedFile, "embedded", result, nameToIds, eventId);

        // 2) Remote URL (community-curated; overrides embedded)
        var remoteFile = LoadAliasFileFromRemote(eventId);
        ApplyAliasEntries(remoteFile, "remote", result, nameToIds, eventId);

        // 3) Local user file (per-user; overrides remote+embedded)
        var localFile = LoadAliasFileFromLocal(eventId, out var localPath);
        ApplyAliasEntries(localFile, $"local ({localPath})", result, nameToIds, eventId);

        _log.Info(nameof(LoadUserAliases),
            $"Quest NPC aliases loaded: {result.Count} effective entries (embedded+remote+local merged)",
            eventId);
        return result;
    }

    /// <summary>
    /// English-name-only index (matches the convention in <c>HarvestQuestDialogs.npcNameLookup</c>).
    /// Aliases are author-curated, so enforcing a single canonical language prevents subtle
    /// mismatches when an NPC has differently-spelled names across locales.
    /// </summary>
    private static Dictionary<string, List<uint>> BuildAliasNameIndex(
        Dictionary<uint, Dictionary<string, string>> npcNames,
        Dictionary<uint, Dictionary<string, string>> bnpcNames)
    {
        var nameToIds = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
        const uint bnpcIdOffset = 0x40000000;
        void Index(uint id, Dictionary<string, string> names)
        {
            if (!names.TryGetValue("en", out var enName) || string.IsNullOrEmpty(enName)) return;
            var norm = enName.Replace(" ", "").Replace("'", "").Replace("-", "").ToUpperInvariant();
            if (!nameToIds.TryGetValue(norm, out var ids))
                nameToIds[norm] = ids = new List<uint>();
            if (!ids.Contains(id)) ids.Add(id);
        }
        foreach (var (id, nm) in npcNames) Index(id, nm);
        foreach (var (id, nm) in bnpcNames) Index(id + bnpcIdOffset, nm);
        return nameToIds;
    }

    private QuestNpcAliasFile? LoadAliasFileFromEmbedded(EKEventId eventId)
    {
        try
        {
            using var stream = typeof(DialogHarvestService).Assembly
                .GetManifestResourceStream("Echokraut.Resources.QuestNpcAliases.json");
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<QuestNpcAliasFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadUserAliases),
                $"Failed to read embedded QuestNpcAliases.json: {ex.Message}", eventId);
            return null;
        }
    }

    private QuestNpcAliasFile? LoadAliasFileFromRemote(EKEventId eventId)
    {
        var url = _remoteUrls.Urls.QuestNpcAliasesUrl;
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = http.GetStringAsync(url).GetAwaiter().GetResult();
            return JsonSerializer.Deserialize<QuestNpcAliasFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadUserAliases),
                $"Failed to fetch remote quest NPC aliases ({url}): {ex.Message} — using embedded+local only",
                eventId);
            return null;
        }
    }

    private QuestNpcAliasFile? LoadAliasFileFromLocal(EKEventId eventId, out string path)
    {
        var baseDir = _config.SaveToLocal ? _config.LocalSaveLocation : @"C:\alltalk_tts";
        path = Path.Combine(baseDir, "harvest", "quest_npc_aliases.json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<QuestNpcAliasFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadUserAliases),
                $"Failed to parse {path}: {ex.Message} — local aliases ignored", eventId);
            return null;
        }
    }

    private void ApplyAliasEntries(
        QuestNpcAliasFile? file,
        string sourceLabel,
        Dictionary<(uint questId, string npcNameKey), uint> result,
        Dictionary<string, List<uint>> nameToIds,
        EKEventId eventId)
    {
        if (file?.Aliases == null) return;

        var loaded = 0;
        var overridden = 0;
        var skippedUnknown = 0;
        foreach (var entry in file.Aliases)
        {
            if (string.IsNullOrEmpty(entry.NpcNameKey)) continue;

            uint? resolved = null;
            if (entry.NpcId.HasValue && entry.NpcId.Value > 0)
            {
                resolved = entry.NpcId.Value;
            }
            else if (!string.IsNullOrEmpty(entry.NpcName))
            {
                var norm = entry.NpcName.Replace(" ", "").Replace("'", "").Replace("-", "").ToUpperInvariant();
                if (nameToIds.TryGetValue(norm, out var ids))
                {
                    // Take the first match. All NPCs with the same (name, gender, race, language)
                    // funnel into the same DB character row anyway, so "which specific NpcId"
                    // only matters for per-spawn npc_base_id tracking — picking any one works
                    // for voice attribution. Caller can set an explicit NpcId if they need a
                    // specific instance (rare).
                    resolved = ids[0];
                    if (ids.Count > 1)
                    {
                        _log.Debug(nameof(LoadUserAliases),
                            $"[{sourceLabel}] Quest {entry.QuestId} alias '{entry.NpcNameKey}' → name '{entry.NpcName}' " +
                            $"matches {ids.Count} NPCs [{string.Join(",", ids)}], using first ({ids[0]})",
                            eventId);
                    }
                }
                else
                {
                    _log.Warning(nameof(LoadUserAliases),
                        $"[{sourceLabel}] Quest {entry.QuestId} alias '{entry.NpcNameKey}' → name '{entry.NpcName}' " +
                        $"not found among English NPC names — entry ignored (use the English name)", eventId);
                    skippedUnknown++;
                }
            }

            if (resolved.HasValue)
            {
                var k = (entry.QuestId, entry.NpcNameKey.ToUpperInvariant());
                if (result.ContainsKey(k)) overridden++;
                result[k] = resolved.Value;
                loaded++;
            }
        }

        _log.Info(nameof(LoadUserAliases),
            $"[{sourceLabel}] {loaded} aliases applied ({overridden} overrides, {skippedUnknown} unknown)",
            eventId);
    }

    // ── Silent-actor paren-prefix heuristic ──────────────────────────────────────
    // FFXIV Lua cutscenes pre-spawn ALL their ACTORs at scene start (default position, no model
    // change). When a scene opens with "(-???-)Hello" or "(-Sylvie-)..." (text-side speaker hint)
    // and the bytecode-resolution priorities (1..5 in BuildLuaTextKeyMapping) couldn't pin down
    // an ACTOR, the speaker is almost always the actor that:
    //   - hasn't been resolved to any prior call in the *same scene/function*, and
    //   - does speak in at least one *later* call in the same scene/function.
    // 1 such actor → auto-attribute. ≥2 → emit a candidate JSON entry for manual disambiguation.
    // Trigger is purely text-based (starts with "(-X-)") — independent of NpcNameKey value.
    private static readonly Regex ParenSpeakerPrefixRegex = new(@"^\(-[^-]+-\)", RegexOptions.Compiled);

    internal static bool HasParenSpeakerPrefix(Dictionary<string, string> texts)
    {
        foreach (var text in texts.Values)
            if (!string.IsNullOrEmpty(text) && ParenSpeakerPrefixRegex.IsMatch(text))
                return true;
        return false;
    }

    /// <summary>
    /// Build a small window of surrounding dialog lines for a paren-prefix entry: 1 preceding
    /// line and up to 3 following lines from the SAME Lua function. Each entry includes the
    /// resolved speaker (when known via <paramref name="luaTextKeyToNpcId"/>). The user uses this
    /// context to identify who speaks the unresolved <c>(-...-)</c> line.
    /// </summary>
    private static List<ParenContextEntry> BuildContextWindow(
        string textKey,
        LuaQuestMapping luaMapping,
        Dictionary<string, uint> luaTextKeyToNpcId,
        Dictionary<string, Dictionary<string, string>> keyTexts,
        IList<string> orderedKeys,
        Dictionary<uint, Dictionary<string, string>> npcNames)
    {
        // Pick the ordering source: Lua scene order is most accurate (groups by FunctionIndex,
        // strict bytecode-linear ordering). For quests without Lua data (no .luab or no
        // QuestParams ACTORn), fall back to dialog-sheet row order — less precise (mixes
        // multiple scenes) but still useful for surrounding-text identification.
        List<string>? orderingFromLua = null;
        var firstCall = luaMapping.TalkCalls.FirstOrDefault(c => c.TextKey == textKey);
        if (firstCall != null)
        {
            orderingFromLua = luaMapping.TalkCalls
                .Where(c => c.FunctionIndex == firstCall.FunctionIndex)
                .Select(c => c.TextKey)
                .ToList();
        }

        var ordering = orderingFromLua ?? orderedKeys.ToList();
        var idx = ordering.IndexOf(textKey);
        if (idx < 0) return new List<ParenContextEntry>();

        var result = new List<ParenContextEntry>();
        var positions = new (int offset, string label)[]
        {
            (-1, "before"),
            (1, "after+1"),
            (2, "after+2"),
            (3, "after+3"),
        };
        foreach (var (offset, label) in positions)
        {
            var ni = idx + offset;
            if (ni < 0 || ni >= ordering.Count) continue;
            var ncKey = ordering[ni];
            if (!keyTexts.TryGetValue(ncKey, out var ncTexts)) continue;
            luaTextKeyToNpcId.TryGetValue(ncKey, out var npcId);
            result.Add(new ParenContextEntry
            {
                Position = label,
                TextKey = ncKey,
                Texts = ncTexts,
                NpcId = npcId,
                NpcNames = npcId != 0 && npcNames.TryGetValue(npcId, out var nm) ? nm : new Dictionary<string, string>(),
            });
        }
        return result;
    }

    /// <summary>
    /// Per-scene candidate analysis. Returns the ACTOR NPC IDs that satisfy the silent-before /
    /// speaks-after pattern within the SAME Lua function as the unresolved call.
    /// </summary>
    internal static List<uint> ComputeSilentActorCandidates(string textKey, LuaQuestMapping luaMapping)
    {
        if (luaMapping.TalkCalls.Count == 0 || luaMapping.ActorNameToNpcId.Count == 0)
            return new List<uint>();

        var firstCall = luaMapping.TalkCalls.FirstOrDefault(c => c.TextKey == textKey);
        if (firstCall == null) return new List<uint>();

        var sceneCalls = luaMapping.TalkCalls
            .Where(c => c.FunctionIndex == firstCall.FunctionIndex)
            .ToList();
        var unresolvedIdx = sceneCalls.IndexOf(firstCall);
        if (unresolvedIdx < 0) return new List<uint>();

        var candidates = new List<uint>();
        var actorIds = luaMapping.ActorNameToNpcId.Values.Distinct().ToList();
        foreach (var actorId in actorIds)
        {
            var silentBefore = true;
            var speaksAfter = false;
            for (var i = 0; i < sceneCalls.Count; i++)
            {
                if (i == unresolvedIdx) continue;
                if (!luaMapping.TextKeyToNpcId.TryGetValue(sceneCalls[i].TextKey, out var resolved)) continue;
                if (resolved != actorId) continue;
                if (i < unresolvedIdx) { silentBefore = false; break; }
                speaksAfter = true;
            }
            if (silentBefore && speaksAfter)
                candidates.Add(actorId);
        }
        return candidates;
    }

    // Text key pattern: TEXT_{QUESTID}_{NPCNAME}_{SCENE}_{LINE}
    private static readonly Regex QuestTextKeyRegex = new(
        @"^TEXT_[A-Za-z0-9]+_(\d+)_([A-Z0-9]+)_\d+_\d+$",
        RegexOptions.Compiled);

    // Alternate pattern without leading type prefix: TEXT_{QUESTID}_{NPCNAME}_{SCENE}_{LINE}
    // The quest ID part is variable length, NPC name is uppercase letters/digits
    private static string? ExtractNpcNameFromKey(string key, string questId)
    {
        // Key format: TEXT_{QUESTID}_{NPCNAME}_{SCENE}_{LINE}
        // questId is like "ManFst404_00519" → uppercase in key: "MANFST404_00519"
        var prefix = $"TEXT_{questId.ToUpperInvariant()}_";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var remainder = key[prefix.Length..]; // "NPCNAME_000_1"
        var parts = remainder.Split('_');
        // Need at least 3 parts: NPCNAME, scene, line
        if (parts.Length < 3) return null;

        // NPC name is everything before the last two parts (scene + line)
        var npcNameParts = parts[..^2];
        return string.Join("_", npcNameParts);
    }

    // Strip trailing instance IDs and known suffixes from NPC keys:
    // FORTEMPSGUARD00054 → FORTEMPSGUARD, ROAILLE_BATTLETALK → ROAILLE,
    // INVESTIGATORA00043 → INVESTIGATOR, FORTEMPSGUARD00054_Q1 → FORTEMPSGUARD
    private static readonly Regex NpcKeySuffixRegex = new(
        @"(_BATTLETALK|_[QA]\d+|[A-Z]?\d{3,}|MOB\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string NormalizeNpcKey(string npcNameKey)
    {
        var normalized = npcNameKey;

        // Strip known suffixes BEFORE removing underscores (so _BATTLETALK, _Q1, _A1 are matched)
        for (var i = 0; i < 3; i++)
        {
            var stripped = NpcKeySuffixRegex.Replace(normalized, "");
            if (stripped == normalized || stripped.Length < 3) break;
            normalized = stripped;
        }

        // Remove remaining underscores after suffix stripping
        normalized = normalized.Replace("_", "");

        return normalized;
    }

    private (List<LinkedQuestDialog> linked, List<UnmatchedQuestDialog> unmatched, List<ParenCandidateEntry> parenCandidates) HarvestQuestDialogs(
        Dictionary<uint, Dictionary<string, string>> npcNames,
        Dictionary<uint, Dictionary<string, string>> bnpcNames,
        ExcelSheet<ENpcBase> npcBaseSheet,
        Dictionary<uint, HashSet<uint>> npcTerritories,
        Dictionary<uint, string> npcZoneNames,
        Dalamud.Game.ClientLanguage language,
        CancellationToken ct,
        EKEventId eventId,
        int? questTypeFilter = null)
    {
        // Pre-resolve the QuestType enum filter once. >0 means user picked a specific quest type.
        QuestType? requiredQuestType = questTypeFilter is > 0 ? (QuestType)questTypeFilter.Value : null;
        var linked = new List<LinkedQuestDialog>();
        var unmatched = new List<UnmatchedQuestDialog>();
        var parenCandidates = new List<ParenCandidateEntry>();

        // User-supplied per-quest aliases — priority-1 override (wins over Lua mapping etc.)
        var userAliases = LoadUserAliases(npcNames, bnpcNames, eventId);

        // Load localized PlaceName sheet for quest zone fallback
        var placeNameSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>(language);

        // Build NPC name lookup from ENpcResident English names
        var npcNameLookup = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (npcId, names) in npcNames)
        {
            if (names.TryGetValue("en", out var enName) && !string.IsNullOrEmpty(enName))
            {
                var normalized = enName.Replace(" ", "").Replace("'", "").Replace("-", "").ToUpperInvariant();
                if (!npcNameLookup.TryGetValue(normalized, out var ids))
                {
                    ids = new List<uint>();
                    npcNameLookup[normalized] = ids;
                }
                ids.Add(npcId);
            }
        }

        // Also add BNpcName entries (battle NPCs like "Miraudont the Madder")
        // Use high IDs (offset by 0x40000000) to distinguish from ENpc IDs
        const uint bnpcIdOffset = 0x40000000;
        foreach (var (bnpcId, names) in bnpcNames)
        {
            if (names.TryGetValue("en", out var enName) && !string.IsNullOrEmpty(enName))
            {
                var normalized = enName.Replace(" ", "").Replace("'", "").Replace("-", "").ToUpperInvariant();
                if (!npcNameLookup.TryGetValue(normalized, out var ids))
                {
                    ids = new List<uint>();
                    npcNameLookup[normalized] = ids;
                }
                ids.Add(bnpcId + bnpcIdOffset);
            }
        }

        // Read all quests
        var questSheet = _dataManager.GetExcelSheet<Quest>();
        if (questSheet == null) return (linked, unmatched, parenCandidates);

        var questCount = 0;
        var questTotal = questSheet.Count();
        var questsWithDialog = 0;
        var skippedVoiced = 0;

        // SCD-language code used by the voiced-filter below. Mirrors HarvestCutsceneDialogs:
        // a quest dialog row whose audio file actually ships in the harvest language is
        // dropped because FFXIV already plays its own voice acting for that line — there's
        // nothing for Echokraut TTS to fill in. Only the silent residue gets harvested.
        var scdLangCode = VoiceScdPaths.LanguageCodeForScd(language);

        BeginPhase(Loc.S("Scanning for quest dialogs..."), questTotal);
        _log.Info(nameof(HarvestQuestDialogs),
            $"Beginning quest scan: {questTotal} quests to process", eventId);

        foreach (var quest in questSheet)
        {
            ct.ThrowIfCancellationRequested();
            questCount++;
            // Periodic progress so the harvest doesn't go silent for minutes during the loop.
            if (questCount % 500 == 0)
                _log.Info(nameof(HarvestQuestDialogs),
                    $"Quest scan progress: {questCount}/{questTotal} ({linked.Count} linked so far)",
                    eventId);

            var questId = quest.Id.ExtractText();
            if (string.IsNullOrEmpty(questId)) continue;

            var questName = quest.Name.ExtractText();
            if (string.IsNullOrEmpty(questName)) continue;

            var questType = ClassifyQuest(quest);

            ReportPhaseProgress(questCount);

            // Single-quest-type filter: skip everything that doesn't match the user's selection.
            if (requiredQuestType is { } req && questType != req)
                continue;

            // Compute sheet path: quest/{subdir}/{questId}
            // FFXIV organizes quest EXDs in folders of 100 (not 1000)
            var suffix = quest.RowId - 65536;
            var subdir = (suffix / 100).ToString("D3");
            var sheetPath = $"quest/{subdir}/{questId}";

            // Parse Lua script to get textKey → actor register mapping (plus the raw Talk()
            // calls and ACTOR table — needed for the silent-actor paren-prefix heuristic below).
            var luaMapping = BuildLuaTextKeyMapping(quest, questId, subdir, eventId);
            var luaTextKeyToNpcId = luaMapping.TextKeyToNpcId;
            // Track keys auto-resolved by the silent-actor heuristic so the main loop can label
            // them with the correct MatchSource (instead of plain LuaScript).
            var heuristicResolvedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect all text keys and their translations
            var keyTexts = new Dictionary<string, Dictionary<string, string>>(); // textKey → lang → text

            for (var langIdx = 0; langIdx < LangCodes.Length; langIdx++)
            {
                var langCode = LangCodes[langIdx];
                var lang = LangValues[langIdx];

                ExcelSheet<RawRow>? dialogSheet;
                try
                {
                    dialogSheet = _dataManager.GetExcelSheet<RawRow>(lang, sheetPath);
                }
                catch
                {
                    continue;
                }
                if (dialogSheet == null) continue;

                foreach (var row in dialogSheet)
                {
                    try
                    {
                        var key = row.ReadStringColumn(0).ExtractText();
                        if (string.IsNullOrEmpty(key) || !key.StartsWith("TEXT_")) continue;

                        var text = ExtractTextWithPlayerName(row.ReadStringColumn(1), langCode);
                        if (string.IsNullOrEmpty(text)) continue;

                        if (!keyTexts.TryGetValue(key, out var langDict))
                        {
                            // First sighting of this textKey across the 4-language scan — apply
                            // the voiced filter once, on the harvest (= client) language. If the
                            // text-key parses to an audioBase that has a matching SCD audio file,
                            // FFXIV already voices the line and we drop it for ALL languages of
                            // this textKey (the user spec: "only client language matters").
                            if (VoiceExtractKey.TryParse(key, out _, out var audioBase)
                                && VoiceScdPaths.Exists(_dataManager, audioBase, scdLangCode))
                            {
                                skippedVoiced++;
                                continue;
                            }
                            langDict = new Dictionary<string, string>();
                            keyTexts[key] = langDict;
                        }
                        langDict[langCode] = text;
                    }
                    catch
                    {
                        // Skip rows that can't be parsed
                    }
                }
            }

            questsWithDialog += keyTexts.Count;
            // Cache the key ordering once per quest — used as a fallback ordering source in
            // BuildContextWindow when Lua data is missing. Computed lazily so quests without
            // any unmatched paren-prefix entries don't pay the allocation.
            List<string>? cachedOrderedKeys = null;

            // Silent-actor paren-prefix heuristic — runs ONCE before the main match loop.
            // 1-candidate cases are auto-attributed (flow through the existing LuaScript path).
            // 0/≥2-candidate cases are remembered so the main loop can emit a candidates JSON
            // entry IF the key ends up unmatched (entries that get rescued by name-fallback
            // matching later are NOT emitted — they're correctly attributed).
            var paramCandidatesPerKey = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (paKey, paTexts) in keyTexts)
            {
                if (luaTextKeyToNpcId.ContainsKey(paKey)) continue;
                if (!HasParenSpeakerPrefix(paTexts)) continue;

                var candidates = ComputeSilentActorCandidates(paKey, luaMapping);
                if (candidates.Count == 1)
                {
                    luaTextKeyToNpcId[paKey] = candidates[0];
                    heuristicResolvedKeys.Add(paKey);
                }
                else
                {
                    // Defer JSON emission — this key may still be matched by name-fallback in
                    // the main loop. paramCandidatesPerKey tracks the candidate list so the
                    // unmatched branch can pick it up if the key really stays unmatched.
                    paramCandidatesPerKey[paKey] = candidates;
                }
            }

            // Process collected text entries
            foreach (var (key, texts) in keyTexts)
            {
                // Apply same filters as DefaultTalk
                if (texts.Values.All(v => v.Length <= 1)) continue;
                if (texts.Values.Where(v => !string.IsNullOrEmpty(v)).Any(v => PlaceholderTexts.Contains(v.Trim()))) continue;
                var nonEmpty = texts.Values.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
                if (nonEmpty.Count == 1) continue;
                var lengths = texts.Values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v.Length).ToList();
                if (lengths.Count >= 2 && lengths.Min() > 0 && lengths.Max() > lengths.Min() * 5) continue;

                // Extract NPC name from key
                var npcNameKey = ExtractNpcNameFromKey(key, questId);
                if (string.IsNullOrEmpty(npcNameKey)) continue;

                // Skip non-dialog keys
                if (npcNameKey.Length <= 2) continue;
                if (Regex.IsMatch(npcNameKey, @"^(ACCESS|GUIDE|CAPTION|CUTSCENE|SEQ|TODO|INSTANCE_BATTLE.*)$"))
                    continue;

                // SYSTEM / QIB / POPMESSAGE entries are narrator dialog
                if (npcNameKey.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
                    || npcNameKey.StartsWith("QIB", StringComparison.OrdinalIgnoreCase)
                    || npcNameKey.Equals("POPMESSAGE", StringComparison.OrdinalIgnoreCase))
                {
                    linked.Add(new LinkedQuestDialog
                    {
                        QuestId = quest.RowId,
                        QuestName = questName,
                        NpcNameKey = "SYSTEM",
                        NpcId = 0,
                        NpcName = new Dictionary<string, string>
                            { { "en", "Narrator" }, { "de", "Erzähler" }, { "ja", "ナレーター" }, { "fr", "Narrateur" } },
                        Race = "Unknown",
                        Gender = Genders.None.ToString(),
                        MatchSource = DialogMatchSource.Narrator.ToString(),
                        QuestType = questType,
                        Texts = texts
                    });
                    continue;
                }

                List<uint>? matchedIds = null;
                var matchSource = DialogMatchSource.NameExact;

                // Priority 1a: user-supplied per-quest alias (most specific — wins everything).
                // Priority 1b: user-supplied global alias (QuestId=0 — same NpcNameKey always
                // maps to the same NPC across quests, e.g. ORTHRUS → Ultros).
                var npcKeyUpper = npcNameKey.ToUpperInvariant();
                if (userAliases.TryGetValue((quest.RowId, npcKeyUpper), out var userNpcId)
                    || userAliases.TryGetValue((0u, npcKeyUpper), out userNpcId))
                {
                    matchedIds = new List<uint> { userNpcId };
                    matchSource = DialogMatchSource.UserAlias;
                }

                // Priority 2: Lua script mapping (5 sub-priorities) or silent-actor heuristic.
                if (matchedIds == null && luaTextKeyToNpcId.TryGetValue(key, out var luaNpcId))
                {
                    matchedIds = new List<uint> { luaNpcId };
                    matchSource = heuristicResolvedKeys.Contains(key)
                        ? DialogMatchSource.SilentActorHeuristic
                        : DialogMatchSource.LuaScript;
                }

                // Check cutscene NPC alias map (for dialog played by native cutscene system)
                if (matchedIds == null && CutsceneNpcAliases.TryGetValue(npcNameKey, out var aliasNpcId))
                {
                    matchedIds = new List<uint> { aliasNpcId };
                    matchSource = DialogMatchSource.Direct;
                }

                // Fall back to name-based multi-stage matching
                // (needed for quests without Lua scripts or where other methods don't apply)
                if (matchedIds == null)
                {
                    var normalizedNpcKey = NormalizeNpcKey(npcNameKey);
                    (matchedIds, matchSource) = ResolveNpcIds(normalizedNpcKey, npcNameLookup, npcTerritories, quest.PlaceName.RowId);
                }

                if (matchedIds != null)
                {
                    var rawId = matchedIds[0];
                    var isBNpc = rawId >= bnpcIdOffset;
                    var npcId = isBNpc ? rawId - bnpcIdOffset : rawId;
                    var raceStr = "Unknown";
                    var gender = Genders.None;
                    Dictionary<string, string> matchedNames;
                    var finalSource = isBNpc ? DialogMatchSource.BNpc : matchSource;

                    if (isBNpc)
                    {
                        // BNpcName → BNpcBase mapping doesn't exist in sheet data
                        // (linked only at runtime via spawn tables), so race/gender stays Unknown
                        matchedNames = bnpcNames.TryGetValue(npcId, out var bn) ? bn : new Dictionary<string, string>();
                    }
                    else
                    {
                        var npcBase = npcBaseSheet.GetRowOrDefault(npcId);
                        if (npcBase is { } nb)
                        {
                            raceStr = GetRaceString(nb);
                            var race = ParseNpcRace(raceStr);
                            gender = NpcIdentityHelper.DetermineGender(nb, race, _jsonData.ModelGenderMap);
                        }
                        matchedNames = npcNames.TryGetValue(npcId, out var en) ? en : new Dictionary<string, string>();
                    }

                    // Use quest PlaceName as fallback zone for NPCs not in the Level sheet
                    if (!npcZoneNames.ContainsKey(npcId) && placeNameSheet != null)
                    {
                        var placeRow = placeNameSheet.GetRowOrDefault(quest.PlaceName.RowId);
                        var questPlaceName = placeRow?.Name.ExtractText();
                        if (!string.IsNullOrEmpty(questPlaceName))
                            npcZoneNames[npcId] = questPlaceName;
                    }

                    linked.Add(new LinkedQuestDialog
                    {
                        QuestId = quest.RowId,
                        QuestName = questName,
                        NpcNameKey = npcNameKey,
                        NpcId = npcId,
                        NpcName = matchedNames,
                        Race = raceStr,
                        Gender = gender.ToString(),
                        MatchSource = finalSource.ToString(),
                        QuestType = questType,
                        Texts = texts
                    });
                }
                else
                {
                    unmatched.Add(new UnmatchedQuestDialog
                    {
                        QuestId = quest.RowId,
                        QuestName = questName,
                        NpcNameKey = npcNameKey,
                        Texts = texts
                    });

                    // Paren-prefix entries that stayed unmatched: emit a candidates JSON entry
                    // so the user can manually map them. Candidates = heuristic narrow-down
                    // (may be empty); AllActors = the full QuestParams ACTOR list so the user
                    // can pick any cutscene cast member when the heuristic didn't help.
                    if (HasParenSpeakerPrefix(texts))
                    {
                        var heuristicCandidates = paramCandidatesPerKey.TryGetValue(key, out var hc) ? hc : new List<uint>();
                        var allActorIds = luaMapping.ActorNameToNpcId.Values.Distinct().ToList();
                        cachedOrderedKeys ??= keyTexts.Keys.ToList();
                        var context = BuildContextWindow(key, luaMapping, luaTextKeyToNpcId, keyTexts, cachedOrderedKeys, npcNames);
                        parenCandidates.Add(new ParenCandidateEntry
                        {
                            QuestId = quest.RowId,
                            QuestName = questName,
                            TextKey = key,
                            NpcNameKey = npcNameKey,
                            Texts = texts,
                            Candidates = heuristicCandidates.Select(id => new ParenCandidateOption
                            {
                                NpcId = id,
                                Names = npcNames.TryGetValue(id, out var nm) ? nm : new Dictionary<string, string>()
                            }).ToList(),
                            AllActors = allActorIds.Select(id => new ParenCandidateOption
                            {
                                NpcId = id,
                                Names = npcNames.TryGetValue(id, out var nm) ? nm : new Dictionary<string, string>()
                            }).ToList(),
                            Context = context,
                        });
                    }
                }
            }
        }

        _log.Info(nameof(HarvestQuestDialogs),
            $"Quest harvest: {linked.Count} linked, {unmatched.Count} unmatched, " +
            $"{questCount} quests scanned ({questsWithDialog} text entries), " +
            $"{skippedVoiced} voiced lines skipped (FFXIV ships dub for them), " +
            $"{linked.Count(d => d.MatchSource == DialogMatchSource.SilentActorHeuristic.ToString())} via silent-actor heuristic, " +
            $"{parenCandidates.Count} multi-candidate paren-prefix entries needing manual mapping",
            eventId);

        EndPhase();
        return (linked, unmatched, parenCandidates);
    }

    // ── Cut_scene dialog harvest ─────────────────────────────────────────────
    /// <summary>
    /// Harvest cutscene voice text from the <c>cut_scene/*</c> Excel sheets that FFXIV uses
    /// for cinematic dialog. Each row's TEXT key (e.g. <c>TEXT_VOICEMAN_06006_000010_YSHTOLA</c>)
    /// gets parsed via <see cref="VoiceExtractKey.TryParse"/>, the trailing shortname
    /// resolved to an NPC ID via the existing English name index, and the multilingual text
    /// extracted (with macro / placeholder handling).
    /// <para>
    /// <b>Filter — only unvoiced lines are kept.</b> If <see cref="VoiceScdPaths.Exists"/>
    /// reports an SCD audio file for the harvest language, the line is skipped — FFXIV
    /// already plays its own voice acting and Echokraut should not compete with TTS for
    /// content that's already voiced. The starter-set extractor consumes those voiced lines
    /// for AllTalk training; the harvest captures only the silent residue.
    /// </para>
    /// <para>
    /// QuestType is fixed to <see cref="QuestType.None"/> — we don't parse the .cutb
    /// timeline files, so we can't reliably tell which quest a cutscene belongs to. None
    /// keeps these lines visible under the "Non-Quest Dialog" filter and out of single
    /// quest-type filters where the tagging would be a guess.
    /// </para>
    /// </summary>
    private List<LinkedDialog> HarvestCutsceneDialogs(
        Dictionary<uint, Dictionary<string, string>> npcNames,
        ExcelSheet<ENpcBase> npcBaseSheet,
        Dalamud.Game.ClientLanguage harvestLanguage,
        CancellationToken ct,
        EKEventId eventId)
    {
        var result = new List<LinkedDialog>();
        var nameIndex = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        var scdLangCode = VoiceScdPaths.LanguageCodeForScd(harvestLanguage);
        var harvestLangCode = LangToCode.GetValueOrDefault(harvestLanguage, "en");

        IReadOnlyList<string>? sheetNames = null;
        try { sheetNames = _dataManager.Excel.SheetNames; }
        catch (Exception ex)
        {
            _log.Warning(nameof(HarvestCutsceneDialogs),
                $"Could not enumerate Excel sheet names: {ex.Message}", eventId);
            return result;
        }
        var cutSheets = sheetNames
            .Where(n => n.StartsWith("cut_scene/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (cutSheets.Count == 0)
        {
            _log.Info(nameof(HarvestCutsceneDialogs), "No cut_scene/* sheets found.", eventId);
            return result;
        }

        BeginPhase(Loc.S("Harvesting cutscene dialogs..."), Math.Max(1, cutSheets.Count));
        _log.Info(nameof(HarvestCutsceneDialogs),
            $"Scanning {cutSheets.Count} cut_scene/* sheets for unvoiced lines (lang={scdLangCode})", eventId);

        var processed = 0;
        var voicedSkipped = 0;
        var unmatchedSkipped = 0;
        var unparsedSkipped = 0;
        var noneVoiceSkipped = 0;
        var added = 0;
        var multiNpcWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Diagnostics: track which TEXT_-key shapes we reject so we can spot when the harvest
        // is silently dropping a whole class of unvoiced lines (e.g. an unrecognized prefix).
        // Prefix = first two underscore tokens (e.g. "TEXT_NONVOICEMAN"), value = (count, sample).
        var unparsedPrefixes = new Dictionary<string, (int count, string sampleKey, string sampleText)>(StringComparer.OrdinalIgnoreCase);
        string? noneVoiceSample = null;
        string? noneVoiceSampleText = null;

        foreach (var sheetName in cutSheets)
        {
            ct.ThrowIfCancellationRequested();
            processed++;
            ReportPhaseProgress(processed);

            // Open the sheet in all 4 languages so we can capture the multilingual text
            // payload at the moment we resolve the row. Missing-locale entries fall through
            // as empty.
            var byLang = new Dictionary<string, ExcelSheet<RawRow>?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < LangCodes.Length; i++)
            {
                try { byLang[LangCodes[i]] = _dataManager.GetExcelSheet<RawRow>(LangValues[i], sheetName); }
                catch { byLang[LangCodes[i]] = null; }
            }

            var primary = byLang.GetValueOrDefault(harvestLangCode);
            if (primary == null) continue;

            foreach (var primaryRow in primary)
            {
                ct.ThrowIfCancellationRequested();

                string textKey;
                try { textKey = primaryRow.ReadStringColumn(0).ExtractText(); }
                catch { continue; }
                if (string.IsNullOrEmpty(textKey)) continue;

                if (!VoiceExtractKey.TryParse(textKey, out var shortName, out var audioBase))
                {
                    // Special case: TEXT_VOICEMAN_<scene>_..._NONE_VOICE is FFXIV's explicit
                    // "unvoiced cutscene line, no speaker info in key" marker. The actual
                    // speaker for these lines lives in the .cutb timeline file (which we
                    // don't parse). They're real unvoiced lines that we'd want to harvest
                    // — but without speaker attribution they'd land as orphan rows. Surface
                    // them in the diagnostic separately so the user knows how much .cutb
                    // parsing would unlock.
                    // TODO: Implement .cutb timeline parsing so these get attributed and
                    // persisted instead of dropped — see plans/cutb-parser.md (Havok packfile
                    // reader; ~85% of unvoiced cutscene dialog gated behind this).
                    if (textKey.EndsWith("_NONE_VOICE", StringComparison.OrdinalIgnoreCase))
                    {
                        noneVoiceSkipped++;
                        if (noneVoiceSample == null)
                        {
                            noneVoiceSample = textKey;
                            try { noneVoiceSampleText = primaryRow.ReadStringColumn(1).ExtractText() ?? ""; }
                            catch { /* best effort */ }
                        }
                        continue;
                    }

                    unparsedSkipped++;
                    // Capture the shape (first two tokens) + a sample for the end-of-run log.
                    // Helps identify whether the unparsed pile contains a coherent class of
                    // keys we should add to TryParse (a different prefix for unvoiced lines,
                    // a 6-token quest-name format, etc.) versus genuine garbage.
                    var firstUnderscore = textKey.IndexOf('_');
                    var secondUnderscore = firstUnderscore >= 0 ? textKey.IndexOf('_', firstUnderscore + 1) : -1;
                    var prefix = secondUnderscore > 0 ? textKey.Substring(0, secondUnderscore) : textKey;
                    if (!unparsedPrefixes.TryGetValue(prefix, out var existing))
                    {
                        string sampleText = string.Empty;
                        try { sampleText = primaryRow.ReadStringColumn(1).ExtractText() ?? ""; }
                        catch { /* best effort */ }
                        if (sampleText.Length > 80) sampleText = sampleText.Substring(0, 80) + "…";
                        unparsedPrefixes[prefix] = (1, textKey, sampleText);
                    }
                    else
                    {
                        unparsedPrefixes[prefix] = (existing.count + 1, existing.sampleKey, existing.sampleText);
                    }
                    continue;
                }

                // Voiced-line filter: skip anything FFXIV already plays in this locale.
                // Checking just the harvest language is intentional — voiced-in-JP-only lines
                // are silent for a German user and SHOULD get TTS-harvested for them.
                if (VoiceScdPaths.Exists(_dataManager, audioBase, scdLangCode))
                {
                    voicedSkipped++;
                    continue;
                }

                // Shortname → NPC. We deliberately don't use the alias map here; cutscene
                // shortnames overlap with the voice-extractor's input set and the alias map
                // already maps them through the same name-index path.
                var resolved = VoiceExtractKey.Resolve(shortName, nameIndex);
                if (resolved == null || resolved.Count == 0)
                {
                    unmatchedSkipped++;
                    continue;
                }
                var npcId = resolved[0];
                if (resolved.Count > 1 && multiNpcWarned.Add(shortName))
                    _log.Debug(nameof(HarvestCutsceneDialogs),
                        $"Cutscene shortname '{shortName}' maps to {resolved.Count} NPC instances — using first ({npcId})",
                        eventId);

                if (!npcNames.TryGetValue(npcId, out var names)) continue;

                string raceStr;
                Genders gender;
                try
                {
                    var npcBase = npcBaseSheet.GetRow(npcId);
                    raceStr = GetRaceString(npcBase);
                    var race = ParseNpcRace(raceStr);
                    gender = NpcIdentityHelper.DetermineGender(npcBase, race, _jsonData.ModelGenderMap);
                }
                catch
                {
                    // ENpcBase row missing for this id — without race/gender PersistLinkedDialogs
                    // would store as Unknown/None which would silently miss bestIdentity merging.
                    // Skip rather than persist garbage.
                    continue;
                }

                // Multilingual text via ExtractTextWithPlayerName so PcName / gstr / conditional
                // macros translate to the correct -PlayerFirstName- etc. placeholders.
                var texts = new Dictionary<string, string>();
                for (var i = 0; i < LangCodes.Length; i++)
                {
                    var lc = LangCodes[i];
                    var sheet = byLang.GetValueOrDefault(lc);
                    if (sheet == null) continue;
                    try
                    {
                        var langRow = sheet.GetRowOrDefault(primaryRow.RowId);
                        if (langRow == null) continue;
                        var t = ExtractTextWithPlayerName(langRow.Value.ReadStringColumn(1), lc);
                        if (!string.IsNullOrEmpty(t)) texts[lc] = t;
                    }
                    catch { /* per-locale failures are non-fatal */ }
                }

                if (texts.Count == 0) continue;

                result.Add(new LinkedDialog
                {
                    NpcId = npcId,
                    NpcName = names,
                    Race = raceStr,
                    Gender = gender.ToString(),
                    Sheet = sheetName,
                    DialogId = primaryRow.RowId,
                    MatchSource = DialogMatchSource.Direct.ToString(),
                    QuestType = QuestType.None,
                    Texts = texts,
                });
                added++;
            }
        }

        EndPhase();
        _log.Info(nameof(HarvestCutsceneDialogs),
            $"Cutscene harvest: {processed} sheets scanned, {added} unvoiced lines added, " +
            $"{voicedSkipped} voiced (skipped), {unmatchedSkipped} shortname unmatched, " +
            $"{noneVoiceSkipped} NONE_VOICE marker (no speaker in key — needs .cutb), " +
            $"{unparsedSkipped} other unparseable keys",
            eventId);

        if (noneVoiceSkipped > 0 && noneVoiceSample != null)
        {
            var sampleTextDisplay = noneVoiceSampleText ?? "";
            if (sampleTextDisplay.Length > 80) sampleTextDisplay = sampleTextDisplay.Substring(0, 80) + "…";
            _log.Info(nameof(HarvestCutsceneDialogs),
                $"NONE_VOICE sample: \"{noneVoiceSample}\" → \"{sampleTextDisplay}\". " +
                $"These {noneVoiceSkipped} lines have no speaker info in the TEXT key — attribution " +
                $"requires parsing the per-cutscene .cutb timeline (Havok packfile).",
                eventId);
        }

        // Diagnostic: top 10 unparsed key prefixes by count, with one sample key + text each.
        // If the dominant prefix is something other than TEXT_VOICEMAN/TEXT_MANFST it likely
        // means we're missing a class of keys that should also be parsed (e.g. silent-line
        // prefix or a 6-token format) — extend VoiceExtractKey.TryParse to cover them.
        if (unparsedPrefixes.Count > 0)
        {
            var top = unparsedPrefixes
                .OrderByDescending(kvp => kvp.Value.count)
                .Take(10)
                .ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Top unparsed cutscene-key prefixes ({unparsedPrefixes.Count} unique):");
            foreach (var (prefix, info) in top)
            {
                sb.AppendLine($"  {prefix}: {info.count}x — sample: \"{info.sampleKey}\" → \"{info.sampleText}\"");
            }
            _log.Info(nameof(HarvestCutsceneDialogs), sb.ToString().TrimEnd(), eventId);
        }

        return result;
    }

    // ── Voice-name suggestion emit ───────────────────────────────────────────
    /// <summary>
    /// Captures the inner token from a <c>(-Fakename-)</c> speaker hint at the start of a
    /// dialog line. Same shape as <see cref="ParenSpeakerPrefixRegex"/> but exposes a
    /// capture group so the harvester can pull the fakename out.
    /// </summary>
    internal static readonly Regex ParenSpeakerCaptureRegex = new(
        @"^\(-([^-]+)-\)", RegexOptions.Compiled);

    /// <summary>
    /// True when the captured fakename is an anonymous speaker marker (<c>???</c> / <c>?</c>
    /// / etc.) rather than an actual character hint. FFXIV uses these prefixes when the
    /// dialog box itself displays "???" — i.e. nobody knows who speaks YET. Persisting them
    /// as DB aliases would be catastrophic: every "???"-speaker NPC would write the same
    /// <c>(language, "???")</c> key, the alias-map cache would last-wins to a random
    /// character row, and the live runtime would map every future <c>???</c> speaker onto
    /// that random NPC. Filter them out at both the DB-persist and the JSON-suggestion sites.
    /// </summary>
    internal static bool IsAnonymousFakename(string fake)
    {
        if (string.IsNullOrWhiteSpace(fake)) return true;
        var span = fake.AsSpan().Trim();
        foreach (var ch in span)
            if (ch != '?') return false;
        return span.Length > 0;
    }

    /// <summary>
    /// Detect cases where a fakename string contains the name of a different NPC as a
    /// substring — strongly suggests the line is misattributed (FFXIV's DefaultTalk rows
    /// are often referenced by multiple ENpcBase rows, so the same line can land on
    /// several NPCs even though only one of them is the real speaker).
    ///
    /// Returns the list of OTHER NPC names that appear inside the fakename. Empty list
    /// → fakename looks clean. Names shorter than 4 chars are skipped to avoid trivial
    /// hits like "Al" matching everything.
    ///
    /// Internal+static for unit tests. Plain substring (no word-boundary) — German
    /// genitive forms like "Kriles" should still flag against "Krile". The downside
    /// is occasional false positives (e.g. "Yoshida" flagging against "Yoshi"); those
    /// land in the collisions JSON for manual review where the user can decide.
    /// </summary>
    internal static List<string> FindCollidingNames(
        string fakename,
        string currentName,
        IReadOnlyCollection<string> nameIndex)
    {
        var hits = new List<string>();
        if (string.IsNullOrEmpty(fakename) || nameIndex.Count == 0) return hits;
        foreach (var other in nameIndex)
        {
            if (string.IsNullOrEmpty(other) || other.Length < 4) continue;
            if (string.Equals(other, currentName, StringComparison.OrdinalIgnoreCase)) continue;
            if (fakename.IndexOf(other, StringComparison.OrdinalIgnoreCase) >= 0)
                hits.Add(other);
        }
        return hits;
    }

    /// <summary>
    /// Pure accumulator behind <see cref="EmitVoiceNameSuggestions"/> — no I/O, no logger,
    /// just <c>(NpcId, NpcName-per-lang, Text-per-lang) → per-language NpcName→Fakenames</c>.
    /// Exposed as <c>internal static</c> for unit tests (the outer method is private and
    /// touches <c>_config</c>/<c>_log</c>).
    /// </summary>
    internal static void AccumulateVoiceNameSuggestion(
        uint npcId,
        Dictionary<string, string> npcName,
        Dictionary<string, string> texts,
        IReadOnlyList<string> langCodes,
        Dictionary<string, Dictionary<string, HashSet<string>>> perLanguage)
    {
        if (npcId == 0) return;
        foreach (var lc in langCodes)
        {
            if (!texts.TryGetValue(lc, out var text) || string.IsNullOrEmpty(text)) continue;
            if (!npcName.TryGetValue(lc, out var name) || string.IsNullOrEmpty(name)) continue;

            var match = ParenSpeakerCaptureRegex.Match(text);
            if (!match.Success) continue;
            var fake = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(fake)) continue;

            // Anonymous "???" markers are kept in the DB alias table (the live runtime
            // disambiguates them via physical-presence + already-spoken tracking) but
            // skipped in the JSON suggestion output — that file is meant for community
            // PRs into VoiceNames{LANG}.json, where "???" entries have no meaning.
            if (IsAnonymousFakename(fake)) continue;

            // Skip if the fakename is just the NPC's own name — no aliasing value
            // (and no point cluttering the suggestion file with no-op entries).
            if (string.Equals(fake, name, StringComparison.OrdinalIgnoreCase)) continue;

            if (!perLanguage.TryGetValue(lc, out var byNpc))
                perLanguage[lc] = byNpc = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (!byNpc.TryGetValue(name, out var set))
                byNpc[name] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(fake);
        }
    }

    /// <summary>
    /// After every dialog has been linked to an NPC, scan the multilingual texts for
    /// <c>(-Fakename-)</c> speaker hints and emit one
    /// <c>voice_name_suggestions_&lt;lang&gt;.json</c> file per language. Each entry
    /// matches the <see cref="VoiceMap"/> schema so the user can copy lines straight into
    /// <c>VoiceNames{LANG}.json</c>:
    /// <code>
    /// [
    ///   { "voiceName": "Y'shtola Rhul", "speakers": ["Mysterious Lady"] },
    ///   { "voiceName": "Tataru Taru",   "speakers": ["Energetic Lalafell"] }
    /// ]
    /// </code>
    /// Filters: skip when the fakename equals the NPC name (no aliasing value), skip when
    /// either name or text is missing for the language, skip dialogs without a resolved
    /// NPC. Per (NPC, language) we accumulate a deduplicated set of fakenames.
    /// </summary>
    private void EmitVoiceNameSuggestions(
        List<LinkedDialog> linkedDialogs,
        List<LinkedQuestDialog> linkedQuests,
        EKEventId eventId)
    {
        // Per language: NpcName → set of distinct fakenames.
        var perLanguage = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
        // Per language: full set of NPC display names — used to detect "this fakename
        // contains another NPC's name" misattributions. Built from ALL linked dialogs
        // (not just those carrying a fakename) so collisions are detectable even when the
        // colliding NPC has no fakename suggestions of its own.
        var nameIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var lc in LangCodes)
        {
            perLanguage[lc] = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            nameIndex[lc] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        void IndexNames(Dictionary<string, string> names)
        {
            foreach (var lc in LangCodes)
                if (names.TryGetValue(lc, out var n) && !string.IsNullOrEmpty(n))
                    nameIndex[lc].Add(n);
        }

        foreach (var d in linkedDialogs)
        {
            AccumulateVoiceNameSuggestion(d.NpcId, d.NpcName, d.Texts, LangCodes, perLanguage);
            IndexNames(d.NpcName);
        }
        foreach (var d in linkedQuests)
        {
            AccumulateVoiceNameSuggestion(d.NpcId, d.NpcName, d.Texts, LangCodes, perLanguage);
            IndexNames(d.NpcName);
        }

        // Always write the suggestion + collision files (even if empty) so the user has a
        // stable set of paths to look for; an empty file is a clear "no findings this run"
        // signal.
        var baseDir = _config.SaveToLocal ? _config.LocalSaveLocation : @"C:\alltalk_tts";
        var outputDir = Path.Combine(baseDir, "harvest");
        try { Directory.CreateDirectory(outputDir); }
        catch (Exception ex)
        {
            _log.Warning(nameof(EmitVoiceNameSuggestions),
                $"Could not create harvest output dir '{outputDir}': {ex.Message}", eventId);
            return;
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var totalEntries = 0;
        var totalFakenames = 0;
        var totalCollisions = 0;
        foreach (var lc in LangCodes)
        {
            var cleanEntries = new List<VoiceNameSuggestion>();
            var collisionEntries = new List<VoiceNameCollision>();

            var langNames = nameIndex[lc];
            var byNpc = perLanguage[lc].OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var (currentName, fakenames) in byNpc)
            {
                if (fakenames.Count == 0) continue;
                var clean = new List<string>();
                foreach (var fake in fakenames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    var collisions = FindCollidingNames(fake, currentName, langNames);
                    if (collisions.Count > 0)
                    {
                        collisionEntries.Add(new VoiceNameCollision
                        {
                            Fakename = fake,
                            ResolvedAs = currentName,
                            LikelyMeantFor = collisions
                                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                                .ToList(),
                        });
                    }
                    else
                    {
                        clean.Add(fake);
                    }
                }
                if (clean.Count > 0)
                {
                    cleanEntries.Add(new VoiceNameSuggestion
                    {
                        voiceName = currentName,
                        speakers = clean,
                    });
                }
            }

            totalEntries += cleanEntries.Count;
            totalFakenames += cleanEntries.Sum(e => e.speakers.Count);
            totalCollisions += collisionEntries.Count;

            var suggestionsPath = Path.Combine(outputDir, $"voice_name_suggestions_{lc}.json");
            var collisionsPath = Path.Combine(outputDir, $"voice_name_collisions_{lc}.json");
            try
            {
                File.WriteAllText(suggestionsPath, JsonSerializer.Serialize(cleanEntries, jsonOptions));
                File.WriteAllText(collisionsPath, JsonSerializer.Serialize(collisionEntries, jsonOptions));
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(EmitVoiceNameSuggestions),
                    $"Failed to write voice-name suggestion / collision JSON for {lc}: {ex.Message}",
                    eventId);
            }
        }

        _log.Info(nameof(EmitVoiceNameSuggestions),
            $"Voice-name files written: {totalEntries} clean NPC entries / {totalFakenames} fakenames " +
            $"across 4 languages, {totalCollisions} collisions for manual review — under {outputDir}",
            eventId);
    }

    /// <summary>
    /// Build NPC ID → set of PlaceName IDs from the Level sheet (Type 8 = NPC placement).
    /// Also builds NPC ID → resolved zone name string for instance creation.
    /// </summary>
    private (Dictionary<uint, HashSet<uint>> territories, Dictionary<uint, string> zoneNames)
        BuildNpcTerritoryLookup(Dalamud.Game.ClientLanguage language, CancellationToken ct, EKEventId eventId)
    {
        var territories = new Dictionary<uint, HashSet<uint>>();
        var zoneNames = new Dictionary<uint, string>();
        var levelSheet = _dataManager.GetExcelSheet<Level>();
        if (levelSheet == null) return (territories, zoneNames);

        // Use the selected harvest language for PlaceName resolution
        var placeNameSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>(language);

        var levelTotal = levelSheet.Count;
        BeginPhase(Loc.S("Loading NPC locations..."), levelTotal);
        var count = 0;
        foreach (var level in levelSheet)
        {
            ct.ThrowIfCancellationRequested();
            count++;
            ReportPhaseProgress(count);

            if (level.Type != 8) continue; // Type 8 = NPC placement

            var npcId = level.Object.RowId;
            if (npcId == 0) continue;

            var territory = level.Territory.ValueNullable;
            var placeNameId = territory?.PlaceName.RowId ?? 0;
            if (placeNameId == 0) continue;

            if (!territories.TryGetValue(npcId, out var places))
            {
                places = new HashSet<uint>();
                territories[npcId] = places;
            }
            places.Add(placeNameId);

            // Resolve zone name for instance labels (first placement wins)
            if (!zoneNames.ContainsKey(npcId) && placeNameSheet != null)
            {
                var placeName = placeNameSheet.GetRowOrDefault(placeNameId);
                var name = placeName?.Name.ExtractText() ?? "";
                if (!string.IsNullOrEmpty(name))
                    zoneNames[npcId] = name;
            }
        }

        _log.Debug(nameof(BuildNpcTerritoryLookup),
            $"Built territory lookup for {territories.Count} NPCs ({zoneNames.Count} with zone names) from {count} Level entries", eventId);
        EndPhase();
        return (territories, zoneNames);
    }

    /// <summary>
    /// Multi-stage NPC name resolution. Returns (matchedIds, matchSource) or (null, _).
    /// </summary>
    private static (List<uint>? ids, DialogMatchSource source) ResolveNpcIds(
        string normalizedKey,
        Dictionary<string, List<uint>> npcNameLookup,
        Dictionary<uint, HashSet<uint>> npcTerritories,
        uint questPlaceNameId)
    {
        // Stage 1: Exact match
        if (npcNameLookup.TryGetValue(normalizedKey, out var exactMatch))
            return (exactMatch, DialogMatchSource.NameExact);

        // Stage 2: NPC name starts with key (titled NPCs)
        var startsWithMatches = npcNameLookup
            .Where(kvp => kvp.Key.StartsWith(normalizedKey, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kvp => kvp.Value)
            .Distinct()
            .ToList();
        if (startsWithMatches.Count == 1)
            return (startsWithMatches, DialogMatchSource.NameStartsWith);
        if (startsWithMatches.Count > 1)
            return (FilterByTerritory(startsWithMatches, npcTerritories, questPlaceNameId), DialogMatchSource.NameStartsWith);

        // Stage 3a: Key contains an NPC name
        var containsMatches = npcNameLookup
            .Where(kvp => kvp.Key.Length >= 4 && normalizedKey.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kvp => kvp.Key.Length)
            .ToList();

        if (containsMatches.Count > 0)
        {
            var bestLength = containsMatches[0].Key.Length;
            var bestMatches = containsMatches
                .TakeWhile(kvp => kvp.Key.Length == bestLength)
                .SelectMany(kvp => kvp.Value)
                .Distinct()
                .ToList();

            if (bestMatches.Count == 1)
                return (bestMatches, DialogMatchSource.NameKeyContainsNpc);
            if (bestMatches.Count > 1)
                return (FilterByTerritory(bestMatches, npcTerritories, questPlaceNameId), DialogMatchSource.NameKeyContainsNpc);
        }

        // Stage 3b: NPC name contains the key
        if (normalizedKey.Length >= 4)
        {
            var reverseMatches = npcNameLookup
                .Where(kvp => kvp.Key.Length > normalizedKey.Length
                              && kvp.Key.Contains(normalizedKey, StringComparison.OrdinalIgnoreCase))
                .SelectMany(kvp => kvp.Value)
                .Distinct()
                .ToList();

            if (reverseMatches.Count == 1)
                return (reverseMatches, DialogMatchSource.NameNpcContainsKey);
            if (reverseMatches.Count > 1)
                return (FilterByTerritory(reverseMatches, npcTerritories, questPlaceNameId), DialogMatchSource.NameNpcContainsKey);
        }

        // Stage 4: Levenshtein fuzzy match
        if (normalizedKey.Length >= 4)
        {
            var maxDist = normalizedKey.Length <= 6 ? 1 : 2;
            var fuzzyMatches = npcNameLookup
                .Where(kvp => Math.Abs(kvp.Key.Length - normalizedKey.Length) <= maxDist
                              && LevenshteinDistance(normalizedKey, kvp.Key) <= maxDist)
                .SelectMany(kvp => kvp.Value)
                .Distinct()
                .ToList();

            if (fuzzyMatches.Count == 1)
                return (fuzzyMatches, DialogMatchSource.NameFuzzy);
            if (fuzzyMatches.Count > 1)
                return (FilterByTerritory(fuzzyMatches, npcTerritories, questPlaceNameId), DialogMatchSource.NameFuzzy);
        }

        return (null, DialogMatchSource.NameExact);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    /// <summary>
    /// Filter NPC candidates by quest territory. Returns the filtered list, or the original if
    /// territory filtering eliminates all candidates.
    /// </summary>
    private static List<uint>? FilterByTerritory(
        List<uint> candidates,
        Dictionary<uint, HashSet<uint>> npcTerritories,
        uint questPlaceNameId)
    {
        if (questPlaceNameId == 0 || candidates.Count == 0)
            return candidates;

        var filtered = candidates
            .Where(id => npcTerritories.TryGetValue(id, out var places) && places.Contains(questPlaceNameId))
            .ToList();

        // If territory filter narrowed it down, use filtered; otherwise keep all candidates
        return filtered.Count > 0 ? filtered : candidates;
    }

    public string? ExportQuestLuaDebug(uint questRowId)
    {
        var questSheet = _dataManager.GetExcelSheet<Quest>();
        if (questSheet == null) return null;

        var quest = questSheet.GetRowOrDefault(questRowId);
        if (quest is not { } q) return null;

        var questId = q.Id.ExtractText();
        if (string.IsNullOrEmpty(questId)) return null;

        var questName = q.Name.ExtractText();
        var suffix = q.RowId - 65536;
        var subdir = (suffix / 100).ToString("D3");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Quest: {questName} (RowId={questRowId}, Id={questId})");
        sb.AppendLine($"Script path: game_script/quest/{subdir}/{questId}.luab");
        sb.AppendLine();

        // QuestParams
        sb.AppendLine("=== QuestParams ===");
        for (var i = 0; i < q.QuestParams.Count; i++)
        {
            var param = q.QuestParams[i];
            var instruction = param.ScriptInstruction.ExtractText();
            if (string.IsNullOrEmpty(instruction)) continue;
            sb.AppendLine($"  [{i}] {instruction} = {param.ScriptArg}");
        }
        sb.AppendLine();

        // Dump raw Quest sheet fields (Listener, ActorSpawnSeq, Behavior, etc.)
        try
        {
            var rawQuestSheet = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "Quest");
            var rawRow = rawQuestSheet?.GetRowOrDefault(questRowId);
            if (rawRow is { } rr)
            {
                // ActorSpawnSeq: 64 entries at index 150
                sb.AppendLine("=== ActorSpawnSeq (index 150-213) ===");
                for (var i = 0; i < 64; i++)
                {
                    try
                    {
                        var val = rr.ReadUInt16Column(150 + i);
                        if (val != 0) sb.AppendLine($"  [{i}] = {val}");
                    }
                    catch { }
                }

                // ActorDespawnSeq: 64 entries at index 214
                sb.AppendLine("=== ActorDespawnSeq (index 214-277) ===");
                for (var i = 0; i < 64; i++)
                {
                    try
                    {
                        var val = rr.ReadUInt16Column(214 + i);
                        if (val != 0) sb.AppendLine($"  [{i}] = {val}");
                    }
                    catch { }
                }

                // Listener: 64 entries at index 278
                sb.AppendLine("=== Listener (index 278-341) ===");
                for (var i = 0; i < 64; i++)
                {
                    try
                    {
                        var val = rr.ReadUInt32Column(278 + i);
                        if (val != 0) sb.AppendLine($"  [{i}] = {val}");
                    }
                    catch { }
                }

                // ConditionType: 64 entries at index 406
                sb.AppendLine("=== ConditionType (index 406-469) ===");
                for (var i = 0; i < 64; i++)
                {
                    try
                    {
                        var val = rr.ReadUInt32Column(406 + i);
                        if (val != 0) sb.AppendLine($"  [{i}] = {val}");
                    }
                    catch { }
                }

                // Behavior: 64 entries at index 598
                sb.AppendLine("=== Behavior (index 598-661) ===");
                for (var i = 0; i < 64; i++)
                {
                    try
                    {
                        var val = rr.ReadUInt16Column(598 + i);
                        if (val != 0) sb.AppendLine($"  [{i}] = {val}");
                    }
                    catch { }
                }

                sb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERROR reading raw Quest fields: {ex.Message}");
            sb.AppendLine();
        }

        // Load and parse Lua script
        var scriptPath = $"game_script/quest/{subdir}/{questId}.luab";
        var file = _dataManager.GetFile(scriptPath);
        if (file == null)
        {
            sb.AppendLine("ERROR: Lua script file not found.");
            return sb.ToString();
        }

        sb.AppendLine($"Lua file size: {file.Data.Length} bytes");
        sb.AppendLine();

        var parser = new LuabParser(file.Data);
        var functions = parser.ParseDebug();

        sb.AppendLine($"=== Parsed {functions.Count} functions ===");
        foreach (var func in functions)
        {
            sb.AppendLine($"--- Function: {func.Source} (params={func.NumParams}) ---");

            if (func.LocalNames.Count > 0)
            {
                sb.AppendLine("  Locals:");
                foreach (var local in func.LocalNames)
                    sb.AppendLine($"    {local}");
            }

            if (func.StringConstants.Count > 0)
            {
                sb.AppendLine("  String constants:");
                foreach (var c in func.StringConstants)
                    sb.AppendLine($"    {c}");
            }

            if (func.TalkCallSummaries.Count > 0)
            {
                sb.AppendLine("  Talk() calls:");
                foreach (var t in func.TalkCallSummaries)
                    sb.AppendLine($"    {t}");
            }

            if (func.InstructionDump.Count > 0)
            {
                sb.AppendLine("  Instructions:");
                foreach (var inst in func.InstructionDump)
                    sb.AppendLine($"    {inst}");
            }

            sb.AppendLine();
        }

        // Show dispatch analysis results
        var dispatchParser = new LuabParser(file.Data);
        var (_, sceneActorMap, debugSceneNameToFuncIndex) = dispatchParser.ParseWithDispatch();

        // Dump init function instructions for debugging
        var debugFuncs2 = dispatchParser.ParseDebug();
        // Wait, dispatchParser already consumed the data. Need a fresh parser.
        var initDebugParser = new LuabParser(file.Data);
        var initDebugFuncs = initDebugParser.ParseDebug();
        var initDbg = initDebugFuncs.FirstOrDefault(f =>
            f.NumParams == 0
            && f.StringConstants.Any(c => c.Contains("OnScene")));
        if (initDbg != null)
        {
            sb.AppendLine("=== Init Function Instructions ===");
            for (var ii = 0; ii < initDbg.StringConstants.Count; ii++)
                sb.AppendLine($"  Const: {initDbg.StringConstants[ii]}");
            // Dump ALL instructions for this function
            // We need raw instruction access — not available from FunctionDebugData
            // So let's just note the function was found
            sb.AppendLine($"  (params={initDbg.NumParams}, found init function)");
            sb.AppendLine();
        }

        sb.AppendLine("=== Scene Name → FuncIndex (CLOSURE+SETGLOBAL) ===");
        if (debugSceneNameToFuncIndex.Count > 0)
        {
            foreach (var (name, fi) in debugSceneNameToFuncIndex.OrderBy(kvp => kvp.Key))
                sb.AppendLine($"  {name} → funcIndex {fi}");
        }
        else
        {
            sb.AppendLine("  (empty — no scene name mappings found)");
        }
        sb.AppendLine();

        sb.AppendLine("=== Dispatch Analysis (scene funcIndex → ACTOR) ===");
        if (sceneActorMap.Count > 0)
        {
            foreach (var (funcIdx, actorName) in sceneActorMap.OrderBy(kvp => kvp.Key))
                sb.AppendLine($"  funcIndex={funcIdx} → {actorName}");
        }
        else
        {
            sb.AppendLine("  (no dispatch mappings found)");
        }
        sb.AppendLine();

        // Show QuestParams scene mapping (SEQ_N_ACTORN)
        sb.AppendLine("=== QuestParams Scene Map (SEQ_N_ACTORN) ===");
        var debugActorMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var debugSceneMap = new Dictionary<int, string>();
        for (var i = 0; i < q.QuestParams.Count; i++)
        {
            var param = q.QuestParams[i];
            var instr = param.ScriptInstruction.ExtractText();
            if (string.IsNullOrEmpty(instr)) continue;

            if (instr.StartsWith("ACTOR", StringComparison.OrdinalIgnoreCase) && !instr.Contains("_"))
                debugActorMap[instr] = (uint)param.ScriptArg;
            else if (Regex.IsMatch(instr, @"^SEQ_\d+_ACTOR\d+$", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(instr, @"(ACTOR\d+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var sn = (int)param.ScriptArg;
                    var an = m.Value.ToUpperInvariant();
                    debugSceneMap[sn] = an;
                    var fi = sn + 1;
                    var nid = debugActorMap.TryGetValue(an, out var v) ? v : 0;
                    sb.AppendLine($"  {instr}={sn} → scene {sn} → funcIndex {fi} → {an} → NPC {nid}");
                }
            }
        }
        if (debugSceneMap.Count == 0)
            sb.AppendLine("  (no SEQ_N_ACTORN entries found)");
        sb.AppendLine();

        // Also show what BuildLuaTextKeyMapping would produce
        var eventId = new EKEventId(0, TextSource.None);
        var mapping = BuildLuaTextKeyMapping(q, questId, subdir, eventId).TextKeyToNpcId;
        sb.AppendLine("=== Final textKey → NPC ID mapping ===");
        foreach (var (key, npcId) in mapping)
            sb.AppendLine($"  {key} → {npcId}");

        if (mapping.Count == 0)
            sb.AppendLine("  (empty — no mappings produced)");

        // Write to file
        var baseDir = _config.SaveToLocal ? _config.LocalSaveLocation : @"C:\alltalk_tts";
        var outputDir = Path.Combine(baseDir, "harvest");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"debug_quest_{questRowId}.txt");
        File.WriteAllText(outputPath, sb.ToString());

        return outputPath;
    }

    /// <summary>
    /// Build scene function → NPC ID mapping from the Quest sheet's ConditionType field.
    /// ConditionType (col 406-469) lists NPC IDs in listener order.
    /// Each entry maps to a scene: ConditionType[i] → OnScene(i + offset) → funcIndex.
    /// Uses sceneNameToFuncIndex from bytecode analysis for correct funcIndex resolution.
    /// </summary>
    private Dictionary<int, uint> BuildConditionTypeSceneMap(
        uint questRowId,
        Dictionary<string, int> sceneNameToFuncIndex)
    {
        var result = new Dictionary<int, uint>();

        try
        {
            if (sceneNameToFuncIndex.Count == 0) return result;

            var rawQuestSheet = _dataManager.GetExcelSheet<RawRow>(
                Dalamud.Game.ClientLanguage.English, "Quest");
            var rawRow = rawQuestSheet?.GetRowOrDefault(questRowId);
            if (rawRow is not { } rr) return result;

            // Read non-zero ConditionType entries (NPC IDs)
            var conditionEntries = new List<uint>();
            for (var i = 0; i < 64; i++)
            {
                try
                {
                    var val = rr.ReadUInt32Column(406 + i);
                    if (val == 0) break;
                    conditionEntries.Add(val);
                }
                catch { break; }
            }

            if (conditionEntries.Count == 0) return result;

            // Count scene functions from the name mapping
            var sceneCount = sceneNameToFuncIndex.Keys
                .Count(k => k.StartsWith("OnScene", StringComparison.OrdinalIgnoreCase));

            // Offset = scene count - conditionType entry count (SEQ_0 scenes at start)
            var offset = sceneCount - conditionEntries.Count;
            if (offset < 0) offset = 0;

            // Map: ConditionType[i] → OnScene(i+offset) → funcIndex via sceneNameToFuncIndex
            for (var i = 0; i < conditionEntries.Count; i++)
            {
                var sceneName = $"OnScene{(i + offset):D5}";
                if (sceneNameToFuncIndex.TryGetValue(sceneName, out var funcIndex))
                    result[funcIndex] = conditionEntries[i];
            }
        }
        catch (Exception ex)
        {
            _log.Debug(nameof(BuildConditionTypeSceneMap),
                $"Failed to build ConditionType scene map: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }

        return result;
    }

    private void ReportProgress(string message)
    {
        ProgressChanged?.Invoke(message);
    }

    /// <summary>
    /// Start a new sub-stage. Sets the label, resets the count to 0/total.
    /// If total is 1, the bar appears full immediately (instant phase).
    /// </summary>
    private void BeginPhase(string label, int total)
    {
        ReportProgress(label);
        _phaseTotal = Math.Max(1, total);
        _phaseCurrent = total <= 1 ? _phaseTotal : 0;
        ProgressCountChanged?.Invoke(_phaseCurrent, _phaseTotal);
    }

    /// <summary>Update progress within the current sub-stage.</summary>
    private void ReportPhaseProgress(int current)
    {
        _phaseCurrent = current;
        ProgressCountChanged?.Invoke(_phaseCurrent, _phaseTotal);
    }

    /// <summary>Mark the current sub-stage as fully complete (bar full).</summary>
    private void EndPhase()
    {
        _phaseCurrent = _phaseTotal;
        ProgressCountChanged?.Invoke(_phaseCurrent, _phaseTotal);
    }


    /// <summary>
    /// Known formatting-only macros that don't produce visible text content.
    /// </summary>
    private static readonly HashSet<MacroCode> FormattingOnlyMacros = new()
    {
        MacroCode.Color, MacroCode.EdgeColor, MacroCode.ShadowColor,
        MacroCode.ColorType, MacroCode.EdgeColorType,
        MacroCode.Bold, MacroCode.Italic, MacroCode.Edge, MacroCode.Shadow,
        MacroCode.SoftHyphen, MacroCode.Icon, MacroCode.Icon2,
        MacroCode.Scale, MacroCode.Ruby, MacroCode.Sound,
        MacroCode.SetResetTime, MacroCode.SetTime, MacroCode.Wait,
        MacroCode.Key,
    };

    /// <summary>
    /// Regex to detect Split index in a macro ToString() representation. Matches both
    /// <c>Split(PcName(...), …, N)</c> AND <c>Split(string(gstrM), …, N)</c> — the latter
    /// is FFXIV's convention for player-name references in cutscene quest text (slot
    /// <c>gstr1</c> overwhelmingly carries the player's name; we detect any digit suffix).
    /// </summary>
    private static readonly Regex SplitIndexRegex = new(
        @"Split\(.*?(?:PcName|gstr\d+).*?,\s*.+?,\s*(\d+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Regex matching ANY player-name macro reference in a payload's repr. Used both for
    /// the first-pass scan (decides whether to take the slow rebuild path) and the
    /// second-pass append (decides whether to emit a placeholder for unhandled macros).
    /// Covers <c>PcName</c> calls (the standard payload) and <c>gstr&lt;N&gt;</c> global
    /// string references (used by cutscene quest text — see <see cref="SplitIndexRegex"/>).
    /// </summary>
    private static readonly Regex PlayerNameMacroRegex = new(
        @"PcName|gstr\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extract text from a SeString, replacing player-name macros with
    /// -PlayerFirstName-, -PlayerLastName-, or -PlayerName- placeholders.
    /// </summary>
    /// <summary>
    /// Macros that produce conditional/computed text (need special extraction).
    /// </summary>
    private static readonly HashSet<MacroCode> ConditionalTextMacros = new()
    {
        MacroCode.IfPcGender, MacroCode.IfPcName, MacroCode.IfSelf,
        MacroCode.If, MacroCode.Switch,
    };

    // ── TEMPORARY DEBUG: payload-tree dump for the Gaius/Praetorium row ──────
    // Writes the full SeString payload tree (high-level repr + per-payload type/MacroCode/
    // raw repr) to %TEMP%\echokraut_payload_dump.txt the first time the German line
    // "..., uns erwartet ein Kampf auf Leben und Tod..." comes through extraction. Used
    // to diagnose why ExtractConditionalText returned empty for the leading address
    // macro. REMOVE THIS BLOCK AND THE CALL BELOW once the placeholder fallback in
    // ExtractTextWithPlayerName is verified end-to-end against a fresh harvest.
    private static int _gaiusDumpOnce;
    private const string DebugDumpTriggerSnippet = "uns erwartet ein Kampf auf Leben und Tod";

    private static void DebugDumpPayloadTree(ReadOnlySeString seString, string langCode, string extractedText)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "echokraut_payload_dump.txt");
            var sb = new StringBuilder();
            sb.AppendLine("================================================");
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine($"Lang code: {langCode}");
            sb.AppendLine($"Extracted: {extractedText}");
            sb.AppendLine("------------------------------------------------");
            sb.AppendLine("SeString.ToString():");
            sb.AppendLine(seString.ToString());
            sb.AppendLine("------------------------------------------------");
            sb.AppendLine("Per-payload:");
            var idx = 0;
            foreach (var payload in seString)
            {
                if (payload.Type == ReadOnlySePayloadType.Text)
                {
                    var bytes = payload.Body.Span.ToArray();
                    var text = Encoding.UTF8.GetString(bytes);
                    sb.AppendLine($"  [{idx}] Text: '{text}' (raw bytes: {BitConverter.ToString(bytes)})");
                }
                else if (payload.Type == ReadOnlySePayloadType.Macro)
                {
                    sb.AppendLine($"  [{idx}] Macro {payload.MacroCode} ({(int)payload.MacroCode}): {payload}");
                }
                else
                {
                    sb.AppendLine($"  [{idx}] {payload.Type}");
                }
                idx++;
            }
            sb.AppendLine();
            System.IO.File.AppendAllText(path, sb.ToString());
        }
        catch { /* best effort — diagnostic only, never crash the harvest over a dump */ }
    }
    // ── END TEMPORARY DEBUG ─────────────────────────────────────────────────

    private static string ExtractTextWithPlayerName(ReadOnlySeString seString, string langCode)
    {
        // Quick scan: check if any payload needs special handling
        var needsCustomExtraction = false;
        foreach (var payload in seString)
        {
            if (payload.Type != ReadOnlySePayloadType.Macro) continue;
            if (payload.MacroCode == MacroCode.PcName)
            {
                needsCustomExtraction = true;
                break;
            }
            if (ConditionalTextMacros.Contains(payload.MacroCode))
            {
                needsCustomExtraction = true;
                break;
            }
            if (!FormattingOnlyMacros.Contains(payload.MacroCode)
                && payload.MacroCode != MacroCode.NewLine
                && payload.MacroCode != MacroCode.Hyphen
                && payload.MacroCode != MacroCode.NonBreakingSpace)
            {
                var repr = payload.ToString();
                if (PlayerNameMacroRegex.IsMatch(repr))
                {
                    needsCustomExtraction = true;
                    break;
                }
            }
        }

        // Fast path: no macros that need special handling
        if (!needsCustomExtraction)
        {
            var fastResult = seString.ExtractText();
            // TEMPORARY DEBUG — see DebugDumpPayloadTree comment block above. Remove after diag.
            if (fastResult.Contains(DebugDumpTriggerSnippet, StringComparison.OrdinalIgnoreCase)
                && System.Threading.Interlocked.CompareExchange(ref _gaiusDumpOnce, 1, 0) == 0)
            {
                DebugDumpPayloadTree(seString, langCode, fastResult);
            }
            return fastResult;
        }

        // Rebuild text, handling player name and conditional macros
        var sb = new StringBuilder();
        foreach (var payload in seString)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                sb.Append(Encoding.UTF8.GetString(payload.Body.Span));
            }
            else if (payload.Type == ReadOnlySePayloadType.Macro)
            {
                if (payload.MacroCode == MacroCode.PcName)
                {
                    sb.Append(TalkTextHelper.PlaceholderFullName);
                }
                else if (payload.MacroCode == MacroCode.NewLine)
                {
                    sb.Append('\n');
                }
                else if (payload.MacroCode == MacroCode.Hyphen)
                {
                    sb.Append('-');
                }
                else if (payload.MacroCode == MacroCode.NonBreakingSpace)
                {
                    sb.Append(' ');
                }
                else if (ConditionalTextMacros.Contains(payload.MacroCode))
                {
                    // Extract first text branch from conditional macros (e.g., IfPcGender → male form)
                    var conditionalText = ExtractConditionalText(payload);
                    if (string.IsNullOrEmpty(conditionalText))
                    {
                        // ExtractConditionalText returned empty because every branch was a nested
                        // expression instead of a literal string — typical when FFXIV wraps the
                        // player address as <If(...)<PcName>...<PcName></If>, uns erwartet ein Kampf...>.
                        // Without this fallback the macro silently disappears and OriginalText starts
                        // with orphan punctuation like ", uns erwartet…" while HasPlayerPlaceholder
                        // stays false (a real DB-side bug we hit in the wild on Gaius's Praetorium
                        // line). When the macro tree references PcName, emit a player placeholder so
                        // (a) the visible text reads correctly, and (b) the live path substitutes
                        // the actual player name at speak-time.
                        var repr = payload.ToString();
                        if (PlayerNameMacroRegex.IsMatch(repr))
                            sb.Append(GetPlayerPlaceholderFromRepr(repr));
                    }
                    else
                    {
                        sb.Append(conditionalText);
                    }
                }
                else if (!FormattingOnlyMacros.Contains(payload.MacroCode))
                {
                    var repr = payload.ToString();
                    if (PlayerNameMacroRegex.IsMatch(repr))
                        sb.Append(GetPlayerPlaceholderFromRepr(repr));
                }
            }
        }
        var result = sb.ToString();
        // TEMPORARY DEBUG — see DebugDumpPayloadTree comment block above. Remove after diag.
        if (result.Contains(DebugDumpTriggerSnippet, StringComparison.OrdinalIgnoreCase)
            && System.Threading.Interlocked.CompareExchange(ref _gaiusDumpOnce, 1, 0) == 0)
        {
            DebugDumpPayloadTree(seString, langCode, result);
        }
        return result;
    }

    /// <summary>
    /// Extract text from conditional macros like IfPcGender.
    /// For IfPcGender: stores both forms as -IfGender:male|female- placeholder.
    /// For other conditionals: extracts first available text branch.
    /// </summary>
    private static string ExtractConditionalText(ReadOnlySePayload payload)
    {
        // IfPcGender: 3 expressions (condition, male_text, female_text)
        if (payload.MacroCode == MacroCode.IfPcGender
            && payload.TryGetExpression(out _, out var maleExpr, out var femaleExpr))
        {
            var maleText = maleExpr.TryGetString(out var ms) ? ms.ExtractText() : "";
            var femaleText = femaleExpr.TryGetString(out var fs) ? fs.ExtractText() : "";
            if (!string.IsNullOrEmpty(maleText) || !string.IsNullOrEmpty(femaleText))
                return TalkTextHelper.MakeGenderPlaceholder(maleText, femaleText);
        }

        // Other conditionals: try to extract first text branch
        if (payload.TryGetExpression(out _, out var expr2, out var expr3))
        {
            if (expr2.TryGetString(out var text2)) return text2.ExtractText();
            if (expr3.TryGetString(out var text3)) return text3.ExtractText();
        }
        if (payload.TryGetExpression(out _, out var e2))
        {
            if (e2.TryGetString(out var text)) return text.ExtractText();
        }
        if (payload.TryGetExpression(out var e1))
        {
            if (e1.TryGetString(out var text)) return text.ExtractText();
        }

        return "";
    }

    /// <summary>
    /// Determine the correct placeholder from a macro's string representation.
    /// Split(..., " ", 1) → first name, Split(..., " ", 2) → last name, else → full name.
    /// </summary>
    private static string GetPlayerPlaceholderFromRepr(string repr)
    {
        var match = SplitIndexRegex.Match(repr);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
        {
            return index switch
            {
                1 => TalkTextHelper.PlaceholderFirstName,
                2 => TalkTextHelper.PlaceholderLastName,
                _ => TalkTextHelper.PlaceholderFullName,
            };
        }
        return TalkTextHelper.PlaceholderFullName;
    }
}
