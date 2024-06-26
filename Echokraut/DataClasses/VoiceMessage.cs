using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class VoiceMessage
    {
        public string Text { get; set; }
        public string TextTemplate { get; set; }
        public NpcMapData Speaker { get; set; }
        public string Source { get; set; }
        public int? ChatType { get; set; }
        public string Language { get; set; }

    }
}
