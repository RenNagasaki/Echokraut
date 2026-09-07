using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class VoiceMap
    {
        public string voiceName { get; set; } = null!;
        public List<string> speakers { get; set; } = null!;
    }
}
