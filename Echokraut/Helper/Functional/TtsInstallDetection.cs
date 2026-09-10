using System.IO;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Detects which TTS engine(s) are actually installed under the shared root, independent of the
/// persisted <c>LocalInstall</c> flags. Used to (a) show per-engine install state in the UI,
/// (b) gate the voice-copy source on an existing install, (c) recover a lost <c>LocalInstall</c>
/// flag when a prior install is found on disk.
/// </summary>
public static class TtsInstallDetection
{
    /// <summary>AllTalk is present when its entry script exists under <c>alltalk_tts\</c>.</summary>
    public static bool IsAllTalkInstalled(string installRoot) =>
        !string.IsNullOrEmpty(installRoot) &&
        File.Exists(Path.Combine(TtsPaths.AllTalkRoot(installRoot), "script.py"));

    /// <summary>EchokrauTTS is present when the bootstrap's model-preload marker exists
    /// (<c>echokrautts\.state\model.done</c>) — the last step that completes a usable install.</summary>
    public static bool IsEchokrauTtsInstalled(string installRoot) =>
        !string.IsNullOrEmpty(installRoot) &&
        File.Exists(Path.Combine(TtsPaths.EchokrauTtsRoot(installRoot), ".state", "model.done"));
}
