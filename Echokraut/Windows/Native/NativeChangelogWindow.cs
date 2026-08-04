using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

using static Echokraut.Windows.Native.NativeNodeFactory;

namespace Echokraut.Windows.Native;

/// <summary>
/// One-shot popup that surfaces every changelog entry the user hasn't seen yet
/// (versions strictly greater than <see cref="DataClasses.Configuration.LastSeenChangelogVersion"/>
/// and at most the current plugin version). Pulled up automatically by
/// <see cref="Plugin.HandleStartup"/> after FirstTime, dismissed via the bottom
/// "Got it" button which calls <see cref="IChangelogService.MarkAllSeen"/>.
/// </summary>
public sealed unsafe class NativeChangelogWindow : NativeAddon
{
    private readonly IChangelogService _changelog;

    public NativeChangelogWindow(IChangelogService changelog)
    {
        _changelog = changelog ?? throw new ArgumentNullException(nameof(changelog));
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, System.Span<AtkValue> atkValueSpan)
    {
        var pos = ContentStartPosition;
        var size = ContentSize;
        var w = size.X - 16;  // ScrollingListNode reserves 16px for its scrollbar.

        // Bottom-anchored close button (outside the scrolling area so it stays visible
        // even when the user scrolls the changelog body).
        const float buttonHeight = 30f;
        const float buttonGap = 8f;
        var closeBtn = new TextButtonNode
        {
            Position = new Vector2(pos.X + (size.X - 200) / 2, pos.Y + size.Y - buttonHeight),
            Size = new Vector2(200, buttonHeight),
            String = Loc.S("I've read it"),
            OnClick = () =>
            {
                _changelog.MarkAllSeen();
                Close();
            },
        };
        AddNode(closeBtn);

        // Scrolling content area for the changelog entries.
        var listSize = new Vector2(size.X, size.Y - buttonHeight - buttonGap);
        var list = new ScrollingListNode
        {
            Position = pos,
            Size = listSize,
            FitWidth = true,
            // ItemSpacing 2 (vs 6 default) — the changelog content already carries its
            // own visual breathing room via the source ===…=== headers and bullet
            // indentation, so a tight inter-node gap reads as "single document" instead
            // of "stack of cards".
            ItemSpacing = 2,
        };
        AddNode(list);

        var entries = _changelog.GetUnseenChangelogs();
        if (entries.Count == 0)
        {
            // Should never happen — Plugin.HandleStartup gates the toggle on
            // HasUnseenChangelogs. Leave a fallback so the window degrades gracefully
            // if the gate is bypassed (e.g. someone wires up a manual /command later).
            list.AddNode(BuildHeader(Loc.S("No changelog available."), w));
            return;
        }

        // Top-of-window banner: pulled out of the per-entry rendering so a multi-version
        // sequence reads as a single document, not a stack of separate posts.
        list.AddNode(BuildHeader(
            Loc.S("Echokraut Update — What's New"), w, fontSize: 16));
        list.AddNode(new HorizontalLineNode { Size = new Vector2(w, 4) });

        foreach (var entry in entries)
        {
            // Per-version section header (e.g. "v0.19.0.0").
            list.AddNode(BuildHeader(entry.Version, w, fontSize: 14));

            // The body is split into multiple smaller MultiLine TextNodes — one per
            // section in the source file. Empirically, KamiToolKit/ATK does NOT render
            // a single MultiLine TextNode whose Size.Y exceeds a few hundred pixels;
            // existing pattern (NativeFirstTimeWindow._finishDetails) caps at 140px ≈ 7
            // lines. Splitting on the source's "===…===" divider rule keeps each chunk
            // small. A thin <see cref="HorizontalLineNode"/> goes between each chunk
            // for visual structure — combined with the tightened EstimateSectionHeight
            // (+4px gutter) and ItemSpacing (2), the gap reads as one visual line, not
            // the 4-5 blank lines an earlier oversized-padding version produced.
            foreach (var section in SplitIntoSections(entry.Content))
            {
                if (string.IsNullOrWhiteSpace(section)) continue;
                var sectionNode = new TextNode
                {
                    Size = new Vector2(w, EstimateSectionHeight(section)),
                    String = section,
                    FontType = FontType.Axis,
                    FontSize = 12,
                    TextColor = LabelColor,
                };
                sectionNode.AddTextFlags(TextFlags.WordWrap, TextFlags.MultiLine);
                list.AddNode(sectionNode);
                list.AddNode(new HorizontalLineNode { Size = new Vector2(w, 4) });
            }
        }

        // Without this, the scrollbar isn't aware of cumulative child height and the
        // user can't scroll past the first viewport — content rendered below stays
        // unreachable. Mirrors the pattern in NativeConfigWindow.Logs / VoiceSelection
        // / Phonetics where every list build ends with the same call.
        list.RecalculateLayout();
    }

    private static TextNode BuildHeader(string text, float width, int fontSize = 12) => new()
    {
        Size = new Vector2(width, fontSize + 8),
        String = text,
        FontType = FontType.Axis,
        FontSize = (byte)fontSize,
    };

    /// <summary>
    /// Splits the source changelog content into renderable chunks at two boundary types:
    /// <list type="bullet">
    /// <item>Lines made entirely of '=' chars — the section-divider rule the embedded
    ///     files use between major sections (NEUE HAUPTFEATURES, VERBESSERUNGEN, ...).</item>
    /// <item>Blank lines — the long-form format separates individual <c>[NEU]</c> /
    ///     <c>[UI]</c> entries with a single blank line. Splitting here gives one
    ///     <see cref="TextNode"/> per entry instead of one giant node per major section,
    ///     which the FFXIV ATK text renderer can't display when <c>Size.Y</c> exceeds a
    ///     few hundred pixels.</item>
    /// </list>
    /// Boundary lines themselves are consumed (not retained as content). Adjacent
    /// boundaries (e.g. <c>===</c> followed by a blank line) yield empty sections that
    /// the caller skips via <see cref="string.IsNullOrWhiteSpace(string)"/>.
    /// </summary>
    internal static IEnumerable<string> SplitIntoSections(string content)
    {
        if (string.IsNullOrEmpty(content)) yield break;
        var sb = new StringBuilder();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var isDivider = line.Length > 0 && line.All(c => c == '=');
            var isBlank = line.Length == 0;
            if (isDivider || isBlank)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString().Trim('\n', '\r');
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(line).Append('\n');
            }
        }
        if (sb.Length > 0) yield return sb.ToString().Trim('\n', '\r');
    }

    /// <summary>
    /// Tight per-section height: line count × 17px (matches FontSize 12 on Axis) plus
    /// a small 4px bottom gutter. Anything bigger compounds across the dozens of small
    /// chunks the splitter produces and reads as 4-5 blank lines between every section
    /// — earlier +24px padding hit exactly that complaint. With <see cref="TextFlags.WordWrap"/>
    /// long lines may still wrap and this underestimates by one line; the source files
    /// are pre-wrapped at ~80 chars so wraps are rare and clipping a single trailing
    /// line of a single edge-case entry is preferable to inserting visible empty rows
    /// between every entry.
    /// </summary>
    private static int EstimateSectionHeight(string section)
    {
        if (string.IsNullOrEmpty(section)) return 17;
        var lineCount = section.Count(c => c == '\n') + 1;
        return lineCount * 17 + 4;
    }
}
