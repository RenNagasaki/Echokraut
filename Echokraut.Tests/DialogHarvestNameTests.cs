using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

public class DialogHarvestNameTests
{
    [Theory]
    [InlineData("stille Druidin", "Stille Druidin")]   // German [a]→"e" lowercase stem
    [InlineData("kleine Helferlein", "Kleine Helferlein")]
    [InlineData("Stille Druidin", "Stille Druidin")]   // already title-cased — pass-through
    [InlineData("a", "A")]                              // single char
    [InlineData("", "")]                                // empty stays empty
    [InlineData("(special)", "(special)")]              // non-letter first char untouched
    [InlineData("Über", "Über")]                        // uppercase umlaut already canonical
    public void NormalizeNpcName_TitleCasesFirstLetter(string input, string expected)
    {
        Assert.Equal(expected, DialogHarvestService.NormalizeNpcName(input));
    }
}
