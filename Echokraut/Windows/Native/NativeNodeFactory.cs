using System;
using System.Numerics;
using KamiToolKit;
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
}
