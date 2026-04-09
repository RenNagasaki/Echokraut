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

    /// <summary>Sheets excluded from /ekdump — already harvested or clearly non-dialog.</summary>
    private static readonly HashSet<string> DumpExcludedSheets = new(StringComparer.OrdinalIgnoreCase)
    {
        // Already harvested
        "DefaultTalk", "Balloon", "Quest",
        "ENpcResident", "ENpcBase", "BNpcName", "Race",
        "GilShop", "Warp", "FateShop", "SpecialShop", "TripleTriad",
        "PreHandler", "SwitchTalkVariation", "ArrayEventHandler", "CustomTalk",
        "TerritoryType", "Level", "Behavior",
        "InstanceContentTextData", "ContentTalk", "NpcYell", "PublicContentTextData",
        "GimmickTalk", "PartyContentTextData", "MassivePcContentTextData", "GoldSaucerTextData",
        "ContentDirectorBattleTalk", "ContentNpcTalk", "ContentTalkParam",

        // Achievements / Titles
        "Achievement", "AchievementCategory", "AchievementKind", "AchievementHideCondition",
        "AchievementTarget", "Title",

        // Items / Equipment / Inventory
        "Item", "ItemAction", "ItemFood", "ItemLevel", "ItemSearchCategory", "ItemSeries",
        "ItemSortCategory", "ItemSpecialBonus", "ItemUICategory", "ItemRetainerCategory",
        "EquipSlotCategory", "EquipRaceCategory", "BaseParam", "Materia", "MateriaGrade",
        "Stain", "StainTransient", "Relic", "Relic3", "RelicNote", "RelicNoteCategory",

        // Actions / Abilities / Traits
        "Action", "ActionCategory", "ActionComboRoute", "ActionIndirection", "ActionParam",
        "ActionProcStatus", "ActionTimeline", "ActionTimelineReplace", "ActionTransient",
        "CraftAction", "BuddyAction", "PetAction", "CompanyAction", "EventAction",
        "Trait", "TraitRecast", "TraitTransient", "Status", "StatusHitEffect", "StatusLoopVFX",

        // Jobs / Classes
        "ClassJob", "ClassJobCategory",

        // Crafting / Gathering
        "Recipe", "RecipeLookup", "RecipeNotebookList", "CraftType", "CraftLeve", "CraftLevelDifference",
        "GatheringItem", "GatheringItemLevelConvertTable", "GatheringItemPoint", "GatheringPoint",
        "GatheringPointBase", "GatheringPointBonus", "GatheringPointBonusType", "GatheringPointName",
        "GatheringType", "GatheringCondition", "GatheringSubCategory",
        "SpearfishingItem", "SpearfishingNotebook", "FishParameter", "FishingSpot",

        // Maps / Zones / Weather
        "Map", "MapMarker", "MapSymbol", "PlaceName", "Weather", "WeatherGroup", "WeatherRate",
        "Aetheryte", "AetherCurrentCompFlgSet", "AetheryteSystemDefine",

        // UI / HUD / System
        "Addon", "AddonParam", "Lobby", "MainCommand", "MainCommandCategory",
        "GeneralAction", "Marker", "FieldMarker", "HudParamMaster",
        "ConfigKey", "Completion", "Tutorial", "TutorialDPS", "TutorialHealer", "TutorialTank",
        "ScreenImage", "LoadingImage", "LoadingTips", "LoadingTipsSubCategory",

        // Mounts / Minions / Companions
        "Mount", "MountAction", "MountCustomize", "MountFlyingCondition", "MountSpeed",
        "Companion", "CompanionMove", "CompanionTransient",
        "BuddyEquip", "BuddyItem", "BuddySkill",
        "Ornament",

        // Housing / Furniture
        "HousingFurniture", "HousingYardObject", "HousingPreset", "HousingExterior",
        "HousingInterior", "HousingMapMarkerInfo", "HousingPlacement", "HousingUnitedExterior",

        // Gold Saucer / Triple Triad / LoVM / Chocobo Racing
        "GoldSaucerArcadeMachine", "GoldSaucerTextData",
        "TripleTriadCard", "TripleTriadCardType", "TripleTriadRule", "TripleTriadResident",
        "TripleTriadCardResident", "TripleTriadTournament",

        // Fashion / Aesthetics
        "CharaMakeCustomize", "CharaMakeType", "HairMakeType", "Glasses", "GlassesStyle",

        // Orchestrion / BGM / Sound
        "Orchestrion", "OrchestrionCategory", "OrchestrionPath", "OrchestrionUiparam",
        "BGM", "BGMFade", "BGMScene", "BGMSituation", "BGMSwitch",

        // Leves / FATEs
        "Leve", "LeveAssignmentType", "LeveClient", "LeveRewardItem", "LeveString", "LeveVfx",
        "Fate", "FateEvent", "FateProgressUI", "FateTokenType",

        // Grand Company / Free Company
        "GrandCompany", "GrandCompanyRank", "GCShop", "GCScripShopCategory", "GCScripShopItem",
        "FCActivity", "FCAuthority", "FCChestName", "FCProfile", "FCRank", "FCReputation", "FCRights",

        // PvP
        "PvPAction", "PvPActionSort", "PvPRank", "PvPSeries", "PvPTrait",

        // Deep Dungeon
        "DeepDungeon", "DeepDungeonBan", "DeepDungeonDanger", "DeepDungeonEquipment",
        "DeepDungeonFloorEffectUI", "DeepDungeonItem", "DeepDungeonLayer", "DeepDungeonMagicStone",
        "DeepDungeonRoom", "DeepDungeonStatus",

        // Eureka / Bozja
        "DynamicEvent", "DynamicEventType",

        // Retainers / Ventures
        "RetainerTask", "RetainerTaskLvRange", "RetainerTaskNormal", "RetainerTaskParameter",
        "RetainerTaskRandom",

        // Misc data tables (no dialog)
        "AnimationLOD", "AttackType", "BattleLeve", "BeastTribe", "BeastReputationRank",
        "Calendar", "CircleActivity", "CollectablesShop", "ContentType", "ContentFinderCondition",
        "ContentFinderConditionTransient", "ContentRoulette", "ContentRouletteRoleBonus",
        "Currency", "DawnContent", "Emote", "EmoteCategory", "EmoteMode",
        "EObj", "EObjName", "ExVersion", "GardeningSeed",
        "GroupPoseStamp", "GroupPoseStampCategory",
        "InstanceContent", "InstanceContentCSBonus",
        "Jingle", "JournalCategory", "JournalGenre", "JournalSection",
        "LogFilter", "LogKind", "LogMessage",
        "ModelChara", "ModelScale", "ModelSkeleton", "ModelState", "MonsterNote", "MonsterNoteTarget",
        "MYCWarResultNotebook",
        "NotebookDivision", "NotebookDivisionCategory",
        "OnlineStatus", "ParamGrow", "Perform", "Permission",
        "PhysicsGroup", "QuestClassJobReward", "QuestRepeatFlag", "QuestRewardOther",
        "Resident", "SatisfactionNpc", "SatisfactionSupply", "SatisfactionSupplyReward",
        "TextCommand", "TextCommandParam",
        "TopicSelect", "Town", "Transformation", "Treasure", "TreasureHuntRank",
        "UIColor", "VFX", "World", "WorldDCGroupType",
    };

    private static readonly string[] LangCodes = { "en", "de", "ja", "fr" };
    private static readonly Dalamud.Game.ClientLanguage[] LangValues =
    {
        Dalamud.Game.ClientLanguage.English,
        Dalamud.Game.ClientLanguage.German,
        Dalamud.Game.ClientLanguage.Japanese,
        Dalamud.Game.ClientLanguage.French
    };

    private static readonly Dictionary<string, string> RaceNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Hyuran", "Hyur" },
        { "Miqo'te", "Miqote" },
        { "Au Ra", "AuRa" },
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
        INpcDataService npcData)
    {
        _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));
    }

    public async Task RunAsync(Dalamud.Game.ClientLanguage language, CancellationToken ct)
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
            lock (_runLock) { _runningTask = Task.Run(() => DoHarvest(language, linkedCt, eventId), linkedCt); task = _runningTask; }
            await task;
        }
        catch (OperationCanceledException)
        {
            _log.Info(nameof(RunAsync), "Harvest cancelled by user.", eventId);
            BeginPhase(Loc.S("Cancelled."), 1);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(RunAsync), $"Harvest failed: {ex}", eventId);
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

    private void DoHarvest(Dalamud.Game.ClientLanguage language, CancellationToken ct, EKEventId eventId)
    {
        // Step 1: Load dialog sheets in all languages (LoadDialogSheet drives its own phase progress)
        var defaultTalkTexts = LoadDialogSheet<DefaultTalk>("DefaultTalk", GetDefaultTalkTexts, ct, eventId);
        ct.ThrowIfCancellationRequested();

        var balloonTexts = LoadDialogSheet<Balloon>("Balloon", GetBalloonTexts, ct, eventId);
        ct.ThrowIfCancellationRequested();

        var instanceTexts = LoadDialogSheet<InstanceContentTextData>("InstanceContentTextData", GetSingleTextSheet, ct, eventId);
        ct.ThrowIfCancellationRequested();

        var contentTalkTexts = LoadDialogSheet<ContentTalk>("ContentTalk", GetSingleTextSheet, ct, eventId);
        ct.ThrowIfCancellationRequested();

        var npcYellTexts = LoadDialogSheet<NpcYell>("NpcYell", GetSingleTextSheet, ct, eventId);
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

        // Pre-build NPC → Balloon ID lookup from:
        // 1. ENpcBase col 105 (direct Balloon link)
        // 2. ENpcBase col 64 (Behavior) → Behavior col 8 (Balloon link)
        BeginPhase(Loc.S("Building Balloon lookup..."), 1);
        var behaviorToBalloon = new Dictionary<uint, HashSet<uint>>();
        var behaviorBalloonCount = 0;
        try
        {
            // Behavior is a sub-row sheet. Balloon is at column 8 (SaintCoinach index 8).
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
        _log.Debug(nameof(DoHarvest),
            $"Behavior→Balloon: {behaviorToBalloon.Count} Behavior rows, {behaviorBalloonCount} sub-rows",
            eventId);

        var npcBalloonIds = new Dictionary<uint, HashSet<uint>>();
        if (npcBaseRaw != null)
        {
            foreach (var rawRow in npcBaseRaw)
            {
                var ids = new HashSet<uint>();
                // Direct Balloon field (col 105)
                try { var v = rawRow.ReadUInt32Column(105); if (v != 0) ids.Add(v); } catch { }
                // Behavior → Balloon chain (col 64 → Behavior sub-rows → col 8)
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

        var uniqueBalloonFromBehavior = npcBalloonIds.Values.SelectMany(s => s).Distinct()
            .Count(id => allDialogSheets["Balloon"].ContainsKey(id));
        _log.Debug(nameof(DoHarvest),
            $"Balloon lookup: {behaviorToBalloon.Count} Behaviors with Balloon, " +
            $"{npcBalloonIds.Count} NPCs with Balloon IDs, " +
            $"{uniqueBalloonFromBehavior} unique IDs match Balloon sheet", eventId);
        var matchedDialogIds = new Dictionary<string, HashSet<uint>>();
        foreach (var sheetName in allDialogSheets.Keys)
            matchedDialogIds[sheetName] = new HashSet<uint>();

        // Extract Balloon → ENpcBase mappings from LGB territory files.
        // Two-pass hybrid: (1) scan within each entry's own bounds for accurate matches,
        // (2) for remaining unmatched Balloon IDs, find them anywhere in LGB and attribute
        // to the nearest ENpc entry.
        EndPhase();
        var ttSheetCount = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.Count ?? 1;
        BeginPhase(Loc.S("Scanning LGB territory files..."), ttSheetCount);
        var lgbBalloonToNpc = new Dictionary<uint, uint>(); // Balloon ID → ENpcBase ID
        var lgbTerritoriesScanned = 0;
        var lgbEntriesTotal = 0;
        var lgbBoundsMapped = 0;
        var lgbNearestMapped = 0;
        var balloonSheetIds = new HashSet<uint>(allDialogSheets["Balloon"].Keys);

        // Store per-file data for the second pass
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

        // Pass 2: for remaining unmatched Balloon IDs, search all LGB files and attribute
        // to the nearest ENpc entry
        var unmatchedBalloonIds = new HashSet<uint>(
            balloonSheetIds.Where(id => !lgbBalloonToNpc.ContainsKey(id)));
        if (unmatchedBalloonIds.Count > 0)
        {
            EndPhase();
            BeginPhase(string.Format(Loc.S("LGB pass 2: searching for {0} unmatched Balloon IDs..."), unmatchedBalloonIds.Count), 1);
            foreach (var (data, entries) in lgbFileCache)
            {
                ct.ThrowIfCancellationRequested();
                if (unmatchedBalloonIds.Count == 0) break;

                var nearestResults = LgbParser.FindNearestEntryForIds(
                    data, entries, unmatchedBalloonIds);
                foreach (var (balloonId, npcId) in nearestResults)
                {
                    if (lgbBalloonToNpc.TryAdd(balloonId, npcId))
                        lgbNearestMapped++;
                }
            }
        }

        lgbFileCache.Clear(); // free memory

        _log.Info(nameof(DoHarvest),
            $"LGB Balloon scan: {lgbBalloonToNpc.Count} unique Balloon IDs mapped " +
            $"({lgbBoundsMapped} within bounds, {lgbNearestMapped} nearest-entry). " +
            $"{lgbEntriesTotal} ENpc entries across {lgbTerritoriesScanned} territories", eventId);

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
            var gender = DetermineGender(npcBase, race);

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

        // Diagnostic: count non-zero Balloon field values across ALL named NPCs
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
                        if (allDialogSheets["Balloon"].ContainsKey(bVal))
                            balloonFieldMatched++;
                    }
                }
                catch { }
            }
        }
        // Count unique Balloon IDs that are in allDialogSheets
        var uniqueBalloonIdsInSheet = npcBalloonIds
            .Where(kvp => npcNames.TryGetValue(kvp.Key, out var n) && n.Values.Any(v => !string.IsNullOrEmpty(v)))
            .SelectMany(kvp => kvp.Value)
            .Where(bId => allDialogSheets["Balloon"].ContainsKey(bId))
            .Distinct()
            .Count();
        var uniqueBalloonIdsNotMatched = npcBalloonIds
            .Where(kvp => npcNames.TryGetValue(kvp.Key, out var n) && n.Values.Any(v => !string.IsNullOrEmpty(v)))
            .SelectMany(kvp => kvp.Value)
            .Where(bId => allDialogSheets["Balloon"].ContainsKey(bId) && !matchedDialogIds["Balloon"].Contains(bId))
            .Distinct()
            .Count();

        _log.Info(nameof(DoHarvest),
            $"Balloon: {balloonFieldNonZero} NPCs non-zero, {balloonFieldMatched} NPCs match sheet, " +
            $"{uniqueBalloonIdsInSheet} unique IDs in sheet, {uniqueBalloonIdsNotMatched} unique not yet matched. " +
            $"matchedDialogIds Balloon={matchedDialogIds["Balloon"].Count}, DT={matchedDialogIds["DefaultTalk"].Count}", eventId);

        // Count unnamed NPCs with unmatched Balloon fields
        var unnamedWithBalloon = 0;
        var unnamedBalloonMatched = 0;
        if (npcBaseRaw != null)
        {
            foreach (var npcBase2 in npcBaseSheet)
            {
                var nid = npcBase2.RowId;
                if (npcNames.TryGetValue(nid, out var n2) && n2.Values.Any(v => !string.IsNullOrEmpty(v)))
                    continue; // skip named

                var rr2 = npcBaseRaw.GetRowOrDefault(nid);
                if (rr2 is not { } raw2) continue;

                try
                {
                    var bId = raw2.ReadUInt32Column(105);
                    if (bId != 0 && !matchedDialogIds["Balloon"].Contains(bId)
                        && allDialogSheets["Balloon"].ContainsKey(bId))
                    {
                        unnamedWithBalloon++;

                        // Try appearance match for Balloon too
                        var ak = $"{npcBase2.Race.RowId}_{npcBase2.Gender}_{npcBase2.Face}_{npcBase2.HairStyle}";
                        if (appearanceToNamedNpc.TryGetValue(ak, out var namedNpc2))
                        {
                            unnamedBalloonMatched++;

                            foreach (var texts in FlattenTexts(allDialogSheets["Balloon"][bId]))
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

        _log.Info(nameof(DoHarvest),
            $"Pass 2: {pass2UnnamedWithDialog} unnamed NPCs with new dialog, " +
            $"{pass2AppearanceMatched} appearance matched, " +
            $"{unnamedWithBalloon} unnamed with unmatched Balloon, " +
            $"{unnamedBalloonMatched} Balloon appearance matched", eventId);

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
            var gender = DetermineGender(npcBase.Value, race);

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
        var (linkedQuests, unmatchedQuests) = HarvestQuestDialogs(npcNames, bnpcNames, npcBaseSheet, npcTerritories, npcZoneNames, language, ct, eventId);

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
        _db.SuppressEvents = true;
        _db.BulkMode = true;
        int persisted;
        int persistedQuest;
        try
        {
            ct.ThrowIfCancellationRequested();
            persisted = PersistLinkedDialogs(linkedDialogs, npcBaseSheet, language, npcZoneNames, ct, eventId);

            ct.ThrowIfCancellationRequested();
            persistedQuest = PersistLinkedQuestDialogs(linkedQuests, npcBaseSheet, language, npcZoneNames, ct, eventId);
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
        }

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

                var context = _db.UpsertContext(character.Id, dialog.Sheet == "Balloon" ? "bubble" : "npc");

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
                    return RaceNameMap.TryGetValue(raceName, out var mapped) ? mapped : raceName;
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
    private static Dictionary<string, string> ResolveGenderTags(Dictionary<string, string> names, Genders gender, int dePronoun = 0)
    {
        var resolved = new Dictionary<string, string>(names.Count);
        foreach (var (lang, name) in names)
        {
            var n = name;
            if (n.Contains('['))
            {
                switch (lang)
                {
                    case "de":
                        if (gender == Genders.Female)
                        {
                            n = n.Replace("[a]", "e");
                            // [p] adds "in" only when the noun is grammatically masculine (Pronoun 0).
                            // Pronoun 1 = "sie" = already feminine noun → [p] should be empty.
                            n = dePronoun == 1
                                ? n.Replace("[p]", "")
                                : n.Replace("[p]", "in");
                        }
                        else
                        {
                            n = n.Replace("[a]", "er").Replace("[p]", "");
                        }
                        break;
                    case "fr":
                        n = gender == Genders.Female
                            ? n.Replace("[a]", "e").Replace("[p]", "e")
                            : n.Replace("[a]", "").Replace("[p]", "");
                        break;
                }
                // Strip any remaining unknown bracket tags
                n = System.Text.RegularExpressions.Regex.Replace(n, @"\[[a-z]\]", "");
            }
            resolved[lang] = n;
        }
        return resolved;
    }


    private Genders DetermineGender(ENpcBase npcBase, NpcRaces race)
    {
        var gender = (Genders)npcBase.Gender;

        // For wild races with Male gender, apply ModelBody heuristic
        if (gender == Genders.Male && IsWildRace(race))
        {
            var modelBody = npcBase.ModelBody;
            if (modelBody < 256)
            {
                var localModelBody = (byte)modelBody;
                var npcGenderMap = _jsonData.ModelGenderMap.Find(p =>
                    p.race == race && p.maleDefault && p.male != localModelBody);

                if (npcGenderMap == null)
                {
                    npcGenderMap = _jsonData.ModelGenderMap.Find(p =>
                        p.race == race && !p.maleDefault && p.female == localModelBody);
                }

                if (npcGenderMap != null)
                    gender = Genders.Female;
            }
        }

        return gender;
    }

    private static bool IsWildRace(NpcRaces race)
    {
        return race switch
        {
            NpcRaces.Hyur or NpcRaces.AuRa or NpcRaces.Miqote or NpcRaces.Roegadyn or
            NpcRaces.Hrothgar or NpcRaces.Lalafell or NpcRaces.Elezen or NpcRaces.Viera => false,
            _ => true
        };
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
    /// Parse the quest's Lua script to build a textKey → ENpcBase ID mapping.
    /// Uses QuestParams ACTOR definitions + Lua bytecode analysis.
    /// </summary>
    private Dictionary<string, uint> BuildLuaTextKeyMapping(Quest quest, string questId, string subdir, EKEventId eventId)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

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

            if (actorNameToNpcId.Count == 0) return result;

            // Load the Lua script file
            var scriptPath = $"game_script/quest/{subdir}/{questId}.luab";
            var file = _dataManager.GetFile(scriptPath);
            if (file == null) return result;

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
        }
        catch (Exception ex)
        {
            _log.Debug(nameof(BuildLuaTextKeyMapping), $"Failed to parse Lua script for {questId}: {ex.Message}", eventId);
        }

        return result;
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

    private (List<LinkedQuestDialog> linked, List<UnmatchedQuestDialog> unmatched) HarvestQuestDialogs(
        Dictionary<uint, Dictionary<string, string>> npcNames,
        Dictionary<uint, Dictionary<string, string>> bnpcNames,
        ExcelSheet<ENpcBase> npcBaseSheet,
        Dictionary<uint, HashSet<uint>> npcTerritories,
        Dictionary<uint, string> npcZoneNames,
        Dalamud.Game.ClientLanguage language,
        CancellationToken ct,
        EKEventId eventId)
    {
        var linked = new List<LinkedQuestDialog>();
        var unmatched = new List<UnmatchedQuestDialog>();

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
        if (questSheet == null) return (linked, unmatched);

        var questCount = 0;
        var questTotal = questSheet.Count();
        var questsWithDialog = 0;

        BeginPhase(Loc.S("Scanning for quest dialogs..."), questTotal);

        foreach (var quest in questSheet)
        {
            ct.ThrowIfCancellationRequested();
            questCount++;

            var questId = quest.Id.ExtractText();
            if (string.IsNullOrEmpty(questId)) continue;

            var questName = quest.Name.ExtractText();
            if (string.IsNullOrEmpty(questName)) continue;

            var questType = ClassifyQuest(quest);

            ReportPhaseProgress(questCount);

            // Compute sheet path: quest/{subdir}/{questId}
            // FFXIV organizes quest EXDs in folders of 100 (not 1000)
            var suffix = quest.RowId - 65536;
            var subdir = (suffix / 100).ToString("D3");
            var sheetPath = $"quest/{subdir}/{questId}";

            // Parse Lua script to get textKey → actor register mapping
            var luaTextKeyToNpcId = BuildLuaTextKeyMapping(quest, questId, subdir, eventId);

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

                // Try Lua script mapping first (most accurate — from actual game scripts)
                List<uint>? matchedIds = null;
                var matchSource = DialogMatchSource.NameExact;

                if (luaTextKeyToNpcId.TryGetValue(key, out var luaNpcId))
                {
                    matchedIds = new List<uint> { luaNpcId };
                    matchSource = DialogMatchSource.LuaScript;
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
                            gender = DetermineGender(nb, race);
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
                }
            }
        }

        _log.Info(nameof(HarvestQuestDialogs),
            $"Quest harvest: {linked.Count} linked, {unmatched.Count} unmatched, " +
            $"{questCount} quests scanned ({questsWithDialog} text entries)", eventId);

        EndPhase();
        return (linked, unmatched);
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
        var mapping = BuildLuaTextKeyMapping(q, questId, subdir, eventId);
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
    /// Dump all Lumina Excel sheets to TSV files in the harvest directory.
    /// Each sheet gets one file with RowId + all string columns extracted in German.
    /// </summary>
    public async Task DumpAllSheetsAsync(CancellationToken ct)
    {
        if (IsRunning) return;
        IsRunning = true;

        try
        {
            await Task.Run(() =>
            {
                var baseDir = _config.SaveToLocal ? _config.LocalSaveLocation : @"C:\alltalk_tts";
                var outputDir = Path.Combine(baseDir, "sheet_dump");
                Directory.CreateDirectory(outputDir);

                var sheetNames = _dataManager.Excel.SheetNames;
                var total = sheetNames.Count;
                var done = 0;

                foreach (var sheetName in sheetNames)
                {
                    ct.ThrowIfCancellationRequested();
                    done++;

                    // Skip sheets already processed by the harvest
                    if (sheetName.StartsWith("quest/")
                        || sheetName.StartsWith("custom/")
                        || DumpExcludedSheets.Contains(sheetName)) { continue; }

                    if (done % 50 == 0)
                        ReportProgress($"Dumping sheets... {done}/{total} ({sheetName})");

                    try
                    {
                        var sheet = _dataManager.GetExcelSheet<RawRow>(
                            Dalamud.Game.ClientLanguage.German, sheetName);
                        if (sheet == null) continue;

                        var rows = new List<string>();
                        var hasText = false;

                        foreach (var row in sheet)
                        {
                            // Probe columns for string data
                            var cols = new List<string> { row.RowId.ToString() };
                            for (var c = 0; c < 64; c++)
                            {
                                try
                                {
                                    var text = row.ReadStringColumn(c).ExtractText();
                                    cols.Add(text?.Replace("\t", " ").Replace("\n", "\\n").Replace("\r", "") ?? "");
                                    if (!string.IsNullOrWhiteSpace(text)) hasText = true;
                                }
                                catch
                                {
                                    // Column doesn't exist or isn't a string — stop probing
                                    break;
                                }
                            }

                            if (cols.Count > 1)
                                rows.Add(string.Join("\t", cols));
                        }

                        // Only write sheets that have at least one non-empty string
                        if (hasText && rows.Count > 0)
                        {
                            var safeName = sheetName.Replace("/", "_").Replace("\\", "_");
                            File.WriteAllLines(Path.Combine(outputDir, $"{safeName}.tsv"), rows);
                        }
                    }
                    catch
                    {
                        // Sheet can't be read — skip
                    }
                }

                // Dump ContentDirectorBattleTalk → speaker ID + text mapping
                ReportProgress("Dumping battle talk speaker mapping...");
                DumpBattleTalkMapping(outputDir);

                ReportProgress($"Done. Dumped {done} sheets to {outputDir}");
            }, ct);
        }
        catch (OperationCanceledException)
        {
            ReportProgress("Dump cancelled.");
        }
        catch (Exception ex)
        {
            ReportProgress($"Dump error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// Dump all ContentDirectorBattleTalk entries with duty name, speaker ID, and text.
    /// Output: battle_talk_speakers.tsv with columns: DutyName, SpeakerID, TextRowId, Text_DE, Text_EN
    /// </summary>
    private void DumpBattleTalkMapping(string outputDir)
    {
        try
        {
            var icSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.InstanceContent>();
            if (icSheet == null) return;

            var cfcSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>(
                Dalamud.Game.ClientLanguage.German);

            var textSheetDe = _dataManager.GetExcelSheet<InstanceContentTextData>(Dalamud.Game.ClientLanguage.German);
            var textSheetEn = _dataManager.GetExcelSheet<InstanceContentTextData>(Dalamud.Game.ClientLanguage.English);
            var bnpcNameDe = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.BNpcName>(Dalamud.Game.ClientLanguage.German);
            var enpcDe = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>(Dalamud.Game.ClientLanguage.German);

            string ResolveSpeakerNameDe(uint speakerId)
            {
                if (speakerId == 0) return "";
                try
                {
                    var bn = bnpcNameDe?.GetRowOrDefault(speakerId)?.Singular.ExtractText();
                    if (!string.IsNullOrWhiteSpace(bn)) return bn!;
                }
                catch { }
                try
                {
                    var en = enpcDe?.GetRowOrDefault(speakerId)?.Singular.ExtractText();
                    if (!string.IsNullOrWhiteSpace(en)) return en!;
                }
                catch { }
                return "";
            }

            var lines = new List<string> { "Source\tDutyName_DE\tSpeakerID\tSpeakerName_DE\tRowId\tText_DE\tText_EN" };

            foreach (var ic in icSheet)
            {
                // Get duty name from ContentFinderCondition
                var dutyName = "";
                var cfcRef = ic.ContentFinderCondition;
                if (cfcRef.RowId != 0 && cfcSheet != null)
                {
                    var cfc = cfcSheet.GetRowOrDefault(cfcRef.RowId);
                    dutyName = cfc?.Name.ExtractText() ?? "";
                }
                if (string.IsNullOrEmpty(dutyName)) dutyName = $"InstanceContent#{ic.RowId}";

                // Read ContentDirectorBattleTalk subrows via RawSubrow
                try
                {
                    var cdbtSheet = _dataManager.GetSubrowExcelSheet<RawSubrow>(
                        Dalamud.Game.ClientLanguage.English, "ContentDirectorBattleTalk");
                    if (cdbtSheet == null) continue;

                    var subrows = cdbtSheet.GetRowOrDefault(ic.RowId);
                    if (subrows == null) continue;

                    foreach (var subrow in subrows.Value)
                    {
                        try
                        {
                            var speakerId = subrow.ReadUInt32Column(0);  // Unknown0
                            var textRef = subrow.ReadUInt32Column(2);    // Text RowRef (after Unknown1)

                            if (textRef == 0) continue;

                            var textDe = textSheetDe?.GetRowOrDefault(textRef)?.Text.ExtractText() ?? "";
                            var textEn = textSheetEn?.GetRowOrDefault(textRef)?.Text.ExtractText() ?? "";

                            if (string.IsNullOrWhiteSpace(textDe) && string.IsNullOrWhiteSpace(textEn)) continue;

                            textDe = textDe.Replace("\t", " ").Replace("\n", "\\n");
                            textEn = textEn.Replace("\t", " ").Replace("\n", "\\n");

                            var speakerNameDe = ResolveSpeakerNameDe(speakerId);
                            lines.Add($"BattleTalk\t{dutyName}\t{speakerId}\t{speakerNameDe}\t{textRef}\t{textDe}\t{textEn}");
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Also dump ContentTalk entries (used by some dungeons instead of ContentDirectorBattleTalk)
            try
            {
                var ctSheet = _dataManager.GetExcelSheet<ContentTalk>(Dalamud.Game.ClientLanguage.German);
                var ctSheetEn = _dataManager.GetExcelSheet<ContentTalk>(Dalamud.Game.ClientLanguage.English);
                if (ctSheet != null && ctSheetEn != null)
                {
                    // ContentTalk has a ContentTalkParam ref which may link to an NPC
                    var ctParamSheet = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "ContentTalkParam");

                    foreach (var row in ctSheet)
                    {
                        var textDe = row.Text.ExtractText();
                        if (string.IsNullOrWhiteSpace(textDe)) continue;

                        var textEn = ctSheetEn.GetRowOrDefault(row.RowId)?.Text.ExtractText() ?? "";

                        // Try to get speaker from ContentTalkParam
                        var paramRef = row.ContentTalkParam.RowId;
                        var speakerId = paramRef;

                        textDe = textDe.Replace("\t", " ").Replace("\n", "\\n");
                        textEn = textEn.Replace("\t", " ").Replace("\n", "\\n");

                        lines.Add($"ContentTalk\t\t{speakerId}\t\t{row.RowId}\t{textDe}\t{textEn}");
                    }
                }
            }
            catch { }

            // Also dump NpcYell entries
            try
            {
                var yellDe = _dataManager.GetExcelSheet<NpcYell>(Dalamud.Game.ClientLanguage.German);
                var yellEn = _dataManager.GetExcelSheet<NpcYell>(Dalamud.Game.ClientLanguage.English);
                if (yellDe != null && yellEn != null)
                {
                    foreach (var row in yellDe)
                    {
                        var textDe = row.Text.ExtractText();
                        if (string.IsNullOrWhiteSpace(textDe)) continue;

                        var textEn = yellEn.GetRowOrDefault(row.RowId)?.Text.ExtractText() ?? "";

                        textDe = textDe.Replace("\t", " ").Replace("\n", "\\n");
                        textEn = textEn.Replace("\t", " ").Replace("\n", "\\n");

                        lines.Add($"NpcYell\t\t0\t\t{row.RowId}\t{textDe}\t{textEn}");
                    }
                }
            }
            catch { }

            File.WriteAllLines(Path.Combine(outputDir, "battle_talk_speakers.tsv"), lines);
            _log.Info(nameof(DumpBattleTalkMapping),
                $"Dumped {lines.Count - 1} battle talk entries to battle_talk_speakers.tsv",
                new EKEventId(0, TextSource.None));

            // Probe for instance content scripts
            var scriptLines = new List<string> { "DutyName\tTerritoryId\tBgPath\tScriptPath\tScriptSize" };
            var cfcSheetEn = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>(
                Dalamud.Game.ClientLanguage.English);
            var ttSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
            if (cfcSheetEn != null && ttSheet != null)
            {
                foreach (var cfc in cfcSheetEn)
                {
                    var name = cfc.Name.ExtractText();
                    if (string.IsNullOrEmpty(name)) continue;

                    var ttId = cfc.TerritoryType.RowId;
                    if (ttId == 0) continue;
                    var tt = ttSheet.GetRowOrDefault(ttId);
                    var bg = tt?.Bg.ExtractText() ?? "";
                    if (string.IsNullOrEmpty(bg)) continue;

                    // Extract the instance code from Bg path (e.g., "ffxiv/sea_s1/dun/s1d1/level/s1d1" → "s1d1")
                    var parts = bg.Split('/');
                    var code = parts.Length >= 4 ? parts[3] : parts[^1];
                    // Also try the last segment and the territory name
                    var lastPart = parts[^1];
                    var ttName = tt?.Name.ExtractText() ?? "";

                    // Try various script paths — content scripts are in game_script/content/
                    // Also try raid/, public_content/, party_content/, massive_pc_content/
                    var pathsToTry = new[]
                    {
                        $"game_script/content/{code}.luab",
                        $"game_script/content/{code}/{code}.luab",
                        $"game_script/content/{code}/director.luab",
                        $"game_script/content/{code}/battletalk.luab",
                        $"game_script/raid/{code}.luab",
                        $"game_script/raid/{code}/{code}.luab",
                        $"game_script/public_content/{code}.luab",
                        $"game_script/public_content/{code}/{code}.luab",
                        $"game_script/party_content/{code}.luab",
                        $"game_script/party_content/{code}/{code}.luab",
                        $"game_script/massive_pc_content/{code}.luab",
                        $"game_script/story/{code}.luab",
                        $"game_script/story/{code}/{code}.luab",
                        $"game_script/content/{lastPart}.luab",
                        $"game_script/content/{lastPart}/{lastPart}.luab",
                        $"game_script/content/{ttName}.luab",
                        $"game_script/raid/{lastPart}.luab",
                        $"game_script/raid/{lastPart}/{lastPart}.luab",
                    };

                    var found = false;
                    foreach (var path in pathsToTry)
                    {
                        if (string.IsNullOrEmpty(path) || path.Contains("//")) continue;
                        var file = _dataManager.GetFile(path);
                        if (file != null)
                        {
                            scriptLines.Add($"{name}\t{ttId}\t{bg}\t{path}\t{file.Data.Length}");
                            found = true;

                            // Dump the script file
                            var scriptDir = Path.Combine(outputDir, "content_scripts");
                            Directory.CreateDirectory(scriptDir);
                            var safeName = path.Replace("/", "_").Replace("\\", "_");
                            File.WriteAllBytes(Path.Combine(scriptDir, safeName), file.Data);
                            break;
                        }
                    }
                    if (!found)
                        scriptLines.Add($"{name}\t{ttId}\t{bg}\tNOT FOUND (tried: {code})\t0");
                }
            }
            File.WriteAllLines(Path.Combine(outputDir, "instance_scripts.tsv"), scriptLines);
            _log.Info(nameof(DumpBattleTalkMapping),
                $"Found {scriptLines.Count - 1} instance content scripts",
                new EKEventId(0, TextSource.None));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DumpBattleTalkMapping), $"Error dumping battle talks: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
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
    /// Regex to detect Split index in a macro ToString() representation.
    /// Matches patterns like: Split(PcName(...), ..., 1) or Split(Head(PcName(...)), ..., 2)
    /// </summary>
    private static readonly Regex SplitIndexRegex = new(@"Split\(.*?PcName.*?,\s*.+?,\s*(\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                if (repr.Contains("PcName", StringComparison.OrdinalIgnoreCase))
                {
                    needsCustomExtraction = true;
                    break;
                }
            }
        }

        // Fast path: no macros that need special handling
        if (!needsCustomExtraction)
            return seString.ExtractText();

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
                    sb.Append(ExtractConditionalText(payload));
                }
                else if (!FormattingOnlyMacros.Contains(payload.MacroCode))
                {
                    var repr = payload.ToString();
                    if (repr.Contains("PcName", StringComparison.OrdinalIgnoreCase))
                        sb.Append(GetPlayerPlaceholderFromRepr(repr));
                }
            }
        }
        return sb.ToString();
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
