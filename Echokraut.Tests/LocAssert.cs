using System.Collections.Generic;
using System.Reflection;
using Dalamud.Game;
using Echokraut.Localization;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Asserts that English localization keys have a non-empty DE/FR/JP translation.</summary>
internal static class LocAssert
{
    public static void Translated(IEnumerable<string> keys)
    {
        var field = typeof(Loc).GetField("Translations", BindingFlags.NonPublic | BindingFlags.Static)!;
        var table = (Dictionary<string, Dictionary<ClientLanguage, string>>)field.GetValue(null)!;
        foreach (var key in keys)
        {
            Assert.True(table.TryGetValue(key, out var map), $"missing Loc entry: {key}");
            foreach (var lang in new[] { ClientLanguage.German, ClientLanguage.French, ClientLanguage.Japanese })
                Assert.True(map!.TryGetValue(lang, out var t) && !string.IsNullOrWhiteSpace(t), $"{lang} missing for: {key}");
        }
    }
}
