using System;
using System.Numerics;
using Echokraut.DataClasses;

namespace Echokraut.Services;

/// <summary>
/// Cross-cutting dialogue state shared by the addon hooks, the speech pipeline and the in-dialog
/// toolbar. Was a static class (<c>DialogState</c>); injected now so it is testable and doesn't
/// leak between plugin reloads (SonarQube S2696).
/// </summary>
public interface IDialogStateService
{
    /// <summary>
    /// The line currently being spoken, or the one just resolved. Published early by
    /// <c>VoiceMessageProcessor</c> — before the mute / no-voice / volume checks — because the
    /// in-dialog toolbar needs a speaker to offer a voice dropdown for. See the dialog-lifecycle
    /// section in Services/CLAUDE.md.
    /// </summary>
    VoiceMessage? CurrentVoiceMessage { get; set; }

    /// <summary>True while the game itself voices the current line (its own VO is playing).</summary>
    bool IsVoiced { get; set; }

    /// <summary>
    /// Hit-test against the plugin's own windows, registered by <c>DialogTalkController</c> and
    /// read by <c>AddonTalkHelper</c> so a click inside one of our windows doesn't advance or
    /// cancel the dialogue underneath.
    /// </summary>
    Func<Vector2, bool>? IsInsideOwnedWindow { get; set; }

    /// <summary>Records that a character has been resolved via the alias lookup this dialog.</summary>
    void MarkSpeakerResolved(int characterId);

    /// <summary>
    /// Whether a character was already resolved this dialog session. Drives the tie-breaker when a
    /// fakename like <c>???</c> matches several physically-present characters.
    /// </summary>
    bool WasSpeakerResolved(int characterId);

    /// <summary>Forgets every resolved speaker — called when the dialog closes.</summary>
    void ClearResolvedSpeakers();
}
