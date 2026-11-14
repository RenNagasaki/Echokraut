using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper;
using System;
using System.Collections.Generic;

namespace Echokraut.DataClasses
{
    public class NpcMapData : StringKeyedComparable
    {
        public string Name { get; set; } = null!;
        public NpcRaces Race { get; set; }
        public string RaceStr { get; set; } = null!;
        public Genders Gender { get; set; }

        /// <summary>FFXIV world (server) name in English. Only set for Player characters; empty for NPCs.</summary>
        public string World { get; set; } = "";

        public BodyType BodyType { get; set; } = BodyType.Adult;

        /// <summary>
        /// User's correction of <see cref="Gender"/>, or <c>null</c> when the game's value stands.
        /// <b>Never part of identity</b> — <c>Gender</c> and <c>Race</c> are the lookup key and are
        /// re-read from the game on every line, so editing them would only orphan the row.
        /// </summary>
        public Genders? GenderOverride { get; set; }

        /// <summary>User's correction of <see cref="Race"/>. Same rules as <see cref="GenderOverride"/>.</summary>
        public NpcRaces? RaceOverride { get; set; }

        /// <summary>Gender that voice selection must use: the user's correction, else the game's.</summary>
        public Genders EffectiveGender => GenderOverride ?? Gender;

        /// <summary>Race that voice selection must use: the user's correction, else the game's.</summary>
        public NpcRaces EffectiveRace => RaceOverride ?? Race;

        /// <summary>True when the user corrected anything about how this character is classified.</summary>
        public bool HasIdentityOverride => GenderOverride.HasValue || RaceOverride.HasValue;

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
            // Shows what voice selection actually uses, and marks a corrected entry so a user
            // comparing the list against the game can see why the two differ.
            var raceString = EffectiveRace == NpcRaces.Unknown ? RaceStr : EffectiveRace.ToString();
            var mark = HasIdentityOverride ? "*" : string.Empty;
            return $"{EffectiveGender}{mark} - {raceString} - {Name}";
        }

    }
}
