using System;
using System.Collections.Generic;
using System.IO;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Builds the label list for a voice dropdown and maps a picked label back to its voice.
///
/// <para>Why this isn't just <c>voices.ConvertAll(v =&gt; v.VoiceName)</c> plus a
/// <c>Find(v =&gt; v.VoiceName == picked)</c>: <see cref="EchokrautVoice.VoiceName"/> is a display
/// name derived from the sample filename (first segment that isn't a gender / race / body-type
/// token). Two different samples can therefore produce the SAME display name — e.g.
/// <c>Male_All_NPC001.wav</c> and <c>Male_All-Elder_NPC001.wav</c> both show "NPC001" — and a
/// filename made up entirely of grammar tokens (<c>Male_All.wav</c>) produces an EMPTY one.
/// A name-based reverse lookup then silently resolves to the first match, so picking the second
/// entry applied the first voice: the user's selection looked ignored. Labels here are made
/// unique (and never empty), and the reverse lookup goes through the same list by index.</para>
/// </summary>
public static class VoiceOptionList
{
    /// <summary>
    /// Display labels, positionally aligned with <paramref name="voices"/>. Empty display names
    /// fall back to the sample filename; duplicates get the sample filename appended so every
    /// entry is distinguishable and stable across rebuilds.
    /// </summary>
    public static List<string> BuildLabels(IReadOnlyList<EchokrautVoice>? voices)
    {
        var labels = new List<string>();
        if (voices == null || voices.Count == 0) return labels;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var voice in voices)
        {
            var name = BaseLabel(voice);
            counts.TryGetValue(name, out var seen);
            counts[name] = seen + 1;
        }

        foreach (var voice in voices)
        {
            var name = BaseLabel(voice);
            labels.Add(counts[name] > 1 ? $"{name} ({FileLabel(voice)})" : name);
        }

        return labels;
    }

    /// <summary>
    /// Maps a label produced by <see cref="BuildLabels"/> back to its voice in the same list.
    /// Returns null when the label doesn't belong to this list (stale selection, list rebuilt
    /// underneath) — callers should treat that as "no change".
    /// </summary>
    public static EchokrautVoice? Resolve(IReadOnlyList<EchokrautVoice>? voices, string? label)
    {
        if (voices == null || string.IsNullOrEmpty(label)) return null;

        var labels = BuildLabels(voices);
        for (var i = 0; i < labels.Count; i++)
        {
            if (string.Equals(labels[i], label, StringComparison.Ordinal))
                return voices[i];
        }

        return null;
    }

    /// <summary>Label for a voice as it appears in a freshly built list (used to preselect).</summary>
    public static string LabelFor(IReadOnlyList<EchokrautVoice>? voices, EchokrautVoice? voice)
    {
        if (voices == null || voice == null) return string.Empty;

        var labels = BuildLabels(voices);
        for (var i = 0; i < labels.Count; i++)
        {
            if (string.Equals(voices[i].BackendVoice, voice.BackendVoice, StringComparison.OrdinalIgnoreCase))
                return labels[i];
        }

        return string.Empty;
    }

    private static string BaseLabel(EchokrautVoice voice)
    {
        var name = voice.VoiceName;
        return string.IsNullOrWhiteSpace(name) ? FileLabel(voice) : name;
    }

    private static string FileLabel(EchokrautVoice voice)
    {
        var backend = voice.BackendVoice ?? string.Empty;
        var withoutExtension = Path.GetFileNameWithoutExtension(backend);
        return string.IsNullOrWhiteSpace(withoutExtension) ? backend : withoutExtension;
    }
}
