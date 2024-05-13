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
        public static readonly List<string> RACESFORRANDOMNPC = new List<string>() { "Hyur", "Roegadyn", "Viera", "Au=Ra", "Miqo=te", "Hrothgar" };
        public const string NARRATORVOICE = "Narrator.wav";
        public const string UNVOICEDNPCS = "FF14NPCData.json";
        public const string TESTMESSAGE = "In der Stadt gab es auch ein paar Barbaren. Die hatten von Barbaras Rhabarberbar erfahren und da sie fort an jeden Tag bei Barbara waren, nannte man sie bald die \"Rhabarberbar-Barbaren\".";
    }
}
