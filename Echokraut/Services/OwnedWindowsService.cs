using System;

namespace Echokraut.Services;

/// <inheritdoc />
public sealed class OwnedWindowsService : IOwnedWindowsService
{
    private volatile bool _anyAlive;

    public bool AnyAlive => _anyAlive;

    public event Action? CloseAllRequested;

    public event Action<OwnedWindowsProbe>? ProbeRequested;

    public void RequestCloseAll() => CloseAllRequested?.Invoke();

    public void Probe(OwnedWindowsProbe probe)
    {
        if (ProbeRequested == null) probe.Error = "no window manager";
        else ProbeRequested.Invoke(probe);
    }

    public void ReportAlive(bool anyAlive) => _anyAlive = anyAlive;
}
