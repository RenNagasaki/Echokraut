using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Coverage for <see cref="VoicePackService"/> — the legal replacement for the removed in-game
/// audio feature. Everything here is filesystem + HTTP plumbing, so the tests use a stub
/// handler serving an in-memory zip and a temp output directory.
/// </summary>
public class VoicePackServiceTests : IDisposable
{
    private readonly Mock<ILogService> _log = new();
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "ek-voicepack-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup — a locked file must not fail the test run.
        }
        GC.SuppressFinalize(this);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class StubHandler(byte[] payload, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(payload),
            });
        }
    }

    private static byte[] MakeZip(params (string Path, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    private static Mock<IRemoteUrlService> MakeUrls(string voicePackUrl)
    {
        var mock = new Mock<IRemoteUrlService>();
        mock.SetupGet(u => u.Urls).Returns(new RemoteUrlsData { VoicePackUrl = voicePackUrl });
        return mock;
    }

    private VoicePackService MakeSut(byte[] payload, string url = "https://example.invalid/pack.zip",
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var config = new Configuration { LocalSaveLocation = _tempRoot };
        var http = new HttpClient(new StubHandler(payload, status));
        return new VoicePackService(_log.Object, config, MakeUrls(url).Object, http);
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadAsync_UnpacksSamplesAndLicenseFiles()
    {
        var zip = MakeZip(
            ("Male_01.wav", "wav-bytes"),
            ("Male_01.txt", "the spoken line"),
            ("LICENSE.txt", "CC BY 4.0"),
            ("ATTRIBUTION.txt", "speaker list"));
        var sut = MakeSut(zip);

        await sut.DownloadAsync(CancellationToken.None);

        var outDir = Path.Combine(_tempRoot, "Voices");
        Assert.True(File.Exists(Path.Combine(outDir, "Male_01.wav")));
        Assert.Equal("the spoken line", File.ReadAllText(Path.Combine(outDir, "Male_01.txt")));
        // The license/attribution pair is what keeps redistribution lawful — it must survive.
        Assert.True(File.Exists(Path.Combine(outDir, "LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(outDir, "ATTRIBUTION.txt")));
    }

    [Fact]
    public async Task DownloadAsync_KeepsNestedFolders()
    {
        var sut = MakeSut(MakeZip(("child/Girl_01.wav", "wav")));

        await sut.DownloadAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_tempRoot, "Voices", "child", "Girl_01.wav")));
    }

    [Fact]
    public async Task DownloadAsync_SkipsEntriesEscapingTheTargetFolder()
    {
        // Zip-slip: a crafted pack must never write outside the voice folder.
        var sut = MakeSut(MakeZip(("../escaped.wav", "nope"), ("Male_01.wav", "ok")));

        await sut.DownloadAsync(CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_tempRoot, "escaped.wav")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "Voices", "Male_01.wav")));
    }

    [Fact]
    public async Task DownloadAsync_WithOutputRootOverride_WipesTargetFirst()
    {
        var root = Path.Combine(_tempRoot, "alltalk_tts");
        var voices = Path.Combine(root, "voices");
        Directory.CreateDirectory(voices);
        File.WriteAllText(Path.Combine(voices, "stale.wav"), "leftover");

        var sut = MakeSut(MakeZip(("Male_01.wav", "wav")));
        await sut.DownloadAsync(CancellationToken.None, root, "voices");

        Assert.False(File.Exists(Path.Combine(voices, "stale.wav")));
        Assert.True(File.Exists(Path.Combine(voices, "Male_01.wav")));
    }

    [Fact]
    public async Task DownloadAsync_WithoutOutputRootOverride_KeepsExistingFiles()
    {
        var voices = Path.Combine(_tempRoot, "Voices");
        Directory.CreateDirectory(voices);
        File.WriteAllText(Path.Combine(voices, "mine.wav"), "user file");

        var sut = MakeSut(MakeZip(("Male_01.wav", "wav")));
        await sut.DownloadAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(voices, "mine.wav")));
        Assert.True(File.Exists(Path.Combine(voices, "Male_01.wav")));
    }

    [Fact]
    public async Task DownloadAsync_WithoutConfiguredUrl_SkipsWithoutTouchingDisk()
    {
        var handler = new StubHandler(MakeZip(("Male_01.wav", "wav")));
        var config = new Configuration { LocalSaveLocation = _tempRoot };
        var sut = new VoicePackService(_log.Object, config, MakeUrls(string.Empty).Object,
            new HttpClient(handler));

        await sut.DownloadAsync(CancellationToken.None);

        Assert.Equal(0, handler.Calls);
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "Voices")));
    }

    [Fact]
    public async Task DownloadAsync_HttpFailure_PropagatesAndLeavesNoPartialOutput()
    {
        var sut = MakeSut([], status: HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => sut.DownloadAsync(CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "Voices")));
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgressAndClearsIsRunning()
    {
        var sut = MakeSut(MakeZip(("Male_01.wav", "wav"), ("Male_01.txt", "line")));
        var labels = new System.Collections.Generic.List<string>();
        sut.ProgressChanged += (label, _, _) => labels.Add(label);

        Assert.False(sut.IsRunning);
        await sut.DownloadAsync(CancellationToken.None);

        Assert.False(sut.IsRunning);
        Assert.Contains(labels, l => l.Contains("Unpacking", StringComparison.Ordinal));
        Assert.Contains(labels, l => l.Contains("Done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadAsync_CancelledBeforeStart_DoesNotWriteOutput()
    {
        var sut = MakeSut(MakeZip(("Male_01.wav", "wav")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.DownloadAsync(cts.Token));

        Assert.False(File.Exists(Path.Combine(_tempRoot, "Voices", "Male_01.wav")));
    }

    // ── Filename grammar (drives gender/race/body-type auto-assignment) ──────

    [Theory]
    [InlineData("Male_All_NPC001")]
    [InlineData("Female_All_NPC012")]
    [InlineData("Female_All-Child_NPC091")]
    [InlineData("Male_All-Elder_NPC120")]
    [InlineData("male_hyur_Tataru")]
    public void IsWellFormedVoiceName_AcceptsConventionalNames(string name)
        => Assert.True(VoicePackService.IsWellFormedVoiceName(name));

    [Theory]
    [InlineData("voice_m_07")]          // no gender segment
    [InlineData("Male_01")]             // only two segments, no race pool
    [InlineData("NPC001")]              // no segments at all
    [InlineData("Neutral_All_NPC001")]  // not a Genders value
    [InlineData("Hyur_Male_NPC001")]    // gender and race swapped
    public void IsWellFormedVoiceName_RejectsNamesThatLoseTheirFilter(string name)
        => Assert.False(VoicePackService.IsWellFormedVoiceName(name));

    [Theory]
    [InlineData("Male_01_NPC001")]
    [InlineData("01_All_NPC001")]
    public void IsWellFormedVoiceName_RejectsNumericSegments(string name)
    {
        // Enum.TryParse accepts "01" as the enum value 1, so a numeric segment would be
        // silently filed as Genders.Female / NpcRaces.Hyur instead of failing loudly.
        Assert.False(VoicePackService.IsWellFormedVoiceName(name));
    }

    [Theory]
    [InlineData("Male_All_NPC001", true)]
    [InlineData("Male_All_M01", false)]   // parses fine, but UseAsRandom stays false
    [InlineData("Male_All_npc001", false)] // the check in MapVoices is case-sensitive
    public void IsRandomPoolName_MatchesMapVoicesRule(string name, bool expected)
        => Assert.Equal(expected, VoicePackService.IsRandomPoolName(name));

    [Fact]
    public async Task DownloadAsync_MisnamedSamples_LogsAWarning()
    {
        var sut = MakeSut(MakeZip(("voice_m_07.wav", "wav"), ("Male_All_NPC001.wav", "wav")));

        await sut.DownloadAsync(CancellationToken.None);

        _log.Verify(l => l.Warning(
                It.IsAny<string>(),
                It.Is<string>(m => m.Contains("naming convention", StringComparison.Ordinal)),
                It.IsAny<Echotools.Logging.DataClasses.EchoEventId>()),
            Times.Once);
    }

    [Fact]
    public async Task DownloadAsync_SamplesWithoutNpcMarker_LogsAWarning()
    {
        // Well-formed but never auto-assigned — the silent-inert case that motivated the check.
        var sut = MakeSut(MakeZip(("Male_All_M01.wav", "wav")));

        await sut.DownloadAsync(CancellationToken.None);

        _log.Verify(l => l.Warning(
                It.IsAny<string>(),
                It.Is<string>(m => m.Contains("UseAsRandom", StringComparison.Ordinal)),
                It.IsAny<Echotools.Logging.DataClasses.EchoEventId>()),
            Times.Once);
    }
}
