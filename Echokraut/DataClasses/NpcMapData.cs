using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class NpcMapData : IComparable
    {
        public string name { get; set; }
        public NpcRaces race { get; set; }
        public string raceStr { get; set; }
        public Gender gender { get; set; }
        public BackendVoiceItem voiceItem { get; set; }

        public ObjectKind objectKind { get; set; }

        public NpcMapData(ObjectKind objectKind) {
            this.objectKind = objectKind;
        }

        public string ToString(bool showRace = false)
        {
            if (showRace)
            {
                var raceString = race == NpcRaces.Default ? raceStr : race.ToString();
                return $"{gender} - {raceString} - {name}";
            }

            return $"{gender} - {name}";
        }
        public override bool Equals(object obj)
        {
            var item = obj as NpcMapData;

            if (item == null)
            {
                return false;
            }

            return this.ToString(true).Equals(item.ToString(true), System.StringComparison.OrdinalIgnoreCase);
        }

        public int CompareTo(object? obj)
        {
            var otherObj = ((NpcMapData)obj);
            return otherObj.ToString(true).ToLower().CompareTo(ToString(true).ToLower());
        }
    }
}
