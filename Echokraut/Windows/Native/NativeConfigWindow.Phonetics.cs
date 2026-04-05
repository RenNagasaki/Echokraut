using System;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Localization;
using Echotools.UI.Nodes;
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
    private PaginationBar? _phonPaginationBar;
    private const int PhonPageSize = 100;

    private string _phonFilterOrigText = "";
    private string _phonFilterCorrText = "";
    private string _phonNewOrigText = "";
    private string _phonNewCorrText = "";

    private bool _phonNeedRebuild;
    private bool _phonBuilt;

    // Column widths (computed at setup)
    private float _phonColField;
    private float _phonColBtn;
    private float _phonColBtnPadL;

    private void SetupPhonetics()
    {
        var w = _contentWidth;
        var y = _topContentPos.Y;
        var x = _topContentPos.X;

        // 40% each field, button auto-sized and centered in remaining space
        _phonColField = (w - 2 * 4) * 0.42f; // 42% of width minus gaps
        var addBtn = Button(Loc.S("Add"), 60, () =>
        {
            if (string.IsNullOrWhiteSpace(_phonNewOrigText)) return;
            _npcData.UpsertPhoneticCorrection(_phonNewOrigText.Trim(), _phonNewCorrText.Trim());
            _phonNeedRebuild = true;
        });
        _phonColBtn = addBtn.Size.X;
        var remaining = w - _phonColField * 2 - _phonColBtn - 3 * 4;
        _phonColBtnPadL = remaining / 2;

        // Row 1: Column headers
        _phonFilterRow = new HorizontalListNode
        {
            Size = new Vector2(w, 20),
            ItemSpacing = 4,
            Position = new Vector2(x, y),
        };
        _phonFilterRow.AddNode(Label(Loc.S("Original"), _phonColField));
        _phonFilterRow.AddNode(Label(Loc.S("Corrected"), _phonColField));
        _phonFilterRow.AddNode(Label("", _phonColBtn + remaining));

        // Row 2: Filter inputs below headers
        _phonAddRow = new HorizontalListNode
        {
            Size = new Vector2(w, 28),
            ItemSpacing = 4,
            Position = new Vector2(x, y + 20),
        };
        _phonAddRow.AddNode(Input(Loc.S("Filter"), _phonColField, 40, "",
            v => { _phonFilterOrigText = v; _phonNeedRebuild = true; }));
        _phonAddRow.AddNode(Input(Loc.S("Filter"), _phonColField, 40, "",
            v => { _phonFilterCorrText = v; _phonNeedRebuild = true; }));
        _phonAddRow.AddNode(Spacer(_phonColBtn + remaining, 28));

        _phonHeaderSep1 = new HorizontalLineNode
        {
            Size = new Vector2(w, 4),
            Position = new Vector2(x, y + 50),
        };

        // Row 3: Add new correction inputs (button centered in remaining space)
        _phonHeaderSep2 = new HorizontalListNode
        {
            Size = new Vector2(w, 28),
            ItemSpacing = 4,
            Position = new Vector2(x, y + 56),
        };
        _phonHeaderSep2.AddNode(Input(Loc.S("New original"), _phonColField, 40, "",
            v => { _phonNewOrigText = v; }));
        _phonHeaderSep2.AddNode(Input(Loc.S("New corrected"), _phonColField, 40, "",
            v => { _phonNewCorrText = v; }));
        _phonHeaderSep2.AddNode(Spacer(_phonColBtnPadL, 28));
        _phonHeaderSep2.AddNode(addBtn);

        // Pagination bar
        const float paginationH = 28f;
        var pagY = _topContentPos.Y + _topContentSize.Y - paginationH;
        _phonPaginationBar = new PaginationBar(
            new Vector2(x, pagY), w,
            page => _phonNeedRebuild = true);

        // Data list below (above pagination)
        var dataY = y + 88;
        var dataH = pagY - dataY - 4;
        _phonDataList = Panel(new Vector2(x, dataY), new Vector2(w, dataH));
    }

    private void AddPhoneticsNodes()
    {
        AddNode(_phonFilterRow!);
        AddNode(_phonHeaderSep1!);
        AddNode(_phonAddRow!);
        AddNode(_phonHeaderSep2!);
        AddNode(_phonDataList!);
        if (_phonPaginationBar != null)
            foreach (var node in _phonPaginationBar.Nodes)
                AddNode(node);
    }

    private void ShowPhoneticsSection(bool visible)
    {
        SetVisible(_phonFilterRow, visible);
        SetVisible(_phonHeaderSep1, visible);
        SetVisible(_phonAddRow, visible);
        SetVisible(_phonHeaderSep2, visible);
        SetVisible(_phonDataList, visible);
        if (_phonPaginationBar != null)
            foreach (var node in _phonPaginationBar.Nodes)
                SetVisible(node, visible);

        if (visible && !_phonBuilt)
            _phonNeedRebuild = true;
    }

    private void UpdatePhonetics()
    {
        if (_activeTopTab != 2) return;

        _phonPaginationBar?.Update();

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

        var corrections = _npcData.GetPhoneticCorrections()
            .Where(c => string.IsNullOrEmpty(filterOrig) ||
                        c.OriginalText.Contains(filterOrig, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrEmpty(filterCorr) ||
                        c.CorrectedText.Contains(filterCorr, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.OriginalText)
            .ToList();

        _phonPaginationBar?.SetTotalItems(corrections.Count, PhonPageSize);
        var page = _phonPaginationBar?.CurrentPage ?? 0;
        var pageStart = page * PhonPageSize;
        var pageEnd = Math.Min(pageStart + PhonPageSize, corrections.Count);

        for (var idx = pageStart; idx < pageEnd; idx++)
        {
            var corr = corrections[idx];
            var row = new HorizontalListNode { Size = new Vector2(w, 24), ItemSpacing = 4 };
            row.AddNode(Label(corr.OriginalText, _phonColField));
            row.AddNode(Label(corr.CorrectedText, _phonColField));
            row.AddNode(Spacer(_phonColBtnPadL, 24));
            row.AddNode(Button(Loc.S("Delete"), 60, () =>
            {
                _npcData.DeletePhoneticCorrection(corr.OriginalText);
                _phonNeedRebuild = true;
            }));
            panel.AddNode(row);
        }

        if (corrections.Count == 0)
            panel.AddNode(Label(Loc.S("No phonetic corrections found."), w));

        panel.RecalculateLayout();
    }
}
