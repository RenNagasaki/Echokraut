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
        public const string ALLTALKGITHUBURL = "https://github.com/erew123";
        public const string DISCORDURL = "https://discord.gg/5gesjDfDBr";
        public static readonly Vector4 DISCORDCOLOR = new Vector4(0.345f, 0.396f, 0.949f, 1f);
        public static readonly Vector4 INFOLOGCOLOR = new Vector4(.3f, 1.0f, 1.0f, 1f);
        public static readonly Vector4 DEBUGLOGCOLOR = new Vector4(0.0f, 1.0f, 0.0f, 1f);
        public static readonly Vector4 ERRORLOGCOLOR = new Vector4(1.0f, 0.0f, 0.0f, 1f);
        public static readonly Vector4 BLACKCOLOR = new Vector4(0.0f, 0.0f, 0.0f, 1f);
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

        public static readonly string[] XTTS203URLS =
        {
            "https://huggingface.co/coqui/XTTS-v2/resolve/v2.0.3/LICENSE.txt?download=true",
            "https://huggingface.co/coqui/XTTS-v2/resolve/v2.0.3/README.md?download=true",
            "https://huggingface.co/coqui/XTTS-v2/resolve/v2.0.3/config.json?download=true",
            "https://huggingface.co/coqui/XTTS-v2/resolve/v2.0.3/model.pth?download=true",
            "https://huggingface.co/coqui/XTTS-v2/resolve/v2.0.3/dvae.pth?download=true",
            "https://huggingface.co/coqui/XTTS-v2/resolve/v2.0.3/mel_stats.pth?download=true",
            "https://huggingface.co/coqui/XTTS-v2/resolve/v2.0.3/speakers_xtts.pth?download=true",
            "https://huggingface.co/coqui/XTTS-v2/resolve/v2.0.3/vocab.json?download=true"
        };

        public const string ALLTALKFOLDERNAME = "alltalk_tts";

        public const string MSBUILDTOOLSURL = "https://aka.ms/vs/17/release/vs_BuildTools.exe";
        public const string MSBUILDTOOLSMSVC = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";
        public const string MSBUILDTOOLSWIN10SDK = "Microsoft.VisualStudio.Component.Windows10SDK.19041";
        public const string MSBUILDTOOLSWIN11SDK = "Microsoft.VisualStudio.Component.Windows11SDK.22621";
        public const string VOICES2URL = "https://drive.google.com/uc?export=download&id=1CPnx1rpkuKvVj5fGr9OiUJHZ_e8DfTzP";
        public const string VOICESURL = "https://drive.google.com/uc?export=download&id=1bYdZdr3L69kmzUN3vSiqZmLRD7-A3M47";

        public const string ALLTALKURL = "https://github.com/RenNagasaki/alltalk_tts/releases/download/alltalk_tts-alltalkbeta/alltalk_tts-alltalkbeta.zip";
        public static readonly string[] ALLTALKDEBUGLOGCOLOR = { @"\033[94m", @"\033[93m" };
        public static readonly string[] ALLTALKERRORLOGCOLOR = { @"\033[91m" };

        public const int MASTERVOLUMEOFFSET = 47392;
        public const int VOICEVOLUMEOFFSET = 47440;
        public static readonly string SENTENCESEPARATORS = ".!?";
        public static readonly string[] BACKENDS = { "Alltalk" };
        public static readonly List<NpcRaces> RACESFORRANDOMNPC = new List<NpcRaces>() { NpcRaces.Hyur, NpcRaces.Roegadyn, NpcRaces.Viera, NpcRaces.AuRa, NpcRaces.Miqote, NpcRaces.Hrothgar };
        public const string NARRATORVOICE = "Narrator.wav";
        public const string TESTMESSAGEDE = "In der Stadt gab es auch ein paar Barbaren. Die hatten von Barbaras Rhabarberbar erfahren und da sie fort an jeden Tag bei Barbara waren, nannte man sie bald die \"Rhabarberbar-Barbaren\".";
        public const string TESTMESSAGEEN = "How much wood would a woodchuck chuck if a woodchuck could chuck wood?";
        public const string TESTMESSAGEFR = "Les chaussettes de l'archiduchesse sont-elles sèches ? Elles sont sèches, archi-sèches";
        public const string TESTMESSAGEJP = "この竹垣に竹立て掛けたのは竹立て掛けたかったから、竹立て掛けた";
    }
}
