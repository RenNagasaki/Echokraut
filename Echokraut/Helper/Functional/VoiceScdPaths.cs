using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Plugin.Services;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Resolves the on-disk SCD path family for a FFXIV cutscene-voice audio base. Used by:
/// <list type="bullet">
/// <item><see cref="Echokraut.Services.VoiceSampleExtractorService"/> — picks the first
///   path that would resolve the audio file itself (Echokraut never reads game audio).</item>
/// <item><see cref="Echokraut.Services.DialogHarvestService"/> — checks whether any path
///   exists at all, treating "exists in user's locale" as "voiced; skip TTS-harvesting".</item>
/// </list>
/// Centralized here so the substring math (proven against live game data) lives in one
/// place — both consumers stay agreement on what "voiced" means.
/// </summary>
public static class VoiceScdPaths
{
    /// <summary>FFXIV expansion sub-paths used in <c>cut/{exp}/sound/...</c>. We try each
    /// in order until the first SCD resolves (same cutscene-id range can be referenced
    /// from any expansion's archive; first hit wins).</summary>
    private static readonly string[] ExpansionKeys =
    {
        "ffxiv", "ex1", "ex2", "ex3", "ex4", "ex5",
    };

    /// <summary>
    /// Map a Dalamud client language onto the language token embedded in SCD filenames
    /// (<c>en</c>/<c>de</c>/<c>fr</c>/<c>ja</c>). Defaults to English when the language
    /// is unrecognized (future-proofs against new ClientLanguage values).
    /// </summary>
    public static string LanguageCodeForScd(ClientLanguage language) => language switch
    {
        ClientLanguage.English => "en",
        ClientLanguage.German => "de",
        ClientLanguage.French => "fr",
        ClientLanguage.Japanese => "ja",
        _ => "en",
    };

    /// <summary>
    /// Build the list of candidate SCD paths for an audio file base. The proven layout
    /// (verified against live game data) is Tools' substring math:
    /// <c>cut/{exp}/sound/{base[3..6]}/{base[3..14]}/{base}_{gender}_{lang}.scd</c> — for
    /// <c>"vo_voiceman_06006_000010"</c> that becomes
    /// <c>cut/{exp}/sound/voicem/voiceman_06006/vo_voiceman_06006_000010_m_de.scd</c>. The
    /// gender marker is empty (single underscore separator) for non-gender-branching lines,
    /// or <c>m</c>/<c>f</c> for the gendered variants. We iterate all expansions and all
    /// three gender suffixes — caller short-circuits on the first hit.
    ///
    /// <para>Note: the <c>_m_</c>/<c>_f_</c> suffix is NOT the speaker's gender — it marks
    /// player-gender-specific TEXT variants ("he"/"she" etc.) of the same line spoken by the
    /// same voice actor. So which one resolves first does not change the voice in the clip.</para>
    /// </summary>
    public static List<string> Build(string audioBase, string langCode)
    {
        var result = new List<string>(18);
        if (string.IsNullOrEmpty(audioBase) || audioBase.Length < 17) return result;

        var folder6 = audioBase.Substring(3, 6);
        var folder14 = audioBase.Substring(3, 14);
        // ManFst entries use a 9-char inner folder and the file path drops trailing tokens
        // (truncated base of 12 chars); we keep that legacy layout as a fallback.
        var isManFst = audioBase.Contains("manfst");

        foreach (var exp in ExpansionKeys)
        {
            // Gender marker has a LEADING underscore — real filenames are
            // <base>_m_<lang>.scd / <base>_f_<lang>.scd / <base>_<lang>.scd.
            foreach (var suffix in new[] { "_m_", "_", "_f_" })
            {
                if (isManFst && audioBase.Length >= 12)
                {
                    var folder9 = audioBase.Substring(3, 9);
                    result.Add($"cut/{exp}/sound/{folder6}/{folder9}/{audioBase.Substring(0, 12)}{suffix}{langCode}.scd");
                }
                else
                {
                    result.Add($"cut/{exp}/sound/{folder6}/{folder14}/{audioBase}{suffix}{langCode}.scd");
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Smallest SCD we accept as actual voice acting. Square Enix ships stub files for lines it
    /// cut: measured on live game data (2026-08-09) those are either 1 byte or a 416/448-byte
    /// header with no audio payload, while the smallest real clip is comfortably above 1 KB —
    /// 80 German / 76 English cutscene lines sit in that gap. The boundary is therefore
    /// empirical, not a guess.
    /// </summary>
    public const int MinVoicedScdBytes = 1024;

    /// <summary>
    /// True if at least one candidate SCD path resolves via Lumina <b>and holds real audio</b>.
    /// Used by the harvest to drop already-voiced lines — Echokraut won't TTS-fy what FFXIV
    /// already speaks in the user's locale.
    ///
    /// <para>⚠ Existence alone is not enough, and the scan must not stop at the first file it
    /// finds: a line can have a 1-byte <c>_m_</c> stub next to a real <c>_f_</c> clip, so a
    /// first-hit-wins loop would classify it from the stub. We keep scanning until a candidate
    /// clears <see cref="MinVoicedScdBytes"/>.</para>
    /// </summary>
    public static bool Exists(IDataManager dataManager, string audioBase, string langCode)
    {
        foreach (var path in Build(audioBase, langCode))
        {
            try
            {
                var file = dataManager.GetFile(path);
                if (file != null && file.Data.Length >= MinVoicedScdBytes)
                    return true;
            }
            catch
            {
                // Lumina occasionally throws on speculative paths — keep scanning.
            }
        }
        return false;
    }
}
