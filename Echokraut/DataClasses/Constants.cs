using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public static class Constants
    {
        public static readonly char[] SENTENCESEPARATORS = { '.', '!', '?' };
        public static readonly string[] BACKENDS = { "Alltalk", "Custom Webservice" };
        public static readonly List<NpcRaces> RACESFORRANDOMNPC = new List<NpcRaces>() { NpcRaces.Hyur, NpcRaces.Roegadyn, NpcRaces.Viera, NpcRaces.AuRa, NpcRaces.Miqote, NpcRaces.Hrothgar };
        public const string NARRATORVOICE = "Narrator.wav";
        public const string TESTMESSAGE = "In der Stadt gab es auch ein paar Barbaren. Die hatten von Barbaras Rhabarberbar erfahren und da sie fort an jeden Tag bei Barbara waren, nannte man sie bald die \"Rhabarberbar-Barbaren\".";
    }
}
