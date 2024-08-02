using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class NpcGenderRaceMap
    {
        public NpcRaces race {  get; set; }
        public uint male { get; set; }
        public uint female { get; set; }
        public bool maleDefault { get; set; }
    }
}
