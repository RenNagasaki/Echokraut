using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.SelfTest.Checks;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage 8 (plan › Stages, Abort and dependency rules): runs at the end of every run — also when the request
/// lists other stages only and after a stage ended the run — and undoes what the run left behind. Stages remove
/// their own traces right away (scenarios, remote); here only what a crash or unload left in
/// <see cref="E2eState.CreatedIds"/> and <see cref="E2eState.TouchedOptions"/> goes, plus every
/// <c>Echokraut Selftest</c> character. Config and DB are touched only on an E2E config (phase ≥ reset-done);
/// before that the run never changed them, so the stage only reports. Report, <c>done</c> line and the chat
/// warning about the E2E config are written by the run service when the run finishes (after an optional restore).
/// </summary>
public sealed class CleanupStage : E2eStage
{
    public const string StageId = "cleanup";

    internal const string CharacterPrefix = "character:";
    internal const string ClipPrefix = "clip:";
    internal const string WavPrefix = "wav:";

    private readonly Configuration _config;
    private readonly IDatabaseService _db;
    private readonly INpcDataService _npcData;
    private readonly IGameVoiceReferenceService _gameVoice;

    public CleanupStage(Configuration config, IDatabaseService db, INpcDataService npcData, IGameVoiceReferenceService gameVoice)
    {
        _config = config;
        _db = db;
        _npcData = npcData;
        _gameVoice = gameVoice;
    }

    public override string Id => StageId;
    public override bool AlwaysRuns => true;
    public override bool RunsAfterAbort => true;

    public override Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var s = ctx.State;
        if (!s.ConfigIsE2e || s.Phase < E2ePhase.ResetDone)
        {
            var left = s.CreatedIds.Count + s.TouchedOptions.Count;
            return Task.FromResult(left == 0
                ? SelfTestResult.Pass("No reset in this run: config and DB were not changed, report only.")
                : SelfTestResult.Fail($"No E2E config, nothing touched, but state lists {s.CreatedIds.Count} created id(s) and " +
                                      $"{s.TouchedOptions.Count} option(s): {string.Join(", ", s.CreatedIds.Concat(s.TouchedOptions.Keys))}."));
        }

        var options = RestoreOptions(ctx);
        ctx.Save();
        ct.ThrowIfCancellationRequested();

        var (removed, kept) = RemoveCreated(ctx);
        ctx.Save();
        var selftest = RemoveCharacters(PipelineCheck.SpeakerName);
        var leftRows = CharactersNamed(PipelineCheck.SpeakerName).Count;

        var detail = $"{removed} created id(s) removed, {selftest} '{PipelineCheck.SpeakerName}' character(s) removed, " +
                     $"options restored: {(options.Count == 0 ? "none" : string.Join(", ", options))}; game-voice opt-in off.";
        if (kept.Count == 0 && leftRows == 0) return Task.FromResult(SelfTestResult.Pass(detail));

        var problems = new List<string>();
        if (kept.Count > 0) problems.Add($"still listed: {string.Join(", ", kept)}");
        if (leftRows > 0) problems.Add($"{leftRows} '{PipelineCheck.SpeakerName}' row(s) left");
        return Task.FromResult(SelfTestResult.Fail($"{detail} Not clean - {string.Join("; ", problems)}."));
    }

    /// <summary>Every touched option back; the game-voice opt-in (D2) is off afterwards in any case.</summary>
    private IReadOnlyList<string> RestoreOptions(E2eContext ctx)
    {
        var restored = E2eOptions.Restore(ctx.State, _config);
        var forced = _config.GameVoiceReferenceEnabled;
        if (forced) _config.GameVoiceReferenceEnabled = false;
        if (restored.Count > 0 || forced) _config.Save();
        _gameVoice.ClearMemory();
        return forced ? restored.Append($"{nameof(Configuration.GameVoiceReferenceEnabled)} (forced)").ToList() : restored;
    }

    /// <summary>WAVs first, then clip rows, then characters (their clip rows go with them). Unknown or failed ids stay listed.</summary>
    private (int Removed, IReadOnlyList<string> Kept) RemoveCreated(E2eContext ctx)
    {
        var ids = ctx.State.CreatedIds;
        var removed = 0;
        var kept = new List<string>();
        foreach (var id in ids.OrderBy(Rank).ToList())
        {
            var done = id switch
            {
                _ when id.StartsWith(WavPrefix, StringComparison.Ordinal) => DeleteWav(id[WavPrefix.Length..], ctx.Request.InstallRoot),
                _ when id.StartsWith(ClipPrefix, StringComparison.Ordinal) => DeleteClip(id[ClipPrefix.Length..]),
                _ when id.StartsWith(CharacterPrefix, StringComparison.Ordinal) => DeleteCharacter(id[CharacterPrefix.Length..]),
                _ => false,
            };
            if (done)
            {
                ids.Remove(id);
                removed++;
            }
            else kept.Add(id);
        }
        return (removed, kept);
    }

    private static int Rank(string id) =>
        id.StartsWith(WavPrefix, StringComparison.Ordinal) ? 0 : id.StartsWith(ClipPrefix, StringComparison.Ordinal) ? 1 : 2;

    /// <summary>Only a WAV under the run's install root (where <c>LocalSaveLocation</c> points) is deleted.</summary>
    internal static bool DeleteWav(string path, string installRoot)
    {
        try
        {
            if (!IsUnder(path, installRoot)) return false;
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(path)) return false;
        var full = Path.GetFullPath(path);
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }

    private bool DeleteClip(string id)
    {
        if (!int.TryParse(id, out var clipId) || clipId <= 0) return false;
        _db.DeleteVoiceClip(clipId);
        return true;
    }

    private bool DeleteCharacter(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        RemoveCharacters(name);
        return CharactersNamed(name).Count == 0;
    }

    /// <summary>Removes every character called <paramref name="name"/> (cache + DB) with its clip rows; returns how many there were.</summary>
    private int RemoveCharacters(string name)
    {
        var rows = CharactersNamed(name);
        foreach (var row in rows)
            foreach (var clip in _db.GetVoiceClipsForCharacter(row.Id))
                _db.DeleteVoiceClip(clip.Id);

        // Through the NPC data service so the in-memory cache loses them too (no ghost entry in the UI).
        foreach (var cached in _npcData.MappedNpcs.Concat(_npcData.MappedPlayers).Where(n => n.Name == name).ToList())
            _npcData.RemoveCharacter(cached);
        foreach (var row in CharactersNamed(name))
            _db.DeleteCharacter(row.Id);
        return rows.Count;
    }

    private List<DataClasses.Database.CharacterEntity> CharactersNamed(string name) =>
        _db.GetAllCharacters().Where(c => c.Name == name).ToList();
}
