using System;
using System.Collections.Generic;
using System.Linq;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional;

/// <summary>One entry of a Pocket TTS option dropdown.</summary>
/// <param name="Value">Stored number (layers or decoding steps).</param>
/// <param name="Label">Localized dropdown text.</param>
/// <param name="Allowed">False for an entry that is shown but cannot be picked with the current model
/// size — a dropdown cannot grey out a single entry, so the label says why and the pick is refused.</param>
public record PocketOption(int Value, string Label, bool Allowed);

/// <summary>
/// The two Pocket TTS dropdowns: model size (24 / 6 layers) and decoding steps (1 / 4 / 8). Steps
/// depend on the size — the 6-layer model never runs with 1 (user decision 2026-11-13, its word
/// error rate more than doubled there) — and each size keeps its own steps
/// (<see cref="EchokrauTtsData.PocketDecodeSteps6"/> / <see cref="EchokrauTtsData.PocketDecodeSteps24"/>).
/// </summary>
public static class PocketOptionLabels
{
    public static IReadOnlyList<PocketOption> Layers(Func<string, string> localize) =>
    [
        new(24, localize("24 layers - better sound"), true),
        new(6, localize("6 layers - about twice as fast"), true),
    ];

    public static IReadOnlyList<PocketOption> Steps(int layers, Func<string, string> localize)
    {
        var allowed = EchokrauTtsData.PocketStepOptionsFor(layers);
        return EchokrauTtsData.PocketDecodeStepOptions.Select(steps =>
        {
            var label = localize(steps switch
            {
                1 => "1 - fastest, occasional echo",
                4 => "4 - recommended",
                _ => "8 - slowest",
            });
            var ok = Array.IndexOf(allowed, steps) >= 0;
            return new PocketOption(steps, ok ? label : string.Format(localize("{0} (24 layers only)"), label), ok);
        }).ToList();
    }

    /// <summary>Label of <paramref name="value"/>, or of the first entry when it is not listed.</summary>
    public static string LabelOf(IReadOnlyList<PocketOption> options, int value)
        => (options.FirstOrDefault(o => o.Value == value) ?? options[0]).Label;
}
