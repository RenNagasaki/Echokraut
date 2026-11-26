using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.Enums;
using Echokraut.Localization;

namespace Echokraut.Services.SelfTest.E2e.Stages;

public sealed partial class UiStage
{
    /// <summary>The windows the sweep and the clamp open themselves (the changelog has its own part).</summary>
    private static readonly string[] SweptWindows = [OwnedWindowIds.Config, OwnedWindowIds.VoiceClipManager, OwnedWindowIds.GameDataTools];
    private static readonly string[] ClampedWindows =
        [OwnedWindowIds.Config, OwnedWindowIds.VoiceClipManager, OwnedWindowIds.GameDataTools, OwnedWindowIds.FirstTime];

    internal static readonly TimeSpan EngineReadyWait = TimeSpan.FromMinutes(5);

    // ── sweep ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Every side-effect-free step of every window (tabs, dropdown open/close, pages, the first VCM row → detail
    /// window and its pages) under the frame watchdog: no step may fail, close its window or freeze the game.
    /// </summary>
    private async Task<SelfTestResult> SweepAsync(E2eContext ctx, CancellationToken ct)
    {
        var problems = new List<string>();
        var count = 0;
        async Task Sweep()
        {
            foreach (var id in SweptWindows)
            {
                count += await SweepWindowAsync(id, open: true, problems, ct).ConfigureAwait(false);
                // The VCM's first row opens the detail window: its steps belong to the sweep too.
                if (id == OwnedWindowIds.VoiceClipManager
                    && await _ui.StateAsync(OwnedWindowIds.VoiceClipDetail).ConfigureAwait(false) is { IsOpen: true })
                    count += await SweepWindowAsync(OwnedWindowIds.VoiceClipDetail, open: false, problems, ct).ConfigureAwait(false);
                await CloseAllAsync(ct).ConfigureAwait(false);
            }
        }

        string frames;
        if (_frames == null)
        {
            await Sweep().ConfigureAwait(false);
            frames = "no frame monitor";
        }
        else
        {
            var op = await _frames.MeasureAsync("ui-sweep", Sweep).ConfigureAwait(false);
            ctx.State.Telemetry.Ops.RemoveAll(o => o.Op == op.Op);
            ctx.State.Telemetry.Ops.Add(op);
            ctx.Save();
            frames = op.Frames.ToString();
            if (op.Frames.MaxMs > ctx.State.Telemetry.FreezeLimitMs)
                problems.Add($"a step froze the game ({op.Frames.MaxMs:0} ms frame > {ctx.State.Telemetry.FreezeLimitMs:0} ms)");
        }
        return problems.Count == 0
            ? SelfTestResult.Pass($"{count} steps over {SweptWindows.Length} windows + detail, no error, no window lost; frames {frames}.")
            : SelfTestResult.Fail($"{string.Join("; ", problems)} (frames {frames}).");
    }

    private async Task<int> SweepWindowAsync(string id, bool open, List<string> problems, CancellationToken ct)
    {
        if (open && await _ui.OpenAsync(id, ct).ConfigureAwait(false) is { } openError)
        {
            problems.Add(openError);
            return 0;
        }
        var steps = await _ui.StepsAsync(id).ConfigureAwait(false);
        if (steps.Count == 0) problems.Add($"{id} offers no sweep step");
        var done = 0;
        foreach (var step in steps)
        {
            if (await _ui.StepAsync(id, step, ct).ConfigureAwait(false) is { } error)
            {
                problems.Add($"{id} {step}: {error}");
                continue;
            }
            done++;
            var state = await _ui.StateAsync(id).ConfigureAwait(false);
            if (state is not { IsOpen: true })
            {
                problems.Add($"{id} closed after {step}");
                break;
            }
            Seen(state);
        }
        return done;
    }

    // ── clamp ──────────────────────────────────────────────────────────────

    /// <summary>Each window pushed 80 % off the bottom-right and the top-left edge must come back to ≥ 50 % visible.</summary>
    private async Task<SelfTestResult> ClampAsync(E2eContext ctx, CancellationToken ct)
    {
        var problems = new List<string>();
        foreach (var id in ClampedWindows)
        {
            if (await _ui.OpenAsync(id, ct).ConfigureAwait(false) is { } openError) { problems.Add(openError); continue; }
            var start = await _ui.StateAsync(id).ConfigureAwait(false);
            if (start is not { IsOpen: true }) { problems.Add($"{id} not open"); continue; }
            Seen(start);
            Vector2[] targets =
            [
                start.Screen - start.Size * 0.2f,
                -start.Size * 0.8f,
            ];
            foreach (var target in targets)
            {
                if (await _ui.MoveAsync(id, target, ct).ConfigureAwait(false) is { } moveError) { problems.Add(moveError); break; }
                var moved = await _ui.StateAsync(id).ConfigureAwait(false);
                if (moved == null) { problems.Add($"{id} gone after moving"); break; }
                if (E2eUiChecks.ClampProblem(moved) is { } clamp) problems.Add($"{clamp} after a move to ({target.X:0}, {target.Y:0})");
            }
            await _ui.MoveAsync(id, start.Position, ct).ConfigureAwait(false);
            await _ui.CloseAsync(id, ct).ConfigureAwait(false);
        }
        return problems.Count == 0
            ? SelfTestResult.Pass($"{ClampedWindows.Length} windows stay at least half on screen when pushed off either edge.")
            : SelfTestResult.Fail(string.Join("; ", problems) + ".");
    }

