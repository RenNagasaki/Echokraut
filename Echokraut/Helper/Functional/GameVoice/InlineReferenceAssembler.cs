using System;
using System.Collections.Generic;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>One decoded reference clip (mono 16-bit, <see cref="InlineVoiceReference.SampleRate"/>).</summary>
public sealed record DecodedReferenceClip(string AudioBase, string Text, short[] Pcm);

/// <summary>
/// Joins the ranked clips of one NPC into the single <c>ref_audio</c> + <c>ref_text</c> the wrapper
/// takes. Clips are appended in rank order, separated by a short pause, while the total stays within
/// <see cref="MaxSeconds"/> (the wrapper's <c>inline_ref_max_seconds</c>); the best clip is always
/// included (the ranking already caps a single clip at 15 s).
/// </summary>
public static class InlineReferenceAssembler
{
    public const double MaxSeconds = 15.0;
    public const int GapMilliseconds = 300;

    public static InlineVoiceReference? Assemble(IReadOnlyList<DecodedReferenceClip> rankedClips)
    {
        ArgumentNullException.ThrowIfNull(rankedClips);
        if (rankedClips.Count == 0) return null;

        var maxSamples = (int)(MaxSeconds * InlineVoiceReference.SampleRate);
        var gap = GapMilliseconds * InlineVoiceReference.SampleRate / 1000;
        var parts = new List<DecodedReferenceClip>();
        var total = 0;
        foreach (var clip in rankedClips)
        {
            var add = clip.Pcm.Length + (parts.Count > 0 ? gap : 0);
            if (parts.Count > 0 && total + add > maxSamples) continue; // a shorter later clip may still fit
            parts.Add(clip);
            total += add;
        }

        var pcm = new short[total];
        var texts = new List<string>(parts.Count);
        var ids = new List<string>(parts.Count);
        var pos = 0;
        foreach (var part in parts)
        {
            if (ids.Count > 0) pos += gap; // zero-filled pause
            Array.Copy(part.Pcm, 0, pcm, pos, part.Pcm.Length);
            pos += part.Pcm.Length;
            texts.Add(part.Text);
            ids.Add(part.AudioBase);
        }
        return new InlineVoiceReference(pcm, string.Join(" ", texts), ids);
    }
}
