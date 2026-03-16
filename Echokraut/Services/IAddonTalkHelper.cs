using Echokraut.DataClasses;
using System;

namespace Echokraut.Services;

public interface IAddonTalkHelper : IDisposable
{
    void NotifyNextIsVoice();
    void RecreateInference();
    void Click(EKEventId eventId);
}
