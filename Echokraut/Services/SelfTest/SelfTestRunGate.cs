using System.Threading;

namespace Echokraut.Services.SelfTest;

/// <summary>
/// Only one test run at a time: <c>/ektest</c> refuses while an E2E run is going and vice versa
/// (plan <c>docs/plans/ingame-e2e.md</c> › Trigger and gate). Both services share one instance.
/// </summary>
public interface ISelfTestRunGate
{
    /// <summary>Who holds the gate (e.g. "self-test", "e2e 20261122-1"); null when free.</summary>
    string? Owner { get; }

    /// <summary>Takes the gate for <paramref name="owner"/>; false when someone else holds it.</summary>
    bool TryEnter(string owner);

    /// <summary>Frees the gate if <paramref name="owner"/> holds it.</summary>
    void Exit(string owner);
}

public sealed class SelfTestRunGate : ISelfTestRunGate
{
    private string? _owner;

    public string? Owner => Volatile.Read(ref _owner);

    public bool TryEnter(string owner) => Interlocked.CompareExchange(ref _owner, owner, null) == null;

    public void Exit(string owner) => Interlocked.CompareExchange(ref _owner, null, owner);
}
