using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public static class Constants
    {
        public static readonly Vector4 INFOLOGCOLOR = new Vector4(.3f, 1.0f, 1.0f, 1f);
        public static readonly Vector4 DEBUGLOGCOLOR = new Vector4(0.0f, 1.0f, 0.0f, 1f);
        public static readonly Vector4 ERRORLOGCOLOR = new Vector4(1.0f, 0.0f, 0.0f, 1f);
        public const int MASTERVOLUMEOFFSET = 47392;
        public const int VOICEVOLUMEOFFSET = 47440;
        public static readonly char[] SENTENCESEPARATORS = { '.', '!', '?' };
        public static readonly string[] BACKENDS = { "Alltalk", "Custom Webservice" };
        public static readonly List<NpcRaces> RACESFORRANDOMNPC = new List<NpcRaces>() { NpcRaces.Hyur, NpcRaces.Roegadyn, NpcRaces.Viera, NpcRaces.AuRa, NpcRaces.Miqote, NpcRaces.Hrothgar };
        public const string NARRATORVOICE = "Narrator.wav";
        public const string TESTMESSAGEDE = "In der Stadt gab es auch ein paar Barbaren. Die hatten von Barbaras Rhabarberbar erfahren und da sie fort an jeden Tag bei Barbara waren, nannte man sie bald die \"Rhabarberbar-Barbaren\".";
        public const string TESTMESSAGEEN = "In der Stadt gab es auch ein paar Barbaren. Die hatten von Barbaras Rhabarberbar erfahren und da sie fort an jeden Tag bei Barbara waren, nannte man sie bald die \"Rhabarberbar-Barbaren\".";
        public const string TESTMESSAGEFR = "In der Stadt gab es auch ein paar Barbaren. Die hatten von Barbaras Rhabarberbar erfahren und da sie fort an jeden Tag bei Barbara waren, nannte man sie bald die \"Rhabarberbar-Barbaren\".";
        public const string TESTMESSAGEJP = "In der Stadt gab es auch ein paar Barbaren. Die hatten von Barbaras Rhabarberbar erfahren und da sie fort an jeden Tag bei Barbara waren, nannte man sie bald die \"Rhabarberbar-Barbaren\".";
    }
}
