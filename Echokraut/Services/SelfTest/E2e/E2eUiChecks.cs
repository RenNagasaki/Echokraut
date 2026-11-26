using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// The judgements of the E2E stage <c>ui</c> (plan docs/plans/ingame-e2e.md › B5) over what the windows
/// show, kept apart from the game so they are unit-tested: static labels for a close/reopen compare,
/// untranslated texts, the rendered engine dropdown against its expected entries, the screen clamp.
/// </summary>
public static class E2eUiChecks
{
    /// <summary>The texts that are a fixed UI label (one of <paramref name="labels"/>), each once.</summary>
    public static SortedSet<string> StaticLabels(IEnumerable<OwnedWindowText> texts, ISet<string> labels)
        => new(texts.Select(t => t.Text).Where(labels.Contains), StringComparer.Ordinal);

    /// <summary>"lost a, b; gained c" between two label sets, null when they are equal.</summary>
    public static string? LabelDiff(ISet<string> before, ISet<string> after)
    {
        var lost = before.Where(l => !after.Contains(l)).ToList();
        var gained = after.Where(l => !before.Contains(l)).ToList();
        if (lost.Count == 0 && gained.Count == 0) return null;
        var parts = new List<string>();
        if (lost.Count > 0) parts.Add("lost " + string.Join(", ", lost.Select(Quote)));
        if (gained.Count > 0) parts.Add("gained " + string.Join(", ", gained.Select(Quote)));
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Texts a non-English client shows in English: a text that is a translation key (the English
    /// original) while that language has a different translation (the call site skipped <c>Loc.S</c>),
    /// or has none at all (the key is missing). A key whose translation is the same word is fine.
    /// </summary>
    public static IReadOnlyList<string> Untranslated(IEnumerable<string> texts, ISet<string> keys,
        Func<string, string?> translation)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            if (!keys.Contains(text)) continue;
            var translated = translation(text);
            if (translated == null) found.Add($"{Quote(text)} (no translation)");
            else if (!string.Equals(translated, text, StringComparison.Ordinal)) found.Add($"{Quote(text)} (translation {Quote(translated)} not used)");
        }
        return found.ToList();
    }

    /// <summary>
    /// The open engine dropdown against the entries it should list (<paramref name="expected"/>, in order):
    /// the closed dropdown shows <paramref name="selected"/>; opening adds one option node per entry, read
    /// top to bottom, which must be the first entries in order (a list shorter than the catalog scrolls),
    /// none cut off. Null when it matches, else what is wrong.
    /// </summary>
    public static string? DropdownProblem(IReadOnlyList<OwnedWindowText> closed, IReadOnlyList<OwnedWindowText> open,
        IReadOnlyList<string> expected, string selected)
    {
        var problems = new List<string>();
        if (expected.Count == 0) return "no expected entries";
        if (!closed.Any(t => t.Text == selected)) problems.Add($"closed dropdown does not show the selected {Quote(selected)}");

        var labels = expected.ToHashSet(StringComparer.Ordinal);
        // One closed occurrence per label is the dropdown's own label (or another node showing the same text).
        var closedCount = closed.Where(t => labels.Contains(t.Text)).GroupBy(t => t.Text).ToDictionary(g => g.Key, g => g.Count());
        var rendered = new List<OwnedWindowText>();
        foreach (var group in open.Where(t => labels.Contains(t.Text)).GroupBy(t => t.Text))
        {
            var extra = group.OrderBy(t => t.ScreenY).ToList();
            var skip = closedCount.GetValueOrDefault(group.Key);
            // The label occurrence stays where it was; the option sits elsewhere. Drop the ones nearest the label.
            rendered.AddRange(skip >= extra.Count ? [] : extra.Skip(skip));
        }
        rendered = rendered.OrderBy(t => t.ScreenY).ToList();

        if (rendered.Count == 0) problems.Add("no option appeared when opened");
        else
        {
            var names = rendered.Select(t => t.Text).ToList();
            var want = expected.Take(names.Count).ToList();
            if (!names.SequenceEqual(want, StringComparer.Ordinal))
                problems.Add($"options {string.Join(" | ", names.Select(Quote))} != expected {string.Join(" | ", want.Select(Quote))}");
            foreach (var cut in rendered.Where(t => t.Overflows))
                problems.Add($"{Quote(cut.Text)} cut off ({cut.DrawWidth:0} px text in {cut.NodeWidth:0} px)");
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    /// <summary>Null when at least <paramref name="minShare"/> of the window's width and height is on the screen.</summary>
    public static string? ClampProblem(OwnedWindowState state, float minShare = 0.5f)
    {
        var share = state.VisibleFraction;
        const float slack = 0.001f;
        return share.X + slack >= minShare && share.Y + slack >= minShare
            ? null
            : $"{state.Id} at ({state.Position.X:0}, {state.Position.Y:0}) size {state.Size.X:0}x{state.Size.Y:0} on {state.Screen.X:0}x{state.Screen.Y:0}: {share.X:P0} x {share.Y:P0} visible";
    }

    private static string Quote(string s) => "'" + s + "'";
}
