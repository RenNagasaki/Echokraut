using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Xunit;

namespace Echokraut.Tests;

public class StringKeyedComparableTests
{
    private static NpcMapData Npc(string name) =>
        new(ObjectKind.BattleNpc) { Name = name, Gender = Genders.Male, Race = NpcRaces.Hyur };

    [Fact]
    public void Equals_And_HashCode_AreCaseInsensitive_OnTheKey()
    {
        var a = Npc("Alphinaud");
        var b = Npc("alphinaud");
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentKey_NotEqual()
    {
        Assert.False(Npc("Alphinaud").Equals(Npc("Alisaie")));
    }

    [Fact]
    public void Equals_DifferentRuntimeType_False()
    {
        object voice = new EchokrautVoice { voiceName = "Male - Hyur - x" };
        Assert.False(Npc("x").Equals(voice));
    }

    [Fact]
    public void CompareTo_IsDescending_AndCaseInsensitive()
    {
        var a = Npc("AAA");
        var b = Npc("BBB");
        Assert.True(a.CompareTo(b) > 0);          // smaller key sorts AFTER → descending
        Assert.True(b.CompareTo(a) < 0);
        Assert.Equal(0, a.CompareTo(Npc("aaa"))); // case-insensitive
    }

    [Fact]
    public void PhoneticCorrection_Equals_StaysCaseInsensitive()
    {
        Assert.True(new PhoneticCorrection("Hello", "World")
            .Equals(new PhoneticCorrection("hello", "world")));
    }

    [Fact]
    public void BackendVoiceItem_CompareTo_StaysCaseSensitive()
    {
#pragma warning disable CS0618 // obsolete, migration-only — its frozen ordering is exactly what we assert
        var lower = new BackendVoiceItem { Gender = Genders.Male, Race = NpcRaces.Hyur, VoiceName = "abc" };
        var upper = new BackendVoiceItem { Gender = Genders.Male, Race = NpcRaces.Hyur, VoiceName = "ABC" };
#pragma warning restore CS0618
        Assert.NotEqual(0, lower.CompareTo(upper)); // case-sensitive override → not equal
    }
}
