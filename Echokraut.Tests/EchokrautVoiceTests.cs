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

    // ── Gender mismatch (the main concern: NPC must NOT get a wrong-gender voice) ──

    [Fact]
    public void FitsNpcData_FemaleHyur_DoesNotMatchMaleOnlyVoice()
    {
        var maleVoice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(maleVoice.FitsNpcData(Genders.Female, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
    }

    [Fact]
    public void FitsNpcData_MaleHyur_DoesNotMatchFemaleOnlyVoice()
    {
        var femaleVoice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Female],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(femaleVoice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
    }

    [Fact]
    public void FitsNpcData_FemaleLalafell_DoesNotMatchMaleOnlyVoice()
    {
        var maleLalafell = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Lalafell],
        };
        Assert.False(maleLalafell.FitsNpcData(Genders.Female, NpcRaces.Lalafell, BodyType.Adult, isGenderedRace: true));
    }

    [Fact]
    public void FitsNpcData_GenderedRace_EmptyAllowedGenders_AcceptsBoth()
    {
        // Documents the existing behaviour: an empty AllowedGenders list on a gendered race
        // is treated as "any gender allowed" (see FitsNpcData logic).
        var anyGenderVoice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [], // empty = wildcard
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.True(anyGenderVoice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
        Assert.True(anyGenderVoice.FitsNpcData(Genders.Female, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
    }

    // ── Race mismatch ────────────────────────────────────────────────────────

    [Fact]
    public void FitsNpcData_VoiceWithoutHyurInAllowed_DoesNotMatchHyur()
    {
        var nonHyurVoice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Male, Genders.Female],
            AllowedRaces = [NpcRaces.Elezen, NpcRaces.AuRa],
        };
        Assert.False(nonHyurVoice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
        Assert.False(nonHyurVoice.FitsNpcData(Genders.Female, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
    }

    [Fact]
    public void FitsNpcData_HyurOnlyVoice_DoesNotMatchOtherPlayerRaces()
    {
        var hyurOnly = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(hyurOnly.FitsNpcData(Genders.Male, NpcRaces.Elezen, BodyType.Adult, isGenderedRace: true));
        Assert.False(hyurOnly.FitsNpcData(Genders.Male, NpcRaces.AuRa, BodyType.Adult, isGenderedRace: true));
        Assert.False(hyurOnly.FitsNpcData(Genders.Male, NpcRaces.Hrothgar, BodyType.Adult, isGenderedRace: true));
        Assert.False(hyurOnly.FitsNpcData(Genders.Male, NpcRaces.Viera, BodyType.Adult, isGenderedRace: true));
    }

    // ── Non-gendered races (beast tribes — Loporrit, Moogle, etc.) ───────────

    [Fact]
    public void FitsNpcData_MaleLoporrit_NonGenderedRace_IgnoresGenderConstraint()
    {
        // Loporrits aren't gendered — the voice's AllowedGenders constraint is ignored
        // as long as the race is allowed.
        var loporritFemaleOnlyVoice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Female],
            AllowedRaces = [NpcRaces.Loporrit],
        };
        Assert.True(loporritFemaleOnlyVoice.FitsNpcData(Genders.Male, NpcRaces.Loporrit, BodyType.Adult, isGenderedRace: false));
        Assert.True(loporritFemaleOnlyVoice.FitsNpcData(Genders.Female, NpcRaces.Loporrit, BodyType.Adult, isGenderedRace: false));
        Assert.True(loporritFemaleOnlyVoice.FitsNpcData(Genders.None, NpcRaces.Loporrit, BodyType.Adult, isGenderedRace: false));
    }

    [Fact]
    public void FitsNpcData_LoporritVoice_DoesNotMatchHyur()
    {
        // Race mismatch still applies even for non-gendered races.
        var loporritVoice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [],
            AllowedRaces = [NpcRaces.Loporrit],
        };
        Assert.False(loporritVoice.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
        Assert.False(loporritVoice.FitsNpcData(Genders.Male, NpcRaces.Moogle, BodyType.Adult, isGenderedRace: false));
    }

    [Fact]
    public void FitsNpcData_HyurVoice_DoesNotMatchLoporrit()
    {
        var hyurVoice = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(hyurVoice.FitsNpcData(Genders.Male, NpcRaces.Loporrit, BodyType.Adult, isGenderedRace: false));
    }

    // ── Disabled / non-random voice ─────────────────────────────────────────

    [Fact]
    public void FitsNpcData_DisabledVoice_NeverMatches()
    {
        var disabled = new EchokrautVoice
        {
            IsEnabled = false,
            UseAsRandom = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(disabled.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
    }

    // ── IsSelectable (UI dropdown) ──────────────────────────────────────────

    [Fact]
    public void IsSelectable_DefaultVoice_AlwaysSelectable()
    {
        // Even with mismatched race/gender, a default voice should appear in the dropdown.
        var def = new EchokrautVoice
        {
            IsEnabled = true,
            IsDefault = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.True(def.IsSelectable("Random NPC", Genders.Female, NpcRaces.Loporrit, BodyType.Adult));
    }

    [Fact]
    public void IsSelectable_DisabledVoice_NeverSelectable()
    {
        var disabled = new EchokrautVoice
        {
            IsEnabled = false,
            IsDefault = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(disabled.IsSelectable("Some NPC", Genders.Male, NpcRaces.Hyur, BodyType.Adult));
    }

    [Fact]
    public void IsSelectable_FemaleHyur_DoesNotSelectMaleOnlyVoice()
    {
        var maleHyurVoice = new EchokrautVoice
        {
            IsEnabled = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        // Random NPC name (no substring match), gendered race mismatch → not selectable.
        Assert.False(maleHyurVoice.IsSelectable("Random NPC", Genders.Female, NpcRaces.Hyur, BodyType.Adult));
    }

    // ── BodyType matching ────────────────────────────────────────────────────

    [Fact]
    public void FitsNpcData_AdultOnlyVoice_DoesNotMatchChildOrElder()
    {
        var adultOnly = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            IsAdultVoice = true,
            IsChildVoice = false,
            IsElderVoice = false,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.True(adultOnly.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
        Assert.False(adultOnly.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Child, isGenderedRace: true));
        Assert.False(adultOnly.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Elder, isGenderedRace: true));
    }

    [Fact]
    public void FitsNpcData_ChildVoice_DoesNotMatchAdultOrElder()
    {
        var childOnly = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            IsAdultVoice = false,
            IsChildVoice = true,
            IsElderVoice = false,
            AllowedGenders = [Genders.Female],
            AllowedRaces = [NpcRaces.Lalafell],
        };
        Assert.True(childOnly.FitsNpcData(Genders.Female, NpcRaces.Lalafell, BodyType.Child, isGenderedRace: true));
        Assert.False(childOnly.FitsNpcData(Genders.Female, NpcRaces.Lalafell, BodyType.Adult, isGenderedRace: true));
        Assert.False(childOnly.FitsNpcData(Genders.Female, NpcRaces.Lalafell, BodyType.Elder, isGenderedRace: true));
    }

    [Fact]
    public void FitsNpcData_VoiceWithNoBodyTypeFlags_NeverMatches()
    {
        // A voice with no body type flag enabled is effectively unusable for any NPC.
        var noBody = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            IsAdultVoice = false,
            IsChildVoice = false,
            IsElderVoice = false,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(noBody.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
        Assert.False(noBody.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Child, isGenderedRace: true));
        Assert.False(noBody.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Elder, isGenderedRace: true));
    }

    [Fact]
    public void FitsNpcData_AllBodyTypesVoice_MatchesAllBodyTypes()
    {
        var allBody = new EchokrautVoice
        {
            IsEnabled = true,
            UseAsRandom = true,
            IsAdultVoice = true,
            IsChildVoice = true,
            IsElderVoice = true,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.True(allBody.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Adult, isGenderedRace: true));
        Assert.True(allBody.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Child, isGenderedRace: true));
        Assert.True(allBody.FitsNpcData(Genders.Male, NpcRaces.Hyur, BodyType.Elder, isGenderedRace: true));
    }

    [Fact]
    public void IsSelectable_ChildNpc_DoesNotSelectAdultOnlyVoice()
    {
        var adultOnly = new EchokrautVoice
        {
            IsEnabled = true,
            IsAdultVoice = true,
            IsChildVoice = false,
            AllowedGenders = [Genders.Male],
            AllowedRaces = [NpcRaces.Hyur],
        };
        // race+gender+name OK, only bodytype mismatch → not selectable
        Assert.False(adultOnly.IsSelectable("Random NPC", Genders.Male, NpcRaces.Hyur, BodyType.Child));
    }

    // ── Voice name extraction (lazy-initialized voiceNameShort) ─────────────

    [Theory]
    [InlineData("Female_Hyur_Yshtola", "Yshtola")]
    [InlineData("Male_Lalafell_Krile", "Krile")]
    [InlineData("Female_AuRa_WukLamat", "WukLamat")]
    [InlineData("Male_Hrothgar_Magnai", "Magnai")]
    [InlineData("Female_Loporrit_Live", "Live")]
    public void VoiceName_StripsKnownGenderRaceTokens(string fullName, string expectedShort)
    {
        var voice = new EchokrautVoice { voiceName = fullName };
        Assert.Equal(expectedShort, voice.VoiceName);
    }

    [Theory]
    [InlineData("Female_Hyur_Adult-Hyur_Yshtola", "Yshtola")]
    [InlineData("Female_Hyur_Child-Hyur_Wedge", "Wedge")]
    [InlineData("Male_Hyur_Elder-Hyur_Urianger", "Urianger")]
    [InlineData("Female_Hyur_All-Hyur_Yshtola", "Yshtola")]
    public void VoiceName_StripsBodyTypeDashRaceTokens(string fullName, string expectedShort)
    {
        var voice = new EchokrautVoice { voiceName = fullName };
        Assert.Equal(expectedShort, voice.VoiceName);
    }

    [Fact]
    public void VoiceName_UnrecognizedTokenBecomesShortName()
    {
        // "Narrator" doesn't match any gender/race → it IS the short name.
        var voice = new EchokrautVoice { voiceName = "Narrator" };
        Assert.Equal("Narrator", voice.VoiceName);
    }

    // ── IsSelectable: name match path (only works after voiceNameShort is initialized) ──

    [Fact]
    public void IsSelectable_NameMatch_OverridesRaceGenderFilter_OnFirstCall()
    {
        // Regression test for the "Male Elezen Alphinaud should see the Alphinaud voice" bug:
        // voiceNameShort is lazy-initialized via the VoiceName getter, and IsSelectable now
        // calls VoiceName (not the raw field) so the very first IsSelectable call works.
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            voiceName = "Female_Hyur_Yshtola",
            AllowedGenders = [Genders.Female],
            AllowedRaces = [NpcRaces.Hyur],
        };
        // No prior VoiceName read — IsSelectable must still find "Yshtola" inside the voice name.
        Assert.True(voice.IsSelectable("Yshtola", Genders.Male, NpcRaces.Lalafell, BodyType.Adult));
    }

    [Fact]
    public void IsSelectable_NameMatch_IsCaseInsensitive()
    {
        // Aligns with BackendService.PickVoice's OrdinalIgnoreCase compare.
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            voiceName = "Female_Hyur_Yshtola",
            AllowedGenders = [Genders.Female],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.True(voice.IsSelectable("Yshtola", Genders.Male, NpcRaces.Lalafell, BodyType.Adult));
        Assert.True(voice.IsSelectable("yshtola", Genders.Male, NpcRaces.Lalafell, BodyType.Adult));
        Assert.True(voice.IsSelectable("YSHTOLA", Genders.Male, NpcRaces.Lalafell, BodyType.Adult));
    }

    [Fact]
    public void IsSelectable_EmptyNpcName_DoesNotMatchEverything()
    {
        // string.Contains("") returns true — guard prevents an empty NPC name from making
        // every voice selectable.
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            voiceName = "Female_Hyur_Yshtola",
            AllowedGenders = [Genders.Female],
            AllowedRaces = [NpcRaces.Hyur],
        };
        Assert.False(voice.IsSelectable("", Genders.Male, NpcRaces.Lalafell, BodyType.Adult));
    }

    [Fact]
    public void IsSelectable_AlphinaudNpc_FindsAlphinaudVoiceEvenWithoutAllowedFlags()
    {
        // Concrete reproducer of the user-reported bug:
        // NPC "Alphinaud" (Male Elezen Adult), voice named "Male_Elezen_Alphinaud" but with empty
        // AllowedGenders/AllowedRaces — only the name-match path can save it.
        var voice = new EchokrautVoice
        {
            IsEnabled = true,
            voiceName = "Male_Elezen_Alphinaud",
            AllowedGenders = [],
            AllowedRaces = [],
        };
        Assert.True(voice.IsSelectable("Alphinaud", Genders.Male, NpcRaces.Elezen, BodyType.Adult));
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
