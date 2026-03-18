using System;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Localization;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

public sealed unsafe partial class NativeConfigWindow
{
    // ── Phonetics fields ─────────────────────────────────────────────────────

    // Persistent header area (headers, filters, add row) — never cleared
    private HorizontalListNode? _phonFilterRow;   // Column headers
    private HorizontalListNode? _phonAddRow;      // Filter inputs
    private HorizontalLineNode? _phonHeaderSep1;  // Separator
    private HorizontalListNode? _phonHeaderSep2;  // Add new row

    // Data list — cleared and rebuilt on filter/data change
    private ScrollingListNode? _phonDataList;

    private string _phonFilterOrigText = "";
    private string _phonFilterCorrText = "";
    private string _phonNewOrigText = "";
    private string _phonNewCorrText = "";

    private bool _phonNeedRebuild;
    private bool _phonBuilt;

    // Column widths
    private const float PhonColOrig = 250f;
    private const float PhonColCorr = 250f;
    private const float PhonColDel  = 60f;

    private void SetupPhonetics()
    {
        var w = _contentWidth;
        var y = _topContentPos.Y;
        var x = _topContentPos.X;

        // Row 1: Column headers
        _phonFilterRow = new HorizontalListNode
        {
            Size = new Vector2(w, 20),
            ItemSpacing = 4,
            Position = new Vector2(x, y),
        };
        _phonFilterRow.AddNode(Label(Loc.S("Original"), PhonColOrig));
        _phonFilterRow.AddNode(Label(Loc.S("Corrected"), PhonColCorr));
        _phonFilterRow.AddNode(Label("", PhonColDel));

        // Row 2: Filter inputs below headers
        _phonAddRow = new HorizontalListNode
        {
            Size = new Vector2(w, 28),
            ItemSpacing = 4,
            Position = new Vector2(x, y + 20),
        };
        _phonAddRow.AddNode(Input(Loc.S("Filter"), PhonColOrig, 40, "",
            v => { _phonFilterOrigText = v; _phonNeedRebuild = true; }));
        _phonAddRow.AddNode(Input(Loc.S("Filter"), PhonColCorr, 40, "",
            v => { _phonFilterCorrText = v; _phonNeedRebuild = true; }));
        _phonAddRow.AddNode(Spacer(PhonColDel, 28));

        _phonHeaderSep1 = new HorizontalLineNode
        {
            Size = new Vector2(w, 4),
            Position = new Vector2(x, y + 50),
        };

        // Row 3: Add new correction inputs
        _phonHeaderSep2 = new HorizontalListNode
        {
            Size = new Vector2(w, 28),
            ItemSpacing = 4,
            Position = new Vector2(x, y + 56),
        };
        _phonHeaderSep2.AddNode(Input(Loc.S("New original"), PhonColOrig, 40, "",
            v => { _phonNewOrigText = v; }));
        _phonHeaderSep2.AddNode(Input(Loc.S("New corrected"), PhonColCorr, 40, "",
            v => { _phonNewCorrText = v; }));
        _phonHeaderSep2.AddNode(Button(Loc.S("Add"), PhonColDel, () =>
        {
            if (string.IsNullOrWhiteSpace(_phonNewOrigText)) return;
            var correction = new PhoneticCorrection(_phonNewOrigText.Trim(), _phonNewCorrText.Trim());
            if (!_config.PhoneticCorrections.Contains(correction))
            {
                _config.PhoneticCorrections.Add(correction);
                _config.Save();
                _phonNeedRebuild = true;
            }
        }));

        // Data list below
        var dataY = y + 88;
        var dataH = _topContentSize.Y - 88;
        _phonDataList = Panel(new Vector2(x, dataY), new Vector2(w, dataH));
    }

    private void AddPhoneticsNodes()
    {
        AddNode(_phonFilterRow!);
        AddNode(_phonHeaderSep1!);
        AddNode(_phonAddRow!);
        AddNode(_phonHeaderSep2!);
        AddNode(_phonDataList!);
    }

    private void ShowPhoneticsSection(bool visible)
    {
        SetVisible(_phonFilterRow, visible);
        SetVisible(_phonHeaderSep1, visible);
        SetVisible(_phonAddRow, visible);
        SetVisible(_phonHeaderSep2, visible);
        SetVisible(_phonDataList, visible);

        if (visible && !_phonBuilt)
            _phonNeedRebuild = true;
    }

    private void UpdatePhonetics()
    {
        if (_activeTopTab != 2) return;

        if (_phonNeedRebuild)
        {
            _phonNeedRebuild = false;
            _phonBuilt = true;
            RebuildPhoneticsList();
        }
    }

    private void RebuildPhoneticsList()
    {
        var panel = _phonDataList;
        if (panel == null) return;

        var w = _contentWidth;
        panel.Clear();

        // Filter
        var filterOrig = _phonFilterOrigText.Trim();
        var filterCorr = _phonFilterCorrText.Trim();

        var corrections = _config.PhoneticCorrections
            .Where(c => string.IsNullOrEmpty(filterOrig) ||
                        c.OriginalText.Contains(filterOrig, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrEmpty(filterCorr) ||
                        c.CorrectedText.Contains(filterCorr, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.OriginalText)
            .ToList();

        foreach (var correction in corrections)
        {
            var corr = correction;
            var row = new HorizontalListNode { Size = new Vector2(w, 24), ItemSpacing = 4 };
            row.AddNode(Label(corr.OriginalText, PhonColOrig));
            row.AddNode(Label(corr.CorrectedText, PhonColCorr));
            row.AddNode(Button(Loc.S("Delete"), PhonColDel, () =>
            {
                _config.PhoneticCorrections.Remove(corr);
                _config.Save();
                _phonNeedRebuild = true;
            }));
            panel.AddNode(row);
        }

        if (corrections.Count == 0)
            panel.AddNode(Label(Loc.S("No phonetic corrections found."), w));

        panel.RecalculateLayout();
    }
}
