using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System;

namespace Echokraut.Services;

public interface IAddonTalkHelper : IDisposable
{
    void NotifyNextIsVoice();
    void RecreateInference();
    void Click(EKEventId eventId);

    /// <summary>
    /// Advances a box whose text was already spoken with the previous one, after a short wait and
    /// only while that box is still shown (auto-advance option on).
    /// </summary>
    void AdvanceAlreadySpoken(EKEventId eventId);
}
