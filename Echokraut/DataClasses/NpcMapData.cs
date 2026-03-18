using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper;
using OtterGui.Widgets;
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

        public bool IsChild { get; set; }

        public string voice = "";
        internal EchokrautVoice? Voice
        {
            get => Voices?.Find(p => p.BackendVoice == voice);
            set => voice = value != null ? value.BackendVoice : string.Empty;
        }
#pragma warning disable CS0618
        public BackendVoiceItem? voiceItem { get; set; }
#pragma warning restore CS0618

        public bool DoNotDelete { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsEnabledBubble { get; set; } = true;
        public float Volume { get; set; } = 1f;
        public float VolumeBubble { get; set; } = 1f;
        public bool HasBubbles { get; set; }

        public ObjectKind ObjectKind { get; set; }

        internal ClippedSelectableCombo<EchokrautVoice> VoicesSelectable { get; set; } = null!;
        internal ClippedSelectableCombo<EchokrautVoice> VoicesSelectableDialogue { get; set; } = null!;

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

        public void RefreshSelectable()
        {
            VoicesSelectable = new($"##AllVoices{ToString()}", string.Empty, 200, Voices.FindAll(f => f.IsSelectable(Name, Gender, Race, IsChild)), g => g.VoiceNameNote);
            VoicesSelectableDialogue = new($"##AllVoices{ToString()}", string.Empty, 200, Voices.FindAll(f => f.IsSelectable(Name, Gender, Race, IsChild)), g => g.VoiceNameNote);
        }
    }
}
