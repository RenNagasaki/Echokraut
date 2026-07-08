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

    public const string MSBUILDTOOLSMSVC = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";
    public const string MSBUILDTOOLSWIN10SDK = "Microsoft.VisualStudio.Component.Windows10SDK.19041";
    public const string MSBUILDTOOLSWIN11SDK = "Microsoft.VisualStudio.Component.Windows11SDK.22621";

    public static readonly string[] ALLTALKDEBUGLOGCOLOR = { @"\033[94m", @"\033[93m" };
    public static readonly string[] ALLTALKERRORLOGCOLOR = { @"\033[91m" };
}
