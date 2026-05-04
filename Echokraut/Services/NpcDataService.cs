using Echotools.Logging.Services;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Services;

public class NpcDataService : INpcDataService
{
    private readonly ILogService _log;
    private readonly IDatabaseService _db;
    private readonly IJsonDataService _jsonData;

    // In-memory lists maintained in sync with the database.
    // These are the same mutable lists that UI code iterates/modifies.
    private readonly List<NpcMapData> _mappedNpcs = new();
    private readonly List<NpcMapData> _mappedPlayers = new();

    public List<NpcMapData> MappedNpcs => _mappedNpcs;
    public List<NpcMapData> MappedPlayers => _mappedPlayers;

    public NpcDataService(ILogService log, IDatabaseService db, IJsonDataService jsonData)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));

        LoadFromDatabase();
        _db.VoiceClipLogged += LoadFromDatabase;
    }

    private void LoadFromDatabase()
    {
        // Skip if harvest is running — the harvest holds the DbContext on its own thread.
        // NpcDataService will reload once the harvest fires the final VoiceClipLogged event.
        if (_db.SuppressEvents) return;

        // VoiceClipLogged fires from inside _db's _writeLock on EVERY dialog line. Without
        // this guard each line would clear and re-allocate up to N×NpcMapData on the framework
        // thread, freezing the game for users with large mapping sets. SaveCharacter keeps the
        // in-memory _mappedNpcs/_mappedPlayers in sync with DB writes already, so a wholesale
        // reload only matters when a path bypasses SaveCharacter (the harvest does — it
        // populates DB rows directly via UpsertCharacter and triggers exactly one reload at
        // end-of-batch). Count diff cleanly distinguishes "harvest just landed N new rows"
        // from "another dialog line was logged, no character changes."
        var dbNpcCount = _db.GetNpcs().Count;
        var dbPlayerCount = _db.GetPlayers().Count;
        if (dbNpcCount == _mappedNpcs.Count && dbPlayerCount == _mappedPlayers.Count)
            return;

        _mappedNpcs.Clear();
        _mappedPlayers.Clear();

        foreach (var entity in _db.GetNpcs())
            _mappedNpcs.Add(EntityToNpcMapData(entity, "npc"));

        foreach (var entity in _db.GetPlayers())
            _mappedPlayers.Add(EntityToNpcMapData(entity, "player"));

        _log.Debug(nameof(LoadFromDatabase),
            $"Loaded {_mappedNpcs.Count} NPCs, {_mappedPlayers.Count} players from database",
            new EKEventId(0, TextSource.None));
    }

    public bool IsGenderedRace(NpcRaces race)
    {
        if (((int)race > 0 && (int)race < 9) || _jsonData.ModelGenderMap.Find(p => p.race == race) != null)
            return true;

        return false;
    }

    public void ReSetVoiceRaces(EchokrautVoice voice, EKEventId? eventId = null)
    {
        if (eventId == null)
            eventId = new EKEventId(0, TextSource.None);

        voice.AllowedRaces.Clear();
        var foundBodyType = false;
        string[] splitVoice = voice.voiceName.Split('_');

        // Filename convention is Gender_Race[-BodyType]_Name(.wav). The race+body-type info
        // lives in segment[1] ONLY — segment[0] is the gender and segment[2..] is the NPC's
        // name, which can legitimately contain race-like words (e.g.
        // "Male_Roegadyn_Roegadyn-Gladiator.wav" — the NPC is named Roegadyn-Gladiator,
        // and the file's race is Roegadyn ONCE). Scanning all segments would re-parse the
        // name and emit duplicate / wrong races. AddIfMissing kept as a belt-and-suspenders
        // dedupe in case segment[1] itself somehow carries a duplicate (e.g. "Hyur-Hyur").
        void AddIfMissing(NpcRaces race)
        {
            if (!voice.AllowedRaces.Contains(race))
                voice.AllowedRaces.Add(race);
        }

        if (splitVoice.Length < 2) return;
        var raceStrArr = splitVoice[1].Split('-');
        foreach (var raceStr in raceStrArr)
        {
            if (Enum.TryParse(typeof(NpcRaces), raceStr, true, out object? race))
            {
                AddIfMissing((NpcRaces)race);
                _log.Debug(nameof(ReSetVoiceRaces), $"Found {race} race", eventId);
            }
            else if (raceStr.Equals("Child", StringComparison.InvariantCultureIgnoreCase))
            {
                if (!foundBodyType) { voice.IsAdultVoice = false; foundBodyType = true; }
                voice.IsChildVoice = true;
                _log.Debug(nameof(ReSetVoiceRaces), $"Found Child option", eventId);
            }
            else if (raceStr.Equals("Elder", StringComparison.InvariantCultureIgnoreCase))
            {
                if (!foundBodyType) { voice.IsAdultVoice = false; foundBodyType = true; }
                voice.IsElderVoice = true;
                _log.Debug(nameof(ReSetVoiceRaces), $"Found Elder option", eventId);
            }
            else if (raceStr.Equals("Adult", StringComparison.InvariantCultureIgnoreCase))
            {
                if (!foundBodyType) { voice.IsAdultVoice = false; foundBodyType = true; }
                voice.IsAdultVoice = true;
                _log.Debug(nameof(ReSetVoiceRaces), $"Found Adult option", eventId);
            }
            else if (raceStr.Equals("All", StringComparison.InvariantCultureIgnoreCase))
            {
                foreach (var raceObj in Constants.RACELIST)
                {
                    AddIfMissing(raceObj);
                    _log.Debug(nameof(ReSetVoiceRaces), $"Found {raceObj} race", eventId);
                }
            }
            else
                _log.Debug(nameof(ReSetVoiceRaces), $"Skipped segment '{raceStr}' (not a race, body type, or 'All')", eventId);
        }
    }

    public void ReSetVoiceGenders(EchokrautVoice voice, EKEventId? eventId = null)
    {
        if (eventId == null)
            eventId = new EKEventId(0, TextSource.None);

        voice.AllowedGenders.Clear();
        string[] splitVoice = voice.voiceName.Split('_');

        // Filename convention: gender lives in segment[0] only. Same reasoning as
        // ReSetVoiceRaces — scanning all segments would mis-pick gender-like tokens out
        // of the NPC's name. Defensive Contains check stays in case segment[0] is itself
        // duplicated somehow.
        if (splitVoice.Length == 0) return;
        var genderStr = splitVoice[0];
        if (Enum.TryParse(typeof(Genders), genderStr, true, out object? gender))
        {
            _log.Debug(nameof(ReSetVoiceGenders), $"Found {gender} gender", eventId);
            if (!voice.AllowedGenders.Contains((Genders)gender))
                voice.AllowedGenders.Add((Genders)gender);
        }
    }

    public void MigrateOldData(EchokrautVoice? oldVoice = null, EchokrautVoice? newEkVoice = null)
    {
        if (oldVoice == null)
        {
            var voices = _db.GetVoices();
            var oldPlayerMapData = _mappedPlayers.FindAll(p => p.voiceItem != null);
            var oldNpcMapData = _mappedNpcs.FindAll(p => p.voiceItem != null);

            if (oldPlayerMapData.Count > 0 || oldNpcMapData.Count > 0)
            {
                _log.Info(nameof(MigrateOldData), $"Migrating old npcdata", new EKEventId(0, TextSource.None));

                foreach (var player in oldPlayerMapData)
                {
                    var matchedVoice = voices.FirstOrDefault(v => v.BackendVoice == player.voiceItem?.Voice);
                    if (matchedVoice != null)
                    {
                        player.voice = matchedVoice.BackendVoice;
                        player.voiceItem = null;
                        SaveCharacter(player);
                    }
                    _log.Debug(nameof(MigrateOldData), $"Migrated player {player.Name}", new EKEventId(0, TextSource.None));
                }

                foreach (var npc in oldNpcMapData)
                {
                    var matchedVoice = voices.FirstOrDefault(v => v.BackendVoice == npc.voiceItem?.Voice);
                    if (matchedVoice != null)
                    {
                        npc.voice = matchedVoice.BackendVoice;
                        npc.voiceItem = null;
                        SaveCharacter(npc);
                    }
                    _log.Debug(nameof(MigrateOldData), $"Migrated npc {npc.Name}", new EKEventId(0, TextSource.None));
                }
            }
        }
        else
        {
            var oldPlayerMapData = _mappedPlayers.FindAll(p => p.Voice == oldVoice);
            var oldNpcMapData = _mappedNpcs.FindAll(p => p.Voice == oldVoice);

            if (oldPlayerMapData.Count > 0 || oldNpcMapData.Count > 0)
            {
                var newVoiceKey = newEkVoice?.BackendVoice ?? "";
                var label = newEkVoice != null ? newEkVoice.ToString() : "NO VOICE";
                _log.Info(nameof(MigrateOldData), $"Migrating old npcdata from {oldVoice} to {label}", new EKEventId(0, TextSource.None));

                foreach (var player in oldPlayerMapData)
                {
                    player.voice = newVoiceKey;
                    SaveCharacter(player);
                    _log.Debug(nameof(MigrateOldData), $"Migrated player {player.Name}", new EKEventId(0, TextSource.None));
                }
                foreach (var npc in oldNpcMapData)
                {
                    npc.voice = newVoiceKey;
                    SaveCharacter(npc);
                    _log.Debug(nameof(MigrateOldData), $"Migrated npc {npc.Name}", new EKEventId(0, TextSource.None));
                }
            }
        }
    }

    public void RefreshSelectables(List<EchokrautVoice> voices)
    {
        try
        {
            _mappedNpcs.ForEach(p => p.Voices = voices);
            _mappedPlayers.ForEach(p => p.Voices = voices);
            _log.Debug(nameof(RefreshSelectables), $"Refreshed selectables: {_mappedNpcs.Count} NPCs, {_mappedPlayers.Count} players", new EKEventId(0, TextSource.None));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(RefreshSelectables), $"Error Exception: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    public NpcMapData GetAddCharacterMapData(NpcMapData data, EKEventId eventId, IBackendService backend)
    {
        NpcMapData? result = null;
        var datas = GetCharacterMapDatas(eventId);

        if (data.Race == NpcRaces.Unknown)
        {
            var oldResult = datas.Find(p => MatchesIdentity(p, data));
            result = datas.Find(p => p.Name == data.Name && p.Language == data.Language && p.Race != NpcRaces.Unknown);

            if (result != null && oldResult != null)
            {
                datas.Remove(oldResult);
                RemoveCharacter(oldResult);
            }
        }
        else if (data.Race != NpcRaces.Unknown)
        {
            result = datas.Find(p => p.Name == data.Name && p.Language == data.Language && p.Race == NpcRaces.Unknown);

            if (result != null)
            {
                data.Voice = result.Voice;
                datas.Remove(result);
                RemoveCharacter(result);
                result = null;
            }
        }

        if (result == null)
        {
            result = datas.Find(p => MatchesIdentity(p, data));

            if (result == null)
            {
                datas.Add(data);
                var voices = _db.GetVoices();
                data.Voices = voices.Select(VoiceEntityToEchokrautVoice).ToList();
                backend.GetVoiceOrRandom(eventId, data);
                backend.NotifyCharacterMapped();
                SaveCharacter(data);
                var mapping = data.ObjectKind == ObjectKind.Pc ? "player" : "npc";
                _log.Debug(nameof(GetAddCharacterMapData), $"Added new {mapping} to mapping: {data.ToString()}", eventId);
                result = data;
            }
            else
            {
                _log.Debug(nameof(GetAddCharacterMapData), $"Found existing mapping for: {data.ToString()} result: {result.ToString()}", eventId);
                result.Voices ??= _db.GetVoices().Select(VoiceEntityToEchokrautVoice).ToList();
            }
        }
        else
        {
            _log.Debug(nameof(GetAddCharacterMapData), $"Found existing mapping for: {data.ToString()} result: {result.ToString()}", eventId);
            result.Voices ??= _db.GetVoices().Select(VoiceEntityToEchokrautVoice).ToList();
        }

        return result;
    }

    public void SaveCharacterWithOldIdentity(NpcMapData data, string oldName, Genders oldGender, NpcRaces oldRace)
    {
        // Delete old character record (identity changed). Language stays the same across an
        // identity change — must be passed explicitly because FindCharacter defaults to English.
        var oldChar = _db.FindCharacter(oldName, oldGender, oldRace, (int)data.Language);
        if (oldChar != null)
            _db.DeleteCharacter(oldChar.Id);

        // Save with new identity
        SaveCharacter(data);
    }

    public void SaveCharacter(NpcMapData data)
    {
        var entity = NpcMapDataToEntity(data);
        var saved = _db.UpsertCharacter(entity);

        // Upsert contexts
        var contextType = data.ObjectKind == ObjectKind.Pc ? "player" : "npc";
        _db.UpsertContext(saved.Id, contextType, data.IsEnabled, data.Volume);

        if (data.HasBubbles && contextType == "npc")
            _db.UpsertContext(saved.Id, "bubble", data.IsEnabledBubble, data.VolumeBubble);

        // Sync the in-memory canonical entry. LoadFromDatabase is subscribed to
        // VoiceClipLogged and rebuilds _mappedNpcs on every dialog line — that means callers
        // mutating an orphaned reference (e.g. DialogTalkController's voice dropdown writing
        // to msg.Speaker, where msg was built before the most recent reload) would persist
        // to the DB but the next pipeline run would fetch the stale in-memory canonical with
        // the old voice. Without this sync, voice picks were observed "one behind".
        var list = data.ObjectKind == ObjectKind.Pc ? _mappedPlayers : _mappedNpcs;
        var canonical = list.Find(p => MatchesIdentity(p, data));
        if (canonical == null)
        {
            list.Add(data);
        }
        else if (!ReferenceEquals(canonical, data))
        {
            canonical.voice = data.voice;
            canonical.IsEnabled = data.IsEnabled;
            canonical.IsEnabledBubble = data.IsEnabledBubble;
            canonical.Volume = data.Volume;
            canonical.VolumeBubble = data.VolumeBubble;
            canonical.HasBubbles = data.HasBubbles;
            canonical.BodyType = data.BodyType;
            canonical.RaceStr = data.RaceStr;
            canonical.World = data.World;
        }
    }

    public void RemoveCharacter(NpcMapData data)
    {
        var existing = _db.FindCharacter(data.Name, data.Gender, data.Race, (int)data.Language);
        if (existing != null)
            _db.DeleteCharacter(existing.Id);
    }

    private List<NpcMapData> GetCharacterMapDatas(EKEventId eventId)
    {
        switch (eventId.TextSource)
        {
            case TextSource.AddonTalk:
            case TextSource.AddonBattleTalk:
            case TextSource.AddonBubble:
                _log.Debug(nameof(GetCharacterMapDatas), $"Found mapping: NPCs count: {_mappedNpcs.Count}", eventId);
                return _mappedNpcs;
            case TextSource.AddonSelectString:
            case TextSource.AddonCutsceneSelectString:
            case TextSource.Chat:
                _log.Debug(nameof(GetCharacterMapDatas), $"Found mapping: Players count: {_mappedPlayers.Count}", eventId);
                return _mappedPlayers;
        }

        _log.Debug(nameof(GetCharacterMapDatas), $"Didn't find a mapping.", eventId);
        return new List<NpcMapData>();
    }

    private static bool MatchesIdentity(NpcMapData a, NpcMapData b)
    {
        return a.Name == b.Name && a.Gender == b.Gender && a.Race == b.Race && a.Language == b.Language;
    }

    // ── Entity ↔ NpcMapData mapping ─────────────────────────

    private NpcMapData EntityToNpcMapData(CharacterEntity entity, string contextType)
    {
        var data = new NpcMapData((ObjectKind)entity.ObjectKind)
        {
            Name = entity.Name,
            Race = (NpcRaces)entity.Race,
            RaceStr = entity.RaceStr,
            Gender = (Genders)entity.Gender,
            BodyType = (BodyType)entity.BodyType,
            Language = (ClientLanguage)entity.Language,
            voice = entity.VoiceKey,
            World = entity.World,
        };

        // Load context-specific settings
        var ctx = entity.Contexts?.FirstOrDefault(c => c.ContextType == contextType);
        if (ctx != null)
        {
            data.IsEnabled = ctx.IsEnabled;
            data.Volume = ctx.Volume;
        }

        var bubbleCtx = entity.Contexts?.FirstOrDefault(c => c.ContextType == "bubble");
        if (bubbleCtx != null)
        {
            data.HasBubbles = true;
            data.IsEnabledBubble = bubbleCtx.IsEnabled;
            data.VolumeBubble = bubbleCtx.Volume;
        }

        return data;
    }

    private CharacterEntity NpcMapDataToEntity(NpcMapData data)
    {
        return new CharacterEntity
        {
            Name = data.Name ?? "",
            Race = (int)data.Race,
            RaceStr = data.RaceStr ?? "",
            Gender = (int)data.Gender,
            BodyType = (int)data.BodyType,
            Language = (int)data.Language,
            VoiceKey = data.voice ?? "",
            ObjectKind = (int)data.ObjectKind,
            World = data.World ?? "",
        };
    }

    public void SaveVoice(EchokrautVoice voice)
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
            Note = voice.Note ?? ""
        };
        entity.AllowedGenders = voice.AllowedGenders
            .Select(g => new VoiceAllowedGenderEntity { Gender = (int)g }).ToList();
        entity.AllowedRaces = voice.AllowedRaces
            .Select(r => new VoiceAllowedRaceEntity { Race = (int)r }).ToList();
        _db.UpsertVoice(entity);
    }

    public List<EchokrautVoice> GetEchokrautVoices()
    {
        return _db.GetVoices().Select(VoiceEntityToEchokrautVoice).ToList();
    }

    public List<PhoneticCorrection> GetPhoneticCorrections()
    {
        return _db.GetPhoneticCorrections()
            .Select(p => new PhoneticCorrection(p.OriginalText, p.CorrectedText)).ToList();
    }

    public void UpsertPhoneticCorrection(string originalText, string correctedText)
    {
        _db.UpsertPhoneticCorrection(originalText, correctedText);
    }

    public void DeletePhoneticCorrection(string originalText)
    {
        _db.DeletePhoneticCorrection(originalText);
    }

    public bool IsDialogueMuted(uint baseId) => _db.GetMutedBaseIds().Contains(baseId);

    public void MuteDialogue(uint baseId) => _db.MuteInstance(baseId);

    public void UnmuteDialogue(uint baseId) => _db.UnmuteInstance(baseId);

    private static EchokrautVoice VoiceEntityToEchokrautVoice(VoiceEntity entity)
    {
        return new EchokrautVoice
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
            AllowedRaces = entity.AllowedRaces?.Select(r => (NpcRaces)r.Race).ToList() ?? new()
        };
    }
}
