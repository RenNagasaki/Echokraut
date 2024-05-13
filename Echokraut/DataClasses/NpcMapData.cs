using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class NpcMapData
    {
        public NpcMapData() 
        {
            patchVersion = 1.0m;
        }

        public string name { get; set; }
        public decimal patchVersion { get; set; }
        public string race { get; set; }
        public string gender { get; set; }
        public BackendVoiceItem voiceItem { get; set; }
    }
}
