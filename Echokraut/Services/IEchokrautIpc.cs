using System;

namespace Echokraut.Services;

/// <summary>
/// Cross-plugin coordination via Dalamud IPC.
/// Other plugins that hook the Talk addon (e.g. Echosync) can ask whether a click landed
/// inside Echokraut's DialogExtraOptions toolbar so they don't treat it as a dialog-advance.
/// </summary>
public interface IEchokrautIpc : IDisposable
{
    /// <summary>
    /// DialogTalkController registers its hit-test here; the IPC provider invokes it on demand.
    /// Pass null to clear (called on detach/dispose).
    /// </summary>
    void SetClickInToolbarCheck(Func<int, int, bool>? check);
}
