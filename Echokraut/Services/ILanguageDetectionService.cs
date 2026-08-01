using Dalamud.Game;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface ILanguageDetectionService
{
    Task<ClientLanguage> GetTextLanguage(string text, EKEventId eventId);
}
