using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// What the voice-pack download must leave in <c>echokrautts/samples</c> (plan › B3, test plan sections 4
/// and 8): <c>LICENSE.txt</c> and <c>ATTRIBUTION.csv</c> (licence terms of the pack,
/// <c>docs/claude/services/voice-pack.md</c> › Pack content) and a same-name <c>.txt</c> transcript next to
/// every <c>.wav</c> (F5 needs it as reference text).
/// </summary>
public static class E2eVoicePackFiles
{
    public static readonly string[] Required = ["LICENSE.txt", "ATTRIBUTION.csv"];

    public static (IReadOnlyList<string> Problems, string Detail) Check(string samplesDir)
    {
        if (!Directory.Exists(samplesDir)) return (["samples folder missing"], "no samples folder");
        var files = Directory.EnumerateFiles(samplesDir, "*", SearchOption.AllDirectories).ToList();
        var names = files.Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = Required.Where(r => !names.Contains(r)).Select(r => $"{r} missing").ToList();

        var wavs = files.Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)).ToList();
        var noText = wavs.Where(w => !File.Exists(Path.ChangeExtension(w, ".txt")))
            .Select(w => Path.GetRelativePath(samplesDir, w)).ToList();
        if (wavs.Count == 0) problems.Add("no .wav in the samples folder");
        if (noText.Count > 0)
            problems.Add($"{noText.Count} .wav without .txt: {string.Join(", ", noText.Take(5))}{(noText.Count > 5 ? ", …" : "")}");

        return (problems, $"{wavs.Count} .wav, {wavs.Count - noText.Count} with .txt; " +
                          string.Join(", ", Required.Select(r => $"{r} {(names.Contains(r) ? "present" : "missing")}")));
    }
}
