using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface INpcDataService
{
    /// <summary>
    /// Snapshot of the cached NPCs. Read-only by design: the cache is copy-on-write, so a
    /// mutation applied to what a getter returned would silently affect nothing. Use
    /// <see cref="RemoveCharacter"/> / <see cref="ClearMappedCaches"/> instead.
    /// </summary>
    IReadOnlyList<NpcMapData> MappedNpcs { get; }

    /// <inheritdoc cref="MappedNpcs"/>
    IReadOnlyList<NpcMapData> MappedPlayers { get; }

    /// <summary>Empties both in-memory caches (used after the database is wiped).</summary>
    void ClearMappedCaches();
    bool IsGenderedRace(NpcRaces race);
    void ReSetVoiceRaces(EchokrautVoice voice, EKEventId? eventId = null);
    void ReSetVoiceGenders(EchokrautVoice voice, EKEventId? eventId = null);
    void MigrateOldData(EchokrautVoice? oldVoice = null, EchokrautVoice? newEkVoice = null);
    void RefreshSelectables(List<EchokrautVoice> voices);
    NpcMapData GetAddCharacterMapData(NpcMapData data, EKEventId eventId, IBackendService backend);
    void SaveCharacter(NpcMapData data);
    void SaveCharacterWithOldIdentity(NpcMapData data, string oldName, Genders oldGender, NpcRaces oldRace);
    void RemoveCharacter(NpcMapData data);

    /// <summary>
    /// Deletes the character from the database WITHOUT touching the in-memory mapped lists.
    ///
    /// <para>For bulk deletes that run off the frame thread: the mapped lists are plain
    /// <c>List&lt;&gt;</c> that the UI enumerates every frame, so mutating them from a background
    /// task is a race. Callers use this and then refresh the caches once, on the frame thread
    /// (<see cref="ClearMappedCaches"/>).</para>
    /// </summary>
    void RemoveCharacterFromDatabase(NpcMapData data);
    List<EchokrautVoice> GetEchokrautVoices();
    void SaveVoice(EchokrautVoice voice);
    List<PhoneticCorrection> GetPhoneticCorrections();
    void UpsertPhoneticCorrection(string originalText, string correctedText);
    void DeletePhoneticCorrection(string originalText);
    bool IsDialogueMuted(uint baseId);
    void MuteDialogue(uint baseId);
    void UnmuteDialogue(uint baseId);
}