    // ── untranslated texts ─────────────────────────────────────────────────

    private SelfTestResult LocResult()
    {
        var language = _language();
        if (language == ClientLanguage.English) return SelfTestResult.Skip("English client - nothing to fall back from.");
        List<string> texts;
        lock (_seenTexts) texts = _seenTexts.Distinct(StringComparer.Ordinal).ToList();
        if (texts.Count == 0) return SelfTestResult.Skip("No window text seen (the earlier parts did not run).");
        var keys = Loc.Keys.ToHashSet(StringComparer.Ordinal);
        var found = E2eUiChecks.Untranslated(texts, keys, k => Loc.Translation(k, language));
        return found.Count == 0
            ? SelfTestResult.Pass($"{texts.Count} distinct texts seen in a {language} client, none an English fallback.")
            : SelfTestResult.Fail($"{found.Count} English text(s) in a {language} client: {string.Join(", ", found)}.");
    }

    // ── changelog popup across two reloads ─────────────────────────────────

    /// <summary>How a changelog entry names the current version ("v1.2.3.4"; the plugin version may carry the "v" already).</summary>
    private string EntryVersion => "v" + _pluginVersion.TrimStart('v', 'V');

    private const string Armed = "armed";
    private const string Read = "read";
    private const string FirstReadProblems = "ui-changelog-problems";

    /// <summary>
    /// <c>LastSeenChangelogVersion</c> = the previous version → reload → the popup opens once by itself (title,
    /// version subtitle, only this version unseen, no wizard) → "I've read it" → reload → neither the popup nor
    /// the wizard opens. Each reload restarts the stage; the marks tell where it stands.
    /// </summary>
    private async Task<SelfTestResult> ChangelogAsync(E2eContext ctx, CancellationToken ct)
    {
        var marks = ctx.State.Marks;
        marks.Remove(ReloadMark);
        marks.Remove(E2eSelfReload.ReloadRequestedMark);
        marks.TryGetValue(ChangelogMark, out var phase);
        if (_reload == null) return SelfTestResult.Skip("No self-reload wired.");

        if (phase == null)
        {
            if (_changelog.PreviousVersion() is not { } previous)
                return SelfTestResult.Skip("No changelog older than the current version is embedded.");
            marks[ChangelogLastSeenMark] = _config.LastSeenChangelogVersion;
            marks[ChangelogMark] = Armed;
            _config.LastSeenChangelogVersion = previous;
            _config.Save();
            ctx.Save();
            ctx.Progress($"LastSeenChangelogVersion = {previous}, reloading");
            return GiveUp(ctx, await ReloadAsync(ctx, ct).ConfigureAwait(false));
        }

        if (phase == Armed)
        {
            var problems = await AfterFirstReloadAsync(ct).ConfigureAwait(false);
            if (await WaitForEngineAsync(ctx, ct).ConfigureAwait(false) is { } engineError) problems.Add(engineError);
            marks[FirstReadProblems] = string.Join("; ", problems);
            marks[ChangelogMark] = Read;
            // Only "I've read it" moves the marker; the second reload must find it at the current version.
            ctx.Save();
            ctx.Progress("changelog read, reloading again");
            var noReload = GiveUp(ctx, await ReloadAsync(ctx, ct).ConfigureAwait(false));
            return problems.Count == 0 ? noReload : SelfTestResult.Fail($"{noReload.Detail} First reload: {string.Join("; ", problems)}.");
        }

        // phase == Read: after the second reload.
        var all = new List<string>();
        if (marks.TryGetValue(FirstReadProblems, out var first) && first.Length > 0) all.Add(first);
        all.AddRange(await NothingOpensAsync(ct).ConfigureAwait(false));
        if (await WaitForEngineAsync(ctx, ct).ConfigureAwait(false) is { } error) all.Add(error);
        if (marks.TryGetValue(ChangelogLastSeenMark, out var original) && _config.LastSeenChangelogVersion != original)
        {
            _config.LastSeenChangelogVersion = original;
            _config.Save();
        }
        ClearChangelogMarks(ctx);
        return all.Count == 0
            ? SelfTestResult.Pass($"After the update reload the changelog opened by itself once ('Echokraut {_pluginVersion}', only {EntryVersion}); after 'I've read it' and a reload neither it nor the wizard opened.")
            : SelfTestResult.Fail(string.Join("; ", all) + ".");
    }

