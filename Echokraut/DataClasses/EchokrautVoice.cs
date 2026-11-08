using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.DataClasses
{
    public class EchokrautVoice : StringKeyedComparable
    {
        public bool IsDefault { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool UseAsRandom { get; set; }
        public bool IsAdultVoice { get; set; } = true;
        public bool IsChildVoice { get; set; }
        public bool IsElderVoice { get; set; }
        public float Volume { get; set; } = 1f;
        public string BackendVoice { get; set; } = "";
        
        public string voiceName { get; set; } = "";
        
        private string voiceNameShort { get; set; } = "";
        internal string VoiceName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(voiceNameShort))
                {
                    var voiceNameArr = voiceName.Split('_');
                    if (voiceNameArr.Length > 0)
                    {
                        foreach (var voiceNme in voiceNameArr)
                        {
                            if (Enum.TryParse(typeof(Genders), voiceNme, true, out object? gender))
                                continue;

                            if (Enum.TryParse(typeof(NpcRaces), voiceNme, true, out object? race))
                                continue;

                            // Bare race-pool / body-type tokens (no dash composite). Without
                            // this branch, filenames like "Female_All_NPC030.wav" would land
                            // on "All" as the display name because "All" isn't an NpcRaces
                            // enum value but is a valid race-pool marker. Same applies to
                            // standalone "Child" / "Elder" / "Adult" segments.
                            if (voiceNme.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                                voiceNme.Equals("Child", StringComparison.OrdinalIgnoreCase) ||
                                voiceNme.Equals("Elder", StringComparison.OrdinalIgnoreCase) ||
                                voiceNme.Equals("Adult", StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (voiceNme.Contains("-"))
                            {

                                var voiceNmeArr = voiceNme.Split('-');
                                if (voiceNmeArr[0] == "All" || voiceNmeArr[0] == "Child" || voiceNmeArr[0] == "Elder" || voiceNmeArr[0] == "Adult")
                                    continue;
                                if (Enum.TryParse(typeof(NpcRaces), voiceNmeArr[0], true, out object? race2))
                                    continue;
                            }

                            voiceNameShort = voiceNme;
                            break;
                        }
                    }
                }
                
                return voiceNameShort;
            }
            set { voiceName = value; }
        }

        internal string VoiceNameNote
        {
            get => $"{VoiceName} ({Note})";
        }

        public string Note = "";

        public List<Genders> AllowedGenders { get; set; } = new List<Genders>();

        public List<NpcRaces> AllowedRaces { get; set; } = new List<NpcRaces>();

        public override string ToString()
        {
            return $"{voiceName}";
        }

        public bool FitsNpcData(Genders gender, NpcRaces race, BodyType bodyType, bool isGenderedRace)
        {
            return IsEnabled &&
                   UseAsRandom &&
                   ((isGenderedRace && (AllowedGenders.Contains(gender) || AllowedGenders.Count == 0)) || !isGenderedRace) &&
                    AllowedRaces.Contains(race) &&
                    FitsBodyType(bodyType);
        }

        public bool IsSelectable(string npcName, Genders gender, NpcRaces race, BodyType bodyType)
        {
            // Use the VoiceName property (not the raw voiceNameShort field) so the lazy-init
            // strips Gender/Race/BodyType prefixes the first time it's called. Otherwise a voice
            // like "Male_Elezen_Alphinaud" wouldn't match an "Alphinaud" NPC the very first time
            // the dropdown is built. Match is case-insensitive to align with BackendService.PickVoice.
            return IsEnabled && (
                IsDefault ||
                (AllowedGenders.Contains(gender) && AllowedRaces.Contains(race) && FitsBodyType(bodyType)) ||
                (!string.IsNullOrEmpty(npcName) &&
                 VoiceName.Contains(npcName, StringComparison.OrdinalIgnoreCase))
            );
        }

        private bool FitsBodyType(BodyType bodyType)
        {
            return bodyType switch
            {
                BodyType.Child => IsChildVoice,
                BodyType.Elder => IsElderVoice,
                _ => IsAdultVoice
            };
        }
    }
}
