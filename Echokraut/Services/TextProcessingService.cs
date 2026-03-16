using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Services;

/// <summary>
/// Service for text cleaning, normalization, and transformation
/// Wraps TalkTextHelper static methods with a proper service interface
/// </summary>
public class TextProcessingService : ITextProcessingService
{
    private readonly ILogService _log;
    private readonly IJsonDataService _jsonData;

    public TextProcessingService(ILogService log, IJsonDataService jsonData)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));
    }

    public string StripAngleBracketedText(string text)
    {
        return TalkTextHelper.StripAngleBracketedText(text);
    }

    public string ReplaceSsmlTokens(string text)
    {
        return TalkTextHelper.ReplaceSsmlTokens(text);
    }

    public string NormalizePunctuation(string text)
    {
        return TalkTextHelper.NormalizePunctuation(text);
    }

    public string RemoveStutters(string text)
    {
        return TalkTextHelper.RemoveStutters(text);
    }

    public string RemovePunctuation(string text)
    {
        return TalkTextHelper.RemovePunctuation(text);
    }

    public string ReplaceDate(string text, ClientLanguage language)
    {
        return TalkTextHelper.ReplaceDate(_log, new EKEventId(0, Enums.TextSource.None), text, language);
    }

    public string ReplaceTime(string text, ClientLanguage language)
    {
        return TalkTextHelper.ReplaceTime(_log, new EKEventId(0, Enums.TextSource.None), text, language);
    }

    public string ReplaceRomanNumbers(string text)
    {
        return TalkTextHelper.ReplaceRomanNumbers(_log, new EKEventId(0, Enums.TextSource.None), text);
    }

    public string ReplaceCurrency(string text)
    {
        return TalkTextHelper.ReplaceCurrency(_log, new EKEventId(0, Enums.TextSource.None), text);
    }

    public string ReplaceIntWithVerbal(string text, ClientLanguage language)
    {
        return TalkTextHelper.ReplaceIntWithVerbal(_log, new EKEventId(0, Enums.TextSource.None), text, language);
    }

    public string ReplacePhonetics(string text, List<PhoneticCorrection> phoneticCorrections)
    {
        return TalkTextHelper.ReplacePhonetics(text, phoneticCorrections);
    }

    public string AnalyzeAndImproveText(string text)
    {
        return TalkTextHelper.AnalyzeAndImproveText(text);
    }

    public string ReplaceEmoticons(string text)
    {
        return TalkTextHelper.ReplaceEmoticons(_log, new EKEventId(0, Enums.TextSource.None), text, _jsonData.Emoticons);
    }

    public bool IsSpeakable(string text)
    {
        return TalkTextHelper.IsSpeakable(text);
    }

    public string CleanUpName(string name)
    {
        return TalkTextHelper.CleanUpName(name);
    }

    public bool TryGetEntityName(SeString seString, out string entityName)
    {
        return TalkTextHelper.TryGetEntityName(seString, out entityName);
    }

    public string StripWorldFromNames(SeString message)
    {
        return TalkTextHelper.StripWorldFromNames(message);
    }

    public string GetBubbleName(Lumina.Excel.Sheets.TerritoryType? territory, IGameObject? speaker, string text)
    {
        return TalkTextHelper.GetBubbleName(territory, speaker, text);
    }
}
