using System;
using System.Numerics;
using Echokraut.DataClasses;

namespace Echokraut.Services;

public static class DialogState
{
    public static VoiceMessage? CurrentVoiceMessage;
    public static bool IsVoiced;
    public static Func<Vector2, bool>? IsInsideOwnedWindow;
}
