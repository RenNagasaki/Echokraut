using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static Echokraut.Helper.Addons.AddonTalkHelper;
using System.Runtime.InteropServices;
using System;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.PublicInstance;
using Echokraut.Enums;
using Echokraut.Utils;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;

namespace Echokraut.Helper.Functional
{
    public unsafe static class ClickHelper
    {
        public static void ClickDialogue(nint addon)
        {
            var unitBase = (AtkUnitBase*)addon;

            if (unitBase != null && AtkStage.Instance() != null)
            {
                var evt = stackalloc AtkEvent[1]
                {
                    new()
                    {
                        Listener = (AtkEventListener*)unitBase,
                        Flags = 132,
                        Target = &AtkStage.Instance()->AtkEventTarget
                    }
                };
                var data = stackalloc AtkEventData[1];

                unitBase->ReceiveEvent(AtkEventType.MouseClick, 0, evt, data);
            }
        }
    }
}
