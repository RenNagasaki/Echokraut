using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Lumina.Excel.Sheets;

namespace Echokraut.Services;

/// <summary>
/// Walks the DB once, builds a dry-run plan, applies it on demand. Shared race/gender
/// resolution lives in <see cref="NpcIdentityHelper"/> so this path and the harvest path
/// produce the same canonical <c>(name, gender, race)</c> for a given <c>NpcBaseId</c> — a
/// fresh harvest and a post-hoc repair must land on the same character row.
/// </summary>
public sealed class NpcAttributionRepairService : INpcAttributionRepairService
{
    private readonly IDatabaseService _db;
    private readonly IDataManager _dataManager;
    private readonly IJsonDataService _jsonData;
    private readonly ILogService _log;

    public NpcAttributionRepairService(
        IDatabaseService db,
        IDataManager dataManager,
        IJsonDataService jsonData,
        ILogService log)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public NpcAttributionRepairReport BuildDryRunReport(CancellationToken ct = default)
    {
        var eventId = _log.Start(nameof(BuildDryRunReport), TextSource.None);
        var ek = new EKEventId(eventId.Id, eventId.TextSource);

        var instances = _db.GetAllInstancesForRepair();
        var actions = new List<NpcAttributionRepairAction>();
        var stats = new ScanStats();

        var npcBaseSheet = _dataManager.GetExcelSheet<ENpcBase>();
        if (npcBaseSheet == null)
        {
            _log.Warning(nameof(BuildDryRunReport), "ENpcBase sheet unavailable — repair cannot proceed.", ek);
            _log.End(nameof(BuildDryRunReport), ek);
            return new NpcAttributionRepairReport(actions, instances.Count, instances.Count, 0, 0);
        }

        foreach (var row in instances)
        {
            ct.ThrowIfCancellationRequested();
            var action = EvaluateInstance(row, npcBaseSheet, stats);
            if (action != null) actions.Add(action);
        }

        _log.Info(nameof(BuildDryRunReport),
            $"Scanned {instances.Count} instances: {actions.Count} mis-attributed, " +
            $"{stats.AlreadyCorrect} already correct, {stats.SkippedNoLumina} no Lumina row, " +
            $"{stats.SkippedNoCanonical} no canonical character in DB.", ek);
        _log.End(nameof(BuildDryRunReport), ek);

        return new NpcAttributionRepairReport(actions, instances.Count, stats.SkippedNoLumina, stats.SkippedNoCanonical, stats.AlreadyCorrect);
    }

    /// <summary>
    /// Per-row evaluation. Returns a <see cref="NpcAttributionRepairAction"/> when this row
    /// is mis-attributed AND a canonical character row exists in DB; otherwise returns null
    /// and updates one of the skip / already-correct counters on <paramref name="stats"/>.
    /// Extracted to keep <see cref="BuildDryRunReport"/> below Sonar's cognitive-complexity bar.
    /// </summary>
    private NpcAttributionRepairAction? EvaluateInstance(
        AttributionInstanceRow row,
        Lumina.Excel.ExcelSheet<ENpcBase> npcBaseSheet,
        ScanStats stats)
    {
        if (row.NpcBaseId <= 0) return null;

        var npcBase = npcBaseSheet.GetRowOrDefault((uint)row.NpcBaseId);
        if (npcBase == null) { stats.SkippedNoLumina++; return null; }

        var canonicalRace = ResolveRace(npcBase.Value);
        var canonicalGender = NpcIdentityHelper.DetermineGender(npcBase.Value, canonicalRace, _jsonData.ModelGenderMap);
        var canonicalName = ResolveDisplayName(npcBase.Value, (ClientLanguage)row.Language, canonicalGender);
        if (string.IsNullOrWhiteSpace(canonicalName)) { stats.SkippedNoLumina++; return null; }

        var alreadyMatches =
            string.Equals(canonicalName, row.CharacterName, StringComparison.OrdinalIgnoreCase)
            && canonicalGender == row.CharacterGender
            && canonicalRace == row.CharacterRace;
        if (alreadyMatches) { stats.AlreadyCorrect++; return null; }

        var canonical = _db.FindCharacter(canonicalName, canonicalGender, canonicalRace, row.Language);
        if (canonical == null) { stats.SkippedNoCanonical++; return null; }
        if (canonical.Id == row.CharacterId) { stats.AlreadyCorrect++; return null; }

        int clipCount = 0;
        try { clipCount = _db.GetVoiceClipCountForCharacter(row.CharacterId); }
        catch { /* informational only */ }

        return new NpcAttributionRepairAction(
            OldCharacterId: row.CharacterId,
            OldCharacterName: row.CharacterName,
            NewCharacterId: canonical.Id,
            CanonicalName: canonicalName,
            NpcBaseId: row.NpcBaseId,
            Language: row.Language,
            VoiceClipCount: clipCount);
    }

