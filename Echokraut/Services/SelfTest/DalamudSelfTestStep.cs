using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.SelfTest;

namespace Echokraut.Services.SelfTest;

/// <summary>
/// Puts one <see cref="SelfTestCheck"/> into Dalamud's self-test window. Dalamud calls
/// <see cref="RunStep"/> once per frame while the step is active (inside its ImGui window, so
/// drawing is allowed here and nowhere else): the first call starts the check in the background,
/// later calls report <see cref="SelfTestStepResult.Waiting"/> until it is done. "Skipped" counts
/// as a pass there — the window knows no third state; the log and <c>/ektest</c> keep it apart.
/// </summary>
public sealed class DalamudSelfTestStep : ISelfTestStep
{
    private readonly SelfTestCheck _check;
    private readonly Action<SelfTestReportEntry> _finished;
    private CancellationTokenSource? _cts;
    private Task<SelfTestReportEntry>? _run;

    public DalamudSelfTestStep(SelfTestCheck check, Action<SelfTestReportEntry> finished)
    {
        _check = check;
        _finished = finished;
    }

    public string Name => $"Echokraut: {_check.Name}";

    public SelfTestStepResult RunStep()
    {
        if (_check.Instruction is { } instruction) ImGui.TextWrapped(instruction);

        if (_run == null)
        {
            _cts = new CancellationTokenSource();
            _run = SelfTestRunner.RunCheckAsync(_check, _cts.Token);
        }
        if (!_run.IsCompleted) return SelfTestStepResult.Waiting;

        var entry = _run.Result; // RunCheckAsync never throws
        _run = null;             // a rerun from the window starts fresh
        _finished(entry);
        return ToDalamud(entry.Outcome);
    }

    public void CleanUp()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _run = null;
    }

    internal static SelfTestStepResult ToDalamud(SelfTestOutcome outcome) =>
        outcome == SelfTestOutcome.Fail ? SelfTestStepResult.Fail : SelfTestStepResult.Pass;
}
