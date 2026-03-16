using Dalamud.Game;
using Echokraut.DataClasses;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface ILanguageDetectionService
{
    Task<ClientLanguage> GetTextLanguage(string text, EKEventId eventId);
}
