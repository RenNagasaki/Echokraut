using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Dalamud.Game;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// EchokrauTtsInstanceService: where StopInstance sends /shutdown. Before the fix it posted to
/// BaseUrl, which after a Local -> Remote switch names the remote server, so the plugin shut down a
/// server it never started.
/// </summary>
public class EchokrauTtsInstanceServiceTests
{
    [Theory]
    [InlineData(8765, "/shutdown", "http://127.0.0.1:8765/shutdown")]
    [InlineData(9000, "shutdown", "http://127.0.0.1:9000/shutdown")]
    public void LocalShutdownUrl_AlwaysLoopback(int port, string path, string expected)
    {
        Assert.Equal(expected, EchokrauTtsInstanceService.LocalShutdownUrl(port, path));
    }

    // ── SelectWrapperReleaseAsync (E2E request wrapperVersion, plan › B8) ──

    private const string LatestUrl = "https://api.github.com/repos/o/Echokrautts/releases/latest";

    private sealed class ReleaseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static string Release(string tag) =>
        $$"""{ "tag_name": "{{tag}}", "assets": [ { "name": "EchokrauTTS.zip", "browser_download_url": "https://github.com/o/Echokrautts/releases/download/{{tag}}/EchokrauTTS.zip" } ] }""";

    private static EchokrauTtsInstanceService Service(ReleaseHandler handler, Configuration? config = null)
    {
        var urls = new Mock<IRemoteUrlService>();
        urls.SetupGet(u => u.Urls).Returns(new RemoteUrlsData { EchokrauTtsReleasesUrl = LatestUrl, EchokrauTtsVersion = "0.0.2.0" });
        return new EchokrauTtsInstanceService(new Mock<ILogService>().Object, config ?? new Configuration(), urls.Object,
            new Mock<IClientState>().Object, new Mock<IVoicePackService>().Object, new Mock<ITtsEngineCatalogService>().Object,
            releaseClient: new HttpClient(handler), isWindows: true);
    }

    [Fact]
    public async Task SelectWrapperRelease_AsksForThatTag_AndTheInstallRecordsIt()
    {
        var handler = new ReleaseHandler(HttpStatusCode.OK, Release("0.0.1.9"));
        var service = Service(handler);

        Assert.Null(await service.SelectWrapperReleaseAsync("0.0.1.9"));

        Assert.Equal(["https://api.github.com/repos/o/Echokrautts/releases/tags/0.0.1.9"], handler.Urls);
        Assert.Equal("0.0.1.9", service.LatestWrapperVersion);
        // The check afterwards must still be possible: the button keeps offering it.
        Assert.Equal(WrapperUpdateState.NotChecked, service.UpdateState);
    }

    [Fact]
    public async Task SelectWrapperRelease_OtherTagInTheAnswer_IsRefusedAndKeepsTheBaseline()
    {
        var service = Service(new ReleaseHandler(HttpStatusCode.OK, Release("0.0.2.0")));

        var error = await service.SelectWrapperReleaseAsync("0.0.1.9");

        Assert.Equal("GitHub answered release 0.0.2.0 for 0.0.1.9", error);
        Assert.Equal("0.0.2.0", service.LatestWrapperVersion);
    }

    [Fact]
    public async Task SelectWrapperRelease_UnknownTag_NamesTheStatus()
    {
        var service = Service(new ReleaseHandler(HttpStatusCode.NotFound, "{}"));

        Assert.Equal("release 9.9.9.9: GitHub returned 404", await service.SelectWrapperReleaseAsync("9.9.9.9"));
        Assert.Equal("0.0.2.0", service.LatestWrapperVersion);
    }

    [Fact]
    public async Task CheckForWrapperUpdate_AfterASelectedOldRelease_OffersTheLatest()
    {
        var handler = new ReleaseHandler(HttpStatusCode.OK, Release("0.0.2.0"));
        var config = new Configuration { EchokrauTts = { LocalInstall = true, InstalledWrapperVersion = "0.0.1.9" } };
        var service = Service(handler, config);

        await service.CheckForWrapperUpdateAsync();

        Assert.Equal(WrapperUpdateState.UpdateAvailable, service.UpdateState);
        Assert.Equal("0.0.2.0", service.LatestWrapperVersion);
        Assert.Equal([LatestUrl], handler.Urls);
    }

    // ── Engine without a model for the game language (Pocket + Japanese client) ──

    // Never written: both the guard and the missing-URL check return before anything touches the disk.
    // No dashes/spaces, the install path validation refuses those.
    private static readonly string InstallRoot = System.OperatingSystem.IsWindows() ? @"C:\EkLanguageGuard" : "/EkLanguageGuard";

    private static EchokrauTtsInstanceService LanguageService(ClientLanguage language, string engine)
    {
        var urls = new Mock<IRemoteUrlService>();
        urls.SetupGet(u => u.Urls).Returns(new RemoteUrlsData { EchokrauTtsVersion = "0.0.2.0" }); // no wrapper URL
        var client = new Mock<IClientState>();
        client.SetupGet(c => c.ClientLanguage).Returns(language);
        var catalog = new Mock<ITtsEngineCatalogService>();
        catalog.SetupGet(c => c.Engines).Returns(new List<TtsEngineInfo>
        {
            new() { Id = "xtts", Name = "XTTS", MinVersion = "0.0.0.1" },
            new() { Id = "pocket", Name = "Pocket TTS", MinVersion = "0.0.2.0", Languages = ["de", "en", "es", "fr", "it", "pt"] },
        });
        var config = new Configuration { TtsInstallRoot = InstallRoot, EchokrauTts = { TtsEngine = engine } };
        return new EchokrauTtsInstanceService(new Mock<ILogService>().Object, config, urls.Object, client.Object,
            new Mock<IVoicePackService>().Object, catalog.Object, isWindows: true);
    }

