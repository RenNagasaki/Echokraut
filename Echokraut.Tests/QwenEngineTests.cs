using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Localization;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Qwen3-TTS engines (wrapper 0.0.2.0): catalog entries, options and their wrapper args.
/// </summary>
public class QwenEngineTests
{
    [Fact]
    public void EmbeddedCatalog_ListsBothQwenEngines_FromWrapper002()
    {
        var engines = TtsEngineCatalogService.Parse(File.ReadAllText(TestPaths.EnginesJsonPath))!;

        foreach (var id in new[] { "qwen-0.6b", "qwen-1.7b" })
        {
            var engine = Assert.Single(engines, e => e.Id == id);
            Assert.Equal("0.0.2.0", engine.MinVersion); // 0.0.1.9 shipped without Qwen
            Assert.False(string.IsNullOrWhiteSpace(engine.Description));
        }
    }

    [Fact]
    public void QwenTexts_AreTranslated()
    {
        // The catalog description is the localization key: it must match a Loc entry verbatim.
        var engines = TtsEngineCatalogService.Parse(File.ReadAllText(TestPaths.EnginesJsonPath))!;
        var keys = engines.Where(e => e.Id.StartsWith("qwen")).Select(e => e.Description).ToList();
        keys.Add("Qwen: calmer delivery (temperature 0.7 instead of 0.9)");

        LocAssert.Translated(keys);
    }

    [Fact]
    public void QwenDefaults_AreTheUsersChoice()
    {
        var data = new EchokrauTtsData();
        Assert.Equal("0.9", data.QwenTemperatureArg);
    }

    [Fact]
    public void ResponseCache_OnLeavesTheWrapperConfig_OffPassesZero()
    {
        var data = new EchokrauTtsData();
        Assert.True(data.ResponseCache);
        Assert.Equal("", data.ResponseCacheMinutesArg); // not passed: the wrapper's config.json decides
        data.ResponseCache = false;
        Assert.Equal("0", data.ResponseCacheMinutesArg);
        LocAssert.Translated(new[] { "Reuse identical lines for a few minutes (restarts local instance)" });
    }

    [Fact]
    public void QwenTemperatureArg_IsCultureInvariant()
    {
        // A German client would otherwise send "0,7", which the wrapper's float parser rejects.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var data = new EchokrauTtsData { QwenTemperature = EchokrauTtsData.CalmQwenTemperature };
            Assert.Equal("0.7", data.QwenTemperatureArg);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
