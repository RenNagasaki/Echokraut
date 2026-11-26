using Echokraut.Localization;
using Echokraut.Windows.E2eUi;

namespace Echokraut.Windows.Native;

/// <summary>
/// The config window's side-effect-free steps for the E2E stage <c>ui</c> (plan docs/plans/ingame-e2e.md › B5):
/// every tab (what a tab click does: radio visual + the tab's own callback), the dropdowns opened and closed
/// without a pick, next/previous page of the paginated lists. No step changes a setting.
/// </summary>
public sealed unsafe partial class NativeConfigWindow : IE2eSweepable
{
    E2eSweepSteps IE2eSweepable.SweepSteps()
    {
        var steps = new E2eSweepSteps();
        var liveGen = _config.HasLiveGeneration;

        steps.Add("tab:settings", () => SelectTop(0, Loc.S("Settings")));
        string[] settings = [Loc.S("General"), Loc.S("Dialogue"), Loc.S("Chat"), Loc.S("Storage"), Loc.S("Backend")];
        for (var i = 0; i < settings.Length; i++)
        {
            if (i == 2 && !liveGen) continue; // the Chat tab is hidden in None mode
            var index = i;
            steps.Add($"tab:settings/{SettingsTabId(i)}", () =>
            {
                SelectTop(0, Loc.S("Settings"));
                _settingsTabBar?.SelectTab(settings[index]);
                ShowSettingsPanel(index);
            });
        }
        steps.DropDown("backend", _backendDropDown);
        // A view state, no setting: the stage expands the collapsed section to read it and folds it back.
        steps.Add("section:ek-local:toggle", _ekLocalSectionToggle?.OnClick);
        steps.DropDown("engine", _ekLocalNodes?.EngineDropDown);
        steps.Add("section:ek-local:toggle-back", _ekLocalSectionToggle?.OnClick);
        AddWrapperActions(steps);

        if (liveGen)
        {
            steps.Add("tab:voices", () => SelectTop(1, Loc.S("Voices")));
            steps.Pages("voices", _vsPaginationBar);
            steps.Add("tab:phonetics", () => SelectTop(2, Loc.S("Phonetics")));
            steps.Pages("phonetics", _phonPaginationBar);
        }
        steps.Add("tab:logs", () => SelectTop(3, Loc.S("Logs")));
        for (var i = 0; i < LogTabs.Length; i++)
        {
            if (LogTabs[i].LiveOnly && !liveGen) continue;
            var index = i;
            steps.Add($"tab:logs/{LogTabs[i].Source}", () =>
            {
                SelectTop(3, Loc.S("Logs"));
                _logsTabBar?.SelectTab(LogTabs[index].Label);
                ShowLogPanel(index);
            });
            steps.Pages($"logs/{LogTabs[i].Source}", _logsPaginationBars[i]);
        }
        return steps;
    }

    /// <summary>
    /// Clicks with a side effect, for the stage <c>wrapper-mixed</c> only (never swept): the wrapper button (check, then
    /// update) and a pick per engine entry through the dropdown's own handler (the guard that refuses an engine the
    /// installed wrapper cannot run).
    /// </summary>
    private void AddWrapperActions(E2eSweepSteps steps)
    {
        if (_ekLocalNodes is not { } nodes) return;
        steps.Action(Services.OwnedWindowActions.WrapperUpdate, nodes.UpdateButton?.OnClick);
        var pick = nodes.EngineDropDown?.OnOptionSelected;
        if (pick == null) return;
        foreach (var option in nodes.EngineOptions)
        {
            var label = option.Label;
            steps.Action(Services.OwnedWindowActions.EnginePick(option.Id), () => pick(label));
        }
    }

    internal static string SettingsTabId(int index) => index switch
    {
        0 => "general",
        1 => "dialogue",
        2 => "chat",
        3 => "storage",
        _ => "backend",
    };

    private void SelectTop(int index, string label)
    {
        _topTabBar?.SelectTab(label);
        ShowTopPanel(index);
    }
}
