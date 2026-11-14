using System;
using System.Collections.Generic;

namespace Echokraut.DataClasses;

/// <summary>
/// On-disk contract of an Echokraut audio package (<c>*.ekpack</c>) — a plain zip holding
/// <c>manifest.json</c> at the root and the generated WAVs under <c>audio/</c>.
///
/// <para>The manifest is the ONLY place the metadata lives. Deliberately no chunk is written into
/// the WAV files themselves: the live generation path stays untouched, and a package can be built
/// from audio that already exists on disk.</para>
/// </summary>
public static class AudioPackageFormat
{
    /// <summary>Manifest schema version written by this plugin.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Highest schema version this plugin can read.</summary>
    public const int MaxSupportedVersion = 1;

    /// <summary>File name of the manifest inside the package.</summary>
    public const string ManifestName = "manifest.json";

    /// <summary>Folder inside the package that holds the audio files.</summary>
    public const string AudioFolder = "audio";

    /// <summary>File extension of a package, including the dot.</summary>
    public const string Extension = ".ekpack";
}

/// <summary>Root object of <c>manifest.json</c>.</summary>
public sealed class AudioPackageManifest
{
    public int FormatVersion { get; set; } = AudioPackageFormat.CurrentVersion;

    /// <summary>Plugin version that produced the package, e.g. <c>Echokraut v0.19.3.1</c>.</summary>
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    /// <summary>Game language of the clips, or <c>null</c> when the package mixes languages.</summary>
    public int? Language { get; set; }

    public List<AudioPackageEntry> Entries { get; set; } = new();
}

/// <summary>
/// One audio file plus everything the receiving install needs to attribute it: which character
/// said it, in which language, with which voice, and which dialog line it belongs to.
/// </summary>
public sealed class AudioPackageEntry
{
    /// <summary>Path of the WAV inside the package's <c>audio/</c> folder, e.g. <c>Alphinaud/hello.wav</c>.</summary>
    public string File { get; set; } = "";

    // ── Character identity (the characters unique index: name + gender + race + language) ──
    public string NpcName { get; set; } = "";
    public int Gender { get; set; }
    public int Race { get; set; }
    public string RaceStr { get; set; } = "";
    public int ObjectKind { get; set; }
    public int Language { get; set; }

    // ── The dialog line ──
    public long NpcBaseId { get; set; }
    public int TextSource { get; set; }
    public int QuestType { get; set; }
    public int BodyType { get; set; }
    public string OriginalText { get; set; } = "";
    public string CleanedText { get; set; } = "";
    public bool HasPlayerPlaceholder { get; set; }
    public string WavFileName { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public float MapX { get; set; }
    public float MapY { get; set; }

    // ── The generation ──
    /// <summary>Voice sample the audio was generated with, e.g. <c>Male_All_NPC001.wav</c>.</summary>
    public string VoiceKey { get; set; } = "";

    /// <summary>0 = player-independent clip, 1 = male alias variant, 2 = female alias variant.</summary>
    public int AliasGender { get; set; }
}

/// <summary>
/// Flat projection of <c>voice_clip_generations ⋈ voice_clips ⋈ characters</c> — everything the
/// exporter needs in one round-trip, without dragging EF entity graphs into the service.
/// </summary>
public sealed class AudioPackageExportRow
{
    public string SavePath { get; set; } = "";
    public int AliasGender { get; set; }
    public string VoiceKey { get; set; } = "";

    public string NpcName { get; set; } = "";
    public int Gender { get; set; }
    public int Race { get; set; }
    public string RaceStr { get; set; } = "";
    public int ObjectKind { get; set; }
    public int Language { get; set; }

    public long NpcBaseId { get; set; }
    public int TextSource { get; set; }
    public int QuestType { get; set; }
    public int BodyType { get; set; }
    public string OriginalText { get; set; } = "";
    public string CleanedText { get; set; } = "";
    public bool HasPlayerPlaceholder { get; set; }
    public string WavFileName { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public float MapX { get; set; }
    public float MapY { get; set; }
}

/// <summary>Outcome of an export run.</summary>
public sealed class AudioPackageExportResult
{
    public string PackagePath { get; set; } = "";
    public int Exported { get; set; }

    /// <summary>Rows whose audio file no longer exists on disk.</summary>
    public int SkippedMissingFile { get; set; }

    /// <summary>
    /// Generations that speak the exporter's own character name (placeholder clip generated for
    /// the real player). Never packaged — see <see cref="Helper.Functional.AudioPackageRules"/>.
    /// </summary>
    public int SkippedPersonal { get; set; }

    public long BytesWritten { get; set; }
    public string? Error { get; set; }
}

/// <summary>Outcome of an import run.</summary>
public sealed class AudioPackageImportResult
{
    public int Imported { get; set; }

    /// <summary>Audio files that already existed locally — kept, the DB row is still written.</summary>
    public int SkippedExistingFile { get; set; }

    public int CreatedCharacters { get; set; }
    public int CreatedClips { get; set; }
    public int Failed { get; set; }
    public string? Error { get; set; }
}
