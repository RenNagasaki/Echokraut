using System.Collections.Generic;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;

namespace Echokraut.Windows.E2eUi;

/// <summary>
/// Reads the texts a window really shows, straight from its ATK node tree (read-only, framework thread):
/// every text node whose own and every ancestor's visibility flag is set, components included. Used by the
/// E2E stage <c>ui</c> for section visibility, truncation and untranslated texts.
/// </summary>
internal static unsafe class AtkTextCollector
{
    private const int MaxNodes = 20000;

    internal static List<OwnedWindowText> Collect(AtkUnitBase* addon)
    {
        var texts = new List<OwnedWindowText>();
        if (addon == null || addon->RootNode == null) return texts;
        var seen = new HashSet<nint>();
        Walk(addon->RootNode, texts, seen);
        return texts;
    }

    private static void Walk(AtkResNode* node, List<OwnedWindowText> texts, HashSet<nint> seen)
    {
        if (node == null || seen.Count >= MaxNodes || !seen.Add((nint)node)) return;
        if (!node->IsVisible()) return;

        if (node->Type == NodeType.Text)
            Add((AtkTextNode*)node, texts);

        // KamiToolKit attaches a component's children as SIBLINGS of the component's root node (its collision
        // node, NodeBase.Linking: AsLastChild → AfterAllSiblings of UldManager.RootNode), so the whole sibling
        // chain of that root is the component's content — walking only below the root missed every
        // ScrollingListNode's items (run 20261125-175241).
        if ((ushort)node->Type >= 1000)
        {
            var component = ((AtkComponentNode*)node)->Component;
            if (component != null) WalkChain(component->UldManager.RootNode, texts, seen);
        }

        WalkChain(node->ChildNode, texts, seen);
    }

    /// <summary><paramref name="start"/> and every sibling in both directions (the seen set stops loops and double counts).</summary>
    private static void WalkChain(AtkResNode* start, List<OwnedWindowText> texts, HashSet<nint> seen)
    {
        for (var n = start; n != null; n = n->PrevSiblingNode) Walk(n, texts, seen);
        if (start != null)
            for (var n = start->NextSiblingNode; n != null; n = n->NextSiblingNode) Walk(n, texts, seen);
    }

    private static void Add(AtkTextNode* node, List<OwnedWindowText> texts)
    {
        var text = new ReadOnlySeString(node->GetText().AsSpan()).ExtractText().Trim();
        if (text.Length == 0) return;
        ushort w = 0, h = 0;
        node->GetTextDrawSize(&w, &h, considerScale: false);
        var wraps = (node->TextFlags & (TextFlags.WordWrap | TextFlags.MultiLine)) != 0;
        texts.Add(new OwnedWindowText(text, node->AtkResNode.Width, w, wraps, node->AtkResNode.ScreenX, node->AtkResNode.ScreenY));
    }
}
