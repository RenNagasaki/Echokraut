using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.Enums;
using Echokraut.Helper;
using OtterGui.Widgets;
using System;

namespace Echokraut.DataClasses
{
    public class NpcMapData : IComparable
    {
        public string name { get; set; }
        public NpcRaces race { get; set; }
        public string raceStr { get; set; }
        public Gender gender { get; set; }
        public BackendVoiceItem voiceItem { get; set; }

        public bool doNotDelete { get; set; }
        public float volume { get; set; } = 1f;
        public float volumeBubble { get; set; } = 1f;
        public bool hasBubbles { get; set; }

        public ObjectKind objectKind { get; set; }

        [NonSerialized]
        public ClippedSelectableCombo<BackendVoiceItem> voicesSelectable;
                

        public NpcMapData(ObjectKind objectKind) {
            this.objectKind = objectKind;
        }

        public override string ToString()
        {
            var raceString = race == NpcRaces.Unknown ? raceStr : race.ToString();
            return $"{gender} - {raceString} - {name}";
        }
        public override bool Equals(object obj)
        {
            var item = obj as NpcMapData;

            if (item == null)
            {
                return false;
            }

            return this.ToString().Equals(item.ToString(), System.StringComparison.OrdinalIgnoreCase);
        }

        public int CompareTo(object? obj)
        {
            var otherObj = ((NpcMapData)obj);
            return otherObj.ToString().ToLower().CompareTo(ToString().ToLower());
        }
    }
}
