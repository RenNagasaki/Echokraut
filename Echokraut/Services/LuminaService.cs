using Echotools.Logging.Services;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Lumina.Excel.Sheets;
using System;

namespace Echokraut.Services;

public class LuminaService : ILuminaService
{
    private readonly ILogService _log;
    private readonly IClientState _clientState;
    private readonly IDataManager _dataManager;

    private ushort _territoryRow;
    private TerritoryType? _territory;

    public LuminaService(ILogService log, IClientState clientState, IDataManager dataManager)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
        _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
    }

    public TerritoryType? GetTerritory()
    {
        var territoryRow = _clientState.TerritoryType;
        if (territoryRow != _territoryRow)
        {
            _territoryRow = territoryRow;
            _territory = _dataManager.GetExcelSheet<TerritoryType>()!.GetRow(territoryRow);
        }

        return _territory;
    }

    public ENpcBase? GetENpcBase(uint dataId, EKEventId eventId)
    {
        try
        {
            return _dataManager.GetExcelSheet<ENpcBase>()!.GetRow(dataId);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(GetENpcBase), $"Error while getting ENpcBase: {ex}", eventId);
        }

        return null;
    }

    public Race? GetRace(byte speakerRace, EKEventId eventId)
    {
        try
        {
            // Default sheet → client language. Used for display strings (RaceStr) so the UI keeps
            // showing localized race names. For enum parsing, use GetRaceEnglishName instead — the
            // NpcRaces enum values are English-only and would not parse on non-English clients.
            return _dataManager.GetExcelSheet<Race>()?.GetRow(speakerRace) ?? null;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(GetRace), $"Error while getting Race: {ex}", eventId);
        }

        return null;
    }

    public string GetRaceEnglishName(byte speakerRace, EKEventId eventId)
    {
        try
        {
            var race = _dataManager.GetExcelSheet<Race>(Dalamud.Game.ClientLanguage.English)?.GetRow(speakerRace);
            return race?.Masculine.ExtractText() ?? "";
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(GetRaceEnglishName), $"Error while getting English Race: {ex}", eventId);
            return "";
        }
    }

    public string GetWorldEnglishName(uint worldRowId)
    {
        if (worldRowId == 0) return "";
        try
        {
            // World names are not localized in FFXIV (server names are global).
            // We pull from the default sheet; ExtractText on the InternalName / Name column gives the English value.
            var world = _dataManager.GetExcelSheet<World>(Dalamud.Game.ClientLanguage.English)?.GetRow(worldRowId);
            return world?.Name.ExtractText() ?? "";
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(GetWorldEnglishName), $"Failed to resolve world {worldRowId}: {ex.Message}",
                new EKEventId(0, TextSource.None));
            return "";
        }
    }
}
