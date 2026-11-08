public static class Constants
{
    public const string ALLTALKFOLDERNAME = "alltalk_tts";
    public const string ECHOKRAUTTSFOLDERNAME = "echokrautts";
    public const string ECHOKRAUTTSREADYFILE = "Ready.EchokrauTTS.txt";
    // On-disk layout under the echokrautts wrapper root — must match TtsPaths in the
    // plugin AND config.py's samples_dir/models_dir + CUSTOM_MODEL_DIRNAME in the wrapper.
    public const string ECHOKRAUTTSSAMPLESFOLDER = "samples";
    public const string ECHOKRAUTTSMODELSFOLDER = "models";
    public const string ECHOKRAUTTSCUSTOMMODELFOLDER = "echokraut_custom";
    // Top-level wrapper folders that a wrapper UPDATE must never touch: the user's voice samples
    // and the (multi-GB) downloaded models. Only the "updateechokrautts" mode honours this — a
    // full reinstall deliberately starts from a clean wrapper folder.
    public static readonly string[] PRESERVEDECHOKRAUTTSFOLDERS =
        { ECHOKRAUTTSSAMPLESFOLDER, ECHOKRAUTTSMODELSFOLDER };

    // Fallbacks for the optional trailing wrapper args — an older plugin build may omit them.
    public const string DEFAULTTTSBACKEND = "xtts";
    public const string DEFAULTXTTSFP16 = "false";

    // uv provides the managed Python that runs the wrapper's bootstrap.py. Kept inside the wrapper
    // folder (.uv/) so the install stays self-contained. Must match what bootstrap/install_win.ps1
    // fetches — that script is the standalone equivalent of EnsureUvAsync().
    public const string UVWINDOWSASSET = "uv-x86_64-pc-windows-msvc.zip";
    public const string UVWINDOWSURL =
        "https://github.com/astral-sh/uv/releases/latest/download/" + UVWINDOWSASSET;
    public const string UVFOLDERNAME = ".uv";
    public const string UVPYTHONVERSION = "3.11";

    public const string MSBUILDTOOLSMSVC = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";
    public const string MSBUILDTOOLSWIN10SDK = "Microsoft.VisualStudio.Component.Windows10SDK.19041";
    public const string MSBUILDTOOLSWIN11SDK = "Microsoft.VisualStudio.Component.Windows11SDK.22621";

    public static readonly string[] ALLTALKDEBUGLOGCOLOR = { @"\033[94m", @"\033[93m" };
    public static readonly string[] ALLTALKERRORLOGCOLOR = { @"\033[91m" };
}
