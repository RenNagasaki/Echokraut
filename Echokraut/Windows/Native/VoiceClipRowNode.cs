using System.Collections.Generic;
using System.Numerics;
using Echokraut.DataClasses.Database;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

/// <summary>
/// Data model for one quest-type group row under an NPC in the Voice Clip Manager tree.
/// Reference type on purpose: the virtualized <see cref="KamiToolKit.Nodes.TreeListNode{T,TU}"/>
/// tracks the selected item via reference equality (see GenericUtil.AreEqual), so the same
/// instance must live in the tree's Options for selection highlighting to persist.
/// </summary>
internal sealed class VcRow
{
    public string NpcKey = "";
    public string DetailTitle = "";                       // "NpcName — Label" shown in the detail window
    public string Label = "";                             // row title (quest type / "Chat")
    public string Subtitle = "";                          // "X Voice Clips | Y Generated"
    public uint IconId;
    public List<VoiceClipEntity> VoiceClips = new();
}

/// <summary>
/// Virtualized item view for a <see cref="VcRow"/>: FFXIV quest icon + title + subtitle,
/// styled like the old IconListItemNode. Hover/selected backgrounds and click handling are
/// provided by the base <see cref="SelectableNode"/> and driven by the parent tree.
/// </summary>
internal sealed class VoiceClipRowNode : TreeListItemNode<VcRow>, ITreeListItemNode
{
    /// <summary>Fixed per-row height used by the tree's virtualization (matches the old 41px rows).</summary>
    public static float ItemHeight => 41f;

    private readonly IconNode iconNode;
    private readonly TextNode titleNode;
    private readonly TextNode subtitleNode;

    private const float TextLeft = 44f;

    // FFXIV journal-style text colors (mirrors Echotools IconListItemNode).
    private static readonly Vector4 TitleColor = new(0.49f, 0.32f, 0.23f, 1f);   // 7D523B
    private static readonly Vector4 SubtitleColor = new(0.67f, 0.47f, 0.32f, 1f); // AB7852
    private static readonly Vector4 OutlineColor = new(1f, 0.95f, 0.91f, 1f);     // FFF3E7

    public VoiceClipRowNode()
    {
        iconNode = new IconNode
        {
            Position = new Vector2(4f, 4f),
            Size = new Vector2(32f, 32f),
        };
        iconNode.IconExtras.IsVisible = false;
        iconNode.AttachNode(this);

        titleNode = new TextNode
        {
            Position = new Vector2(TextLeft, 5f),
            FontType = FontType.Axis,
            FontSize = 14,
            TextColor = TitleColor,
            TextOutlineColor = OutlineColor,
        };
        titleNode.AttachNode(this);

        subtitleNode = new TextNode
        {
            Position = new Vector2(TextLeft, 22f),
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = SubtitleColor,
            TextOutlineColor = OutlineColor,
        };
        subtitleNode.AttachNode(this);
    }

    protected override void SetNodeData(VcRow itemData)
    {
        iconNode.IconId = itemData.IconId;
        titleNode.String = itemData.Label;
        subtitleNode.String = itemData.Subtitle;
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();

        var textW = Width - TextLeft - 4f;
        titleNode.Size = new Vector2(textW, 16f);
        subtitleNode.Size = new Vector2(textW, 14f);
    }
}
