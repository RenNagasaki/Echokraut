using System.IO;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Single source of truth for the on-disk layout under the shared TTS install root
/// (<c>Configuration.TtsInstallRoot</c>). AllTalk lives in <c>alltalk_tts\</c> (voices in
/// <c>voices\</c>), EchokrauTTS in <c>echokrautts\</c> (voices in <c>samples\</c>). Installer,
/// voice-copy, and install-detection all compose paths here so they never desync.
/// </summary>
public static class TtsPaths
{
    public const string AllTalkFolder = "alltalk_tts";
    public const string EchokrauTtsFolder = "echokrautts";
    public const string AllTalkVoicesFolder = "voices";
    public const string EchokrauTtsSamplesFolder = "samples";

    public static string AllTalkRoot(string installRoot) =>
        Path.Combine(installRoot, AllTalkFolder);

    public static string AllTalkVoices(string installRoot) =>
        Path.Combine(AllTalkRoot(installRoot), AllTalkVoicesFolder);

    public static string EchokrauTtsRoot(string installRoot) =>
        Path.Combine(installRoot, EchokrauTtsFolder);

    public static string EchokrauTtsSamples(string installRoot) =>
        Path.Combine(EchokrauTtsRoot(installRoot), EchokrauTtsSamplesFolder);
}
