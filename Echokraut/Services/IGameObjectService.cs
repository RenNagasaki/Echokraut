using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;

namespace Echokraut.Services;

public interface IGameObjectService
{
    IGameObject? GetGameObjectByName(SeString? name, EKEventId eventId);
    void TryGetNextUnknownCharacter(EKEventId eventId);
    void ClearLastUnknownState();
    IGameObject? LocalPlayer { get; }
    string LocalPlayerName { get; }
    ulong LocalPlayerContentId { get; }

    /// <summary>
    /// Re-reads the local player and refreshes the cached name / content id / gender, clearing
    /// them when no player is present (logged out, character switch in progress).
    ///
    /// **Must be ticked from the framework loop.** Those three values used to be filled only as
    /// a side effect of the <see cref="LocalPlayer"/> getter, which nothing calls until the first
    /// dialog line is processed — so a freshly loaded plugin reported an empty player name while
    /// the user was logged in. Everything that resolves a player placeholder degrades silently in
    /// that window: <c>TalkTextHelper.SubstitutePlaceholders</c> becomes a no-op (the Voice Clip
    /// Manager then bakes a literal "-PlayerFirstName-" into generated audio, at exactly the
    /// on-disk path the live path later adopts), and <c>BackfillAudioFiles</c> loses its
    /// placeholder detection.
    /// </summary>
    void RefreshLocalPlayer();

    /// <summary>
    /// The player content ID to persist for a voice clip: the real local-player content ID when
    /// the clip text contains a player placeholder, otherwise 0 (player-independent).
    /// </summary>
    long GetEffectivePlayerContentId(bool hasPlayerPlaceholder);
    bool LocalPlayerIsMale { get; }
    IGameObject? NextUnknownCharacter { get; }
    /// <summary>
    /// BaseIds (ENpcBase row IDs) of every NPC currently spawned in the object table.
    /// Used by the live alias-resolver to filter multi-match alias candidates down to
    /// "physically present in the current cutscene". Excludes player characters.
    /// </summary>
    HashSet<uint> GetSpawnedNpcBaseIds();
}
