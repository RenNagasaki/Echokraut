using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class FF14VoiceMessage
    {
        public string Type { get; set; }
        public string Payload { get; set; }
        public string PayloadTemplate { get; set; }
        public Voice Voice { get; set; }
        public string Speaker { get; set; }
        public string Source { get; set; }
        public bool StuttersRemoved { get; set; }
        public int? NpcId { get; set; }
        public int? ChatType { get; set; }
        public string Language { get; set; }

    }
}
