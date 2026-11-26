using Dalamud.Game;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class TalkTextHelperCultureTests
{
    [Theory]
    [InlineData(ClientLanguage.German, "de-DE")]
    [InlineData(ClientLanguage.French, "fr-FR")]
    [InlineData(ClientLanguage.Japanese, "ja-JP")]
    [InlineData(ClientLanguage.English, "en-US")]
    public void GetCulture_MapsLanguageToSpecificCulture(ClientLanguage language, string expectedName)
    {
        Assert.Equal(expectedName, TalkTextHelper.GetCulture(language).Name);
    }
}
