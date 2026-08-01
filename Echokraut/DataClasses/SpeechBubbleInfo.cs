using Dalamud.Game.Text.SeStringHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    internal class SpeechBubbleInfo
    {
        public SpeechBubbleInfo(SeString messageText, long timeLastSeen_mSec, SeString speakerName)
        {
            TimeLastSeen_mSec = timeLastSeen_mSec;
            HasBeenPrinted = false;
            MessageText = messageText;
            SpeakerName = speakerName;
        }

        protected SpeechBubbleInfo() { SpeakerName = SeString.Empty; MessageText = SeString.Empty; }

        public bool IsSameMessageAs(SpeechBubbleInfo rhs)
        {
            return SpeakerName.TextValue.Equals(rhs.SpeakerName.TextValue) && MessageText.TextValue.Equals(rhs.MessageText.TextValue);
        }

        public long TimeLastSeen_mSec { get; set; }
        public bool HasBeenPrinted { get; set; }
        public SeString SpeakerName { get; set; }
        public SeString MessageText { get; set; }
    }
}