    private async Task<List<string>> AfterFirstReloadAsync(CancellationToken ct)
    {
        var problems = new List<string>();
        var state = await _ui.WaitForAsync(OwnedWindowIds.Changelog, w => w.IsOpen, _ui.Wait * 2, ct).ConfigureAwait(false);
        if (state == null)
        {
            problems.Add("changelog did not open after the reload");
            return problems;
        }
        await _ui.SettleAsync(ct).ConfigureAwait(false);
        state = await _ui.StateAsync(OwnedWindowIds.Changelog).ConfigureAwait(false) ?? state;
        var title = Loc.S("What's New");
        var subtitle = $"Echokraut {_pluginVersion}";
        if (!state.Shows(title)) problems.Add($"changelog title '{title}' not shown");
        if (!state.Shows(subtitle)) problems.Add($"version '{subtitle}' not in the title bar");
        var unseen = _changelog.GetUnseenChangelogs();
        if (unseen.Count != 1 || unseen[0].Version != EntryVersion)
            problems.Add($"unseen entries {string.Join(", ", unseen.Select(u => u.Version))} instead of only {EntryVersion}");
        if (await _ui.StateAsync(OwnedWindowIds.FirstTime).ConfigureAwait(false) is { IsOpen: true })
            problems.Add("the wizard opened next to the changelog");

        if (await _ui.StepAsync(OwnedWindowIds.Changelog, "read", ct).ConfigureAwait(false) is { } error) problems.Add("read: " + error);
        else if (await _ui.WaitForAsync(OwnedWindowIds.Changelog, w => !w.Alive, _ui.Wait, ct).ConfigureAwait(false) == null)
            problems.Add("changelog still open after 'I've read it'");
        if (_config.LastSeenChangelogVersion != _pluginVersion)
            problems.Add($"LastSeenChangelogVersion {_config.LastSeenChangelogVersion} after 'I've read it'");
        return problems;
    }

    /// <summary>Watches for <see cref="_absentWait"/>: neither the changelog nor the wizard may open by itself.</summary>
    private async Task<List<string>> NothingOpensAsync(CancellationToken ct)
    {
        var problems = new List<string>();
        var deadline = DateTime.UtcNow + _absentWait;
        while (DateTime.UtcNow < deadline)
        {
            var windows = await _ui.SnapshotAsync().ConfigureAwait(false);
            foreach (var id in new[] { OwnedWindowIds.Changelog, OwnedWindowIds.FirstTime })
                if (windows.Any(w => w.Id == id && w.IsOpen)) problems.Add($"{id} opened again after the second reload");
            if (problems.Count > 0) return problems;
            await Task.Delay(_poll, ct).ConfigureAwait(false);
        }
        return problems;
    }

    /// <summary>The reload stopped the local engine; HandleStartup starts it again (auto start) — wait for that.</summary>
    private async Task<string?> WaitForEngineAsync(E2eContext ctx, CancellationToken ct)
    {
        var ek = _config.EchokrauTts;
        if (_engineWait == null || !ek.LocalInstall || ek.InstanceType != AlltalkInstanceType.Local || !ek.AutoStartLocalInstance)
            return null;
        var error = await _engineWait.WaitForReadyAsync(ctx, "ui-reload", EngineReadyWait, ct).ConfigureAwait(false);
        return error == null ? null : "engine after the reload: " + error;
    }

    /// <summary>A reload that did not come: marker back to what it was, marks gone, the part fails with <paramref name="result"/>.</summary>
    private SelfTestResult GiveUp(E2eContext ctx, SelfTestResult result)
    {
        if (ctx.State.Marks.TryGetValue(ChangelogLastSeenMark, out var original) && _config.LastSeenChangelogVersion != original)
        {
            _config.LastSeenChangelogVersion = original;
            _config.Save();
        }
        ClearChangelogMarks(ctx);
        return result;
    }

    private static void ClearChangelogMarks(E2eContext ctx)
    {
        ctx.State.Marks.Remove(ChangelogMark);
        ctx.State.Marks.Remove(FirstReadProblems);
        ctx.State.Marks.Remove(ChangelogLastSeenMark);
        ctx.Save();
    }

    /// <summary>Stops the engines, closes the windows, touches the DLL; returns only when no reload came.</summary>
    private async Task<SelfTestResult> ReloadAsync(E2eContext ctx, CancellationToken ct)
    {
        if (await _reload!.StopEnginesAsync(ctx, ct).ConfigureAwait(false) is { } busy) return SelfTestResult.Fail(busy);
        if (await _reload.CloseWindowsAsync(ctx, ct).ConfigureAwait(false) is { } open) return SelfTestResult.Fail(open);
        var phase = ctx.State.Phase;
        await _reload.RequestReloadAsync(ctx, phase, ReloadMark, ct).ConfigureAwait(false);
        E2eSelfReload.Revert(ctx, phase, ReloadMark);
        return SelfTestResult.Fail($"No reload within {_reload.ReloadWait.TotalSeconds:0} s after touching the DLL.");
    }
}
