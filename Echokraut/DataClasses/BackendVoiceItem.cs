using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.Xml.Linq;

namespace Echokraut.DataClasses
{
    [Obsolete("Only kept for migrating old data, not in use anymore", false)]
    public class BackendVoiceItem : StringKeyedComparable
    {
        public string VoiceName { get; set; } = "";
        public string Voice { get; set; } = "";
        public Genders Gender { get; set; }
        public NpcRaces Race { get; set; }

        public override string ToString()
        {
            return $"{Gender} - {Race} - {VoiceName}";
        }

        // Legacy: case-SENSITIVE compare with a hard cast (differs from the base's case-insensitive
        // form). Kept verbatim — this type is [Obsolete] / migration-only, so its ordering is frozen.
        public override int CompareTo(object? obj)
        {
            if (obj == null) return -1;

            return ((BackendVoiceItem)obj).ToString().CompareTo(ToString());
        }
    }
}
