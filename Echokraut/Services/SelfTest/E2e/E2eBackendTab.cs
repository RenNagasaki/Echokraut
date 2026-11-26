using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Localization;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// The config window's Backend tab as the E2E stages reach it (plan › B5, B8): open the tab, expand the collapsed
/// EchokrauTTS local section, read the engine dropdown open and closed, fold the section back. Shared by the
/// stages <c>ui</c> and <c>wrapper-mixed</c>.
/// </summary>
public sealed class E2eBackendTab
{
    public const string TabStep = "tab:settings/backend";
    public const string EkLocalToggle = "section:ek-local:toggle";
    public const string CollapsedMark = "[+]";
    public const string ExpandedMark = "[-]";

    private readonly E2eUiDriver _ui;
    private readonly Configuration _config;

    public E2eBackendTab(E2eUiDriver ui, Configuration config)
    {
        _ui = ui;
        _config = config;
    }

    public sealed record Opened(OwnedWindowState? State, string? Error, bool Expanded);

    public sealed record EngineDropdown(OwnedWindowState? Closed, OwnedWindowState? Open, string? Error);

    /// <summary>
    /// Config window → Backend tab; with <paramref name="expand"/> the collapsed EchokrauTTS local section is
    /// opened (Expanded = the caller did it and folds it back with <see cref="FoldBackAsync"/>).
    /// </summary>
    public async Task<Opened> OpenAsync(bool expand, CancellationToken ct)
    {
        if (await _ui.OpenAsync(OwnedWindowIds.Config, ct).ConfigureAwait(false) is { } openError) return new(null, openError, false);
        if (await _ui.StepAsync(OwnedWindowIds.Config, TabStep, ct).ConfigureAwait(false) is { } stepError) return new(null, stepError, false);
        var state = await _ui.StateAsync(OwnedWindowIds.Config).ConfigureAwait(false);
        if (state is not { IsOpen: true }) return new(null, "config window not open", false);
        if (!expand || _config.EchokrauTts.InstanceType != AlltalkInstanceType.Local || Header(state, CollapsedMark) == null)
            return new(state, null, false);
        if (await _ui.StepAsync(OwnedWindowIds.Config, EkLocalToggle, ct).ConfigureAwait(false) is { } toggleError)
            return new(null, toggleError, false);
        state = await _ui.StateAsync(OwnedWindowIds.Config).ConfigureAwait(false);
        return state is { IsOpen: true }
            ? new(state, null, true)
            : new(null, "config window closed after expanding the section", true);
    }

    /// <summary>Folds the EchokrauTTS local section back when the caller expanded it (window opened for it if needed).</summary>
    public async Task FoldBackAsync(bool expandedByCaller, CancellationToken ct)
    {
        if (!expandedByCaller) return;
        if (await _ui.StateAsync(OwnedWindowIds.Config).ConfigureAwait(false) is not { IsOpen: true })
            await OpenAsync(expand: false, ct).ConfigureAwait(false);
        await _ui.StepAsync(OwnedWindowIds.Config, EkLocalToggle, ct).ConfigureAwait(false);
    }

    /// <summary>The section header "[+] EchokrauTTS local instance" / "[-] …" with the given mark, null when not shown.</summary>
    public static OwnedWindowText? Header(OwnedWindowState state, string mark, string title = "EchokrauTTS local instance")
        => state.Texts.FirstOrDefault(t => t.Text == $"{mark} {Loc.S(title)}");

    /// <summary>
    /// The engine dropdown as the user sees it: the section expanded (closed list = the selected label), then
    /// opened (every entry) and closed again; the section folded back as it was.
    /// </summary>
    public async Task<EngineDropdown> ReadEngineDropdownAsync(CancellationToken ct)
    {
        var closed = await OpenAsync(expand: true, ct).ConfigureAwait(false);
        if (closed.Error != null) return new(null, null, closed.Error);
        try
        {
            if (await _ui.StepAsync(OwnedWindowIds.Config, "dropdown:engine:open", ct).ConfigureAwait(false) is { } openError)
                return new(closed.State, null, openError);
            var open = await _ui.StateAsync(OwnedWindowIds.Config).ConfigureAwait(false);
            await _ui.StepAsync(OwnedWindowIds.Config, "dropdown:engine:close", ct).ConfigureAwait(false);
            return open is { IsOpen: true }
                ? new(closed.State, open, null)
                : new(closed.State, null, "config window closed while the dropdown was open");
        }
        finally
        {
            await FoldBackAsync(closed.Expanded, ct).ConfigureAwait(false);
        }
    }
}
