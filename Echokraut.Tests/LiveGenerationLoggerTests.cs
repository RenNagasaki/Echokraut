using Echokraut.DataClasses;
using Echokraut.Services;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

public class LiveGenerationLoggerTests
{
    private readonly Mock<IDatabaseService> _db = new();
    private readonly Mock<IGameObjectService> _gameObjects = new();
    private readonly Mock<ILogService> _log = new();
    private readonly LiveGenerationLogger _sut;

    public LiveGenerationLoggerTests()
    {
        _sut = new LiveGenerationLogger(_db.Object, _gameObjects.Object, _log.Object);
    }

    [Fact]
    public void LogIfApplicable_SkipsWhenVoiceClipIdIsZero()
    {
        // VoiceTest playback and harvest-only clips arrive without a DB row — must not log.
        _sut.LogIfApplicable(0, hasPlayerPlaceholder: false, "C:/audio/test.wav", "VoiceX", new EKEventId(0, TextSource.None));

        _db.Verify(d => d.LogVoiceClipGeneration(
            It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public void LogIfApplicable_NoPlaceholder_UsesPlayerContentIdZero()
    {
        // Most NPC dialog has no <Player> placeholder. UI's GetEffectivePlayerId returns 0 for
        // these — we must store under 0 too, otherwise the manager's "is generated" lookup misses.
        _gameObjects.Setup(g => g.GetEffectivePlayerContentId(false)).Returns(0L);
        _gameObjects.Setup(g => g.LocalPlayerName).Returns("Test Player");

        _sut.LogIfApplicable(42, hasPlayerPlaceholder: false, "C:/audio/clip.wav", "Female_Hyur_Iceheart", new EKEventId(0, TextSource.AddonTalk));

        _db.Verify(d => d.LogVoiceClipGeneration(42, 0L, "Test Player", "C:/audio/clip.wav", "Female_Hyur_Iceheart", 0),
            Times.Once);
    }

    [Fact]
    public void LogIfApplicable_WithPlaceholder_UsesLocalPlayerContentId()
    {
        // Placeholder clips embed the local player's name, so their generations must be tied
        // to the local player's content id (matching VoiceClipManagerService.GetEffectivePlayerId).
        const ulong contentId = 0x1122334455667788UL;
        _gameObjects.Setup(g => g.GetEffectivePlayerContentId(true)).Returns((long)contentId);
        _gameObjects.Setup(g => g.LocalPlayerName).Returns("Test Player");

        _sut.LogIfApplicable(42, hasPlayerPlaceholder: true, "C:/audio/clip.wav", "VoiceX", new EKEventId(0, TextSource.AddonTalk));

        _db.Verify(d => d.LogVoiceClipGeneration(42, (long)contentId, "Test Player", "C:/audio/clip.wav", "VoiceX", 0),
            Times.Once);
    }

    [Fact]
    public void LogIfApplicable_PassesEmptyVoiceKeyWhenNullProvided()
    {
        // Defensive: Speaker.voice can theoretically be null when the live path drops a
        // generation through with no resolved voice. The logger normalises null → "" so the
        // NOT NULL DB column is satisfied.
        _gameObjects.Setup(g => g.LocalPlayerName).Returns("Test Player");

        _sut.LogIfApplicable(42, hasPlayerPlaceholder: false, "C:/audio/clip.wav", null!, new EKEventId(0, TextSource.AddonTalk));

        _db.Verify(d => d.LogVoiceClipGeneration(42, 0L, "Test Player", "C:/audio/clip.wav", "", 0),
            Times.Once);
    }

    [Fact]
    public void LogIfApplicable_SwallowsDbException()
    {
        _db.Setup(d => d.LogVoiceClipGeneration(
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Throws(new System.InvalidOperationException("DB locked"));

        // Must not throw — failures are best-effort and logged at warning level.
        var ex = Record.Exception(() =>
            _sut.LogIfApplicable(99, hasPlayerPlaceholder: false, "C:/audio/x.wav", "VoiceX", new EKEventId(0, TextSource.None)));
        Assert.Null(ex);
    }
}
