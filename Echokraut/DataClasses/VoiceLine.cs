using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Echokraut.Helper.Functional;

namespace Echokraut.DataClasses
{
    public class VoiceLine
    {
        public Genders Gender { get; set; }
        public NpcRaces Race { get; set; }
        public ClientLanguage Language { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }

        public string GetDebugInfo()
        {
            return $"Gender: {Gender} | Race: {Race} | Name: {Name} | Language: {Language} | Text: {Text}";
        }
        public string GetFileName()
        {
            return $"{Language}_{Gender.ToString()}_{Race.ToString()}_{Name}_{AudioFileHelper.VoiceMessageToFileName(AudioFileHelper.RemovePlayerNameInText(Text))}.json";
        }
    }
}
