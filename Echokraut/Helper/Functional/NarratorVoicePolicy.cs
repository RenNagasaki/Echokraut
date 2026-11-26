using System;
using System.Collections.Generic;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Decides which voice is the narrator — the fallback used for lines without a resolvable speaker
/// (narrator / system text) and for NPCs that no other voice fits.
/// <para>Two sources, in this order:</para>
/// <list type="number">
///   <item>the user's pick (<see cref="Configuration.NarratorVoice"/>, a backend voice key), which
///   overrules everything as long as it still names an <b>enabled</b> voice;</item>
///   <item>otherwise the voice flagged <see cref="EchokrautVoice.IsDefault"/> — set by
///   <c>BackendService.MapVoices</c> on the shipped <c>Narrator.wav</c>.</item>
/// </list>
/// The fallback is deliberate: a stale key (voice pack replaced, voice switched off) must leave the
/// narrator audible with the shipped default instead of mute.
/// </summary>
public static class NarratorVoicePolicy
{
    /// <summary>
    /// The narrator voice out of <paramref name="voices"/>, or <c>null</c> if the list holds neither
    /// the user's pick nor a flagged default.
    /// </summary>
    public static EchokrautVoice? Resolve(IList<EchokrautVoice>? voices, string? userPick)
    {
        if (voices == null || voices.Count == 0) return null;

        EchokrautVoice? flaggedDefault = null;
        EchokrautVoice? picked = null;

        for (var i = 0; i < voices.Count; i++)
        {
            var voice = voices[i];
            if (flaggedDefault == null && voice.IsDefault)
                flaggedDefault = voice;
            // IsEnabled, not just a key match: a user who disables their narrator pick wants it
            // silent, and the shipped default is then the only sensible stand-in.
            if (picked == null && voice.IsEnabled && MatchesUserPick(voice, userPick))
                picked = voice;
        }

        return picked ?? flaggedDefault;
    }

    /// <summary>
    /// True if <paramref name="voice"/> is exempt from the race/gender/body-type filter in
    /// <c>BackendService.IsVoiceFittingForNpc</c> — the narrator must never be rejected as
    /// "unfitting" for the speaker it was picked for.
    /// <para>Exempt are the user's pick AND the flagged <c>Narrator.wav</c>: the flagged default
    /// stays exempt even while overruled, because it is still the fallback whenever the pick goes
    /// away, and revoking its pass would silently re-roll every NPC that already sits on it.
    /// Overruling changes which voice <see cref="Resolve"/> hands out, not which voices are
    /// filter-exempt.</para>
    /// <para>List-free on purpose: this runs per voice per line and must not re-read the voice
    /// table.</para>
    /// </summary>
    public static bool IsFilterExempt(EchokrautVoice? voice, string? userPick)
        => voice != null && (voice.IsDefault || MatchesUserPick(voice, userPick));

    private static bool MatchesUserPick(EchokrautVoice voice, string? userPick)
        => !string.IsNullOrWhiteSpace(userPick)
           && string.Equals(voice.BackendVoice, userPick, StringComparison.OrdinalIgnoreCase);
}
