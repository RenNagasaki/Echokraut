using System;
using R3;
using Echokraut.TextToTalk.Events;

namespace Echokraut.TextToTalk.TextProviders;

public interface IAddonBattleTalkHandler : IDisposable
{
    Observable<TextEmitEvent> OnTextEmit();

    void PollAddon(AddonPollSource pollSource);
}