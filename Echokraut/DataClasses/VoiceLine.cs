using Dalamud.Game;
using Echokraut.Enums;
using Echokraut.Helper.Functional;

namespace Echokraut.DataClasses
{
    public class VoiceLine
    {
        public Genders Gender { get; set; }
        public NpcRaces Race { get; set; }
        public ClientLanguage Language { get; set; }
        public string Name { get; set; } = null!;
        public string Text { get; set; } = null!;

        public string GetDebugInfo()
        {
            return $"Gender: {Gender} | Race: {Race} | Name: {Name} | Language: {Language} | Text: {Text}";
        }
        public string GetFileName(string playerName = "")
        {
            return $"{Language}_{Gender.ToString()}_{Race.ToString()}_{Name}_{TalkTextHelper.VoiceMessageToFileName(TalkTextHelper.RemovePlayerNameInText(Text, playerName))}.json";
        }
    }
}
