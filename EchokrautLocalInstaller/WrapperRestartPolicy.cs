/// <summary>
/// Decides whether the installer restarts an exited EchokrauTTS wrapper.
/// <para>The wrapper exits with <see cref="Constants.ECHOKRAUTTS_EXIT_RESTART_REQUIRED"/> after a
/// fatal GPU error ("CUDA error: device-side assert triggered"): its CUDA context is dead for the
/// whole process, so only a fresh process can serve again. Every other exit code (0 = stopped,
/// 1 = bootstrap failure, a kill) is final — restarting a broken install would just loop.</para>
/// <para>Capped at <see cref="MaxRestarts"/> within <see cref="Window"/>: a fault that recurs on
/// every start (bad driver, broken model) must end instead of reloading the model forever.</para>
/// </summary>
internal sealed class WrapperRestartPolicy
{
    public const int MaxRestarts = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly List<DateTime> _restarts = new();

    /// <summary>Restarts granted so far that still count against the window.</summary>
    public int RecentRestarts(DateTime nowUtc) => _restarts.Count(t => nowUtc - t < Window);

    /// <summary>True when the wrapper itself asked for a restart (not a stop, not a failure).</summary>
    public static bool IsRestartRequest(int? exitCode, bool stopRequested)
        => !stopRequested && exitCode == Constants.ECHOKRAUTTS_EXIT_RESTART_REQUIRED;

    /// <summary>True (and recorded) when the wrapper should be started again.</summary>
    public bool ShouldRestart(int? exitCode, bool stopRequested, DateTime nowUtc)
    {
        if (!IsRestartRequest(exitCode, stopRequested))
            return false;
        _restarts.RemoveAll(t => nowUtc - t >= Window);
        if (_restarts.Count >= MaxRestarts)
            return false;
        _restarts.Add(nowUtc);
        return true;
    }
}
