using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper
{
    public static class NpcRacesHelper
    {
        public static Dictionary<int, NpcRaces> ModelsToRaceMap = new Dictionary<int, NpcRaces>
        {
            { 11001, NpcRaces.Amaljaa },
            { 11002, NpcRaces.Ixal },
            { 11003, NpcRaces.Kobold },
            { 11004, NpcRaces.Goblin },
            { 11005, NpcRaces.Sylph },
            { 11006, NpcRaces.Moogle },
            { 11013, NpcRaces.Moogle },
            { 11007, NpcRaces.Sahagin },
            { 11008, NpcRaces.MamoolJa },
            { 11012, NpcRaces.Qiqirn },
            { 61001, NpcRaces.VanuVanu },
            { 11020, NpcRaces.Gnath },
            { 11028, NpcRaces.Kojin },
            { 11029, NpcRaces.Ananta },
            { 11030, NpcRaces.Lupin },
            { 20494, NpcRaces.Namazu },
            { 11055, NpcRaces.Arkasodara },
            { 11037, NpcRaces.NuMou },
            { 11038, NpcRaces.Pixie },
            { 11052, NpcRaces.Loporrit },
            { 21051, NpcRaces.Omicron },
            { 10188, NpcRaces.Frog },
            { 10706, NpcRaces.Ea }
        };
    }
}
