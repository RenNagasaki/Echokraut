using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Pocket TTS engine (wrapper 0.0.2.0): catalog entry, model size and decoding-steps options.</summary>
public class PocketEngineTests
{
    [Fact]
    public void EmbeddedCatalog_ListsPocket_FromWrapper002()
    {
        var engines = TtsEngineCatalogService.Parse(File.ReadAllText(TestPaths.EnginesJsonPath))!;
        var pocket = Assert.Single(engines, e => e.Id == "pocket");
        Assert.Equal("0.0.2.0", pocket.MinVersion);
        Assert.False(string.IsNullOrWhiteSpace(pocket.Description));
    }

    [Fact]
    public void PocketTexts_AreTranslated()
    {
        var engines = TtsEngineCatalogService.Parse(File.ReadAllText(TestPaths.EnginesJsonPath))!;
        var keys = new List<string>
        {
            Assert.Single(engines, e => e.Id == "pocket").Description,
            "Pocket TTS: decoding steps", "1 - fastest, occasional echo", "4 - recommended", "8 - slowest",
        };
        LocAssert.Translated(keys);
    }

    [Theory]
    [InlineData(24, 4, "4")] // default: no reverb, ~15 % slower than 1
    [InlineData(24, 1, "1")]
    [InlineData(24, 8, "8")]
    [InlineData(24, 3, "4")] // a hand-edited value outside 1/4/8 falls back to the default
    public void PocketDecodeStepsArg_24Layers(int layers, int configured, string expected)
    {
        Assert.Equal(expected, new EchokrauTtsData { PocketLayers = layers, PocketDecodeSteps24 = configured }.PocketDecodeStepsArg);
    }

    [Theory]
    [InlineData(4, "4")]
    [InlineData(8, "8")]
    [InlineData(1, "4")] // a hand-edited 1 never reaches the wrapper for the 6-layer model
    public void PocketDecodeStepsArg_6Layers(int configured, string expected)
    {
        Assert.Equal(expected, new EchokrauTtsData { PocketLayers = 6, PocketDecodeSteps6 = configured }.PocketDecodeStepsArg);
    }

    [Fact]
    public void Defaults_Are24LayersAndFourSteps()
    {
        var data = new EchokrauTtsData();
        Assert.Equal("24", data.PocketLayersArg);
        Assert.Equal(4, data.PocketDecodeSteps);
        Assert.Equal(4, data.PocketDecodeSteps6);
    }

    [Fact]
    public void UnknownLayers_Mean24() => Assert.Equal("24", new EchokrauTtsData { PocketLayers = 12 }.PocketLayersArg);

    [Fact]
    public void SwitchingTo6WithOneSelected_Uses4_AndSwitchingBackRestoresOne()
    {
        var data = new EchokrauTtsData();
        Assert.True(data.TrySetPocketDecodeSteps(1));
        data.PocketLayers = 6;
        Assert.Equal("4", data.PocketDecodeStepsArg);
        Assert.False(data.TrySetPocketDecodeSteps(1));
        Assert.True(data.TrySetPocketDecodeSteps(8));
        data.PocketLayers = 24;
        Assert.Equal("1", data.PocketDecodeStepsArg); // each size keeps its own choice
        data.PocketLayers = 6;
        Assert.Equal("8", data.PocketDecodeStepsArg);
    }

    [Fact]
    public void StepsDropdown_MarksOneAsUnavailableOn6Layers()
    {
        var six = PocketOptionLabels.Steps(6, s => s);
        Assert.Equal(new[] { 1, 4, 8 }, six.Select(o => o.Value));
        Assert.False(six[0].Allowed);
        Assert.Equal("1 - fastest, occasional echo (24 layers only)", six[0].Label);
        Assert.All(six.Skip(1), o => Assert.True(o.Allowed));
        Assert.All(PocketOptionLabels.Steps(24, s => s), o => Assert.True(o.Allowed));
    }

    [Fact]
    public void LayerTexts_AreTranslated()
        => LocAssert.Translated(["Pocket TTS: model size", "24 layers - better sound", "6 layers - about twice as fast", "{0} (24 layers only)"]);
}
