using System.Collections.Generic;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Picks the voice pack to download for the language the client is running in.
///
/// <para>Order: the pack published for that language, else the English one, else the single
/// <c>voicePackUrl</c> the plugin shipped before per-language packs existed. Only the last step is
/// there for compatibility — a pack list that names English makes it unreachable, which is fine.</para>
///
/// <para><b>Why a published map and not a guessed URL:</b> a naming convention (<c>…-DE.zip</c>)
/// would have to be probed over the network on every install, and a hoster answering a missing file
/// with anything but 404 turns "no German pack yet" into a broken download. Listing what exists
/// costs one line per pack and cannot be wrong about it.</para>
/// </summary>
public static class VoicePackUrlResolver
{
    /// <param name="packUrls">Language key → pack URL, as published in <c>RemoteUrls.json</c>.</param>
    /// <param name="languageKey">See <see cref="ClientLanguageKeys.KeyFor"/>.</param>
    /// <param name="legacyUrl">The pre-existing single-pack URL, used when the map says nothing.</param>
    /// <returns>The URL to download, or an empty string when no pack is published at all — the
    /// caller then skips the download instead of failing the install.</returns>
    public static string Resolve(IReadOnlyDictionary<string, string>? packUrls, string? languageKey,
        string? legacyUrl)
    {
        if (packUrls != null)
        {
            if (!string.IsNullOrWhiteSpace(languageKey)
                && packUrls.TryGetValue(languageKey, out var exact)
                && !string.IsNullOrWhiteSpace(exact))
                return exact;

            if (packUrls.TryGetValue(ClientLanguageKeys.Fallback, out var english)
                && !string.IsNullOrWhiteSpace(english))
                return english;
        }

        return string.IsNullOrWhiteSpace(legacyUrl) ? string.Empty : legacyUrl;
    }

    /// <summary>
    /// Whether the resolved URL is the language's own pack (false = the user hears English voices in
    /// a non-English client). Kept separate so the service can say so in the log instead of leaving
    /// the user to wonder why the voices do not match the game.
    /// </summary>
    public static bool IsLanguageSpecific(IReadOnlyDictionary<string, string>? packUrls, string? languageKey)
        => packUrls != null
           && !string.IsNullOrWhiteSpace(languageKey)
           && packUrls.TryGetValue(languageKey, out var url)
           && !string.IsNullOrWhiteSpace(url);
}
