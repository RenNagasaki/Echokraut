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
    /// <summary>
    /// Runs the dialog harvester for the given client language.
    /// </summary>
    /// <param name="questTypeFilter">If non-null, only quests whose <see cref="Echokraut.Enums.QuestType"/> equals this value
    /// (when cast back to the enum) are harvested. <c>null</c> = all quests + non-quest dialog (DefaultTalk, Bubbles).</param>
    Task RunAsync(ClientLanguage language, CancellationToken ct, int? questTypeFilter = null);
    string? ExportQuestLuaDebug(uint questRowId);
}
