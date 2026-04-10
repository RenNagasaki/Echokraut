using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;

namespace Echokraut.Services;

public interface IDialogHarvestService : IDisposable
{
    bool IsRunning { get; }
    event Action<string>? ProgressChanged;
    /// <summary>Fired when the current sub-stage's numeric progress changes (current, total).</summary>
    event Action<int, int>? ProgressCountChanged;
    Task RunAsync(ClientLanguage language, CancellationToken ct);
    string? ExportQuestLuaDebug(uint questRowId);
    Task DumpAllSheetsAsync(CancellationToken ct);
}