    [Fact]
    public void Install_PocketOnJapaneseClient_IsRefusedWithTheReason_BeforeAnythingElse()
    {
        var service = LanguageService(ClientLanguage.Japanese, "pocket");

        service.Install();

        Assert.True(service.ConfiguredEngineBlockedByLanguage);
        Assert.Equal("ja", service.ClientLanguageCode);
        // Not the missing-URL status the next check would set: the guard stopped first.
        Assert.Equal(string.Format(Loc.S(EchokrauTtsInstanceService.LanguageBlockedFormat), "Pocket TTS", "ja"),
            service.CurrentInstallStatus);
        Assert.False(service.Installing);
        Assert.False(service.InstanceStarting);
    }

    [Theory]
    [InlineData(ClientLanguage.German, "pocket")]
    [InlineData(ClientLanguage.Japanese, "xtts")]
    public void Install_EngineWithAModelForTheLanguage_PassesTheGuard(ClientLanguage language, string engine)
    {
        var service = LanguageService(language, engine);

        service.Install();

        Assert.False(service.ConfiguredEngineBlockedByLanguage);
        Assert.Equal("EchokrauTTS download URL not configured", service.CurrentInstallStatus);
    }

    [Fact]
    public void LanguageBlockedTexts_AreTranslated()
        => LocAssert.Translated([EchokrauTtsInstanceService.LanguageBlockedFormat, TtsEngineAvailability.LanguageUnavailableFormat]);

    // --- Why a launch ended: the installer's error file (Error.EchokrauTTS.json) ---

    private static string WriteErrorFile(string content)
    {
        var dir = Directory.CreateTempSubdirectory("ek-launch-error-").FullName;
        var path = Path.Join(dir, EchokrauTtsInstanceService.ErrorFileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ErrorFileName_IsTheOneTheInstallerWrites()
        => Assert.Equal("Error.EchokrauTTS.json", EchokrauTtsInstanceService.ErrorFileName);

    [Fact]
    public void ReadLaunchError_ReturnsTheWrappersMessage()
    {
        // Exactly what the installer's WrapperErrorFile.Write produces.
        var path = WriteErrorFile("{\"key\":\"boot.unsupported_language\",\"message\":\"Pocket TTS hat kein Modell für Sprache ja (verfügbar: de, en). Wähle eine andere Engine oder Sprache.\"}");

        Assert.Equal("Pocket TTS hat kein Modell für Sprache ja (verfügbar: de, en). Wähle eine andere Engine oder Sprache.",
            EchokrauTtsInstanceService.ReadLaunchError(path));
    }

    [Fact]
    public void ReadLaunchError_WithoutMessage_FallsBackToTheKey()
        => Assert.Equal("boot.unexpected",
            EchokrauTtsInstanceService.ReadLaunchError(WriteErrorFile("{\"key\":\"boot.unexpected\",\"message\":\"  \"}")));

    [Theory]
    [InlineData("{\"key\":\"\",\"message\":\"\"}")]
    [InlineData("{not json")]
    [InlineData("")]
    public void ReadLaunchError_UnusableFile_IsNull(string content)
        => Assert.Null(EchokrauTtsInstanceService.ReadLaunchError(WriteErrorFile(content)));

    [Fact]
    public void ReadLaunchError_NoFile_IsNull()
        => Assert.Null(EchokrauTtsInstanceService.ReadLaunchError(
            Path.Join(Path.GetTempPath(), Path.GetRandomFileName(), EchokrauTtsInstanceService.ErrorFileName)));

    [Theory]
    [InlineData(WrapperLaunchMode.Start)]
    [InlineData(WrapperLaunchMode.Install)]
    [InlineData(WrapperLaunchMode.Update)]
    public void ExitStatus_WithAReason_ShowsIt_InEveryMode(WrapperLaunchMode mode)
        => Assert.Equal(string.Format(Loc.S(EchokrauTtsInstanceService.LaunchFailedFormat), "kein Modell"),
            EchokrauTtsInstanceService.ExitStatus(mode, "kein Modell"));

    [Fact]
    public void ExitStatus_PlainStartWithoutReason_PointsAtTheInstallerLog()
        => Assert.Equal(Loc.S(EchokrauTtsInstanceService.StoppedWithoutReason),
            EchokrauTtsInstanceService.ExitStatus(WrapperLaunchMode.Start, null));

    [Fact]
    public void ExitStatus_InstallWithoutReason_SaysWhatDidNotComplete()
        => Assert.Equal("Failed: install did not complete", EchokrauTtsInstanceService.ExitStatus(WrapperLaunchMode.Install, null));

    [Fact]
    public void ExitStatus_StoppedOnPurpose_IsNoFailure()
    {
        Assert.Equal(string.Empty, EchokrauTtsInstanceService.ExitStatus(WrapperLaunchMode.Start, "stale reason", stoppedOnPurpose: true));
        Assert.Equal("Failed: update did not complete",
            EchokrauTtsInstanceService.ExitStatus(WrapperLaunchMode.Update, "stale reason", stoppedOnPurpose: true));
    }

    [Fact]
    public void LaunchFailureTexts_AreTranslated()
        => LocAssert.Translated([EchokrauTtsInstanceService.LaunchFailedFormat, EchokrauTtsInstanceService.StoppedWithoutReason]);
}
