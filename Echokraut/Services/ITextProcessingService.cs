using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.DataClasses;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface ITextProcessingService
{
    string StripAngleBracketedText(string text);
    string ReplaceSsmlTokens(string text);
    string NormalizePunctuation(string text);
    string RemoveStutters(string text);
    string RemovePunctuation(string text);
    string ReplaceDate(string text, ClientLanguage language);
    string ReplaceTime(string text, ClientLanguage language);
    string ReplaceRomanNumbers(string text);
    string ReplaceCurrency(string text);
    string ReplaceIntWithVerbal(string text, ClientLanguage language);
    string ReplacePhonetics(string text, List<PhoneticCorrection> phoneticCorrections);
    string AnalyzeAndImproveText(string text);
    string ReplaceEmoticons(string text);
    bool IsSpeakable(string text);
    string CleanUpName(string name);
    bool TryGetEntityName(SeString seString, out string entityName);
    string StripWorldFromNames(SeString message);
    string GetBubbleName(Lumina.Excel.Sheets.TerritoryType? territory, IGameObject? speaker, string text);
}
