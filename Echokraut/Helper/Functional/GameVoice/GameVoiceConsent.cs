using System;
using System.Security.Cryptography;
using System.Text;
using Echokraut.Enums;
using Echokraut.Services;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>
/// Opt-in for the game-voice reference (plan <c>docs/plans/game-voice-reference.md</c> §5.2/§5.3).
/// The English strings are the <c>Loc</c> keys; the translations live in <c>Localization/Loc.cs</c>.
/// <para><b>Changing any text here requires bumping <see cref="CurrentVersion"/></b>: a stored
/// consent only counts for the exact text the user confirmed, a newer text asks again.
/// <c>GameVoiceConsentTests</c> pins the text hash per version and goes red on an unbumped edit.</para>
/// </summary>
public static class GameVoiceConsent
{
    /// <summary>Version of the confirmed text. 0 in the config = never confirmed.</summary>
    public const int CurrentVersion = 1;

    public const string Title = "Use game voices as reference?";

    /// <summary>One paragraph per entry (each is its own <c>Loc</c> key).</summary>
    public static readonly string[] Paragraphs =
    {
        "Echokraut can use voice recordings from your own FFXIV installation as a reference for the same NPC. They are decoded in memory only and never written to disk.",
        "The result is an AI imitation of real voice actors. Echokraut is not affiliated with Square Enix.",
        "Do not publish, stream, record or share the generated audio.",
        "Only use this with your own private EchokrauTTS instance - not with servers run by other people or shared/public servers.",
        "For private use only. You can switch it off at any time; the voice pack stays the default.",
    };

    public const string Accept = "I understand, enable";
    public const string Decline = "Cancel";

    /// <summary>Shown next to the switch while the active EchokrauTTS instance is Remote (§5.3).</summary>
    public const string RemoteHint = "Remote server: only use your own private instance, never a server of other people or a shared/public one.";

    /// <summary>Shown under the switch: §6 export/sync exclusion.</summary>
    public const string ExportHint = "Clips generated with a game voice stay on this PC: they are not exported to .ekpack and not uploaded to Google Drive.";

    public const string DisableTitle = "Delete clips made with game voices?";
    public const string DisableBody = "The game-voice reference is now off. Clips that were generated with a game voice can be deleted; otherwise they stay on this PC and are regenerated with the voice pack while the feature is off.";
    public const string DisableDelete = "Delete clips";
    public const string DisableKeep = "Keep";

    /// <summary>True when the stored confirmation is for the current text.</summary>
    public static bool IsGranted(int storedVersion) => storedVersion == CurrentVersion;

    /// <summary>
    /// The opt-in step when the user ticks the switch: the dialog must be shown unless the current
    /// text was already confirmed (a revoked switch does not ask twice for the same text).
    /// </summary>
    public static bool NeedsDialog(int storedVersion) => !IsGranted(storedVersion);

    /// <summary>SHA-256 over every user-visible consent string (pinned per version by the tests).</summary>
    public static string TextHash()
    {
        var sb = new StringBuilder();
        sb.Append(Title).Append('\n');
        foreach (var p in Paragraphs) sb.Append(p).Append('\n');
        sb.Append(Accept).Append('\n').Append(Decline);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>English <c>Loc</c> key for the status line under the switch.</summary>
    public static string AvailabilityText(GameVoiceAvailability availability) => availability switch
    {
        GameVoiceAvailability.Disabled => "Status: off - the voice pack speaks.",
        GameVoiceAvailability.WrongBackend => "Status: not supported by AllTalk - switch to EchokrauTTS. The voice pack speaks.",
        GameVoiceAvailability.Unknown => "Status: checking the EchokrauTTS server...",
        GameVoiceAvailability.ServerUnsupported => "Status: this EchokrauTTS server cannot take a game-voice reference (update the wrapper; MOSS on PyTorch cannot). The voice pack speaks.",
        GameVoiceAvailability.Available => "Status: active - indexed NPCs speak with their game voice.",
        _ => "Status: unknown",
    };

    /// <summary>The Remote hint is shown at the switch only for a remote EchokrauTTS instance.</summary>
    public static bool ShowRemoteHint(TTSBackends backend, AlltalkInstanceType instanceType)
        => backend == TTSBackends.EchokrauTTS && instanceType == AlltalkInstanceType.Remote;
}
