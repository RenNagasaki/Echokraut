using Echokraut.DataClasses;
using Echokraut.Enums;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Tests for <see cref="EchokrauTtsData"/>, including the custom-data (model + voices) fields that
/// feed the installer's <c>installcustomdataek</c> mode.
/// </summary>
public class EchokrauTtsDataTests
{
    [Fact]
    public void CustomUrls_DefaultToEmpty()
    {
        var data = new EchokrauTtsData();
        Assert.Equal("", data.CustomModelUrl);
        Assert.Equal("", data.CustomVoicesUrl);
    }

    [Fact]
    public void CustomUrls_Roundtrip()
    {
        var data = new EchokrauTtsData
        {
            CustomModelUrl = "https://drive.google.com/uc?id=model",
            CustomVoicesUrl = "https://example.com/samples.zip",
        };
        Assert.Equal("https://drive.google.com/uc?id=model", data.CustomModelUrl);
        Assert.Equal("https://example.com/samples.zip", data.CustomVoicesUrl);
    }

    [Theory]
    [InlineData(EchokrauTtsEngine.XTTS, "xtts")]
    [InlineData(EchokrauTtsEngine.F5, "f5")]
    public void TtsBackendArg_IsLowerCased(EchokrauTtsEngine engine, string expected)
    {
        var data = new EchokrauTtsData { TtsBackend = engine };
        Assert.Equal(expected, data.TtsBackendArg);
    }

    [Fact]
    public void InstanceType_DefaultsToNone()
    {
        Assert.Equal(AlltalkInstanceType.None, new EchokrauTtsData().InstanceType);
    }
}
