using System;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services;

/// <inheritdoc />
public sealed class FirstTimeWizardController : IFirstTimeWizardController
{
    private static readonly EKEventId BackendEvent = new(0, TextSource.Backend);

    private readonly Configuration _config;
    private readonly IAlltalkInstanceService _alltalk;
    private readonly IEchokrauTtsInstanceService _echokrauTts;
    private readonly IBackendService _backend;
    private readonly IBatchModeService _batchMode;
    private readonly object _testLock = new();

    public FirstTimeWizardController(Configuration config, IAlltalkInstanceService alltalk,
        IEchokrauTtsInstanceService echokrauTts, IBackendService backend, IBatchModeService batchMode)
    {
        _config = config;
        _alltalk = alltalk;
        _echokrauTts = echokrauTts;
        _backend = backend;
        _batchMode = batchMode;
    }

    public event Action? Completed;

    public int Step { get; private set; }
    public bool RemoteTestSucceeded { get; private set; }
    public string RemoteTestUrlSnapshot { get; private set; } = string.Empty;
    public bool RemoteTestRunning { get; private set; }
    public bool IsWindowOpen { get; private set; }

    private bool IsEk => _config.BackendSelection == TTSBackends.EchokrauTTS;
    private string ActiveBaseUrl => IsEk ? _config.EchokrauTts.BaseUrl : _config.Alltalk.BaseUrl;

    public bool CanAdvance
    {
        get
        {
            // A batch op is talking to the backend being configured: nothing may move on.
            if (_batchMode.IsActive) return false;
            return _config.ActiveInstanceType switch
            {
                AlltalkInstanceType.Local => IsEk ? _config.EchokrauTts.LocalInstall : _config.Alltalk.LocalInstall,
                AlltalkInstanceType.Remote => RemoteTestSucceeded
                                              && string.Equals(RemoteTestUrlSnapshot, ActiveBaseUrl, StringComparison.Ordinal),
                _ => true,
            };
        }
    }

    public void ChooseEngine(TTSBackends engine)
    {
        _config.BackendSelection = engine;
        _config.Save();
    }

    public void ChooseMode(AlltalkInstanceType mode)
    {
        // The ACTIVE engine's type (mirrors NativeConfigWindow.SetActiveInstanceType).
        if (IsEk) _config.EchokrauTts.InstanceType = mode;
        else _config.Alltalk.InstanceType = mode;
        _config.Save();
        Step = 1;
    }

    public void SetInstallRoot(string path)
    {
        _config.TtsInstallRoot = path;
        _config.Save();
    }

    public void SetLocalSaveLocation(string path)
    {
        _config.LocalSaveLocation = path;
        _config.Save();
    }

    public void SetShareLines(bool share)
    {
        _config.ShareLinesWithCommunityDb = share;
        _config.Save();
    }

    public Task StartInstall(bool testWrapper = false)
    {
        var ek = IsEk;
        // Stop (HTTP + kill) and Install both block: never on the framework thread.
        return Task.Run(() =>
        {
            if (ek)
            {
                if (_echokrauTts.InstanceRunning || _echokrauTts.InstanceStarting)
                    _echokrauTts.StopInstance(BackendEvent);
                if (testWrapper) _echokrauTts.InstallTestWrapper();
                else _echokrauTts.Install();
            }
            else
            {
                if (_alltalk.InstanceRunning || _alltalk.InstanceStarting)
                    _alltalk.StopInstance(BackendEvent);
                _alltalk.Install();
            }
        });
    }

    public async Task<WizardConnectionResult> TestConnectionAsync()
    {
        lock (_testLock)
        {
            if (RemoteTestRunning) return new WizardConnectionResult(false, "A connection test is already running.");
            RemoteTestRunning = true;
            // Invalidate the prior verdict before the request starts.
            RemoteTestSucceeded = false;
            RemoteTestUrlSnapshot = string.Empty;
        }

        var urlAtStart = ActiveBaseUrl;
        try
        {
            // CheckReady returns "Ready" on success (AllTalk and EchokrauTTS alike), an error text otherwise.
            var answer = (await _backend.CheckReady(new EKEventId(0, TextSource.None)).ConfigureAwait(false)) ?? string.Empty;
            var success = string.Equals(answer.Trim(), "Ready", StringComparison.OrdinalIgnoreCase);
            RemoteTestSucceeded = success;
            RemoteTestUrlSnapshot = success ? urlAtStart : string.Empty;
            return new WizardConnectionResult(success, answer);
        }
        catch (Exception ex)
        {
            RemoteTestSucceeded = false;
            RemoteTestUrlSnapshot = string.Empty;
            return new WizardConnectionResult(false, ex.Message);
        }
        finally
        {
            RemoteTestRunning = false;
        }
    }

    public bool Next()
    {
        // The guard lives here and not only in the dimming: a dimmed node still delivers its click.
        if (Step != 1 || !CanAdvance) return false;
        Step = 2;
        return true;
    }

    public void Back()
    {
        if (Step > 0) Step--;
    }

    public bool Finish()
    {
        if (Step != 2) return false;
        _config.FirstTime = false;
        _config.Save();
        Completed?.Invoke();
        return true;
    }

    public void ReportWindowOpen(bool open) => IsWindowOpen = open;
}
