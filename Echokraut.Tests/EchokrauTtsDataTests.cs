using Echokraut.DataClasses;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Tests for <see cref="EchokrauTtsData"/>, including the custom-data (model + voices) fields that
/// feed the installer's <c>installcustomdataek</c> mode.
/// </summary>
public class EchokrauTtsDataTests
{
    [Theory]
    [InlineData("XTTS", "xtts")]
    [InlineData("f5", "f5")]
    public void TtsBackendArg_IsLowerCased(string engine, string expected)
    {
        var data = new EchokrauTtsData { TtsEngine = engine };
        Assert.Equal(expected, data.TtsBackendArg);
    }

}
