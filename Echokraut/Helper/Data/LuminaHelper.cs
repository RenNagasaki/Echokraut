using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Lumina.Excel.Sheets;
using System;
using System.Reflection;
using Echokraut.DataClasses;
using Echokraut.Helper.Data;

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

        internal static ENpcBase? GetENpcBase(uint dataId, EKEventId eventId)
        {
            try
            {
                return Plugin.DataManager.GetExcelSheet<ENpcBase>()!.GetRow(dataId);
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while starting voice inference: {ex}",
                                eventId);
            }

            return null;
        }

        internal static Race? GetRace(byte speakerRace, EKEventId eventId)
        {
            try
            {
                return Plugin.DataManager.GetExcelSheet<Race>()?.GetRow(speakerRace) ?? null;
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while starting voice inference: {ex}", eventId);
            }

            return null;
        }
    }
}
