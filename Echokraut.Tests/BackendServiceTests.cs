using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.Queue;
using Echotools.Logging.Enums;
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
        IList<EchokrautVoice>? voicesInDb = null,
        string narratorVoice = "")
    {
        var config = new Configuration();
        config.BackendSelection = TTSBackends.EchokrauTTS;
        config.EchokrauTts.InstanceType = AlltalkInstanceType.None;
        config.NarratorVoice = narratorVoice;

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
            new Mock<IAudioPlaybackService>().Object,
            new Mock<IGameVoiceReferenceService>().Object);
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


    // ── Narrator voice: the user's pick overrules the flagged Narrator.wav ─────

    [Fact]
    public void PickVoice_UserNarratorPick_IsUsedAsFallbackInsteadOfFlaggedDefault()
    {
        using var svc = Build(out _, out _, narratorVoice: "MyNarrator.wav");

        var flagged = Voice("Narrator.wav", "Narrator", isDefault: true);
        var picked = Voice("MyNarrator.wav", "Female_Hyur_Chosen", [Genders.Female], [NpcRaces.Hyur]);

        var result = svc.PickVoice(Npc("Nobody", Genders.Female, NpcRaces.AuRa),
                                   new List<EchokrautVoice> { flagged, picked });

        Assert.Same(picked, result);
    }

    [Fact]
    public void PickVoice_StaleNarratorPick_FallsBackToFlaggedDefault()
    {
        using var svc = Build(out _, out _, narratorVoice: "GoneWithTheVoicePack.wav");

        var flagged = Voice("Narrator.wav", "Narrator", isDefault: true);

        var result = svc.PickVoice(Npc("Nobody", Genders.Female, NpcRaces.AuRa),
                                   new List<EchokrautVoice> { flagged });

        Assert.Same(flagged, result);
    }

    [Fact]
    public void PickVoice_DisabledNarratorPick_FallsBackToFlaggedDefault()
    {
        using var svc = Build(out _, out _, narratorVoice: "MyNarrator.wav");

        var flagged = Voice("Narrator.wav", "Narrator", isDefault: true);
        var picked = Voice("MyNarrator.wav", "Female_Hyur_Chosen", [Genders.Female], [NpcRaces.Hyur]);
        picked.IsEnabled = false;

        var result = svc.PickVoice(Npc("Nobody", Genders.Female, NpcRaces.AuRa),
                                   new List<EchokrautVoice> { flagged, picked });

        Assert.Same(flagged, result);
    }

    [Fact]
    public void IsVoiceFittingForNpc_NarratorPick_FitsEveryNpc()
    {
        using var svc = Build(out _, out _, narratorVoice: "MyNarrator.wav");

        // Gender and race that match nothing about the NPC — the narrator still fits.
        var picked = Voice("MyNarrator.wav", "Male_Lalafell_Chosen", [Genders.Male], [NpcRaces.Lalafell]);

        Assert.True(svc.IsVoiceFittingForNpc(picked, Npc("Anyone", Genders.Female, NpcRaces.AuRa)));
    }

    [Fact]
    public void EnsureFittingVoice_UnnamedNarratorRow_FollowsTheCurrentPick()
    {
        var flagged = Voice("Narrator.wav", "Narrator", isDefault: true);
        var picked = Voice("MyNarrator.wav", "Female_Hyur_Chosen", [Genders.Female], [NpcRaces.Hyur]);
        var voices = new List<EchokrautVoice> { flagged, picked };

        using var svc = Build(out _, out var npcData, voices, narratorVoice: "MyNarrator.wav");

        // A narrator row as it sits in the DB: no name, assigned the voice that WAS narrator.
        var narratorRow = Npc("", Genders.None, NpcRaces.Unknown);
        narratorRow.Voices = voices;
        narratorRow.voice = "Narrator.wav";

        Assert.True(svc.EnsureFittingVoice(narratorRow, new EKEventId(0, TextSource.AddonTalk)));
        Assert.Equal("MyNarrator.wav", narratorRow.voice);
        npcData.Verify(n => n.SaveCharacter(narratorRow), Times.Once);
    }

    [Fact]
    public void EnsureFittingVoice_UnnamedNarratorRow_AlreadyOnTheNarrator_WritesNothing()
    {
        var flagged = Voice("Narrator.wav", "Narrator", isDefault: true);
        var voices = new List<EchokrautVoice> { flagged };

        using var svc = Build(out _, out var npcData, voices);

        var narratorRow = Npc("", Genders.None, NpcRaces.Unknown);
        narratorRow.Voices = voices;
        narratorRow.voice = "Narrator.wav";

        Assert.False(svc.EnsureFittingVoice(narratorRow, new EKEventId(0, TextSource.AddonTalk)));
        npcData.Verify(n => n.SaveCharacter(It.IsAny<NpcMapData>()), Times.Never);
    }

    [Fact]
    public void EnsureFittingVoice_ClearedNarratorPick_ReturnsTheRowToTheFlaggedDefault()
    {
        var flagged = Voice("Narrator.wav", "Narrator", isDefault: true);
        var formerPick = Voice("MyNarrator.wav", "Female_Hyur_Chosen", [Genders.Female], [NpcRaces.Hyur]);
        var voices = new List<EchokrautVoice> { flagged, formerPick };

        // narratorVoice left empty = back to automatic.
        using var svc = Build(out _, out _, voices);

        var narratorRow = Npc("", Genders.None, NpcRaces.Unknown);
        narratorRow.Voices = voices;
        narratorRow.voice = "MyNarrator.wav";

        Assert.True(svc.EnsureFittingVoice(narratorRow, new EKEventId(0, TextSource.AddonTalk)));
        Assert.Equal("Narrator.wav", narratorRow.voice);
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
            new Mock<IAudioPlaybackService>().Object,
            new Mock<IGameVoiceReferenceService>().Object);

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
            new Mock<IAudioPlaybackService>().Object,
            new Mock<IGameVoiceReferenceService>().Object);

        Assert.False(svc.IsBackendAvailable());
    }

    // ── Shared Local/Remote/None switch (Backend tab + E2E remote stage) ─────────

    private static BackendService SwitchService(Configuration config, Mock<IEchokrauTtsInstanceService> ek,
        Mock<IAlltalkInstanceService> at) =>
        new(new Mock<IVoiceMessageQueue>().Object, new Mock<ILogService>().Object, config, at.Object, ek.Object,
            new Mock<INpcDataService>().Object, new Mock<IAudioFileService>().Object, new Mock<IDatabaseService>().Object,
            new Mock<IAudioPlaybackService>().Object, new Mock<IGameVoiceReferenceService>().Object);

    [Fact]
    public async Task SwitchInstanceType_LocalToRemote_StopsTheLocalInstanceBeforeWritingTheMode()
    {
        var config = new Configuration { BackendSelection = TTSBackends.EchokrauTTS };
        config.EchokrauTts.InstanceType = AlltalkInstanceType.Local;
        var ek = new Mock<IEchokrauTtsInstanceService>();
        AlltalkInstanceType? typeAtStop = null;
        ek.Setup(e => e.StopInstance(It.IsAny<EKEventId>())).Callback(() => typeAtStop = config.EchokrauTts.InstanceType);
        using var svc = SwitchService(config, ek, new Mock<IAlltalkInstanceService>());

        await svc.SwitchInstanceTypeAsync(AlltalkInstanceType.Remote, "https://tts.example.org", new EKEventId(0, TextSource.None));

        Assert.Equal(AlltalkInstanceType.Local, typeAtStop);
        Assert.Equal(AlltalkInstanceType.Remote, config.EchokrauTts.InstanceType);
        Assert.Equal("https://tts.example.org", config.EchokrauTts.BaseUrl);
    }

    [Fact]
    public async Task SwitchInstanceType_NotLeavingLocal_StopsNothing_AndKeepsTheUrl()
    {
        var config = new Configuration { BackendSelection = TTSBackends.EchokrauTTS };
        config.EchokrauTts.InstanceType = AlltalkInstanceType.Remote;
        config.EchokrauTts.BaseUrl = "https://tts.example.org";
        var ek = new Mock<IEchokrauTtsInstanceService>();
        using var svc = SwitchService(config, ek, new Mock<IAlltalkInstanceService>());

        await svc.SwitchInstanceTypeAsync(AlltalkInstanceType.None, null, new EKEventId(0, TextSource.None));

        ek.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Never);
        Assert.Equal(AlltalkInstanceType.None, config.EchokrauTts.InstanceType);
        Assert.Equal("https://tts.example.org", config.EchokrauTts.BaseUrl);
    }

    [Fact]
    public async Task SwitchInstanceType_AllTalk_StopsAndWritesTheAllTalkSide()
    {
        var config = new Configuration { BackendSelection = TTSBackends.Alltalk };
        config.Alltalk.InstanceType = AlltalkInstanceType.Local;
        var ek = new Mock<IEchokrauTtsInstanceService>();
        var at = new Mock<IAlltalkInstanceService>();
        using var svc = SwitchService(config, ek, at);

        await svc.SwitchInstanceTypeAsync(AlltalkInstanceType.Remote, null, new EKEventId(0, TextSource.None));

        at.Verify(a => a.StopInstance(It.IsAny<EKEventId>()), Times.Once);
        ek.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Never);
        Assert.Equal(AlltalkInstanceType.Remote, config.Alltalk.InstanceType);
    }

    // ── Game-voice reference gate (plan game-voice-reference §5.3) ──────────────

    private static (BackendService Svc, Mock<IGameVoiceReferenceService> GameVoice) GameVoiceFixture(TTSBackends backend)
    {
        var config = new Configuration { BackendSelection = backend };
        config.EchokrauTts.InstanceType = AlltalkInstanceType.None;
        config.Alltalk.InstanceType = AlltalkInstanceType.None;
        var gameVoice = new Mock<IGameVoiceReferenceService>();
        var svc = new BackendService(
            new Mock<IVoiceMessageQueue>().Object,
            new Mock<ILogService>().Object,
            config,
            new Mock<IAlltalkInstanceService>().Object,
            new Mock<IEchokrauTtsInstanceService>().Object,
            new Mock<INpcDataService>().Object,
            new Mock<IAudioFileService>().Object,
            new Mock<IDatabaseService>().Object,
            new Mock<IAudioPlaybackService>().Object,
            gameVoice.Object);
        return (svc, gameVoice);
    }

    private static VoiceMessage GameVoiceMessage(int characterId, ObjectKind kind) => new()
    {
        Text = "Hi.",
        OriginalText = "Hi.",
        Speaker = new NpcMapData(kind) { Name = "Y'shtola" },
        CharacterId = characterId,
        NpcBaseId = 1234,
        Language = Dalamud.Game.ClientLanguage.German,
        EventId = new EKEventId(0, TextSource.AddonTalk),
    };

    [Fact]
    public async Task GameVoice_EchokrauTtsNpc_AsksServiceWithSpeakerIdentity()
    {
        var (svc, gameVoice) = GameVoiceFixture(TTSBackends.EchokrauTTS);
        using var _ = svc;

        await svc.TryGetGameVoiceReferenceAsync(GameVoiceMessage(7, ObjectKind.EventNpc));

        gameVoice.Verify(g => g.GetReferenceAsync(
            It.Is<GameVoiceSpeaker>(s => s.CharacterId == 7 && s.Name == "Y'shtola" && s.NpcBaseId == 1234
                                         && s.Language == Dalamud.Game.ClientLanguage.German),
            It.IsAny<EKEventId>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(TTSBackends.Alltalk, 7, ObjectKind.EventNpc)]    // AllTalk: never (file names only)
    [InlineData(TTSBackends.EchokrauTTS, 0, ObjectKind.EventNpc)] // no DB character
    [InlineData(TTSBackends.EchokrauTTS, 7, ObjectKind.Pc)]       // players have no game voice
    public async Task GameVoice_NeverAskedOutsideItsScope(TTSBackends backend, int characterId, ObjectKind kind)
    {
        var (svc, gameVoice) = GameVoiceFixture(backend);
        using var _ = svc;

        Assert.Null(await svc.TryGetGameVoiceReferenceAsync(GameVoiceMessage(characterId, kind)));
        gameVoice.Verify(g => g.GetReferenceAsync(It.IsAny<GameVoiceSpeaker>(), It.IsAny<EKEventId>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GameVoice_ServiceThrows_FallsBackToVoicePack()
    {
        var (svc, gameVoice) = GameVoiceFixture(TTSBackends.EchokrauTTS);
        using var _ = svc;
        gameVoice.Setup(g => g.GetReferenceAsync(It.IsAny<GameVoiceSpeaker>(), It.IsAny<EKEventId>(), It.IsAny<System.Threading.CancellationToken>()))
            .ThrowsAsync(new System.InvalidOperationException("boom"));

        Assert.Null(await svc.TryGetGameVoiceReferenceAsync(GameVoiceMessage(7, ObjectKind.EventNpc)));
    }

    // ── §6: ReferenceSource marks what a stream was generated from ──────────────

    private static (BackendService Svc, Mock<ITTSBackend> Tts, Mock<IGameVoiceReferenceService> GameVoice) GenerateFixture(
        InlineVoiceReference? reference)
    {
        var config = new Configuration { BackendSelection = TTSBackends.EchokrauTTS };
        // Local with a stopped instance: live generation is on, but the ctor's background
        // RefreshBackend cannot connect and so never replaces the mocked backend (Remote would).
        config.EchokrauTts.InstanceType = AlltalkInstanceType.Local;
        var gameVoice = new Mock<IGameVoiceReferenceService>();
        gameVoice.Setup(g => g.GetReferenceAsync(It.IsAny<GameVoiceSpeaker>(), It.IsAny<EKEventId>(), It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(reference);
        var tts = new Mock<ITTSBackend>();
        var svc = new BackendService(
            new Mock<IVoiceMessageQueue>().Object,
            new Mock<ILogService>().Object,
            config,
            new Mock<IAlltalkInstanceService>().Object,
            new Mock<IEchokrauTtsInstanceService>().Object,
            new Mock<INpcDataService>().Object,
            new Mock<IAudioFileService>().Object,
            new Mock<IDatabaseService>().Object,
            new Mock<IAudioPlaybackService>().Object,
            gameVoice.Object) { BackendForTests = tts.Object };
        return (svc, tts, gameVoice);
    }

    private static VoiceMessage GenerateMessage()
    {
        var m = GameVoiceMessage(7, ObjectKind.EventNpc);
        m.Speaker.voice = "Female_Miqote_NPC001.wav";
        m.ReferenceSource = ReferenceSource.GameVoice; // stale value from an earlier attempt must not survive
        return m;
    }

    private static InlineVoiceReference SomeReference() => new(new short[24000], "Hello.", new[] { "clip1" });

    [Fact]
    public async System.Threading.Tasks.Task GenerateVoice_ReferenceAccepted_MarksGameVoice()
    {
        var (svc, tts, _) = GenerateFixture(SomeReference());
        using var _ = svc;
        tts.Setup(t => t.GenerateAudioStreamFromVoice(It.IsAny<EKEventId>(), It.IsAny<VoiceMessage>(), It.IsAny<string>(),
                It.IsAny<Dalamud.Game.ClientLanguage>(), It.IsAny<System.Action<string>?>(), It.IsNotNull<InlineVoiceReference>()))
            .ReturnsAsync(new System.IO.MemoryStream());
        var message = GenerateMessage();

        Assert.True(await svc.GenerateVoice(message));
        Assert.Equal(ReferenceSource.GameVoice, message.ReferenceSource);
    }

    [Fact]
    public async System.Threading.Tasks.Task GenerateVoice_ReferenceRefused_RetriesWithVoicePackAndMarksVoicePack()
    {
        var (svc, tts, gameVoice) = GenerateFixture(SomeReference());
        using var _ = svc;
        tts.Setup(t => t.GenerateAudioStreamFromVoice(It.IsAny<EKEventId>(), It.IsAny<VoiceMessage>(), It.IsAny<string>(),
                It.IsAny<Dalamud.Game.ClientLanguage>(), It.IsAny<System.Action<string>?>(), It.IsNotNull<InlineVoiceReference>()))
            .ReturnsAsync((System.IO.Stream?)null);
        tts.Setup(t => t.GenerateAudioStreamFromVoice(It.IsAny<EKEventId>(), It.IsAny<VoiceMessage>(), It.IsAny<string>(),
                It.IsAny<Dalamud.Game.ClientLanguage>(), It.IsAny<System.Action<string>?>(), null))
            .ReturnsAsync(new System.IO.MemoryStream());
        var message = GenerateMessage();

        Assert.True(await svc.GenerateVoice(message));
        Assert.Equal(ReferenceSource.VoicePack, message.ReferenceSource);
        gameVoice.Verify(g => g.ReportReferenceRejected(It.IsAny<EKEventId>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task GenerateVoice_NoReference_MarksVoicePack()
    {
        var (svc, tts, _) = GenerateFixture(null);
        using var _ = svc;
        tts.Setup(t => t.GenerateAudioStreamFromVoice(It.IsAny<EKEventId>(), It.IsAny<VoiceMessage>(), It.IsAny<string>(),
                It.IsAny<Dalamud.Game.ClientLanguage>(), It.IsAny<System.Action<string>?>(), null))
            .ReturnsAsync(new System.IO.MemoryStream());
        var message = GenerateMessage();

        Assert.True(await svc.GenerateVoice(message));
        Assert.Equal(ReferenceSource.VoicePack, message.ReferenceSource);
    }
}