    public NpcAttributionRepairResult Apply(NpcAttributionRepairReport report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var eventId = _log.Start(nameof(Apply), TextSource.None);
        var ek = new EKEventId(eventId.Id, eventId.TextSource);

        int totalReassigned = 0;
        int totalMoved = 0;
        int totalMerged = 0;
        var deletedSet = new HashSet<int>();

        foreach (var action in report.Actions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (moved, merged) = _db.ReassignAttribution(action.OldCharacterId, action.NewCharacterId, (uint)action.NpcBaseId);
                totalReassigned++;
                totalMoved += moved;
                totalMerged += merged;

                // After the move, the old character may be empty (no instances, no clips) — drop it.
                // HashSet guards repeat probes when the same OldCharacterId appears multiple times.
                if (deletedSet.Add(action.OldCharacterId) && !_db.DeleteCharacterIfEmpty(action.OldCharacterId))
                    deletedSet.Remove(action.OldCharacterId);
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(Apply),
                    $"Failed to reassign baseId={action.NpcBaseId} from char={action.OldCharacterId} → {action.NewCharacterId}: {ex.Message}",
                    ek);
            }
        }

        _log.Info(nameof(Apply),
            $"Applied: reassigned={totalReassigned} clipsMoved={totalMoved} clipsMerged={totalMerged} charsDeleted={deletedSet.Count}",
            ek);
        _log.End(nameof(Apply), ek);

        return new NpcAttributionRepairResult(totalReassigned, totalMoved, totalMerged, deletedSet.Count);
    }

    private sealed class ScanStats
    {
        public int SkippedNoLumina;
        public int SkippedNoCanonical;
        public int AlreadyCorrect;
    }

    // ── Lumina helpers (mirrors DialogHarvestService) ──────────────────────────

    private NpcRaces ResolveRace(ENpcBase npcBase)
    {
        try
        {
            var raceRowId = npcBase.Race.RowId;
            if (raceRowId != 0)
            {
                var enRaceSheet = _dataManager.GetExcelSheet<Race>(ClientLanguage.English);
                var enRace = enRaceSheet?.GetRowOrDefault(raceRowId);
                if (enRace != null)
                {
                    var raceName = enRace.Value.Masculine.ExtractText();
                    var mapped = NpcIdentityHelper.CanonicalRaceName(raceName);
                    return Enum.TryParse<NpcRaces>(mapped, true, out var r) ? r : NpcRaces.Unknown;
                }
            }

            var modelChara = (int)npcBase.ModelChara.RowId;
            if (modelChara != 0 && _jsonData.ModelsToRaceMap.TryGetValue(modelChara, out var beastRace))
                return beastRace;
        }
        catch
        {
            // Fall through to Unknown — repair leaves the row alone rather than risk a wrong move.
        }
        return NpcRaces.Unknown;
    }

    private string ResolveDisplayName(ENpcBase npcBase, ClientLanguage language, Genders gender)
    {
        try
        {
            var residentSheet = _dataManager.GetExcelSheet<ENpcResident>(language);
            var resident = residentSheet?.GetRowOrDefault(npcBase.RowId);
            if (resident == null) return string.Empty;

            var raw = resident.Value.Singular.ExtractText() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            // German pronoun controls [p] resolution. Other languages don't use it.
            sbyte dePronoun = 0;
            if (language == ClientLanguage.German)
            {
                var deSheet = _dataManager.GetExcelSheet<ENpcResident>(ClientLanguage.German);
                var deResident = deSheet?.GetRowOrDefault(npcBase.RowId);
                if (deResident != null) dePronoun = deResident.Value.Pronoun;
            }

            var langCode = language switch
            {
                ClientLanguage.English => "en",
                ClientLanguage.German => "de",
                ClientLanguage.Japanese => "ja",
                ClientLanguage.French => "fr",
                _ => "en"
            };
            return NpcNameNormalizer.Resolve(raw, langCode, gender == Genders.Female, dePronoun);
        }
        catch
        {
            return string.Empty;
        }
    }
}
