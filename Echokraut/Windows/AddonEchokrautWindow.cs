using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Addon;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.TabBar;

namespace Echokraut.Helper.Addons;

public class AddonEchokrautWindow : NativeAddon
{
    private ResNode mainContainerNode = null!;
    private TabBarNode tabBarNode = null!;
    
    private HorizontalFlexNode searchContainerNode = null!;
    private TextInputNode searchBoxNode = null!;
    private TextNode searchLabelNode = null!;
    
    private VoiceMapOptionNode? selectedOption;
    private ScrollingAreaNode<TreeListNode>? optionsContainerNode = null;
    private readonly List<TreeListCategoryNode> categoryNodes = [];
    private readonly List<VoiceMapOptionNode> voiceMapOptionNodes = [];
    
    private ResNode descriptionContainerNode = null!;
    private TextNode descriptionTextNode = null!;
    private TextNode descriptionVersionTextNode = null!;

    private const float ItemPadding = 5.0f;
    private bool initialized = false;
    public static bool StopRequested = false;
    protected override unsafe void OnSetup(AtkUnitBase* addon) {
    }

    public AddonEchokrautWindow()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "EchokrautConfig", OnPostDraw);
    }

    public void Initialize()
    {
        initialized = true;
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            categoryNodes.Clear();
            voiceMapOptionNodes.Clear();

            mainContainerNode = new ResNode
            {
                Position = ContentStartPosition,
                Size = ContentSize,
                IsVisible = true,
            };
            AttachNode(mainContainerNode);

            tabBarNode = new TabBarNode()
            {
                Height = 28.0f,
                Width = ContentSize.X,
                DrawFlags = DrawFlags.ClickableCursor,
                NodeFlags = NodeFlags.EmitsEvents | NodeFlags.Enabled | NodeFlags.RespondToMouse | NodeFlags.Visible |
                            NodeFlags.Focusable,
                IsVisible = true,
            };
            tabBarNode.AddTab("Players", PlayerTabClicked);
            tabBarNode.AddTab("NPCs", NpcsTabClicked);
            AttachNode(tabBarNode, mainContainerNode);

            BuildSearchContainer();
            BuildDescriptionContainer();
            PlayerTabClicked();
            RecalculateScrollableAreaSize();
            UpdateSizes();
        });
    }

    public void Detach(VoiceMapOptionNode node)
    {
        NativeController.DetachNode(node);
    }

    private void OnPostDraw(AddonEvent type, AddonArgs args)
    {
        if (!initialized)
        {
            Initialize();
        }
    }

    private void PlayerTabClicked()
    {
        BuildVoiceMapCategorys(Plugin.Configuration.MappedPlayers, "Named:", "Random:");
    }
    
    private void NpcsTabClicked()
    {
        BuildVoiceMapCategorys(Plugin.Configuration.MappedNpcs, "Named:", "Random:");
    }

    private void BuildVoiceMapCategorys(List<NpcMapData> npcMapData, string named, string random)
    {
        Plugin.Framework.RunOnFrameworkThread(() => { 
            voiceMapOptionNodes.ForEach(Detach); 
            voiceMapOptionNodes.Clear();
            categoryNodes.ForEach(NativeController.DetachNode);
            categoryNodes.Clear(); 
        });
        
        BuildOptionsContainer();
        BuildVoiceMapOptions(npcMapData, named, random);
    }

    private void BuildVoiceMapOptions(List<NpcMapData> npcMapData, string named, string random)
    {
        TreeListCategoryNode? namedCategoryNode = null, randomCategoryNode = null;
        foreach (var mappedPlayer in npcMapData.FindAll(p => p.IsNamed()))
        {
            if (StopRequested)
                break;
            
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                if (namedCategoryNode == null || randomCategoryNode == null) 
                { 
                    namedCategoryNode = new TreeListCategoryNode {
                        IsVisible = true,
                        SeString = named,
                        OnToggle = isVisible => OnCategoryToggled(isVisible, true),
                        VerticalPadding = 0.0f
                    };
                    AttachNode(namedCategoryNode, optionsContainerNode);
                    randomCategoryNode = new TreeListCategoryNode {
                        IsVisible = true,
                        SeString = random,
                        OnToggle = isVisible => OnCategoryToggled(isVisible, false),
                        VerticalPadding = 0.0f
                    };
                    AttachNode(randomCategoryNode, optionsContainerNode);
                    categoryNodes.Add(namedCategoryNode);
                    categoryNodes.Add(randomCategoryNode);
                }

                var newOptionNode = mappedPlayer.VoiceMapOptionNode;
                newOptionNode!.OnClick = () => OnOptionClicked(newOptionNode);

                namedCategoryNode.AddNode(newOptionNode);
                voiceMapOptionNodes.Add(newOptionNode);
            });
            Thread.Sleep(50);
        }
        
        if (StopRequested)
            return;
        
        foreach (var mappedPlayer in npcMapData.FindAll(p => !p.IsNamed()))
        {
            if (StopRequested)
                break;
            
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                if (namedCategoryNode == null || randomCategoryNode == null) 
                { 
                    namedCategoryNode = new TreeListCategoryNode {
                        IsVisible = true,
                        SeString = named,
                        OnToggle = isVisible => OnCategoryToggled(isVisible, true),
                        VerticalPadding = 0.0f
                    };
                    AttachNode(namedCategoryNode, optionsContainerNode);
                    randomCategoryNode = new TreeListCategoryNode {
                        IsVisible = true,
                        SeString = random,
                        OnToggle = isVisible => OnCategoryToggled(isVisible, false),
                        VerticalPadding = 0.0f
                    };
                    AttachNode(randomCategoryNode, optionsContainerNode);
                    categoryNodes.Add(namedCategoryNode);
                    categoryNodes.Add(randomCategoryNode);
                }
                
                var newOptionNode = mappedPlayer.VoiceMapOptionNode;
                newOptionNode!.OnClick = () => OnOptionClicked(newOptionNode);

                randomCategoryNode.AddNode(newOptionNode);
                voiceMapOptionNodes.Add(newOptionNode);
            });
            Thread.Sleep(50);
        }
    }

    private void BuildOptionsContainer()
    {
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            if (optionsContainerNode != null)
                optionsContainerNode.Dispose();

            optionsContainerNode = new ScrollingAreaNode<TreeListNode>
            {
                IsVisible = true,
                ContentHeight = 1000.0f,
                ScrollSpeed = 24,
            };
            AttachNode(optionsContainerNode, mainContainerNode);
        });
    }

    private void BuildSearchContainer() {
        searchContainerNode = new HorizontalFlexNode {
            Height = 28.0f,
            AlignmentFlags = FlexFlags.FitHeight | FlexFlags.FitWidth,
            IsVisible = true,
        };
        AttachNode(searchContainerNode, mainContainerNode);
        
        searchBoxNode = new TextInputNode {
            IsVisible = true,
            OnInputReceived = OnSearchBoxInputReceived,
        };
        searchContainerNode.AddNode(searchBoxNode);

        searchLabelNode = new TextNode {
            Position = new Vector2(10.0f, 6.0f),
            IsVisible = true,
            TextColor = ColorHelper.GetColor(3),
            String = "Search . . .",
        };
        AttachNode(searchLabelNode, searchBoxNode);

        searchBoxNode.OnFocused += () => {
            searchLabelNode.IsVisible = false;
        };

        searchBoxNode.OnUnfocused += () => {
            if (searchBoxNode.SeString.ToString() is "") {
                searchLabelNode.IsVisible = true;
            }
        };
    }

    private void BuildDescriptionContainer() {
        descriptionContainerNode = new ResNode {
            IsVisible = true,
        };
        AttachNode(descriptionContainerNode, mainContainerNode);
        
        descriptionTextNode = new TextNode {
            AlignmentType = AlignmentType.Center,
            TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            FontSize = 14,
            LineSpacing = 22,
            FontType = FontType.Axis,
            IsVisible = true,
            String = "Please select an option on the left",
            TextColor = ColorHelper.GetColor(1),
        };
        AttachNode(descriptionTextNode, descriptionContainerNode);
        
        descriptionVersionTextNode = new TextNode {
            IsVisible = true,
            AlignmentType = AlignmentType.BottomRight,
            TextColor = ColorHelper.GetColor(3),
        };
        AttachNode(descriptionVersionTextNode, descriptionContainerNode);
    }

    private void OnCategoryToggled(bool isVisible, bool isNamed) {
        var selectionCategory = selectedOption?.MapData?.IsNamed() ?? false;
        if (!isVisible && selectionCategory == isNamed) {
            ClearSelection();
        }

        RecalculateScrollableAreaSize();
    }
    
    private void OnSearchBoxInputReceived(Dalamud.Game.Text.SeStringHandling.SeString searchTerm) {
        List<VoiceMapOptionNode> validOptions = [];
        
        foreach (var option in voiceMapOptionNodes) {
            var isTarget = option.MapData?.IsMatch(searchTerm.ToString());
            option.IsVisible = isTarget ?? false;

            if (isTarget ?? false) {
                validOptions.Add(option);
            }
        }

        foreach (var categoryNode in categoryNodes) {
            categoryNode.RecalculateLayout();
        }

        if (validOptions.All(option => option != selectedOption)) {
            ClearSelection();
        }
        
        optionsContainerNode.ContentNode.RefreshLayout();
        RecalculateScrollableAreaSize();
    }

    private void OnOptionClicked(VoiceMapOptionNode option) {
        ClearSelection();
        
        selectedOption = option;
        selectedOption.IsSelected = true;

        descriptionVersionTextNode.IsVisible = true;
        descriptionVersionTextNode.String = $"Version";
    }

    private void ClearSelection() {
        selectedOption = null;
        foreach (var node in voiceMapOptionNodes) {
            node.IsSelected = false;
            node.IsHovered = false;
        }

        descriptionTextNode.IsVisible = true;
        descriptionTextNode.String = "Please select an option on the left";
        descriptionVersionTextNode.IsVisible = false;
    }

    private void RecalculateScrollableAreaSize() {
        optionsContainerNode.ContentHeight = categoryNodes.Sum(node => node.Height) + 10.0f;
    }

    private void UpdateSizes() {
        searchContainerNode.Size = new Vector2(mainContainerNode.Width, 28.0f);
        tabBarNode.Size = new Vector2(mainContainerNode.Width, 28.0f);
        tabBarNode.Position = new Vector2(0.0f, searchContainerNode.Height + ItemPadding);
        optionsContainerNode.Position = new Vector2(0.0f, tabBarNode.Position.Y + tabBarNode.Height + ItemPadding);
        optionsContainerNode.Size = new Vector2(mainContainerNode.Width * 3.0f / 5.0f - ItemPadding, mainContainerNode.Height - (searchContainerNode.Height + tabBarNode.Height) - ItemPadding*2);
        descriptionContainerNode.Position = new Vector2(mainContainerNode.Width * 3.0f / 5.0f, tabBarNode.Position.Y + tabBarNode.Height + ItemPadding);
        descriptionContainerNode.Size = new Vector2(mainContainerNode.Width * 2.0f / 5.0f, mainContainerNode.Height - (searchContainerNode.Height + tabBarNode.Height) - ItemPadding*2);
        
        descriptionVersionTextNode.Size = new Vector2(200.0f, 28.0f);
        descriptionVersionTextNode.Position = descriptionContainerNode.Size - descriptionVersionTextNode.Size - new Vector2(8.0f, 8.0f);
        
        descriptionTextNode.Size = descriptionContainerNode.Size - new Vector2(16.0f, 16.0f) - new Vector2(0.0f, descriptionVersionTextNode.Height);
        descriptionTextNode.Position = new Vector2(8.0f, 8.0f);
        
        foreach (var node in categoryNodes) {
            node.Width = optionsContainerNode.ContentNode.Width;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        StopRequested = true;
    }
}
