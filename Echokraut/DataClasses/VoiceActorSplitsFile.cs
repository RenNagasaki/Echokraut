using System.Collections.Generic;

namespace Echokraut.DataClasses;

/// <summary>
/// Schema for <c>Resources/VoiceActorSplits.json</c> (and its optional local override at
/// <c>&lt;localSaveLocation&gt;/FF14-Voices/voice_actor_splits.json</c>, which fully replaces
/// the embedded file — no merge, no remote layer).
///
/// <para>Some FFXIV characters had their dub voice actor changed mid-game (e.g. the German
/// Y'shtola/Iceheart voice). Cloning a single voice from samples that mix two actors produces
/// a blurred result. Each split entry tells the voice-sample extractor to partition that
/// character's clips into separate "epochs" at the patch boundaries where the actor changed,
/// writing one sample set per epoch with the epoch encoded in the filename
/// (<c>Female_Hyur_Iceheart_Pre06010.wav</c> / <c>…_Post06010.wav</c>).</para>
/// </summary>
public class VoiceActorSplitsFile
{
    public int Version { get; set; }
    public List<VoiceActorSplitEntry> Splits { get; set; } = new();
}

/// <summary>
/// One voice-actor split. The voice is identified by <see cref="VoiceKey"/> — the canonical
/// <c>Gender_Race[-BodyType]_Name</c> token the extractor produces (e.g.
/// <c>"Female_Hyur_Iceheart"</c>) — combined with <see cref="Language"/>, since actor changes
/// are usually specific to one dub language.
/// </summary>
public class VoiceActorSplitEntry
{
    /// <summary>Canonical voice key: <c>Gender_Race[-BodyType]_Name</c>, matched
    /// case-insensitively against <c>VoiceExtractFileNames.CanonicalNamePart(...)</c>.</summary>
    public string VoiceKey { get; set; } = string.Empty;

    /// <summary>Two-letter client language the split applies to: <c>EN</c>/<c>DE</c>/<c>FR</c>/<c>JA</c>.
    /// Case-insensitive. An entry only fires when the extractor runs in this language.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Optional human-readable note. Ignored by the loader.</summary>
    public string? Comment { get; set; }

    /// <summary>Patch boundaries where the actor changed, as 5-digit zero-padded patch tokens
    /// (the third underscore segment of the audio file base, e.g. <c>"06010"</c> = patch 6.01).
    /// Must be strictly ascending. Each boundary is "the first patch the NEW actor speaks in":
    /// clips with a patch token strictly less than the boundary are pre-boundary, tokens
    /// greater-or-equal are post-boundary. N boundaries → N+1 epochs.</summary>
    public List<string> BoundaryPatches { get; set; } = new();
}
