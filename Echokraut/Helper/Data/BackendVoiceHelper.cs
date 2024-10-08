using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Windows;
using System;
using System.Collections.Generic;

namespace Echokraut.Helper.Data
{
    public static class BackendVoiceHelper
    {
        public static List<BackendVoiceItem> Voices = new List<BackendVoiceItem>();
        public static string[] GenderDisplay = new string[0];
        public static string[] RaceDisplay = new string[0];
        public static List<Gender> GenderArr = new List<Gender>();
        public static List<NpcRaces> RaceArr = new List<NpcRaces>();

        public static void Setup(List<BackendVoiceItem> voices)
        {
            Voices = voices;
            Voices.Sort((x, y) => x.ToString().CompareTo(y.ToString()));

            var raceDisplay = new List<string>();
            var npcRaces = Enum.GetValues(typeof(NpcRaces));
            foreach (var npcRaceObj in npcRaces)
            {
                var npcRace = (NpcRaces)npcRaceObj;
                RaceArr.Add(npcRace);
                raceDisplay.Add(npcRace.ToString());
            }

            var genderDisplay = new List<string>();
            var genders = Enum.GetValues(typeof(Gender));
            foreach (var genderObj in genders)
            {
                var gender = (Gender)genderObj;
                GenderArr.Add(gender);
                genderDisplay.Add(gender.ToString());
            }

            GenderDisplay = genderDisplay.ToArray();
            RaceDisplay = raceDisplay.ToArray();

            NpcDataHelper.RefreshSelectables();
            ConfigWindow.UpdateDataVoices = true;
        }
    }
}
