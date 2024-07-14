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

        protected SpeechBubbleInfo() { }

        public bool IsSameMessageAs(SpeechBubbleInfo rhs)
        {
            //***** TODO: Is there a better comparison that we can easily do on the whole thing, and not just the text value?  Can we encode and compare and get what we want?
            return SpeakerName.TextValue.Equals(rhs.SpeakerName.TextValue) && MessageText.TextValue.Equals(rhs.MessageText.TextValue);
        }

        public long TimeLastSeen_mSec { get; set; }
        public bool HasBeenPrinted { get; set; }
        public SeString SpeakerName { get; set; }
        public SeString MessageText { get; set; }
    }
}
