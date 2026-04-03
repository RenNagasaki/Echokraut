using System;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface IDialogHarvestService
{
    bool IsRunning { get; }
    event Action<string>? ProgressChanged;
    Task RunAsync(CancellationToken ct);
    string? ExportQuestLuaDebug(uint questRowId);
}
