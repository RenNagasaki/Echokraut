using Echokraut.Enums;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class EchokrautVoice
    {
        public bool IsDefault { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool UseAsRandom { get; set; }
        public bool IsChildVoice { get; set; }
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
                        voiceNameShort = voiceNameArr[voiceNameArr.Length - 1];
                }
                
                return voiceNameShort;
            }
            set { voiceName = value; }
        }

        public List<Genders> AllowedGenders { get; set; } = new List<Genders>();

        public List<NpcRaces> AllowedRaces { get; set; } = new List<NpcRaces>();

        public override string ToString()
        {
            return $"{voiceName}";
        }
        public override bool Equals(object obj)
        {
            var item = obj as EchokrautVoice;

            if (item == null)
            {
                return false;
            }

            return this.ToString().Equals(item.ToString(), System.StringComparison.OrdinalIgnoreCase);
        }

        public int CompareTo(object? obj)
        {
            var otherObj = ((EchokrautVoice)obj);
            return otherObj.ToString().ToLower().CompareTo(ToString().ToLower());
        }

        public bool FitsNpcData(Genders gender, NpcRaces race, bool isChild, bool isGenderedRace)
        {
            return IsEnabled && 
                   UseAsRandom && 
                   ((isGenderedRace && AllowedGenders.Contains(gender)) || !isGenderedRace) && 
                    AllowedRaces.Contains(race) &&
                    IsChildVoice == isChild;
        }

        public bool IsSelectable(string npcName, Genders gender, NpcRaces race, bool isChild)
        {
            return IsEnabled && (
                IsDefault ||
                (AllowedGenders.Contains(gender) && AllowedRaces.Contains(race) && IsChildVoice == isChild) ||
                voiceNameShort.Contains(npcName)
            );
        }
    }
}
