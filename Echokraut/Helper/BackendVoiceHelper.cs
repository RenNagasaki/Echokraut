using Echokraut.DataClasses;
using Echokraut.Enums;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Echokraut.Helper
{
    public static class BackendVoiceHelper
    {
        public static List<BackendVoiceItem> Voices = new List<BackendVoiceItem>();
        public static List<BackendVoiceItem> VoicesOriginal = new List<BackendVoiceItem>();
        public static Dictionary<Gender, Dictionary<NpcRaces, List<BackendVoiceItem>>> FilteredVoices = new Dictionary<Gender, Dictionary<NpcRaces, List<BackendVoiceItem>>>();
        public static Dictionary<Gender, Dictionary<NpcRaces, List<BackendVoiceItem>>> FilteredVoicesOriginal = new Dictionary<Gender, Dictionary<NpcRaces, List<BackendVoiceItem>>>();
        public static Dictionary<Gender, List<BackendVoiceItem>> FilteredVoicesAllRaces = new Dictionary<Gender, List<BackendVoiceItem>>();
        public static Dictionary<Gender, List<BackendVoiceItem>> FilteredVoicesAllRacesOriginal = new Dictionary<Gender, List<BackendVoiceItem>>();
        public static Dictionary<NpcRaces, List<BackendVoiceItem>> FilteredVoicesAllGenders = new Dictionary<NpcRaces, List<BackendVoiceItem>>();
        public static Dictionary<NpcRaces, List<BackendVoiceItem>> FilteredVoicesAllGendersOriginal = new Dictionary<NpcRaces, List<BackendVoiceItem>>();
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
                var npcRace = ((NpcRaces)npcRaceObj);
                RaceArr.Add(npcRace);
                raceDisplay.Add(npcRace.ToString());
            }

            var genderDisplay = new List<string>();
            var genders = Enum.GetValues(typeof(Gender));
            foreach (var genderObj in genders)
            {
                var gender = ((Gender)genderObj);
                GenderArr.Add(gender);
                genderDisplay.Add(gender.ToString());

                FilteredVoices.Add(gender, new Dictionary<NpcRaces, List<BackendVoiceItem>>());
                FilteredVoicesOriginal.Add(gender, new Dictionary<NpcRaces, List<BackendVoiceItem>>());
                FilteredVoicesAllRaces.Add(gender, new List<BackendVoiceItem>());
                FilteredVoicesAllRacesOriginal.Add(gender, new List<BackendVoiceItem>());

                foreach (var npcRaceObj in npcRaces)
                {
                    var npcRace = ((NpcRaces)npcRaceObj);
                    FilteredVoices[gender].Add(npcRace, new List<BackendVoiceItem>());
                    FilteredVoicesOriginal[gender].Add(npcRace, new List<BackendVoiceItem>());

                    if (!FilteredVoicesAllGenders.ContainsKey(npcRace))
                        FilteredVoicesAllGenders.Add(npcRace, new List<BackendVoiceItem>());
                    if (!FilteredVoicesAllGendersOriginal.ContainsKey(npcRace))
                        FilteredVoicesAllGendersOriginal.Add(npcRace, new List<BackendVoiceItem>());
                }
            }

            foreach (BackendVoiceItem voice in voices)
            {
                FilteredVoices[voice.gender][voice.race].Add(voice);
                FilteredVoicesAllRaces[voice.gender].Add(voice);
                FilteredVoicesAllGenders[voice.race].Add(voice);

                if (voice.voiceName.ToLower().Contains("npc"))
                {
                    FilteredVoicesOriginal[voice.gender][voice.race].Add(voice);
                    FilteredVoicesAllRacesOriginal[voice.gender].Add(voice);
                    FilteredVoicesAllGendersOriginal[voice.race].Add(voice);
                    VoicesOriginal.Add(voice);
                }
            }

            GenderDisplay = genderDisplay.ToArray();
            RaceDisplay = raceDisplay.ToArray();
        }
    }
}
