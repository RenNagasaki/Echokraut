using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class NpcMapData
    {
        public string name { get; set; }
        public NpcRaces race { get; set; }
        public string raceStr { get; set; }
        public Gender gender { get; set; }
        public BackendVoiceItem voiceItem { get; set; }

        public string ToString(bool showRace = false)
        {
            if (showRace)
            {
                var raceString = race == NpcRaces.Default ? raceStr : race.ToString();
                return $"{gender} - {raceString} - {name}";
            }

            return $"{gender} - {name}";
        }
    }
}
