using System.Collections.Generic;
using System.Threading;

namespace Echokraut.Services;

/// <summary>
/// One-shot DB cleanup that walks every <c>character_instance</c> row, asks Lumina who the
/// canonical NPC for that <c>NpcBaseId</c> actually is, and reassigns instances + voice_clips
/// from mis-attributed character rows to the correct one. Driven by the user via the Game
/// Data Tools window — produces a dry-run report first so the user can review before
/// applying. See <c>plans/npc-attribution-repair.md</c> for background.
/// </summary>
public interface INpcAttributionRepairService
{
    /// <summary>
    /// Walks the DB and returns a list of proposed reassignments without changing anything.
    /// Safe to call repeatedly. Action list is empty when the DB is already consistent.
    /// </summary>
    NpcAttributionRepairReport BuildDryRunReport(CancellationToken ct = default);

    /// <summary>
    /// Applies the actions from <paramref name="report"/>. The report should come from a
    /// recent <see cref="BuildDryRunReport"/> call — actions silently no-op when the DB has
    /// drifted (the source character_instance row was deleted, the canonical character no
    /// longer exists, etc.), so re-running a stale report is safe but may achieve nothing.
    /// </summary>
    NpcAttributionRepairResult Apply(NpcAttributionRepairReport report, CancellationToken ct = default);
}

/// <summary>
/// Single proposed reassignment: instance <c>NpcBaseId</c> currently attributed to
/// <c>OldCharacterId</c> should belong to <c>NewCharacterId</c> per Lumina. Carries the
/// resolved names + voice-clip count for human-readable display in the UI.
/// </summary>
public sealed record NpcAttributionRepairAction(
    int OldCharacterId,
    string OldCharacterName,
    int NewCharacterId,
    string CanonicalName,
    long NpcBaseId,
    int Language,
    int VoiceClipCount);

/// <summary>
/// Output of <see cref="INpcAttributionRepairService.BuildDryRunReport"/>. Carries both the
/// actionable list and skip-reason counts so the UI can show "found 7 mis-attributed
/// instances, 2 cannot be repaired because the canonical NPC hasn't been encountered yet".
/// </summary>
public sealed record NpcAttributionRepairReport(
    IReadOnlyList<NpcAttributionRepairAction> Actions,
    int TotalInstancesScanned,
    int SkippedNoLuminaRow,
    int SkippedNoCanonicalInDb,
    int AlreadyCorrect);

/// <summary>
/// Output of <see cref="INpcAttributionRepairService.Apply"/>. Counts are intentionally
/// flat — the UI surfaces them in a short summary line, not a per-row log.
/// </summary>
public sealed record NpcAttributionRepairResult(
    int InstancesReassigned,
    int VoiceClipsMoved,
    int VoiceClipsMergedAndDeleted,
    int CharactersDeleted);
