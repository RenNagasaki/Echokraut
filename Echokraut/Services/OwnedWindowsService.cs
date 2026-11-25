using System;

namespace Echokraut.Services;

/// <inheritdoc />
public sealed class OwnedWindowsService : IOwnedWindowsService
{
    private volatile bool _anyAlive;

    public bool AnyAlive => _anyAlive;

    public event Action? CloseAllRequested;

    public void RequestCloseAll() => CloseAllRequested?.Invoke();

    public void ReportAlive(bool anyAlive) => _anyAlive = anyAlive;
}
