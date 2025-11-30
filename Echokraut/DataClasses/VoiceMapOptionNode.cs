using System.Numerics;
using Dalamud.Game.Addon.Events;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using Action = System.Action;
namespace Echokraut.DataClasses;

public class VoiceMapOptionNode : SimpleComponentNode {

    private readonly NineGridNode hoveredBackgroundNode;
    private readonly NineGridNode selectedBackgroundNode;
    private readonly CheckboxNode checkboxNode;
    private readonly TextNode mapNameNode;
    private readonly TextNode voiceNameNode;
    private NpcMapData? mapData;

    public VoiceMapOptionNode() {
        hoveredBackgroundNode = new SimpleNineGridNode {
            NodeId = 2,
            TexturePath = "ui/uld/ListItemA.tex",
            TextureCoordinates = new Vector2(0.0f, 22.0f),
            TextureSize = new Vector2(64.0f, 22.0f),
            TopOffset = 6,
            BottomOffset = 6,
            LeftOffset = 16,
            RightOffset = 1,
            IsVisible = false,
        };
        Plugin.NativeController.AttachNode(hoveredBackgroundNode, this);
        
        selectedBackgroundNode = new SimpleNineGridNode {
            NodeId = 3,
            TexturePath = "ui/uld/ListItemA.tex",
            TextureCoordinates = new Vector2(0.0f, 0.0f),
            TextureSize = new Vector2(64.0f, 22.0f),
            TopOffset = 6,
            BottomOffset = 6,
            LeftOffset = 16,
            RightOffset = 1,
            IsVisible = false,
        };
        Plugin.NativeController.AttachNode(selectedBackgroundNode, this);
        
        checkboxNode = new CheckboxNode {
            NodeId = 4,
            IsVisible = true,
            OnClick = ToggleMapping,
        };
        Plugin.NativeController.AttachNode(checkboxNode, this);

        mapNameNode = new TextNode {
            NodeId = 5,
            IsVisible = true,
            TextFlags = TextFlags.AutoAdjustNodeSize | TextFlags.Ellipsis,
            AlignmentType = AlignmentType.BottomLeft,
            TextColor = ColorHelper.GetColor(1),
        };
        Plugin.NativeController.AttachNode(mapNameNode, this);
        
        voiceNameNode = new TextNode {
            NodeId = 6,
            IsVisible = true,
            FontType = FontType.Axis,
            TextFlags = TextFlags.AutoAdjustNodeSize | TextFlags.Ellipsis,
            AlignmentType = AlignmentType.TopLeft,
            TextColor = ColorHelper.GetColor(3),
        };
        Plugin.NativeController.AttachNode(voiceNameNode, this);
        
        CollisionNode.DrawFlags = DrawFlags.ClickableCursor;
        CollisionNode.AddEvent(AtkEventType.MouseOver, () => {
            if (!IsSelected) {
                IsHovered = true;
            }
        });
        
        CollisionNode.AddEvent(AtkEventType.MouseClick, OnClick!);
        
        CollisionNode.AddEvent(AtkEventType.MouseOut, () => {
            IsHovered = false;
        });
    }
    
    public required NpcMapData? MapData {
        get => mapData;
        set {
            mapData = value;
            mapNameNode.String = value?.Name ?? string.Empty;
            voiceNameNode.String = value?.Voice?.VoiceName ?? string.Empty;
            checkboxNode.IsChecked = value?.Active  ?? false;
        }
    }
    
    private void ToggleMapping(bool shouldEnableMapping) {
        if (mapData != null)
            mapData.Active = shouldEnableMapping;
        
        OnClick?.Invoke();
    }

    public Action? OnClick { get; set; }
    
    public bool IsHovered {
        get => hoveredBackgroundNode.IsVisible;
        set => hoveredBackgroundNode.IsVisible = value;
    }
    
    public bool IsSelected {
        get => selectedBackgroundNode.IsVisible;
        set {
            selectedBackgroundNode.IsVisible = value;
            hoveredBackgroundNode.IsVisible = !value;
        }
    }

    protected override void OnSizeChanged() {
        base.OnSizeChanged();
        hoveredBackgroundNode.Size = Size;
        selectedBackgroundNode.Size = Size;

        checkboxNode.Size = new Vector2(Height, Height) * 3.0f / 4.0f;
        checkboxNode.Position = new Vector2(Height, Height) / 8.0f;

        mapNameNode.Height = Height / 2.0f;
        mapNameNode.Position = new Vector2(Height + Height / 3.0f, 0.0f);
        
        voiceNameNode.Height = Height / 2.0f;
        voiceNameNode.Position = new Vector2(Height * 2.0f, Height / 2.0f);
    }
}
