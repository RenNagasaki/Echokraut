using Dalamud.Game.Text;
using Echokraut.TextToTalk.Events;

namespace Echokraut.TextToTalk.Extensions;

public static class TextEmitEventExtensions
{
    public static XivChatType? GetChatType(this TextEmitEvent ev)
    {
        if (ev is ChatTextEmitEvent chatEv)
        {
            return chatEv.ChatType;
        }

        return null;
    }
}
