using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Lumina.Excel.Sheets;

namespace Echokraut.Services;

public interface ILuminaService
{
    TerritoryType? GetTerritory();
    ENpcBase? GetENpcBase(uint dataId, EKEventId eventId);
    Race? GetRace(byte speakerRace, EKEventId eventId);

    /// <summary>Resolves a Race row id to its canonical English masculine name (e.g. "Hyur", "Miqo'te"). Use for parsing into the NpcRaces enum — the localized form goes into RaceStr for display.</summary>
    string GetRaceEnglishName(byte speakerRace, EKEventId eventId);

    /// <summary>Resolves a World row id to its canonical English name (e.g. "Phoenix"), regardless of client language.</summary>
    string GetWorldEnglishName(uint worldRowId);
}
