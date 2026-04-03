using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Echokraut.Services;

public class DialogHarvestService : IDialogHarvestService
{
    private readonly IDataManager _dataManager;
    private readonly IJsonDataService _jsonData;
    private readonly ILogService _log;
    private readonly Configuration _config;

    private static readonly string[] LangCodes = { "en", "de", "ja", "fr" };
    private static readonly Dalamud.Game.ClientLanguage[] LangValues =
    {
        Dalamud.Game.ClientLanguage.English,
        Dalamud.Game.ClientLanguage.German,
        Dalamud.Game.ClientLanguage.Japanese,
        Dalamud.Game.ClientLanguage.French
    };

    private static readonly Dictionary<string, string> RaceNameMap = new()
    {
        { "Hyuran", "Hyur" }
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

    public DialogHarvestService(
        IDataManager dataManager,
        IJsonDataService jsonData,
        ILogService log,
        Configuration config)
    {
        _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (IsRunning) return;
        IsRunning = true;

        var eventId = _log.Start(nameof(RunAsync), TextSource.None);

        try
        {
            await Task.Run(() => DoHarvest(ct, eventId), ct);
        }
        catch (OperationCanceledException)
        {
            _log.Info(nameof(RunAsync), "Harvest cancelled by user.", eventId);
            ReportProgress("Cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error(nameof(RunAsync), $"Harvest failed: {ex}", eventId);
            ReportProgress($"Error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            _log.End(nameof(RunAsync), eventId);
        }
    }

    private void DoHarvest(CancellationToken ct, EKEventId eventId)
    {
        // Step 1: Load dialog sheets in all languages
        ReportProgress("Loading DefaultTalk...");
        var defaultTalkTexts = LoadDialogSheet<DefaultTalk>("DefaultTalk", GetDefaultTalkTexts, ct, eventId);
        ct.ThrowIfCancellationRequested();

        ReportProgress("Loading Balloon...");
        var balloonTexts = LoadDialogSheet<Balloon>("Balloon", GetBalloonTexts, ct, eventId);
        ct.ThrowIfCancellationRequested();

        var allDialogSheets = new Dictionary<string, Dictionary<uint, Dictionary<string, List<string>>>>
        {
            ["DefaultTalk"] = defaultTalkTexts,
            ["Balloon"] = balloonTexts
        };

        // Step 2: Load NPC names in all languages (ENpcResident + BNpcName)
        ReportProgress("Loading NPC names...");
        var npcNames = LoadNpcNames(ct, eventId);
        ct.ThrowIfCancellationRequested();

        ReportProgress("Loading BNpc names...");
        var bnpcNames = LoadBNpcNames(ct, eventId);
        ct.ThrowIfCancellationRequested();

        // Step 3: Load ENpcBase for race/gender
        // Build multi-hop DefaultTalk lookup: intermediate sheet row ID → set of DefaultTalk IDs.
        // ENpcData references GilShop/Warp/FateShop/etc. which then link to DefaultTalk.
        ReportProgress("Building DefaultTalk chain lookup...");
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
        ReportProgress("Loading SwitchTalkVariation...");
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

        ReportProgress("Loading NPC data...");
        var npcBaseSheet = _dataManager.GetExcelSheet<ENpcBase>()!;
        var npcBaseRaw = _dataManager.GetExcelSheet<RawRow>(Dalamud.Game.ClientLanguage.English, "ENpcBase");
        var matchedDialogIds = new Dictionary<string, HashSet<uint>>();
        foreach (var sheetName in allDialogSheets.Keys)
            matchedDialogIds[sheetName] = new HashSet<uint>();

        var linkedDialogs = new List<LinkedDialog>();
        var npcCount = 0;

        // Build appearance → named NPC lookup for pass 2 (unnamed NPC resolution)
        // Key: "race_gender_face_hair" → (npcId, names, raceStr, gender)
        var appearanceToNamedNpc = new Dictionary<string, (uint npcId, Dictionary<string, string> names, string raceStr, Genders gender)>();

        // Pass 1: Scan named NPCs
        foreach (var npcBase in npcBaseSheet)
        {
            ct.ThrowIfCancellationRequested();
            npcCount++;
            if (npcCount % 1000 == 0)
                ReportProgress($"Processing named NPCs... {npcCount}");

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

            // Read dedicated Balloon field (col 105) and DefaultBalloon (col 107) from ENpcBase
            if (npcBaseRaw != null)
            {
                var rawRow = npcBaseRaw.GetRowOrDefault(npcId);
                if (rawRow is { } rr)
                {
                    int[] balloonCols = [105, 107];
                    foreach (var col in balloonCols)
                    {
                        try
                        {
                            var balloonId = rr.ReadUInt32Column(col);
                            if (balloonId == 0) continue;
                            if (matchedDialogIds["Balloon"].Contains(balloonId)) continue;
                            if (!allDialogSheets["Balloon"].TryGetValue(balloonId, out var bTexts)) continue;

                            foreach (var texts in FlattenTexts(bTexts))
                            {
                                if (texts.Values.All(string.IsNullOrEmpty)) continue;

                                linkedDialogs.Add(new LinkedDialog
                                {
                                    NpcId = npcId,
                                    NpcName = names,
                                    Race = raceStr,
                                    Gender = gender.ToString(),
                                    Sheet = "Balloon",
                                    DialogId = balloonId,
                                    MatchSource = DialogMatchSource.Direct.ToString(),
                                    Texts = texts
                                });
                            }
                            matchedDialogIds["Balloon"].Add(balloonId);
                        }
                        catch { }
                    }
                }
            }
        }

        ct.ThrowIfCancellationRequested();

        // Pass 2: Scan unnamed NPCs for dialog not yet matched.
        // Resolve names by matching appearance (race+gender+face+hair) to named NPCs.
        ReportProgress("Processing unnamed NPCs...");
        npcCount = 0;
        var pass2UnnamedWithDialog = 0;
        var pass2AppearanceMatched = 0;
        var pass2NewDialogIds = 0;
        foreach (var npcBase in npcBaseSheet)
        {
            ct.ThrowIfCancellationRequested();
            npcCount++;
            if (npcCount % 1000 == 0)
                ReportProgress($"Processing unnamed NPCs... {npcCount}");

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

        _log.Info(nameof(DoHarvest),
            $"Pass 2: {pass2UnnamedWithDialog} unnamed NPCs with new dialog, " +
            $"{pass2AppearanceMatched} appearance matched", eventId);

        // Diagnostic: check where unmatched DefaultTalk IDs live
        ReportProgress("Diagnosing unmatched DefaultTalk...");
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
        ReportProgress($"Diag: {unmatchedDtIds.Count} unmatched, {foundViaCustomTalk} CustomTalk, {foundViaSwitchTalk} STV, {foundInENpcData} ENpcData");

        ReportProgress($"Pass 2: {pass2UnnamedWithDialog} unnamed with dialog, {pass2AppearanceMatched} matched");

        // Step 4: Collect unmatched dialog entries
        ReportProgress("Collecting unmatched dialogs...");
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

        // Step 5: Harvest quest dialogs
        ct.ThrowIfCancellationRequested();
        ReportProgress("Harvesting quest dialogs...");
        var (linkedQuests, unmatchedQuests) = HarvestQuestDialogs(npcNames, bnpcNames, npcBaseSheet, ct, eventId);

        // Step 6: Write output files
        ct.ThrowIfCancellationRequested();
        ReportProgress("Writing results...");

        var baseDir = _config.SaveToLocal ? _config.LocalSaveLocation : @"C:\alltalk_tts";
        var outputDir = Path.Combine(baseDir, "harvest");
        Directory.CreateDirectory(outputDir);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        var linkedPath = Path.Combine(outputDir, "linked_dialogs.json");
        File.WriteAllText(linkedPath, JsonSerializer.Serialize(linkedDialogs, jsonOptions));

        var unmatchedPath = Path.Combine(outputDir, "unmatched_dialogs.json");
        File.WriteAllText(unmatchedPath, JsonSerializer.Serialize(unmatchedDialogs, jsonOptions));

        var linkedQuestPath = Path.Combine(outputDir, "linked_quest_dialogs.json");
        File.WriteAllText(linkedQuestPath, JsonSerializer.Serialize(linkedQuests, jsonOptions));

        var unmatchedQuestPath = Path.Combine(outputDir, "unmatched_quest_dialogs.json");
        File.WriteAllText(unmatchedQuestPath, JsonSerializer.Serialize(unmatchedQuests, jsonOptions));

        var msg = $"Done: {linkedDialogs.Count} linked, {unmatchedDialogs.Count} unmatched, " +
                  $"{linkedQuests.Count} quest linked, {unmatchedQuests.Count} quest unmatched";
        _log.Info(nameof(DoHarvest), msg, eventId);
        ReportProgress(msg);
    }

    /// <summary>
    /// Load a dialog sheet in all 4 languages. Returns dialogId → lang → list of text strings.
    /// </summary>
    private Dictionary<uint, Dictionary<string, List<string>>> LoadDialogSheet<T>(
        string sheetName,
        Func<T, List<string>> textExtractor,
        CancellationToken ct,
        EKEventId eventId) where T : struct, Lumina.Excel.IExcelRow<T>
    {
        var result = new Dictionary<uint, Dictionary<string, List<string>>>();

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
                if (count % 2000 == 0)
                    ReportProgress($"Loading {sheetName} ({langCode})... {count}");

                var texts = textExtractor(row);
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

        return result;
    }

    private static List<string> GetDefaultTalkTexts(DefaultTalk row)
    {
        var texts = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var text = row.Text[i].ExtractText();
            texts.Add(text ?? "");
        }
        return texts;
    }

    private static List<string> GetBalloonTexts(Balloon row)
    {
        var text = row.Dialogue.ExtractText();
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
                nameDict[langCode] = name ?? "";
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
                nameDict[langCode] = name;
            }

            _log.Debug(nameof(LoadBNpcNames), $"Loaded BNpcName names for {langCode}", eventId);
        }

        return result;
    }

    private static string GetRaceString(ENpcBase npcBase)
    {
        try
        {
            var race = npcBase.Race.ValueNullable;
            if (race == null) return "Unknown";
            var raceName = race.Value.Masculine.ExtractText();
            return RaceNameMap.TryGetValue(raceName, out var mapped) ? mapped : raceName;
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
        CancellationToken ct,
        EKEventId eventId)
    {
        var linked = new List<LinkedQuestDialog>();
        var unmatched = new List<UnmatchedQuestDialog>();

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

        // Build NPC → territory lookup from Level sheet for disambiguation
        ReportProgress("Loading NPC locations...");
        var npcTerritories = BuildNpcTerritoryLookup(ct, eventId);

        // Read all quests
        var questSheet = _dataManager.GetExcelSheet<Quest>();
        if (questSheet == null) return (linked, unmatched);

        var questCount = 0;
        var questTotal = questSheet.Count();

        foreach (var quest in questSheet)
        {
            ct.ThrowIfCancellationRequested();
            questCount++;

            var questId = quest.Id.ExtractText();
            if (string.IsNullOrEmpty(questId)) continue;

            var questName = quest.Name.ExtractText();
            if (string.IsNullOrEmpty(questName)) continue;

            if (questCount % 200 == 0)
                ReportProgress($"Quest dialogs... {questCount}/{questTotal} ({questName})");

            // Compute sheet path: quest/{subdir}/{questId}
            var suffix = quest.RowId - 65536;
            var subdir = (suffix / 1000).ToString("D3");
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

                        var text = row.ReadStringColumn(1).ExtractText();
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
            $"Quest harvest: {linked.Count} linked, {unmatched.Count} unmatched", eventId);

        return (linked, unmatched);
    }

    /// <summary>
    /// Build NPC ID → set of PlaceName IDs from the Level sheet (Type 8 = NPC placement).
    /// </summary>
    private Dictionary<uint, HashSet<uint>> BuildNpcTerritoryLookup(CancellationToken ct, EKEventId eventId)
    {
        var result = new Dictionary<uint, HashSet<uint>>();
        var levelSheet = _dataManager.GetExcelSheet<Level>();
        if (levelSheet == null) return result;

        var count = 0;
        foreach (var level in levelSheet)
        {
            ct.ThrowIfCancellationRequested();
            count++;
            if (count % 50000 == 0)
                ReportProgress($"Loading NPC locations... {count}");

            if (level.Type != 8) continue; // Type 8 = NPC placement

            var npcId = level.Object.RowId;
            if (npcId == 0) continue;

            var placeNameId = level.Territory.ValueNullable?.PlaceName.RowId ?? 0;
            if (placeNameId == 0) continue;

            if (!result.TryGetValue(npcId, out var places))
            {
                places = new HashSet<uint>();
                result[npcId] = places;
            }
            places.Add(placeNameId);
        }

        _log.Debug(nameof(BuildNpcTerritoryLookup), $"Built territory lookup for {result.Count} NPCs from {count} Level entries", eventId);
        return result;
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
        var subdir = (suffix / 1000).ToString("D3");

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
}
