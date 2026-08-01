using System;

namespace Echokraut.Services;

public interface ISoundHelper : IDisposable
{
    event Action? TalkVoiceLine;
    event Action? BattleBubbleVoiceLine;
}
