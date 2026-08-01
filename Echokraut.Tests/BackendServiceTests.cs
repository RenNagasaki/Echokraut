using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.Queue;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Voice-selection decisions in <see cref="BackendService"/> — the pure-ish part that decides
/// which voice an NPC ends up with. No backend, no BASS, no game: everything here runs off the
/// injected DB/NPC mocks.
/// </summary>
public class BackendServiceTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private static EchokrautVoice Voice(
        string backendVoice,
        string voiceName,
        Genders[]? genders = null,
        NpcRaces[]? races = null,
        bool isDefault = false,
        bool child = false,
        bool elder = false)
    {
        return new EchokrautVoice
        {
            BackendVoice = backendVoice,
            voiceName = voiceName,
            IsDefault = isDefault,
            IsEnabled = true,
            UseAsRandom = true,
            IsAdultVoice = !child && !elder,
            IsChildVoice = child,
            IsElderVoice = elder,
            Volume = 1f,
            AllowedGenders = new List<Genders>(genders ?? []),
            AllowedRaces = new List<NpcRaces>(races ?? []),
        };
    }

    private static NpcMapData Npc(string name, Genders gender = Genders.Female,
                                  NpcRaces race = NpcRaces.Elezen, BodyType body = BodyType.Adult)
    {
        return new NpcMapData(ObjectKind.EventNpc)
        {
            Name = name,
            Gender = gender,
            Race = race,
            BodyType = body,
            RaceStr = race.ToString(),
        };
    }

    /// <summary>
    /// Builds a BackendService whose active engine is None, so the constructor's RefreshBackend
    /// short-circuits and no HTTP is attempted.
    /// </summary>
    private static BackendService Build(
        out Mock<IDatabaseService> db,
        out Mock<INpcDataService> npcData,
        IList<EchokrautVoice>? voicesInDb = null)
    {
        var config = new Configuration();
        config.BackendSelection = TTSBackends.EchokrauTTS;
        config.EchokrauTts.InstanceType = AlltalkInstanceType.None;

        db = new Mock<IDatabaseService>();
        npcData = new Mock<INpcDataService>();

        // Gendered by default — the race/gender filter is what most cases here exercise.
        npcData.Setup(n => n.IsGenderedRace(It.IsAny<NpcRaces>())).Returns(true);

        db.Setup(d => d.GetVoices()).Returns(
            (voicesInDb ?? []).Select(ToEntity).ToList());

        return new BackendService(
            new Mock<IVoiceMessageQueue>().Object,
            new Mock<ILogService>().Object,
            config,
            new Mock<IAlltalkInstanceService>().Object,
            new Mock<IEchokrauTtsInstanceService>().Object,
            npcData.Object,
            new Mock<IAudioFileService>().Object,
            db.Object,
            new Mock<IAudioPlaybackService>().Object);
    }

    private static VoiceEntity ToEntity(EchokrautVoice v) => new()
    {
        BackendVoice = v.BackendVoice,
        VoiceName = v.voiceName,
        IsDefault = v.IsDefault,
        IsEnabled = v.IsEnabled,
        UseAsRandom = v.UseAsRandom,
        IsAdultVoice = v.IsAdultVoice,
        IsChildVoice = v.IsChildVoice,
        IsElderVoice = v.IsElderVoice,
        Volume = v.Volume,
        Note = v.Note,
        AllowedGenders = v.AllowedGenders.Select(g => new VoiceAllowedGenderEntity { Gender = (int)g }).ToList(),
        AllowedRaces = v.AllowedRaces.Select(r => new VoiceAllowedRaceEntity { Race = (int)r }).ToList(),
    };

    // ── PickVoice: the empty-name regression ──────────────────────────────────

    [Fact]
    public void PickVoice_EmptyNpcName_DoesNotMatchFirstVoiceByName()
    {
        // "anything".Contains("") is true, so an unnamed speaker used to match voices[0] via the
        // name loop and never reach the race/gender filter.
        using var svc = Build(out _, out _);

        var wrongByPosition = Voice("A.wav", "Male_Hyur_NPC001", [Genders.Male], [NpcRaces.Hyur]);
        var fitting = Voice("B.wav", "Female_Elezen_NPC002", [Genders.Female], [NpcRaces.Elezen]);
        var voices = new List<EchokrautVoice> { wrongByPosition, fitting };

        var picked = svc.PickVoice(Npc("", Genders.Female, NpcRaces.Elezen), voices);

        Assert.Same(fitting, picked);
    }

    [Fact]
    public void PickVoice_WhitespaceNpcName_DoesNotMatchFirstVoiceByName()
    {
        using var svc = Build(out _, out _);

        var wrongByPosition = Voice("A.wav", "Male_Hyur_NPC001", [Genders.Male], [NpcRaces.Hyur]);
        var fitting = Voice("B.wav", "Female_Elezen_NPC002", [Genders.Female], [NpcRaces.Elezen]);

        var picked = svc.PickVoice(Npc("   ", Genders.Female, NpcRaces.Elezen),
                                   new List<EchokrautVoice> { wrongByPosition, fitting });

        Assert.Same(fitting, picked);
    }

    [Fact]
    public void PickVoice_NamedNpc_StillMatchesByName()
    {
        // The guard must not break the name match it guards.
        using var svc = Build(out _, out _);

        var byRace = Voice("A.wav", "Female_Elezen_NPC001", [Genders.Female], [NpcRaces.Elezen]);
        var byName = Voice("B.wav", "Female_Elezen_Alphinaud", [Genders.Male], [NpcRaces.Hyur]);

        var picked = svc.PickVoice(Npc("Alphinaud", Genders.Female, NpcRaces.Elezen),
                                   new List<EchokrautVoice> { byRace, byName });

        Assert.Same(byName, picked);
    }

    [Fact]
    public void PickVoice_NoFittingVoice_FallsBackToDefault()
    {
        using var svc = Build(out _, out _);

        var fallback = Voice("Narrator.wav", "Narrator", isDefault: true);
        var mismatch = Voice("A.wav", "Male_Hyur_NPC001", [Genders.Male], [NpcRaces.Hyur]);

        var picked = svc.PickVoice(Npc("Nobody", Genders.Female, NpcRaces.AuRa),
                                   new List<EchokrautVoice> { fallback, mismatch });

        Assert.Same(fallback, picked);
    }

    [Fact]
    public void PickVoice_NoVoicesAtAll_ReturnsNull()
    {
        using var svc = Build(out _, out _);

        Assert.Null(svc.PickVoice(Npc("Someone"), new List<EchokrautVoice>()));
    }

    [Fact]
    public void PickVoice_KeepsFittingExistingAssignment()
    {
        using var svc = Build(out _, out _);

        var assigned = Voice("A.wav", "Female_Elezen_NPC001", [Genders.Female], [NpcRaces.Elezen]);
        var other = Voice("B.wav", "Female_Elezen_NPC002", [Genders.Female], [NpcRaces.Elezen]);
        var voices = new List<EchokrautVoice> { assigned, other };

        var npc = Npc("Someone", Genders.Female, NpcRaces.Elezen);
        npc.Voices = voices;
        npc.voice = "A.wav";

        Assert.Same(assigned, svc.PickVoice(npc, voices));
    }

    // ── IsVoiceFittingForNpc ──────────────────────────────────────────────────

    [Fact]
    public void IsVoiceFittingForNpc_NullOrDisabled_IsFalse()
    {
        using var svc = Build(out _, out _);
        var npc = Npc("Someone");

        Assert.False(svc.IsVoiceFittingForNpc(null, npc));

        var disabled = Voice("A.wav", "Female_Elezen_NPC001", [Genders.Female], [NpcRaces.Elezen]);
        disabled.IsEnabled = false;
        Assert.False(svc.IsVoiceFittingForNpc(disabled, npc));
    }

    [Fact]
    public void IsVoiceFittingForNpc_DefaultVoice_AlwaysFits()
    {
        using var svc = Build(out _, out _);
        var narrator = Voice("Narrator.wav", "Narrator", isDefault: true);

        Assert.True(svc.IsVoiceFittingForNpc(narrator, Npc("Anyone", Genders.Male, NpcRaces.AuRa)));
    }

    [Fact]
    public void IsVoiceFittingForNpc_GenderMismatch_IsFalse()
    {
        using var svc = Build(out _, out _);
        var maleVoice = Voice("A.wav", "Male_Elezen_NPC001", [Genders.Male], [NpcRaces.Elezen]);

        Assert.False(svc.IsVoiceFittingForNpc(maleVoice, Npc("Someone", Genders.Female, NpcRaces.Elezen)));
    }

    [Fact]
    public void IsVoiceFittingForNpc_GenderIgnoredForUngenderedRace()
    {
        using var svc = Build(out _, out var npcData);
        npcData.Setup(n => n.IsGenderedRace(It.IsAny<NpcRaces>())).Returns(false);

        var maleVoice = Voice("A.wav", "Male_Elezen_NPC001", [Genders.Male], [NpcRaces.Elezen]);

        Assert.True(svc.IsVoiceFittingForNpc(maleVoice, Npc("Someone", Genders.Female, NpcRaces.Elezen)));
    }

    [Fact]
    public void IsVoiceFittingForNpc_BodyTypeMustMatch()
    {
        using var svc = Build(out _, out _);
        var adult = Voice("A.wav", "Female_Elezen_NPC001", [Genders.Female], [NpcRaces.Elezen]);
        var child = Voice("B.wav", "Female_Elezen-Child_NPC002", [Genders.Female], [NpcRaces.Elezen], child: true);

        var childNpc = Npc("Kid", Genders.Female, NpcRaces.Elezen, BodyType.Child);

        Assert.False(svc.IsVoiceFittingForNpc(adult, childNpc));
        Assert.True(svc.IsVoiceFittingForNpc(child, childNpc));
    }

    [Fact]
    public void IsVoiceFittingForNpc_NameMatchBeatsRaceGenderFilter()
    {
        // Mirrors EchokrautVoice.IsSelectable: a user-picked name match wins even when the
        // formal filters disagree.
        using var svc = Build(out _, out _);
        var named = Voice("A.wav", "Male_Hyur_Alphinaud", [Genders.Male], [NpcRaces.Hyur]);

        Assert.True(svc.IsVoiceFittingForNpc(named, Npc("Alphinaud", Genders.Female, NpcRaces.Elezen)));
    }

    // ── EnsureFittingVoice ────────────────────────────────────────────────────

    [Fact]
    public void EnsureFittingVoice_FittingVoice_NoChangeNoSave()
    {
        var fitting = Voice("A.wav", "Female_Elezen_NPC001", [Genders.Female], [NpcRaces.Elezen]);
        using var svc = Build(out _, out var npcData, new[] { fitting });

        var npc = Npc("Someone", Genders.Female, NpcRaces.Elezen);
        npc.Voices = new List<EchokrautVoice> { fitting };
        npc.voice = "A.wav";

        Assert.False(svc.EnsureFittingVoice(npc, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None)));
        Assert.Equal("A.wav", npc.voice);
        npcData.Verify(n => n.SaveCharacter(It.IsAny<NpcMapData>()), Times.Never);
    }

    [Fact]
    public void EnsureFittingVoice_UnfittingVoice_RepicksAndSaves()
    {
        var male = Voice("Male.wav", "Male_Hyur_NPC001", [Genders.Male], [NpcRaces.Hyur]);
        var female = Voice("Female.wav", "Female_Elezen_NPC002", [Genders.Female], [NpcRaces.Elezen]);
        using var svc = Build(out _, out var npcData, new[] { male, female });

        // Race/gender was edited after the assignment — the old voice no longer fits.
        var npc = Npc("Someone", Genders.Female, NpcRaces.Elezen);
        npc.Voices = new List<EchokrautVoice> { male, female };
        npc.voice = "Male.wav";

        var changed = svc.EnsureFittingVoice(npc, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None));

        Assert.True(changed);
        Assert.Equal("Female.wav", npc.voice);
        npcData.Verify(n => n.SaveCharacter(npc), Times.Once);
    }

    [Fact]
    public void EnsureFittingVoice_NoReplacementAvailable_RestoresOldKey()
    {
        var male = Voice("Male.wav", "Male_Hyur_NPC001", [Genders.Male], [NpcRaces.Hyur]);
        using var svc = Build(out _, out var npcData, new[] { male });

        var npc = Npc("Someone", Genders.Female, NpcRaces.Elezen);
        npc.Voices = new List<EchokrautVoice> { male };
        npc.voice = "Male.wav";

        var changed = svc.EnsureFittingVoice(npc, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None));

        Assert.False(changed);
        Assert.Equal("Male.wav", npc.voice); // caller's warnings still see the original key
        npcData.Verify(n => n.SaveCharacter(It.IsAny<NpcMapData>()), Times.Never);
    }

    // ── Availability gates ────────────────────────────────────────────────────

    [Theory]
    [InlineData(AlltalkInstanceType.None, true)]     // None = audio-files-only, always "available"
    [InlineData(AlltalkInstanceType.Remote, true)]   // base url is set in the fixture
    public void IsBackendAvailable_HonoursInstanceType(AlltalkInstanceType type, bool expected)
    {
        var config = new Configuration { BackendSelection = TTSBackends.EchokrauTTS };
        config.EchokrauTts.InstanceType = type;

        using var svc = new BackendService(
            new Mock<IVoiceMessageQueue>().Object,
            new Mock<ILogService>().Object,
            config,
            new Mock<IAlltalkInstanceService>().Object,
            new Mock<IEchokrauTtsInstanceService>().Object,
            new Mock<INpcDataService>().Object,
            new Mock<IAudioFileService>().Object,
            new Mock<IDatabaseService>().Object,
            new Mock<IAudioPlaybackService>().Object);

        Assert.Equal(expected, svc.IsBackendAvailable());
    }

    [Fact]
    public void IsBackendAvailable_RemoteWithoutBaseUrl_IsFalse()
    {
        var config = new Configuration { BackendSelection = TTSBackends.EchokrauTTS };
        config.EchokrauTts.InstanceType = AlltalkInstanceType.Remote;
        config.EchokrauTts.BaseUrl = "";

        using var svc = new BackendService(
            new Mock<IVoiceMessageQueue>().Object,
            new Mock<ILogService>().Object,
            config,
            new Mock<IAlltalkInstanceService>().Object,
            new Mock<IEchokrauTtsInstanceService>().Object,
            new Mock<INpcDataService>().Object,
            new Mock<IAudioFileService>().Object,
            new Mock<IDatabaseService>().Object,
            new Mock<IAudioPlaybackService>().Object);

        Assert.False(svc.IsBackendAvailable());
    }
}
