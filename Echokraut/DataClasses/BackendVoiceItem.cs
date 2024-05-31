using Echokraut.Enums;

namespace Echokraut.DataClasses
{
    public class BackendVoiceItem
    {
        public string voiceName { get; set; }
        public string voice { get; set; }
        public Gender gender { get; set; }
        public NpcRaces race { get; set; }

        public override string ToString()
        {
            if (voiceName == "Remove")
                return voiceName;

            return $"{gender} - {race} - {voiceName}";
        }
        public override bool Equals(object obj)
        {
            var item = obj as BackendVoiceItem;

            if (item == null)
            {
                return false;
            }

            return this.ToString().Equals(item.ToString(), System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
