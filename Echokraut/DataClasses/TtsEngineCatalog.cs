using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Echokraut.DataClasses;

/// <summary>
/// The engine list published by the EchokrauTTS wrapper repo (<c>engines.json</c> in its repo root),
/// read at the release tag the plugin currently points at. It exists so a wrapper release can add a
/// sub-engine without a plugin release — the plugin used to carry the list as a hard-coded enum and
/// went stale the moment the wrapper shipped a new one.
/// <para>An embedded copy (<c>Resources/Engines.json</c>) is the offline fallback, so the dropdown is
/// never empty; see <see cref="Echokraut.Services.TtsEngineCatalogService"/>.</para>
/// </summary>
public class TtsEngineCatalog
{
    /// <summary>Schema version. A file that does not match the expected version is ignored in favour
    /// of the embedded fallback — an incompatible future schema must not produce half-read engines.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("engines")]
    public List<TtsEngineInfo> Engines { get; set; } = new();
}

/// <summary>One sub-engine the wrapper can load at startup.</summary>
public class TtsEngineInfo
{
    /// <summary>Wrapper arg value, lower-case (<c>xtts</c> / <c>f5</c> / <c>moss</c>). Persisted in the
    /// config as <c>EchokrauTtsData.TtsEngine</c> and passed through as <c>--tts-backend</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name for the dropdown. Deliberately NOT localized — these are product names.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional one-line "what is this engine for" in ENGLISH. Optional because the plugin carries a
    /// built-in text for every engine it knows (<see cref="Echokraut.Helper.Functional.TtsEngineDescriptions"/>);
    /// a wrapper that adds a new engine can describe it here without a plugin release. The English
    /// string doubles as the localization key, so a text the plugin knows is shown translated and an
    /// unknown one is shown as written.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional quality grade 1–5 (5 = best), relative to the other engines in the list.
    /// From the wrapper repo's listening tests and measurements; 0/absent = not shown.</summary>
    [JsonPropertyName("quality")]
    public int Quality { get; set; }

    /// <summary>Optional <c>gpu</c> / <c>cpu</c>: where the engine runs. Empty = no requirement shown.</summary>
    [JsonPropertyName("hardware")]
    public string Hardware { get; set; } = string.Empty;

    /// <summary>Optional VRAM in GB the engine needs next to the game (GPU engines only).</summary>
    [JsonPropertyName("vramGb")]
    public double VramGb { get; set; }

    /// <summary>Optional CPU cores the engine keeps busy (CPU engines). Shown instead of <see cref="Load"/>.</summary>
    [JsonPropertyName("cores")]
    public int Cores { get; set; }

    /// <summary>Optional <c>low</c> / <c>medium</c> / <c>high</c>: how hard it works the hardware while speaking.</summary>
    [JsonPropertyName("load")]
    public string Load { get; set; } = string.Empty;

    /// <summary>First wrapper release tag (<c>X.Y.Z.W</c>) that ships this engine. An installed wrapper
    /// older than this can't run it, so it is listed but not selectable.</summary>
    [JsonPropertyName("minVersion")]
    public string MinVersion { get; set; } = string.Empty;

    /// <summary>Optional game language codes (<c>de</c>/<c>en</c>/<c>fr</c>/<c>ja</c>, wrapper <c>--language</c>
    /// values) the engine has a model for. Empty/absent = every language. Pocket TTS has no Japanese model:
    /// started with <c>ja</c>, the wrapper refuses before installing anything.</summary>
    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();
}

/// <summary>A catalog entry resolved against the locally installed wrapper: what the dropdown shows
/// and whether picking it is allowed. Built by <see cref="Echokraut.Helper.Functional.TtsEngineAvailability"/>.</summary>
/// <param name="Id">Wrapper arg value (<see cref="TtsEngineInfo.Id"/>).</param>
/// <param name="Name">Plain display name, without the "from wrapper version X" suffix.</param>
/// <param name="Label">Dropdown text — the plain name when available, name + required version when not.</param>
/// <param name="Description">English description from the catalog, empty when it carries none.</param>
/// <param name="Available">False when the installed wrapper predates <see cref="TtsEngineInfo.MinVersion"/>
/// or the engine has no model for the game language (<see cref="TtsEngineInfo.Languages"/>).</param>
/// <param name="MinVersion">The version that would unlock it (empty when available or blocked by the language,
/// which no wrapper update unlocks).</param>
public record TtsEngineOption(string Id, string Name, string Label, bool Available, string MinVersion,
                              string Description);
