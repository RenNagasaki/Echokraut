using System.Collections.Generic;
using System.Linq;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Converts between the persisted <see cref="VoiceEntity"/> and the in-memory
/// <see cref="EchokrautVoice"/>.
///
/// <para>This lived twice — once in <c>BackendService</c>, once in <c>NpcDataService</c> — and the
/// copies had already drifted (only one of them carried <c>Note</c> across). Both now call in
/// here.</para>
/// </summary>
public static class VoiceEntityMapper
{
    public static EchokrautVoice ToVoice(VoiceEntity entity) => new()
    {
        BackendVoice = entity.BackendVoice,
        voiceName = entity.VoiceName,
        IsDefault = entity.IsDefault,
        IsEnabled = entity.IsEnabled,
        UseAsRandom = entity.UseAsRandom,
        IsAdultVoice = entity.IsAdultVoice,
        IsChildVoice = entity.IsChildVoice,
        IsElderVoice = entity.IsElderVoice,
        Volume = entity.Volume,
        Note = entity.Note,
        AllowedGenders = entity.AllowedGenders?.Select(g => (Genders)g.Gender).ToList() ?? new(),
        AllowedRaces = entity.AllowedRaces?.Select(r => (NpcRaces)r.Race).ToList() ?? new(),
    };

    public static VoiceEntity ToEntity(EchokrautVoice voice)
    {
        var entity = new VoiceEntity
        {
            BackendVoice = voice.BackendVoice ?? "",
            VoiceName = voice.voiceName ?? "",
            IsDefault = voice.IsDefault,
            IsEnabled = voice.IsEnabled,
            UseAsRandom = voice.UseAsRandom,
            IsAdultVoice = voice.IsAdultVoice,
            IsChildVoice = voice.IsChildVoice,
            IsElderVoice = voice.IsElderVoice,
            Volume = voice.Volume,
            Note = voice.Note ?? "",
        };
        entity.AllowedGenders = voice.AllowedGenders
            .Select(g => new VoiceAllowedGenderEntity { Gender = (int)g }).ToList();
        entity.AllowedRaces = voice.AllowedRaces
            .Select(r => new VoiceAllowedRaceEntity { Race = (int)r }).ToList();
        return entity;
    }

    /// <summary>Convenience for the common "map the whole table" call.</summary>
    public static List<EchokrautVoice> ToVoices(IEnumerable<VoiceEntity> entities)
        => entities.Select(ToVoice).ToList();
}
