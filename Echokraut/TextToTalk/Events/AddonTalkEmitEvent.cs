using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Echokraut.TextToTalk;

namespace Echokraut.TextToTalk.Events;

public class AddonTalkEmitEvent(SeString speaker, SeString text, GameObject? speakerObj)
    : TextEmitEvent(TextSource.AddonTalk, speaker, text, speakerObj);
