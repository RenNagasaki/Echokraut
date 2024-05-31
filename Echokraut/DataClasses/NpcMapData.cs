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
        public Gender gender { get; set; }
        public BackendVoiceItem voiceItem { get; set; }

        public override string ToString()
        {
            return $"{gender} - {race} - {name}";
        }
    }
}
