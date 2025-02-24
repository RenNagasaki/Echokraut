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
        public bool IsDefault { get; set; } = false;
        public bool IsEnabled { get; set; } = true;
        public float Volume { get; set; } = 1f;
        public string BackendVoice { get; set; }
        public string? VoiceName { get; set; }

        public List<Genders> AllowedGenders { get; set; } = new List<Genders>();

        public List<NpcRaces> AllowedRaces { get; set; } = new List<NpcRaces>();

        public override string ToString()
        {
            return $"{VoiceName}";
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
    }
}
