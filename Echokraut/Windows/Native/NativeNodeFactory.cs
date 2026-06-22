using System;
using System.Numerics;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

/// <summary>
/// Shared static node factories for the native (KamiToolKit) windows. Each window previously
/// carried byte-identical private copies of these (DRY). Import with
/// <c>using static Echokraut.Windows.Native.NativeNodeFactory;</c> so existing unqualified
/// call sites keep working unchanged.
/// </summary>
internal static class NativeNodeFactory
{
    /// <summary>Auto-sizing text button: grows past <paramref name="minWidth"/> to fit the label.</summary>
    public static TextButtonNode Button(string label, float minWidth, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(minWidth, 24), String = label };
        var textW = node.LabelNode.GetTextDrawSize(label).X + 36;
        if (textW > minWidth) node.Size = new Vector2(textW, 24);
        node.OnClick = onClick;
        return node;
    }

    /// <summary>Single-line text input with placeholder + completion callback.</summary>
    public static TextInputNode Input(string placeholder, float width, int maxChars, string initial, Action<string> onComplete)
    {
        var node = new TextInputNode
        {
            Size = new Vector2(width, 28),
            MaxCharacters = maxChars,
            PlaceholderString = placeholder,
            String = initial,
        };
        node.OnInputReceived = s => onComplete(s.ToString());
        return node;
    }

    /// <summary>Enabled/disabled dim via alpha (1.0 / 0.4). Null-safe.</summary>
    public static void Dim(NodeBase? node, bool enabled)
    {
        if (node != null) node.Alpha = enabled ? 1.0f : 0.4f;
    }

    /// <summary>Null-safe visibility toggle.</summary>
    public static void SetVisible(NodeBase? node, bool visible)
    {
        if (node != null) node.IsVisible = visible;
    }

    /// <summary>
    /// Adds a collapsible section to a <see cref="ScrollingListNode"/> using a TextButtonNode
    /// toggle. Uses component events (no CollisionNode), so it works inside nested containers.
    /// Returns the toggle button so the caller can position it.
    /// </summary>
    public static TextButtonNode CreateCollapsibleSection(
        ScrollingListNode list, string title, float width, bool startCollapsed, NodeBase[] contentNodes)
    {
        var arrow = startCollapsed ? "[+]" : "[-]";
        TextButtonNode? toggle = null;
        toggle = new TextButtonNode { Size = new Vector2(width, 24), String = $"{arrow} {title}" };
        toggle.OnClick = () =>
        {
            var isHidden = contentNodes.Length > 0 && !contentNodes[0].IsVisible;
            foreach (var n in contentNodes)
                n.IsVisible = isHidden;
            toggle!.String = isHidden ? $"[-] {title}" : $"[+] {title}";
            list.RecalculateLayout();
        };

        if (startCollapsed)
            foreach (var n in contentNodes)
                n.IsVisible = false;

        list.AddNode(toggle);
        foreach (var n in contentNodes)
            list.AddNode(n);

        return toggle;
    }

    /// <summary>
    /// Theme-adaptive text colour for text rendered directly on the window background — follows the
    /// in-game UI design (dark on the light parchment theme, light on dark themes).
    ///
    /// KamiToolKit's <see cref="TextNode"/> defaults TextColor to <c>GetColor(8)</c> (renders white/
    /// illegible under the latest KTK). <c>GetColor(50)</c> is the *interactive* label colour — it's
    /// only readable because buttons/dropdowns sit on a darker element background; on the lighter
    /// window background it's washed out. The colour that's actually meant for text on the window
    /// surface is what KTK's own <c>WindowNode</c> uses for its Axis subtitle: <c>GetColor(3)</c>.
    /// </summary>
    public static Vector4 LabelColor => ColorHelper.GetColor(3);

    /// <summary>
    /// Applies <see cref="LabelColor"/> to a <see cref="CheckboxNode"/>'s label. KTK's CheckboxNode
    /// hard-codes its label to GetColor(8) (renders white under the latest KTK); this re-colours it
    /// to the theme-adaptive label colour. Returns the node so it can wrap an initializer inline.
    /// </summary>
    public static CheckboxNode WithLabelColor(CheckboxNode checkbox)
    {
        checkbox.Label.TextColor = LabelColor;
        return checkbox;
    }

    /// <summary>FFXIV-Axis text label, 12pt, sized to <paramref name="width"/>.</summary>
    public static TextNode Label(string text, float width) => new()
    {
        Size = new Vector2(width, 18),
        String = text,
        FontType = FontType.Axis,
        FontSize = 12,
        TextColor = LabelColor,
    };

    /// <summary>Column-header label that ellipsizes overflow instead of bleeding into the next column.</summary>
    public static TextNode HeaderLabel(string text, float width)
    {
        var node = Label(text, width);
        node.AddTextFlags(TextFlags.Ellipsis);
        return node;
    }

    /// <summary>Thin horizontal divider line.</summary>
    public static HorizontalLineNode Separator(float width) => new()
    {
        Size = new Vector2(width, 4),
    };

    /// <summary>Invisible node that reserves layout space in a HorizontalListNode.</summary>
    public static ResNode Spacer(float width, float height) => new()
    {
        Size = new Vector2(width, height),
        Alpha = 0,
    };

    /// <summary>Standard icon-button tints: full colour, brightened on hover.</summary>
    public static readonly Vector3 NormalTint = new(1f, 1f, 1f);
    public static readonly Vector3 HoverTint = new(1.4f, 1.4f, 1.4f);

    /// <summary>
    /// Wires the standard hover behaviour for a <see cref="DynamicIconButtonNode"/>: brighten the
    /// icon on MouseOver, restore on MouseOut, and show/hide the tooltip via <paramref name="onHover"/>
    /// / <paramref name="onOut"/>. <paramref name="isAlive"/> is checked inside the events so they
    /// no-op once the owning window has dropped its node references during teardown — this preserves
    /// the per-site null guards the inline copies used to carry.
    /// </summary>
    public static void WireIconButtonHover(DynamicIconButtonNode node, Func<bool> isAlive, Action onHover, Action onOut)
    {
        node.ImageNode.MultiplyColor = NormalTint;
        node.ImageNode.AddEvent(AtkEventType.MouseOver, () =>
        {
            if (!isAlive()) return;
            node.ImageNode.MultiplyColor = HoverTint;
            onHover();
        });
        node.ImageNode.AddEvent(AtkEventType.MouseOut, () =>
        {
            if (!isAlive()) return;
            node.ImageNode.MultiplyColor = NormalTint;
            onOut();
        });
    }
}
