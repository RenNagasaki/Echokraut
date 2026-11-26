using System.Collections.Generic;
using Dalamud.Game;
using Echokraut.Localization;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Asserts that English localization keys have a non-empty DE/FR/JP translation.</summary>
internal static class LocAssert
{
    public static void Translated(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            foreach (var lang in new[] { ClientLanguage.German, ClientLanguage.French, ClientLanguage.Japanese })
                Assert.False(string.IsNullOrWhiteSpace(Loc.Translation(key, lang)), $"{lang} missing for: {key}");
    }
}
