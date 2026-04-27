using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Echokraut.Services;

/// <summary>
/// Publishes <c>Echokraut.IsClickInToolbar(int x, int y) → bool</c> via Dalamud's CallGate.
/// Other plugins that hook Talk's PreReceiveEvent can call this to filter out clicks meant
/// for our DialogExtraOptions toolbar — listener order is non-deterministic, so we can't
/// rely on PreventOriginal alone to keep them quiet. The IPC name is intentionally stable
/// so Echosync (and any future cooperator) can hard-code it.
/// </summary>
public sealed class EchokrautIpc : IEchokrautIpc
{
    public const string ClickInToolbarIdent = "Echokraut.IsClickInToolbar";

    private readonly ICallGateProvider<int, int, bool> _provider;
    private Func<int, int, bool>? _check;

    public EchokrautIpc(IDalamudPluginInterface pluginInterface)
    {
        _provider = pluginInterface.GetIpcProvider<int, int, bool>(ClickInToolbarIdent);
        _provider.RegisterFunc((x, y) => _check?.Invoke(x, y) ?? false);
    }

    public void SetClickInToolbarCheck(Func<int, int, bool>? check) => _check = check;

    public void Dispose()
    {
        _check = null;
        try { _provider.UnregisterFunc(); } catch { /* already gone */ }
    }
}
