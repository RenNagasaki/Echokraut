using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static Echokraut.Helper.AddonTalkHelper;
using System.Runtime.InteropServices;
using System;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.PublicInstance;
using Echokraut.Enums;
using Echokraut.Utils;
using Dalamud.Game.Addon.Lifecycle;
using ECommons.DalamudServices;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.UIHelpers.AddonMasterImplementations;

namespace Echokraut.Helper
{
    public unsafe static class ClickHelper
    {
        public static void Click(nint addon)
        {
            if (((AtkUnitBase*)addon)->IsVisible)
            {
                new AddonMaster.Talk(addon).Click();
            }
        }
    }
}
