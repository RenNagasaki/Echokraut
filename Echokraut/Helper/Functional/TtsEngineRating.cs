using System;
using System.Collections.Generic;
using System.Globalization;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional;

/// <summary>
/// The "how good, how demanding" suffix of an engine entry in the dropdown, e.g.
/// <c>(Quality 5/5, GPU ~5 GB, high load)</c> — so the engines can be compared right in the list,
/// not only in the wizard's description block. Built from the optional rating fields of
/// <c>engines.json</c> (<see cref="TtsEngineInfo.Quality"/>, <see cref="TtsEngineInfo.Hardware"/>,
/// <see cref="TtsEngineInfo.VramGb"/>, <see cref="TtsEngineInfo.Cores"/>, <see cref="TtsEngineInfo.Load"/>),
/// so a wrapper release can re-grade engines without a plugin release.
/// <para>Plain ASCII apart from the translations: the game font has no reliable glyphs for stars or
/// bullets, and a missing glyph renders as a box.</para>
/// </summary>
public static class TtsEngineRating
{
    /// <summary>The suffix in parentheses, or empty when the entry carries no rating at all.</summary>
    /// <param name="localize">Localization lookup (English text in, translated text out).</param>
    public static string Format(TtsEngineInfo? engine, Func<string, string> localize)
    {
        if (engine == null) return string.Empty;

        var parts = new List<string>();
        if (engine.Quality is >= 1 and <= 5)
            parts.Add(string.Format(localize("Quality {0}/5"), engine.Quality));

        var hardware = Hardware(engine, localize);
        if (hardware.Length > 0) parts.Add(hardware);

        return parts.Count == 0 ? string.Empty : "(" + string.Join(", ", parts) + ")";
    }

    private static string Hardware(TtsEngineInfo engine, Func<string, string> localize)
    {
        var load = engine.Load?.ToLowerInvariant() switch
        {
            "high" => localize("high load"),
            "medium" => localize("medium load"),
            "low" => localize("light"),
            _ => string.Empty,
        };

        switch (engine.Hardware?.ToLowerInvariant())
        {
            case "gpu":
                var gpu = engine.VramGb > 0
                    ? string.Format(localize("GPU ~{0} GB"), engine.VramGb.ToString("0.#", CultureInfo.InvariantCulture))
                    : "GPU";
                return load.Length > 0 ? gpu + ", " + load : gpu;
            case "cpu":
                if (engine.Cores > 0) return string.Format(localize("CPU, {0} cores"), engine.Cores);
                return load.Length > 0 ? "CPU, " + load : "CPU";
            default:
                return load;
        }
    }
}
