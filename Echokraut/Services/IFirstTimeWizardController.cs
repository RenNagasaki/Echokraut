using System;
using System.Threading.Tasks;
using Echokraut.Enums;

namespace Echokraut.Services;

/// <summary>Result of the wizard's remote connection test.</summary>
/// <param name="Success">The active backend answered "Ready".</param>
/// <param name="Message">The backend's answer or the error text (English, not localized).</param>
public sealed record WizardConnectionResult(bool Success, string Message);

/// <summary>
/// The first-time wizard's flow, without any UI (plan <c>docs/plans/ingame-e2e.md</c> › First-time
/// wizard controller). <c>NativeFirstTimeWindow</c> binds its buttons to these actions and reads
/// <see cref="Step"/> per frame; the E2E stage <c>first-time</c> calls the very same actions.
/// Step 0 = engine + mode, 1 = configure, 2 = finish.
/// </summary>
public interface IFirstTimeWizardController
{
    /// <summary>Raised by <see cref="Finish"/> after <c>FirstTime = false</c> was saved.</summary>
    event Action? Completed;

    int Step { get; }

    /// <summary>The last connection test succeeded against <see cref="RemoteTestUrlSnapshot"/>.</summary>
    bool RemoteTestSucceeded { get; }
    string RemoteTestUrlSnapshot { get; }
    bool RemoteTestRunning { get; }

    /// <summary>True while the window exists (reported by the window, read by the E2E stage).</summary>
    bool IsWindowOpen { get; }

    /// <summary>Step 1 → 2 is allowed: Local installed, Remote tested against the current URL, or None.</summary>
    bool CanAdvance { get; }

    void ChooseEngine(TTSBackends engine);

    /// <summary>Sets the active engine's instance type and moves to step 1.</summary>
    void ChooseMode(AlltalkInstanceType mode);

    void SetInstallRoot(string path);
    void SetLocalSaveLocation(string path);
    void SetShareLines(bool share);

    /// <summary>
    /// Installs the active engine locally: stops a running instance first, then <c>Install()</c>,
    /// both on the thread pool (both block). Returns the task that started the install, not the install.
    /// <paramref name="testWrapper"/> (EchokrauTTS only, the E2E run's <c>testWrapper</c>): install the
    /// developer's local wrapper zip (<c>InstallTestWrapper</c>) instead of downloading the release.
    /// </summary>
    Task StartInstall(bool testWrapper = false);

    /// <summary>Asks the active backend whether it is ready and remembers the URL it tested.</summary>
    Task<WizardConnectionResult> TestConnectionAsync();

    /// <summary>Step 1 → 2 when <see cref="CanAdvance"/>; false (nothing changes) otherwise.</summary>
    bool Next();

    void Back();

    /// <summary>On step 2: <c>FirstTime = false</c>, save, raise <see cref="Completed"/>. False on any other step.</summary>
    bool Finish();

    /// <summary>Called by the window from its setup/finalize.</summary>
    void ReportWindowOpen(bool open);
}
