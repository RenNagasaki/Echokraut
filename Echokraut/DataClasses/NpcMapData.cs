using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper;
using System;
using System.Collections.Generic;

namespace Echokraut.DataClasses
{
    public class NpcMapData : IComparable
    {
        public string Name { get; set; } = null!;
        public NpcRaces Race { get; set; }
        public string RaceStr { get; set; } = null!;
        public Genders Gender { get; set; }

        /// <summary>FFXIV world (server) name in English. Only set for Player characters; empty for NPCs.</summary>
        public string World { get; set; } = "";

        public BodyType BodyType { get; set; } = BodyType.Adult;

        public string voice = "";
        internal EchokrautVoice? Voice
        {
            get => Voices?.Find(p => p.BackendVoice == voice);
            set => voice = value != null ? value.BackendVoice : string.Empty;
        }
#pragma warning disable CS0618
        public BackendVoiceItem? voiceItem { get; set; }
#pragma warning restore CS0618

        public bool IsEnabled { get; set; } = true;
        public bool IsEnabledBubble { get; set; } = true;
        public float Volume { get; set; } = 1f;
        public float VolumeBubble { get; set; } = 1f;
        public bool HasBubbles { get; set; }

        public ClientLanguage Language { get; set; } = ClientLanguage.English;

        public ObjectKind ObjectKind { get; set; }

        internal List<EchokrautVoice> Voices { get; set; } = null!;

        public NpcMapData(ObjectKind objectKind) {
            this.ObjectKind = objectKind;
        }

        public override string ToString()
        {
            var raceString = Race == NpcRaces.Unknown ? RaceStr : Race.ToString();
            return $"{Gender} - {raceString} - {Name}";
        }
        public override int GetHashCode() => ToString().ToLowerInvariant().GetHashCode();
        public override bool Equals(object? obj)
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
            var otherObj = obj as NpcMapData;
            return otherObj?.ToString().ToLower().CompareTo(ToString().ToLower()) ?? -1;
        }

    }
}
