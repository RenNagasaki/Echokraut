using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;

namespace Echokraut.Services;

public interface IDialogHarvestService
{
    bool IsRunning { get; }
    event Action<string>? ProgressChanged;
    Task RunAsync(ClientLanguage language, CancellationToken ct);
    string? ExportQuestLuaDebug(uint questRowId);
}
