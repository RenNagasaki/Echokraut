using System;
using System.Collections.Generic;
using System.Text;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional;

/// <summary>
/// The "which engine should I pick?" text shown above the engine dropdown in the first-time wizard,
/// where the choice is made by someone who has never seen these names.
///
/// <para>Two sources, in this order: the catalog's own <see cref="TtsEngineInfo.Description"/> — so a
/// wrapper release can describe a brand-new engine without a plugin release — and otherwise a
/// built-in text for the engines this plugin knows. Both are ENGLISH, and English is the
/// localization key in this project, so either one is shown translated where a translation exists
/// and verbatim where it does not.</para>
///
/// <para>The built-in texts state the trade-off, not the marketing: they come from measurements
/// recorded in the wrapper repo (first-audio latency, model size, whether the GPU stays free), and
/// the honest verdict that the small CPU engine sounds like a small model. A user picking blind is
/// better served by "who is this for" than by an architecture name.</para>
/// </summary>
public static class TtsEngineDescriptions
{
    /// <summary>Built-in English description per known engine id, or null for an unknown one.</summary>
    public static string? EnglishFor(string? id) => id?.ToLowerInvariant() switch
    {
        "xtts" => "Best quality. Needs a graphics card and takes about two seconds before the first "
                  + "word is heard. Pick this if your GPU has room to spare.",
        "f5" => "Fastest overall once it starts, but the slowest to begin, and it needs a written "
                + "transcript for every voice sample. Largest download.",
        "moss" => "For weaker machines: runs on the processor, so your graphics card stays with the "
                  + "game, starts speaking almost instantly and is a small download. Sounds "
                  + "noticeably rougher, and long sentences can fall behind.",
        _ => null,
    };

    /// <summary>
    /// The whole block, one line per engine, as <c>"Name: description"</c>. Engines without any
    /// description are skipped rather than printed as a bare name — a name with nothing after it
    /// reads like a rendering bug.
    /// </summary>
    /// <param name="options">The engines actually offered, in dropdown order.</param>
    /// <param name="localize">Localization lookup (English text in, translated text out).</param>
    public static string Compose(IReadOnlyList<TtsEngineOption>? options, Func<string, string> localize)
    {
        if (options == null) return string.Empty;

        var text = new StringBuilder();
        foreach (var option in options)
        {
            var english = !string.IsNullOrWhiteSpace(option.Description)
                ? option.Description
                : EnglishFor(option.Id);
            if (string.IsNullOrWhiteSpace(english)) continue;

            if (text.Length > 0) text.Append('\n');
            text.Append(option.Name).Append(": ").Append(localize(english));
        }

        return text.ToString();
    }
}
