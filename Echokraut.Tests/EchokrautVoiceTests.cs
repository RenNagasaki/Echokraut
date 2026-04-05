using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Xunit;

namespace Echokraut.Tests;

public class EchokrautVoiceTests
{
    // ── FitsNpcData ──────────────────────────────────────────────────────────

    [Fact]
    public void FitsNpcData_EnabledRandomMatchingRace_True()
    {
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.True(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, true));
    }

    [Fact]
    public void FitsNpcData_NotRandom_False()
    {
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = false,
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, true));
    }

    [Fact]
    public void FitsNpcData_WrongRace_False()
    {
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Elezen],
        };
        Assert.False(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, true));
    }

    [Fact]
    public void FitsNpcData_ChildVoice_MatchesChildOnly()
    {
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            IsAdultVoice = false,
            IsChildVoice = true,
            AllowedGenders = [Genders.Female],
            AllowedRaces = [NpcRaces.Lalafell],
        };
        Assert.True(voice.FitsNpcData(Genders.Female, NpcRaces.Lalafell, BodyType.Child, true));
        Assert.False(voice.FitsNpcData(Genders.Female, NpcRaces.Lalafell, BodyType.Adult, true));
        Assert.False(voice.FitsNpcData(Genders.Female, NpcRaces.Lalafell, BodyType.Elder, true));
    }

    [Fact]
    public void FitsNpcData_ElderVoice_MatchesElderOnly()
    {
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            IsAdultVoice = false,
            IsElderVoice = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.True(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Elder, true));
        Assert.False(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, true));
        Assert.False(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Child, true));
    }

    [Fact]
    public void FitsNpcData_AdultElderVoice_MatchesBoth()
    {
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            IsAdultVoice = true,
            IsElderVoice = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.True(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, true));
        Assert.True(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Elder, true));
        Assert.False(voice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Child, true));
    }

    // ── Equality ─────────────────────────────────────────────────────────────

    [Fact]
    public void Equals_SameVoiceName_CaseInsensitive()
    {
        var a = new EchokrautVoice { voiceName = "test_voice" };
        var b = new EchokrautVoice { voiceName = "TEST_VOICE" };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentVoiceName_NotEqual()
    {
        var a = new EchokrautVoice { voiceName = "voice_a" };
        var b = new EchokrautVoice { voiceName = "voice_b" };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToString_ReturnsVoiceName()
    {
        var voice = new EchokrautVoice { voiceName = "test_voice" };
        Assert.Equal("test_voice", voice.ToString());
    }
}
