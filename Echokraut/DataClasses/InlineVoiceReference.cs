using System;
using System.Collections.Generic;

namespace Echokraut.DataClasses;

/// <summary>
/// A zero-shot voice reference that travels to EchokrauTTS <b>inline</b> (plan
/// <c>docs/plans/game-voice-reference.md</c> §2.4): PCM decoded in memory from the user's own
/// game files, the game text spoken in it, and the ids of the clips it was built from. Lives only
/// for one request (plus the small in-memory LRU of <c>GameVoiceReferenceService</c>); it is never
/// written anywhere. <see cref="ToString"/> deliberately leaves out text and audio.
/// </summary>
public sealed class InlineVoiceReference
{
    /// <summary>The only format the wrapper accepts for <c>ref_audio</c>.</summary>
    public const int SampleRate = 24000;

    public InlineVoiceReference(short[] pcm, string text, IReadOnlyList<string> clipIds)
    {
        Pcm = pcm ?? throw new ArgumentNullException(nameof(pcm));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        ClipIds = clipIds ?? throw new ArgumentNullException(nameof(clipIds));
    }

    /// <summary>Mono 16-bit PCM at <see cref="SampleRate"/>.</summary>
    public short[] Pcm { get; }

    /// <summary>Game text of the clip(s), in order — sent as <c>ref_text</c>.</summary>
    public string Text { get; }

    /// <summary>Clip ids (audio bases) — sent as <c>ref_ids</c>, keys the wrapper's conditioning cache.</summary>
    public IReadOnlyList<string> ClipIds { get; }

    public double Seconds => (double)Pcm.Length / SampleRate;

    public override string ToString() => $"InlineVoiceReference({ClipIds.Count} clip(s), {Seconds:0.00}s)";
}
