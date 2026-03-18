using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Lumina.Excel.Sheets;

namespace Echokraut.Services;

public interface ILuminaService
{
    TerritoryType? GetTerritory();
    ENpcBase? GetENpcBase(uint dataId, EKEventId eventId);
    Race? GetRace(byte speakerRace, EKEventId eventId);
}
