using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.System;

namespace Echokraut.DataClasses;

public static unsafe class Addon {
    public static AtkUnitBase* GetAddonForNode(NodeBase node)
        => RaptureAtkUnitManager.Instance()->GetAddonByNode((AtkResNode*)node);

    public static void UpdateCollisionForNode(NodeBase node) {
        var addon = GetAddonForNode(node);
        if (addon is not null) {
            addon->UpdateCollisionNodeList(false);
        }
    }
}
