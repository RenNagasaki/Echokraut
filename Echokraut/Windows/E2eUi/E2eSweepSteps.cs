using System;
using System.Collections.Generic;
using System.Linq;
using Echotools.UI.Nodes;
using KamiToolKit.UI.Nodes.ComponentNode;

namespace Echokraut.Windows.E2eUi;

/// <summary>
/// A window that offers the E2E stage <c>ui</c> a set of named steps it may run like clicks (plan
/// <c>docs/plans/ingame-e2e.md</c> › B5). Only steps WITHOUT a side effect belong here (user decision
/// 2026-11-25): tabs, dropdown open/close (never a pick), pages, buttons that only show or test something.
/// Built on request, so the nodes the steps reach exist (after <c>OnSetup</c>).
/// </summary>
internal interface IE2eSweepable
{
    E2eSweepSteps SweepSteps();
}

/// <summary>Ordered name → action list with helpers for the node kinds the sweep presses.</summary>
internal sealed class E2eSweepSteps
{
    private readonly List<(string Name, Action Run)> _steps = [];
    private readonly List<(string Name, Action Run)> _actions = [];

    public IReadOnlyList<string> Names => _steps.ConvertAll(s => s.Name);

    public E2eSweepSteps Add(string name, Action? run)
    {
        if (run != null) _steps.Add((name, run));
        return this;
    }

    /// <summary>
    /// A step WITH a side effect (a click that changes something), runnable by name like a step but never listed in
    /// <see cref="Names"/>, so the sweep never presses it. Only stages that own the consequence call it by name
    /// (<c>wrapper-mixed</c>, plan › B8).
    /// </summary>
    public E2eSweepSteps Action(string name, Action? run)
    {
        if (run != null) _actions.Add((name, run));
        return this;
    }

    /// <summary>Open and close as two steps (the sweep looks at the open list in between); never selects.</summary>
    public E2eSweepSteps DropDown<T>(string name, DropDownNode<T>? node)
    {
        if (node == null) return this;
        Add($"dropdown:{name}:open", () => node.Uncollapse(false));
        Add($"dropdown:{name}:close", () => node.Collapse(false));
        return this;
    }

    /// <summary>Next and previous page, through the bar's own buttons (deferred to its next <c>Update</c>).</summary>
    public E2eSweepSteps Pages(string name, PaginationBar? bar)
    {
        if (bar == null) return this;
        Add($"page:{name}:next", () => bar.NextButton.OnClick?.Invoke());
        Add($"page:{name}:prev", () => bar.PrevButton.OnClick?.Invoke());
        return this;
    }

    /// <summary>Runs the step; false when the window has no step of that name.</summary>
    public bool Run(string name)
    {
        foreach (var (n, run) in _steps.Concat(_actions))
        {
            if (!string.Equals(n, name, StringComparison.Ordinal)) continue;
            run();
            return true;
        }
        return false;
    }
}
