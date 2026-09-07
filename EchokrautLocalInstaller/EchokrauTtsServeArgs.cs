/// <summary>
/// Everything the EchokrauTTS wrapper bootstrap needs for one "install/update-if-asked, then serve"
/// run. Bundled into a record because the three modes that launch the wrapper (<c>echokrautts</c>,
/// <c>updateechokrautts</c>, the restart tail of <c>installcustomdataek</c>) otherwise pass the same
/// eight positional values around by hand.
/// </summary>
/// <param name="InstallRoot">Shared TTS install root; the wrapper lives in its <c>echokrautts</c> subfolder.</param>
/// <param name="EchokrauTtsUrl">Wrapper zip to download first, or empty to run the existing install.</param>
/// <param name="PreserveUserData">Update mode: unpack the zip but keep <c>samples/</c> + <c>models/</c>.</param>
sealed record EchokrauTtsServeArgs(
    string InstallRoot,
    string EchokrauTtsUrl,
    string Port,
    string Language,
    string ParentPid,
    string TtsBackend,
    string XttsFp16,
    bool PreserveUserData)
{
    /// <summary>
    /// Parses the <c>echokrautts</c> / <c>updateechokrautts</c> command line:
    /// <c>&lt;mode&gt; &lt;installRoot&gt; &lt;url-or-empty&gt; &lt;isWindows&gt; &lt;port&gt;
    /// &lt;language&gt; &lt;parentPid&gt; [ttsBackend] [xttsFp16]</c>. Both modes take the same
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
        PreserveUserData: args[0] == UpdateMode);

    /// <summary>
    /// Parses the restart tail of <c>installcustomdataek &lt;installRoot&gt; &lt;modelUrl&gt;
    /// &lt;voicesUrl&gt; &lt;isWindows&gt; &lt;shouldRestart&gt; &lt;port&gt; &lt;language&gt;
    /// &lt;parentPid&gt; [ttsBackend] [xttsFp16]</c>. No URL: the custom-data mode never
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
        PreserveUserData: false);

    /// <summary>
    /// Reads an optional trailing argument. The wrapper modes gained arguments over time and an
    /// older plugin build may send fewer, so a missing or blank slot falls back to the default
    /// rather than throwing.
    /// </summary>
    private static string Optional(string[] args, int index, string fallback)
        => args.Length > index && !string.IsNullOrWhiteSpace(args[index]) ? args[index] : fallback;

    /// <summary>Single-line form for the installer log.</summary>
    public string Describe()
        => $"installRoot={InstallRoot} | url={(string.IsNullOrEmpty(EchokrauTtsUrl) ? "(none)" : EchokrauTtsUrl)} " +
           $"| port={Port} | language={Language} | parentPid={ParentPid} " +
           $"| ttsBackend={TtsBackend} | xttsFp16={XttsFp16} | preserveUserData={PreserveUserData}";
}
