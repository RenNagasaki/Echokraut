using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class Voice
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Rate { get; set; }
        public int Volume { get; set; }
        public string? VoiceName { get; set; }
        public int EnabledBackend { get; set; }
    }
}
