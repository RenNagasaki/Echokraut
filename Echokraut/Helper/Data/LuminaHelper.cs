using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Lumina.Excel.Sheets;
using System;

namespace Echokraut.Helper.DataHelper
{
    public static class LuminaHelper
    {
        private static IClientState ClientState;
        private static IDataManager DataManager;
        private static ushort TerritoryRow;
        private static TerritoryType? Territory;

        public static void Setup(IClientState clientState, IDataManager dataManager)
        {
            ClientState = clientState;
            DataManager = dataManager;
        }
        public static TerritoryType? GetTerritory()
        {
            var territoryRow = ClientState.TerritoryType;
            if (territoryRow != TerritoryRow)
            {
                TerritoryRow = territoryRow;
                Territory = DataManager.GetExcelSheet<TerritoryType>()!.GetRow(territoryRow);
            }

            return Territory;
        }

        internal static ENpcBase? GetENpcBase(uint dataId)
        {
            return DataManager.GetExcelSheet<ENpcBase>()!.GetRow(dataId);
        }

        internal static Race? GetRace(byte speakerRace)
        {
            return DataManager.GetExcelSheet<Race>()?.GetRow(speakerRace) ?? null;
        }
    }
}
