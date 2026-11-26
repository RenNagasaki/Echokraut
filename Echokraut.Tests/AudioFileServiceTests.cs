using System.IO;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Coverage for <see cref="AudioFileService"/> file-path helpers — focused on
/// <see cref="IAudioFileService.TryFindExistingLocalAudio"/>, which is consumed by the live
/// path's orphan-WAV adoption (<c>VoiceMessageProcessor.TryLoadCachedAudio</c>).
/// </summary>
public class AudioFileServiceTests : System.IDisposable
{
    private readonly string _tempRoot;
    private readonly AudioFileService _svc;
    private readonly Mock<IGameObjectService> _gameObjects = new();

    public AudioFileServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ek-audiofile-tests-" + System.Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);

        // RemovePlayerNameInText reads LocalPlayerName; nothing in these tests exercises
        // placeholder substitution, but a non-null value avoids NullReferenceException.
        _gameObjects.Setup(g => g.LocalPlayerName).Returns("");
        _svc = new AudioFileService(
            new Mock<ILogService>().Object,
            _gameObjects.Object,
            new Mock<IGoogleDriveSyncService>().Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private VoiceMessage MakeMessage(string speaker, string originalText)
    {
        return new VoiceMessage
        {
            OriginalText = originalText,
            Text = originalText,
            Speaker = new NpcMapData(ObjectKind.BattleNpc) { Name = speaker },
            Source = Echotools.Logging.Enums.TextSource.AddonTalk,
            Language = Dalamud.Game.ClientLanguage.English,
        };
    }

    [Fact]
    public void TryFindExistingLocalAudio_ReturnsPath_WhenWavExists()
    {
        var message = MakeMessage("Y'shtola", "Hello there");
        var expected = _svc.GetLocalAudioPath(_tempRoot, message);
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllBytes(expected, new byte[] { 0x52, 0x49, 0x46, 0x46 }); // "RIFF"

        var found = _svc.TryFindExistingLocalAudio(_tempRoot, message);

        Assert.NotNull(found);
        Assert.Equal(expected, found);
    }

    [Fact]
    public void TryFindExistingLocalAudio_ReturnsNull_WhenWavMissing()
    {
        // Speaker folder exists but no wav under it — must not produce a false positive.
        var message = MakeMessage("Y'shtola", "Some untouched line");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Y'shtola"));

        Assert.Null(_svc.TryFindExistingLocalAudio(_tempRoot, message));
    }

    [Fact]
    public void TryFindExistingLocalAudio_ReturnsNull_WhenLocalSaveLocationEmpty()
    {
        var message = MakeMessage("Y'shtola", "Hello");
        Assert.Null(_svc.TryFindExistingLocalAudio("", message));
        Assert.Null(_svc.TryFindExistingLocalAudio("   ", message));
    }

    [Fact]
    public void TryFindExistingLocalAudio_UsesNoPersonFolder_WhenSpeakerNameEmpty()
    {
        // Mirrors AudioFileService.GetSpeakerAudioPath: empty Speaker.Name → "NOPERSON".
        // A friend's backup of a no-name speaker therefore lives under that folder, and the
        // adopt path must find it.
        var message = MakeMessage("", "Anonymous line");
        var expected = _svc.GetLocalAudioPath(_tempRoot, message);
        Assert.Contains("NOPERSON", expected);
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllBytes(expected, new byte[] { 0x52, 0x49, 0x46, 0x46 });

        Assert.Equal(expected, _svc.TryFindExistingLocalAudio(_tempRoot, message));
    }
}
