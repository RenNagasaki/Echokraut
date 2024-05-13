using System;
using R3;
using Echokraut.TextToTalk.Events;

namespace Echokraut.TextToTalk.TextProviders;

public interface IAddonTalkHandler : IDisposable
{
    Observable<TextEmitEvent> OnTextEmit();

    Observable<AddonTalkAdvanceEvent> OnAdvance();

    Observable<AddonTalkCloseEvent> OnClose();

    void PollAddon(AddonPollSource pollSource);
}