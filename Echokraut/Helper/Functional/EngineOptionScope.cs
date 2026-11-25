using System;

namespace Echokraut.Helper.Functional;

/// <summary>The engine-specific option groups of the EchokrauTTS local instance section.</summary>
public enum EngineOptionGroup
{
    /// <summary>Not tied to an engine — always shown.</summary>
    None,
    Xtts,
    Qwen,
    Pocket,
}

/// <summary>
/// Which engine an option group belongs to. The UI shows an engine's options only while that engine
/// is selected; the wrapper ignores them for every other engine anyway.
/// </summary>
public static class EngineOptionScope
{
    /// <param name="engineId">Normalised engine id (<c>EchokrauTtsData.TtsBackendArg</c>).</param>
    public static bool AppliesTo(EngineOptionGroup group, string? engineId)
    {
        var id = engineId?.Trim() ?? string.Empty;
        return group switch
        {
            EngineOptionGroup.None => true,
            EngineOptionGroup.Xtts => id.Equals("xtts", StringComparison.OrdinalIgnoreCase),
            // Qwen ships in several sizes (qwen-0.6b, qwen-1.7b, …) that share the same options.
            EngineOptionGroup.Qwen => id.StartsWith("qwen", StringComparison.OrdinalIgnoreCase),
            EngineOptionGroup.Pocket => id.Equals("pocket", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }
}
