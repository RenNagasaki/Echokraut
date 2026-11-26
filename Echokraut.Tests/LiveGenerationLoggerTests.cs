using System;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary><see cref="LiveGenerationLogger"/> against a real (in-memory SQLite) database: the row it leaves is the result.</summary>
public sealed class LiveGenerationLoggerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EchokrautDbContext _context;
    private readonly DatabaseService _db;
    private readonly Mock<IGameObjectService> _gameObjects = new();
    private readonly Mock<ILogService> _log = new();
    private readonly LiveGenerationLogger _sut;
    private readonly int _clipId;

    public LiveGenerationLoggerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new EchokrautDbContext(new DbContextOptionsBuilder<EchokrautDbContext>().UseSqlite(_connection).Options);
        _db = new DatabaseService(_log.Object, _context);
        _sut = new LiveGenerationLogger(_db, _gameObjects.Object, _log.Object);
        _gameObjects.Setup(g => g.LocalPlayerName).Returns("Test Player");

        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Somebody",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male,
        });
        _clipId = _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 1,
            TextSource = 2,
            Language = 1,
            OriginalText = "Hello there.",
        }).Id;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void LogIfApplicable_SkipsWhenVoiceClipIdIsZero()
    {
        // VoiceTest playback and harvest-only clips arrive without a DB row — must not log.
        _sut.LogIfApplicable(0, hasPlayerPlaceholder: false, "C:/audio/test.wav", "VoiceX", new EKEventId(0, TextSource.None));

        Assert.Empty(_context.VoiceClipGenerations);
    }

    [Fact]
    public void LogIfApplicable_NoPlaceholder_UsesPlayerContentIdZero()
    {
        // Most NPC dialog has no <Player> placeholder. UI's GetEffectivePlayerId returns 0 for
        // these — we must store under 0 too, otherwise the manager's "is generated" lookup misses.
        _gameObjects.Setup(g => g.GetEffectivePlayerContentId(false)).Returns(0L);

        _sut.LogIfApplicable(_clipId, hasPlayerPlaceholder: false, "C:/audio/clip.wav", "Female_Hyur_Iceheart", new EKEventId(0, TextSource.AddonTalk));

        var row = _db.GetVoiceClipGeneration(_clipId, 0L);
        Assert.Equal(("Test Player", "C:/audio/clip.wav", "Female_Hyur_Iceheart"), (row?.PlayerName, row?.SavePath, row?.VoiceKey));
        Assert.Equal((int)ReferenceSource.VoicePack, row!.ReferenceSource);
    }

    [Fact]
    public void LogIfApplicable_WithPlaceholder_UsesLocalPlayerContentId()
    {
        // Placeholder clips embed the local player's name, so their generations must be tied
        // to the local player's content id (matching VoiceClipManagerService.GetEffectivePlayerId).
        const long contentId = 0x1122334455667788L;
        _gameObjects.Setup(g => g.GetEffectivePlayerContentId(true)).Returns(contentId);

        _sut.LogIfApplicable(_clipId, hasPlayerPlaceholder: true, "C:/audio/clip.wav", "VoiceX", new EKEventId(0, TextSource.AddonTalk));

        Assert.Equal("C:/audio/clip.wav", _db.GetVoiceClipGeneration(_clipId, contentId)?.SavePath);
        Assert.Null(_db.GetVoiceClipGeneration(_clipId, 0L));
    }

    [Fact]
    public void LogIfApplicable_StoresTheReferenceSource()
    {
        _sut.LogIfApplicable(_clipId, hasPlayerPlaceholder: false, "C:/audio/clip.wav", "VoiceX", new EKEventId(0, TextSource.AddonTalk),
            ReferenceSource.GameVoice);

        Assert.Equal((int)ReferenceSource.GameVoice, _db.GetVoiceClipGeneration(_clipId, 0L)?.ReferenceSource);
    }

    [Fact]
    public void LogIfApplicable_PassesEmptyVoiceKeyWhenNullProvided()
    {
        // Defensive: Speaker.voice can theoretically be null when the live path drops a
        // generation through with no resolved voice. The logger normalises null → "" so the
        // NOT NULL DB column is satisfied.
        _sut.LogIfApplicable(_clipId, hasPlayerPlaceholder: false, "C:/audio/clip.wav", null!, new EKEventId(0, TextSource.AddonTalk));

        Assert.Equal("", _db.GetVoiceClipGeneration(_clipId, 0L)?.VoiceKey);
    }

    [Fact]
    public void LogIfApplicable_SwallowsDbException()
    {
        // Failure injection: the database is the boundary that fails here (locked file).
        var db = new Mock<IDatabaseService>();
        db.Setup(d => d.LogVoiceClipGeneration(
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<ReferenceSource>()))
            .Throws(new InvalidOperationException("DB locked"));
        var sut = new LiveGenerationLogger(db.Object, _gameObjects.Object, _log.Object);

        // Must not throw — failures are best-effort and logged at warning level.
        sut.LogIfApplicable(99, hasPlayerPlaceholder: false, "C:/audio/x.wav", "VoiceX", new EKEventId(0, TextSource.None));

        _log.Verify(l => l.Warning(nameof(LiveGenerationLogger.LogIfApplicable),
            It.Is<string>(m => m.Contains("clipId=99") && m.Contains("DB locked")), It.IsAny<EchoEventId>()), Times.Once);
    }
}
