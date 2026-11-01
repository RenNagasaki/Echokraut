using System;
using System.IO;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional;

/// <summary>
/// The pure decisions behind audio package export/import: what may be shared, where a file sits
/// inside the package, and where it lands on the receiving install. Kept out of
/// <c>AudioPackageService</c> so the rules can be unit-tested without a database or a filesystem.
/// </summary>
public static class AudioPackageRules
{
    /// <summary>
    /// Whether a generation may go into a package.
    /// <para>
    /// A clip that carries a player-name placeholder is generated once per player: the row with
    /// <c>alias_gender == 0</c> has the EXPORTER'S character name spoken in the audio. Shipping it
    /// would leak the sender's character name and be wrong for the receiver, whose own live path
    /// would then adopt a file that greets someone else. The shareable form of exactly those clips
    /// already exists — the male/female alias variants (<c>alias_gender</c> 1/2) that say
    /// "Adventurer" / "Abenteurer" instead of a name — so those are packaged and the personal one
    /// is not.
    /// </para>
    /// Clips without a placeholder are player-independent and always shareable.
    /// </summary>
    public static bool IsShareable(bool hasPlayerPlaceholder, int aliasGender)
        => !hasPlayerPlaceholder || aliasGender != 0;

    /// <summary>
    /// Path a generated file gets inside the package: <c>&lt;speaker folder&gt;/&lt;file name&gt;</c>,
    /// mirroring the on-disk layout <c>AudioFileService</c> produces
    /// (<c>&lt;LocalSaveLocation&gt;/&lt;Speaker&gt;/&lt;hash&gt;.wav</c>). Only the last two
    /// segments travel — the sender's absolute save location must not leak into the package, and
    /// the receiver's is a different folder anyway. Returns <c>null</c> for a path we can't split
    /// that way.
    /// </summary>
    public static string? RelativeEntryPath(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath)) return null;

        var normalized = savePath!.Replace('\\', '/').TrimEnd('/');
        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrEmpty(fileName)) return null;

        var folder = Path.GetFileName(Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? "");
        return string.IsNullOrEmpty(folder) ? fileName : $"{folder}/{fileName}";
    }

    /// <summary>
    /// Absolute path the entry is written to on the receiving install. Returns <c>null</c> when the
    /// entry tries to escape the target folder (zip-slip) or is otherwise unusable — same guard the
    /// voice pack unpacker applies.
    /// </summary>
    public static string? ResolveImportTarget(string localSaveLocation, string? entryFile)
    {
        if (string.IsNullOrWhiteSpace(localSaveLocation)) return null;
        if (string.IsNullOrWhiteSpace(entryFile)) return null;
        if (Path.IsPathRooted(entryFile)) return null;

        var root = Path.GetFullPath(localSaveLocation);
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, entryFile!.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception)
        {
            return null;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// Context row an imported character needs so the Voice Clip Manager lists it: players go to
    /// <c>player</c>, speech bubbles to <c>bubble</c>, everything else to <c>npc</c>. Mirrors
    /// <c>NpcDataService</c> (ObjectKind) and the harvest (Balloon sheet → bubble).
    /// </summary>
    public static string ContextTypeFor(int objectKind, int textSource)
    {
        if (objectKind == (int)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc) return "player";
        if (textSource == (int)Echotools.Logging.Enums.TextSource.AddonBubble) return "bubble";
        return "npc";
    }

    /// <summary>
    /// Player id an imported generation is stored under. Always 0: alias variants are
    /// player-independent by definition, and non-placeholder clips key on 0 anyway
    /// (<c>IGameObjectService.GetEffectivePlayerContentId</c>). A package therefore never carries
    /// anybody's content id.
    /// </summary>
    public const long ImportPlayerContentId = 0L;

    /// <summary>Whether this plugin can read a package written with the given schema version.</summary>
    public static bool IsSupportedVersion(int formatVersion)
        => formatVersion > 0 && formatVersion <= AudioPackageFormat.MaxSupportedVersion;
}
