using System.Collections.Generic;
using Dalamud.Game;

namespace Echokraut.Helper.Functional;

/// <summary>
/// The string a <see cref="ClientLanguage"/> is called by in the remote JSON config
/// (<c>voiceNameUrls</c>, <c>voicePackUrls</c>): <c>"German"</c>, <c>"English"</c>,
/// <c>"French"</c>, <c>"Japanese"</c>.
/// <para>Shared so the two consumers cannot drift apart — a key spelled differently in one of them
/// does not fail loudly, it silently falls back to English, which is the kind of bug nobody
/// reports.</para>
/// </summary>
public static class ClientLanguageKeys
{
    /// <summary>The key used when a language has none of its own.</summary>
    public const string Fallback = "English";

    private static readonly Dictionary<ClientLanguage, string> Keys = new()
    {
        [ClientLanguage.German]   = "German",
        [ClientLanguage.English]  = "English",
        [ClientLanguage.French]   = "French",
        [ClientLanguage.Japanese] = "Japanese",
    };

    /// <summary>Key for <paramref name="language"/>, or <see cref="Fallback"/> for anything the
    /// client reports that we have no assets for.</summary>
    public static string KeyFor(ClientLanguage language)
        => Keys.GetValueOrDefault(language, Fallback);
}
