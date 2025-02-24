using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public static class Constants
    {
        public static readonly Vector4 INFOLOGCOLOR = new Vector4(.3f, 1.0f, 1.0f, 1f);
        public static readonly Vector4 DEBUGLOGCOLOR = new Vector4(0.0f, 1.0f, 0.0f, 1f);
        public static readonly Vector4 ERRORLOGCOLOR = new Vector4(1.0f, 0.0f, 0.0f, 1f);
        public static readonly List<Genders> GENDERLIST = new List<Genders>() { 
            Genders.None, 
            Genders.Male, 
            Genders.Female
        };
        public static readonly List<NpcRaces> RACELIST = new List<NpcRaces> {
            NpcRaces.Unknown,
            NpcRaces.Hyur,
            NpcRaces.Elezen,
            NpcRaces.Miqote,
            NpcRaces.Roegadyn,
            NpcRaces.Lalafell,
            NpcRaces.Viera,
            NpcRaces.AuRa,
            NpcRaces.Hrothgar,
            NpcRaces.Amaljaa,
            NpcRaces.Ixal,
            NpcRaces.Sylph,
            NpcRaces.Goblin,
            NpcRaces.Moogle,
            NpcRaces.MamoolJa,
            NpcRaces.Qiqirn,
            NpcRaces.VanuVanu,
            NpcRaces.Kojin,
            NpcRaces.Ananta,
            NpcRaces.Lupin,
            NpcRaces.Arkasodara,
            NpcRaces.NuMou,
            NpcRaces.Pixie,
            NpcRaces.Loporrit,
            NpcRaces.Frog,
            NpcRaces.Ea,
            NpcRaces.YokHuy,
            NpcRaces.Endless,
            NpcRaces.Sahagin,
            NpcRaces.Kobold,
            NpcRaces.Gnath,
            NpcRaces.Namazu,
            NpcRaces.Omicron 
        };
        public static readonly string[] GENDERNAMESLIST = {
            Genders.None.ToString(),
            Genders.Male.ToString(),
            Genders.Female.ToString()
        };
        public static readonly string[] RACENAMESLIST = {
            NpcRaces.Unknown.ToString(),
            NpcRaces.Hyur.ToString(),
            NpcRaces.Elezen.ToString(),
            NpcRaces.Miqote.ToString(),
            NpcRaces.Roegadyn.ToString(),
            NpcRaces.Lalafell.ToString(),
            NpcRaces.Viera.ToString(),
            NpcRaces.AuRa.ToString(),
            NpcRaces.Hrothgar.ToString(),
            NpcRaces.Amaljaa.ToString(),
            NpcRaces.Ixal.ToString(),
            NpcRaces.Sylph.ToString(),
            NpcRaces.Goblin.ToString(),
            NpcRaces.Moogle.ToString(),
            NpcRaces.MamoolJa.ToString(),
            NpcRaces.Qiqirn.ToString(),
            NpcRaces.VanuVanu.ToString(),
            NpcRaces.Kojin.ToString(),
            NpcRaces.Ananta.ToString(),
            NpcRaces.Lupin.ToString(),
            NpcRaces.Arkasodara.ToString(),
            NpcRaces.NuMou.ToString(),
            NpcRaces.Pixie.ToString(),
            NpcRaces.Loporrit.ToString(),
            NpcRaces.Frog.ToString(),
            NpcRaces.Ea.ToString(),
            NpcRaces.YokHuy.ToString(),
            NpcRaces.Endless.ToString(),
            NpcRaces.Sahagin.ToString(),
            NpcRaces.Kobold.ToString(),
            NpcRaces.Gnath.ToString(),
            NpcRaces.Namazu.ToString(),
            NpcRaces.Omicron.ToString()
        };
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
