using System.Collections.Generic;
using System.Linq;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Pure rules turning the published engine catalog into the entries the engine dropdown renders:
/// which sub-engines exist, which of them the locally installed wrapper can actually run, and how an
/// engine that needs a newer wrapper is labelled.
/// <para>Engines the installed wrapper is too old for are <b>listed anyway</b> — that is the whole
/// point: the user should see that a newer wrapper brings a third engine, instead of the option
/// silently not existing.</para>
/// </summary>
public static class TtsEngineAvailability
{
    /// <summary>
    /// Builds the dropdown entries.
    /// </summary>
    /// <param name="engines">Catalog entries, in publication order.</param>
    /// <param name="wrapperVersion">
    /// The wrapper tag to judge against — see <see cref="GatingVersion"/>. Empty or unparseable means
    /// "cannot tell", and then every engine is offered.
    /// </param>
    /// <param name="unavailableFormat">
    /// Localized <c>"{0} (from wrapper version {1})"</c> pattern for an engine that needs a newer
    /// wrapper. Passed in so this stays free of any UI/localization dependency.
    /// </param>
    /// <param name="listUnavailable">
    /// Whether engines the wrapper cannot run are listed at all. <b>True on the Backend tab</b>: the
    /// update button sits right next to the list, so a blocked entry is the invitation to press it.
    /// <b>False in the first-time wizard</b>: there the user is about to install exactly one release,
    /// nothing can unlock the entry in that moment, and a permanently blocked option is noise.
    /// </param>
    /// <param name="selectedId">
    /// Currently configured engine id. If the catalog does not list it (a removed engine, or a
    /// catalog that failed to load), it is appended as an available entry — the dropdown must always
    /// be able to show what is actually configured.
    /// </param>
    public static IReadOnlyList<TtsEngineOption> Build(
        IReadOnlyList<TtsEngineInfo>? engines,
        string? wrapperVersion,
        string unavailableFormat,
        string? selectedId = null,
        bool listUnavailable = true)
    {
        var options = new List<TtsEngineOption>();

        foreach (var engine in engines ?? [])
        {
            if (string.IsNullOrWhiteSpace(engine.Id)) continue;
            if (options.Any(o => IdEquals(o.Id, engine.Id))) continue; // first entry wins

            var name = string.IsNullOrWhiteSpace(engine.Name) ? engine.Id : engine.Name;
            var available = WrapperUpdatePolicy.IsAtLeast(wrapperVersion, engine.MinVersion);

            if (available)
            {
                options.Add(new TtsEngineOption(engine.Id, name, name, true, string.Empty, engine.Description));
                continue;
            }

            // Hidden by listUnavailable — except when it is the engine currently configured: the
            // dropdown must keep showing what the config says, and it must keep showing it as
            // blocked. Dropping it here would let the self-preservation branch below re-add it as
            // available, i.e. hiding it would make it selectable.
            if (listUnavailable || IdEquals(engine.Id, selectedId))
                options.Add(new TtsEngineOption(engine.Id, name,
                    string.Format(unavailableFormat, name, engine.MinVersion), false, engine.MinVersion,
                    engine.Description));
        }

        if (!string.IsNullOrWhiteSpace(selectedId) && !options.Any(o => IdEquals(o.Id, selectedId)))
            options.Add(new TtsEngineOption(selectedId, selectedId, selectedId, true, string.Empty, string.Empty));

        return options;
    }

    /// <summary>
    /// Which wrapper tag an engine's <c>minVersion</c> is measured against.
    /// <list type="bullet">
    ///   <item>With a local install: the tag actually on disk — that wrapper is what a switch would
    ///   restart, and it cannot grow an engine it does not ship.</item>
    ///   <item>Without one: the tag a fresh install would fetch (<c>LatestWrapperVersion</c>).
    ///   <b>Not "everything is available"</b> — the first-time wizard installs a specific release,
    ///   and offering an engine that release does not contain sets the wrapper up to start with an
    ///   unknown <c>--tts-backend</c>.</item>
    /// </list>
    /// </summary>
    public static string GatingVersion(bool localInstall, string? installedVersion, string? latestVersion)
        => localInstall && !string.IsNullOrWhiteSpace(installedVersion)
            ? installedVersion
            : latestVersion ?? string.Empty;

    /// <summary>
    /// Cheap identity of a built option list, for the per-frame "did anything change?" check. Covers
    /// what the dropdown actually renders (ids, labels, availability) — a wrapper update changes the
    /// labels, and that is the case this exists for.
    /// </summary>
    public static string Signature(IReadOnlyList<TtsEngineOption> options)
        => string.Join("", options.Select(o => $"{o.Id}|{o.Label}|{o.Available}"));

    /// <summary>Label of the entry for <paramref name="id"/>, or the id itself when unlisted.</summary>
    public static string LabelFor(IReadOnlyList<TtsEngineOption> options, string? id)
        => options.FirstOrDefault(o => IdEquals(o.Id, id))?.Label ?? id ?? string.Empty;

    /// <summary>The entry a dropdown label maps back to, or null when the label is unknown.</summary>
    public static TtsEngineOption? FromLabel(IReadOnlyList<TtsEngineOption> options, string? label)
        => options.FirstOrDefault(o => string.Equals(o.Label, label, System.StringComparison.Ordinal));

    private static bool IdEquals(string? a, string? b)
        => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
}
