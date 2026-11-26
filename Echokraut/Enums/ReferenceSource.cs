namespace Echokraut.Enums;

/// <summary>
/// What the TTS engine was conditioned on when a stored clip was generated
/// (plan <c>docs/plans/game-voice-reference.md</c> §6). Persisted as an int on
/// <c>voice_clip_generations.reference_source</c>; the numbers must never change.
/// </summary>
public enum ReferenceSource
{
    /// <summary>The voice-pack sample (default, also every row written before schema v7).</summary>
    VoicePack = 0,

    /// <summary>
    /// A game-voice reference from the user's own installation. Such clips are cached locally only:
    /// never in a <c>.ekpack</c> export, never uploaded to Google Drive, deleted on request when the
    /// feature is switched off.
    /// </summary>
    GameVoice = 1,
}
