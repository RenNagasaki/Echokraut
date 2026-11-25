using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Helper.Functional.GameVoice;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using EchoScd;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace Echokraut.Services;

/// <inheritdoc cref="IGameVoiceDataSource"/>
public sealed class GameVoiceDataSource : IGameVoiceDataSource
{
    private readonly IDataManager _dataManager;
    private readonly ILogService _log;

    public GameVoiceDataSource(IDataManager dataManager, ILogService log)
    {
        _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public IEnumerable<CutsceneTextRow> EnumerateCutsceneRows(ClientLanguage language, CancellationToken ct)
    {
        IReadOnlyList<string> sheetNames;
        try { sheetNames = _dataManager.Excel.SheetNames; }
        catch (Exception ex)
        {
            _log.Warning(nameof(EnumerateCutsceneRows), $"Could not enumerate Excel sheet names: {ex.Message}", Event);
            yield break;
        }

        foreach (var sheetName in sheetNames.Where(n => n.StartsWith("cut_scene/", StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            ExcelSheet<RawRow>? sheet;
            try { sheet = _dataManager.GetExcelSheet<RawRow>(language, sheetName); }
            catch { continue; }
            if (sheet == null) continue;

            foreach (var row in sheet)
            {
                string key;
                ReadOnlySeString text;
                try
                {
                    key = row.ReadStringColumn(0).ExtractText();
                    text = row.ReadStringColumn(1);
                }
                catch { continue; }
                if (string.IsNullOrEmpty(key)) continue;
                yield return new CutsceneTextRow(key, text.ExtractText(), HasContentMacro(text));
            }
        }
    }

    public IReadOnlyDictionary<uint, string> GetNpcNames(ClientLanguage language)
    {
        var result = new Dictionary<uint, string>();
        var sheet = _dataManager.GetExcelSheet<ENpcResident>(language);
        if (sheet == null) return result;
        foreach (var row in sheet)
        {
            var name = row.Singular.ExtractText()?.Trim();
            if (!string.IsNullOrEmpty(name)) result[row.RowId] = name;
        }
        return result;
    }

    public PcmBuffer? TryDecodeVoice(string audioBase, ClientLanguage language)
    {
        var lang = VoiceScdPaths.LanguageCodeForScd(language);
        foreach (var path in VoiceScdPaths.Build(audioBase, lang))
        {
            byte[]? data;
            try { data = _dataManager.GetFile(path)?.Data; }
            catch { continue; } // Lumina throws on some speculative paths — keep scanning
            // Same stub rule as the harvest's "is it voiced?" check: 1-byte / header-only files
            // sit next to the real clip, keep looking.
            if (data == null || data.Length < VoiceScdPaths.MinVoicedScdBytes) continue;

            var result = ScdVoiceDecoder.DecodeFirstVoice(data);
            if (result.SkippedEntries > 0)
                _log.Debug(nameof(TryDecodeVoice), $"{path}: skipped {result.SkippedEntries} non-ADPCM entr(y/ies)", Event);
            if (result.Success) return result.Voice;
            _log.Debug(nameof(TryDecodeVoice), $"{path}: not decodable ({result.Error})", Event);
            return null;
        }
        return null;
    }

    /// <summary>
    /// True when <paramref name="text"/> carries a macro that changes what is said (player name,
    /// gender/conditional text, …). Pure layout macros (line break, colour, …) do not count.
    /// </summary>
    private static bool HasContentMacro(ReadOnlySeString text)
    {
        foreach (var payload in text)
        {
            if (payload.Type != ReadOnlySePayloadType.Macro) continue;
            if (payload.MacroCode is Lumina.Text.Payloads.MacroCode.NewLine
                or Lumina.Text.Payloads.MacroCode.Hyphen
                or Lumina.Text.Payloads.MacroCode.NonBreakingSpace) continue;
            if (DialogHarvestService.FormattingOnlyMacros.Contains(payload.MacroCode)) continue;
            return true;
        }
        return false;
    }

    private static readonly EKEventId Event = new(0, TextSource.None);
}
