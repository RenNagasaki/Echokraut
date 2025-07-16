using Echokraut.Enums;
using System;
using System.Xml.Linq;

namespace Echokraut.DataClasses
{
    [Obsolete("Only kept for migrating old data, not in use anymore", false)]
    public class BackendVoiceItem : IComparable
    {
        public string VoiceName { get; set; } = "";
        public string Voice { get; set; } = "";
        public Genders Gender { get; set; }
        public NpcRaces Race { get; set; }

        public override string ToString()
        {
            return $"{Gender} - {Race} - {VoiceName}";
        }
        public override bool Equals(object? obj)
        {
            var item = obj as BackendVoiceItem;

            if (item == null) return false;

            return this.ToString().Equals(item.ToString(), System.StringComparison.OrdinalIgnoreCase);
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return -1;
            
            return ((BackendVoiceItem)obj).ToString().CompareTo(ToString());
        }
    }
}
