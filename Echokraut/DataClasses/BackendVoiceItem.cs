using Echokraut.Enums;
using System;
using System.Xml.Linq;

namespace Echokraut.DataClasses
{
    public class BackendVoiceItem : IComparable
    {
        public string voiceName { get; set; }
        public string voice { get; set; }
        public Gender gender { get; set; }
        public NpcRaces race { get; set; }

        public float volume { get; set; } = 1f;

        public override string ToString()
        {
            return $"{gender} - {race} - {voiceName}";
        }
        public override bool Equals(object obj)
        {
            var item = obj as BackendVoiceItem;

            if (item == null)
            {
                return false;
            }

            return this.ToString().Equals(item.ToString(), System.StringComparison.OrdinalIgnoreCase);
        }

        public int CompareTo(object? obj)
        {
            var otherObj = ((BackendVoiceItem)obj);
            return otherObj.ToString().ToLower().CompareTo(ToString().ToLower());
        }
    }
}
