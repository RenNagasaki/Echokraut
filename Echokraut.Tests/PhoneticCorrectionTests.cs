using Echokraut.DataClasses;
using Xunit;

namespace Echokraut.Tests;

public class PhoneticCorrectionTests
{
    [Fact]
    public void Constructor_SetsFields()
    {
        var correction = new PhoneticCorrection("C'ami", "Kami");
        Assert.Equal("C'ami", correction.OriginalText);
        Assert.Equal("Kami", correction.CorrectedText);
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new PhoneticCorrection("hello", "world");
        var b = new PhoneticCorrection("hello", "world");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new PhoneticCorrection("hello", "world");
        var b = new PhoneticCorrection("foo", "bar");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Contains_WorksInList()
    {
        var list = new List<PhoneticCorrection>
        {
            new("C'ami", "Kami"),
            new("Y'shtola", "Yshtola"),
        };

        Assert.Contains(list, c => c.OriginalText == "C'ami");
        Assert.DoesNotContain(list, c => c.OriginalText == "Unknown");
    }
}
