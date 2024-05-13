using System.Linq;
using Echokraut.TextToTalk.Events;
using R3;

namespace Echokraut;

public partial class Echokraut
{

    private Observable<SourcedTextEvent> OnTextSourceCancel()
    {
        return this.addonTalkHandler.OnAdvance()
            .Cast<AddonTalkAdvanceEvent, SourcedTextEvent>()
            .Merge(this.addonTalkHandler.OnClose().Cast<AddonTalkCloseEvent, SourcedTextEvent>());
    }

    private Observable<TextEmitEvent> OnTextEmit()
    {
        return this.addonTalkHandler.OnTextEmit()
            .Merge(this.addonBattleTalkHandler.OnTextEmit())
            .DistinctUntilChanged(TextEmitEventComparer.Instance);
    }
}
