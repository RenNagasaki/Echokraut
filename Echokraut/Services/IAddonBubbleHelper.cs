using System;

namespace Echokraut.Services;

public interface IAddonBubbleHelper : IDisposable
{
    void NotifyNextIsVoice();
}
