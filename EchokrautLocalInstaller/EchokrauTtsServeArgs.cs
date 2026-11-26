/// <summary>
/// Everything the EchokrauTTS wrapper bootstrap needs for one "install/update-if-asked, then serve"
/// run. Bundled into a record because the three modes that launch the wrapper (<c>echokrautts</c>,
/// <c>updateechokrautts</c>, the restart tail of <c>installcustomdataek</c>) otherwise pass the same
/// eight positional values around by hand.
/// </summary>
/// <param name="InstallRoot">Shared TTS install root; the wrapper lives in its <c>echokrautts</c> subfolder.</param>
/// <param name="EchokrauTtsUrl">Wrapper zip to download first, or empty to run the existing install.</param>
/// <param name="PreserveUserData">Update mode: unpack the zip but keep <c>samples/</c> + <c>models/</c>.</param>
/// <param name="QwenTemperature">Qwen option (e.g. <c>0.7</c>); empty = not passed, the wrapper's own default applies.</param>
/// <param name="PocketDecodeSteps">Pocket option (<c>1</c>/<c>4</c>/<c>8</c>); empty = not passed.</param>
/// <param name="PocketLayers">Pocket model size (<c>24</c>/<c>6</c>); empty = not passed.</param>
/// <param name="ResponseCacheMinutes">Wrapper response cache (<c>--response-cache-minutes</c>, <c>0</c> = off);
/// empty = not passed, the wrapper's <c>config.json</c> decides. A value given here overrides it.</param>
sealed record EchokrauTtsServeArgs(
    string InstallRoot,
    string EchokrauTtsUrl,
    string Port,
    string Language,
    string ParentPid,
    string TtsBackend,
    string XttsFp16,
    bool PreserveUserData,
    string QwenTemperature = "",
    string PocketDecodeSteps = "",
    string PocketLayers = "",
    string ResponseCacheMinutes = "")
{
    /// <summary>
    /// Parses the <c>echokrautts</c> / <c>updateechokrautts</c> command line:
    /// <c>&lt;mode&gt; &lt;installRoot&gt; &lt;url-or-empty&gt; &lt;isWindows&gt; &lt;port&gt;
    /// &lt;language&gt; &lt;parentPid&gt; [ttsBackend] [xttsFp16] [qwenTemperature] [pocketDecodeSteps] [pocketLayers] [responseCacheMinutes]</c>. Both modes take the same
    /// arguments; only the unpacking differs, which is derived from the mode word itself.
    /// </summary>
    public const string UpdateMode = "updateechokrautts";

    public static EchokrauTtsServeArgs FromWrapperMode(string[] args) => new(
        InstallRoot: args[1],
        EchokrauTtsUrl: args[2],
        Port: args[4],
        Language: args[5],
        ParentPid: args[6],
        TtsBackend: Optional(args, 7, Constants.DEFAULTTTSBACKEND),
        XttsFp16: Optional(args, 8, Constants.DEFAULTXTTSFP16),
        PreserveUserData: args[0] == UpdateMode,
        QwenTemperature: Optional(args, 9, ""),
        PocketDecodeSteps: Optional(args, 10, ""),
        PocketLayers: Optional(args, 11, ""),
        ResponseCacheMinutes: Optional(args, 12, ""));

    /// <summary>
    /// Parses the restart tail of <c>installcustomdataek &lt;installRoot&gt; &lt;modelUrl&gt;
    /// &lt;voicesUrl&gt; &lt;isWindows&gt; &lt;shouldRestart&gt; &lt;port&gt; &lt;language&gt;
    /// &lt;parentPid&gt; [ttsBackend] [xttsFp16] [qwenTemperature] [pocketDecodeSteps] [pocketLayers] [responseCacheMinutes]</c>. No URL: the custom-data mode never
    /// re-downloads the wrapper, it only restarts what is already installed.
    /// </summary>
    public static EchokrauTtsServeArgs FromCustomDataMode(string[] args) => new(
        InstallRoot: args[1],
        EchokrauTtsUrl: "",
        Port: args[6],
        Language: args[7],
        ParentPid: args[8],
        TtsBackend: Optional(args, 9, Constants.DEFAULTTTSBACKEND),
        XttsFp16: Optional(args, 10, Constants.DEFAULTXTTSFP16),
        PreserveUserData: false,
        QwenTemperature: Optional(args, 11, ""),
        PocketDecodeSteps: Optional(args, 12, ""),
        PocketLayers: Optional(args, 13, ""),
        ResponseCacheMinutes: Optional(args, 14, ""));

    /// <summary>
    /// Reads an optional trailing argument. The wrapper modes gained arguments over time and an
    /// older plugin build may send fewer, so a missing or blank slot falls back to the default
    /// rather than throwing.
    /// </summary>
    private static string Optional(string[] args, int index, string fallback)
        => args.Length > index && !string.IsNullOrWhiteSpace(args[index]) ? args[index] : fallback;

    /// <summary>
    /// Arguments forwarded to bootstrap.py (on Linux through install_linux.sh, which passes them on).
    /// <paramref name="includeParentPid"/> is false under Wine: <see cref="ParentPid"/> is then a Wine
    /// process id, meaningless to the native wrapper — its watchdog would find no such process and shut
    /// the server down at once. There the launch script's heartbeat check takes over that job.
    /// </summary>
    public IReadOnlyList<string> BootstrapArguments(bool includeParentPid)
    {
        var list = new List<string> { "--host", "127.0.0.1", "--port", Port, "--language", Language };
        if (includeParentPid)
        {
            list.Add("--parent-pid");
            list.Add(ParentPid);
        }
        // Sub-engine (f5/xtts/moss/qwen-0.6b/qwen-1.7b). The bootstrap installs only this one and
        // fetches another on the next start after a switch.
        list.Add("--tts-backend");
        list.Add(TtsBackend);
        // XTTS half-precision (true/false). No-op for f5 / non-CUDA — the wrapper gates it.
        list.Add("--xtts-fp16");
        list.Add(XttsFp16);
        // Qwen/Pocket options — only when the plugin sent them, so an older plugin (or a value left blank)
        // keeps the wrapper's own default instead of an installer-side guess. No-op for other engines.
        AddIfSet(list, "--qwen-temperature", QwenTemperature);
        AddIfSet(list, "--pocket-decode-steps", PocketDecodeSteps);
        AddIfSet(list, "--pocket-layers", PocketLayers);
        // Any engine: a start parameter beats the wrapper's config.json, so the plugin can switch the
        // cache off for listening tests without touching the install.
        AddIfSet(list, "--response-cache-minutes", ResponseCacheMinutes);
        return list;
    }

    private static void AddIfSet(List<string> list, string flag, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        list.Add(flag);
        list.Add(value);
    }

    /// <summary>Single-line form for the installer log.</summary>
    public string Describe()
        => $"installRoot={InstallRoot} | url={(string.IsNullOrEmpty(EchokrauTtsUrl) ? "(none)" : EchokrauTtsUrl)} " +
           $"| port={Port} | language={Language} | parentPid={ParentPid} " +
           $"| ttsBackend={TtsBackend} | xttsFp16={XttsFp16} " +
           $"| qwenTemperature={QwenTemperature} | pocketDecodeSteps={PocketDecodeSteps} | pocketLayers={PocketLayers} | responseCacheMinutes={ResponseCacheMinutes} " +
           $"| preserveUserData={PreserveUserData}";
}
