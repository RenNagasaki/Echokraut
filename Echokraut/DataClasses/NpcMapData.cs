using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.Enums;
using Echokraut.Helper;
using Echokraut.Helper.Data;
using OtterGui.Widgets;
using System;
using System.Collections.Generic;

namespace Echokraut.DataClasses
{
    public class NpcMapData : IComparable
    {
        public string Name { get; set; }
        public NpcRaces Race { get; set; }
        public string RaceStr { get; set; }
        public Genders Gender { get; set; }

        public bool IsChild { get; set; }

        public string voice = "";
        internal EchokrautVoice? Voice
        {
            get => NpcDataHelper.GetVoiceByBackendVoice(voice);
            set => voice = value != null ? value.BackendVoice : string.Empty;
        }
        public BackendVoiceItem voiceItem { get; set; }

        public bool DoNotDelete { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsEnabledBubble { get; set; } = true;
        public float Volume { get; set; } = 1f;
        public float VolumeBubble { get; set; } = 1f;
        public bool HasBubbles { get; set; }

        public ObjectKind ObjectKind { get; set; }

        internal ClippedSelectableCombo<EchokrautVoice> VoicesSelectable { get; set; }
        internal ClippedSelectableCombo<EchokrautVoice> VoicesSelectableDialogue { get; set; }

        internal List<EchokrautVoice> Voices { get; set; }

        public NpcMapData(ObjectKind objectKind) {
            this.ObjectKind = objectKind;
        }

        public override string ToString()
        {
            var raceString = Race == NpcRaces.Unknown ? RaceStr : Race.ToString();
            return $"{Gender} - {raceString} - {Name}";
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

        public void RefreshSelectable()
        {
            VoicesSelectable = new($"##AllVoices{ToString()}", string.Empty, 200, Voices.FindAll(f => f.IsSelectable(Name, Gender, Race, IsChild)), g => g.VoiceNameNote);
            VoicesSelectableDialogue = new($"##AllVoices{ToString()}", string.Empty, 200, Voices.FindAll(f => f.IsSelectable(Name, Gender, Race, IsChild)), g => g.VoiceNameNote);
        }
    }
}
