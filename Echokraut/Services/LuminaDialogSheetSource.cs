using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.Helper.Functional;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Echokraut.Services;

/// <inheritdoc cref="IDialogSheetSource"/>
/// <remarks>
/// <c>quest/&lt;nnn&gt;/&lt;Id&gt;</c> and <c>cut_scene/*</c> sheets: column 0 = text key, column 1 = text
/// (same layout the dialog harvest reads). Each <c>DefaultTalk</c> row holds up to three consecutive boxes.
/// Text goes through <see cref="GameSheetText.ExtractTextWithPlayerName"/>, so it carries the same
/// placeholders as the harvest's <c>voice_clips</c> rows.
/// </remarks>
public sealed class LuminaDialogSheetSource : IDialogSheetSource
{
    private readonly IDataManager _data;

    public LuminaDialogSheetSource(IDataManager data) =>
        _data = data ?? throw new ArgumentNullException(nameof(data));

    public IEnumerable<IReadOnlyList<SheetLine>> ReadDialogueSheets(ClientLanguage language)
    {
        var names = _data.Excel.SheetNames
            .Where(n => n.StartsWith("quest/", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("cut_scene/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var name in names)
        {
            var rows = ReadKeyTextSheet(name, language);
            if (rows.Count > 1) yield return rows;
        }

        ExcelSheet<DefaultTalk>? defaultTalk = null;
        try { defaultTalk = _data.GetExcelSheet<DefaultTalk>(language); }
        catch { /* sheet missing in this client */ }
        if (defaultTalk == null) yield break;
        foreach (var row in defaultTalk)
        {
            var boxes = new List<SheetLine>(3);
            for (var i = 0; i < row.Text.Count; i++)
            {
                var text = GameSheetText.ExtractTextWithPlayerName(row.Text[i]);
                if (!string.IsNullOrWhiteSpace(text)) boxes.Add(new SheetLine("", text));
            }
            if (boxes.Count > 1) yield return boxes;
        }
    }

    private List<SheetLine> ReadKeyTextSheet(string name, ClientLanguage language)
    {
        var rows = new List<SheetLine>();
        try
        {
            foreach (var row in _data.GetExcelSheet<RawRow>(language, name))
            {
                try
                {
                    var key = row.ReadStringColumn(0).ExtractText();
                    var text = GameSheetText.ExtractTextWithPlayerName(row.ReadStringColumn(1));
                    rows.Add(new SheetLine(key, text));
                }
                catch { /* row without the key/text columns */ }
            }
        }
        catch { /* sheet unreadable in this language */ }
        return rows;
    }
}
