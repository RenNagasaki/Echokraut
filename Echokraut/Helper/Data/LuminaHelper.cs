using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Lumina.Excel.Sheets;
using System;

namespace Echokraut.Helper.DataHelper
{
    public static class LuminaHelper
    {
        private static ushort TerritoryRow;
        private static TerritoryType? Territory;
        public static TerritoryType? GetTerritory()
        {
            var territoryRow = Plugin.ClientState.TerritoryType;
            if (territoryRow != TerritoryRow)
            {
                TerritoryRow = territoryRow;
                Territory = Plugin.DataManager.GetExcelSheet<TerritoryType>()!.GetRow(territoryRow);
            }

            return Territory;
        }

        internal static ENpcBase? GetENpcBase(uint dataId)
        {
            return Plugin.DataManager.GetExcelSheet<ENpcBase>()!.GetRow(dataId);
        }

        internal static Race? GetRace(byte speakerRace)
        {
            return Plugin.DataManager.GetExcelSheet<Race>()?.GetRow(speakerRace) ?? null;
        }
    }
}
