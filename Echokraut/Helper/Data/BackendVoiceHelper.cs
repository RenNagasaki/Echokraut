using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Windows;
using System;
using System.Collections.Generic;

namespace Echokraut.Helper.Data
{
    public static class BackendVoiceHelper
    {
        private static Configuration Configuration;
        public static List<BackendVoiceItem> Voices
        {
            get { return Configuration.BackendVoices; }
            set
            {
                if (Configuration.BackendVoices != null)
                {
                    foreach (BackendVoiceItem item in value)
                    {
                        var savedVoice = Configuration.BackendVoices.Find(p => p.voiceName == item.voiceName);
                        if (savedVoice != null)
                        {
                            item.volume = savedVoice.volume;
                        }
                    }
                }

                Configuration.BackendVoices = value;
            }
        }
        public static string[] GenderDisplay = new string[0];
        public static string[] RaceDisplay = new string[0];
        public static List<Gender> GenderArr = new List<Gender>();
        public static List<NpcRaces> RaceArr = new List<NpcRaces>();

        public static void Setup(List<BackendVoiceItem> voices, Configuration configuration)
        {
            Configuration = configuration;
            voices.Sort((x, y) => x.ToString().CompareTo(y.ToString()));
            Voices = voices;

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
