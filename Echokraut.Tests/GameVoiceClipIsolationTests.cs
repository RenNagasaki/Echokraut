using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Plan game-voice-reference §6: clips generated from a game-voice reference are marked
/// (<c>voice_clip_generations.reference_source</c>, schema v7) and never leave the machine —
/// not in an <c>.ekpack</c> export, not uploaded to Google Drive — and can be deleted in one go.
/// </summary>
public class GameVoiceClipIsolationTests
{
    // ── v6 → v7 migration ───────────────────────────────────────────────

    [Fact]
    public void EnsureReferenceSource_AddsColumnWhenMissing_DefaultingToVoicePack()
    {
        using var f = new MigrationTestFixture();
        var clipId = SeedClip(f.Service);
        f.Service.LogVoiceClipGeneration(clipId, 0, "P", "/a.wav", "v");
        f.Exec("ALTER TABLE voice_clip_generations DROP COLUMN reference_source");
        Assert.False(f.ColumnExists("voice_clip_generations", "reference_source"));

        f.Service.EnsureVoiceClipGenerationReferenceSource();

        Assert.True(f.ColumnExists("voice_clip_generations", "reference_source"));
        Assert.Equal((int)ReferenceSource.VoicePack, f.Service.GetVoiceClipGeneration(clipId, 0)!.ReferenceSource);
    }

    [Fact]
    public void EnsureReferenceSource_IsIdempotent()
    {
        using var f = new MigrationTestFixture();
        Assert.Null(Record.Exception(() => f.Service.EnsureVoiceClipGenerationReferenceSource()));
        Assert.True(f.ColumnExists("voice_clip_generations", "reference_source"));
    }

    // ── Logging / upsert ────────────────────────────────────────────────

    [Fact]
    public void Log_StoresSource_AndUpsertFollowsTheNewestGeneration()
    {
        using var f = new MigrationTestFixture();
        var clipId = SeedClip(f.Service);

        f.Service.LogVoiceClipGeneration(clipId, 0, "P", "/a.wav", "v", referenceSource: ReferenceSource.GameVoice);
        Assert.Equal((int)ReferenceSource.GameVoice, f.Service.GetVoiceClipGeneration(clipId, 0)!.ReferenceSource);

        // Regenerated with the voice pack → same row, file overwritten, mark cleared.
        f.Service.LogVoiceClipGeneration(clipId, 0, "P", "/a.wav", "v");
        Assert.Equal((int)ReferenceSource.VoicePack, f.Service.GetVoiceClipGeneration(clipId, 0)!.ReferenceSource);
    }

    // ── .ekpack export exclusion ────────────────────────────────────────

    [Fact]
    public void ExportRows_ExcludeGameVoiceGenerations()
    {
        using var f = new MigrationTestFixture();
        var packClip = SeedClip(f.Service, "Voice pack line.");
        var gameClip = SeedClip(f.Service, "Game voice line.");
        f.Service.LogVoiceClipGeneration(packClip, 0, "P", "/pack.wav", "v");
        f.Service.LogVoiceClipGeneration(gameClip, 0, "P", "/game.wav", "v", referenceSource: ReferenceSource.GameVoice);

        var rows = f.Service.GetAudioPackageExportRows();

        Assert.Equal(new[] { "/pack.wav" }, rows.Select(r => r.SavePath).ToArray());
    }

    // ── Delete on switch-off ────────────────────────────────────────────

    [Fact]
    public void GetAndDeleteGameVoiceGenerations_TouchOnlyGameVoiceRows()
    {
        using var f = new MigrationTestFixture();
        var packClip = SeedClip(f.Service, "Voice pack line.");
        var gameClip = SeedClip(f.Service, "Game voice line.");
        f.Service.LogVoiceClipGeneration(packClip, 0, "P", "/pack.wav", "v");
        f.Service.LogVoiceClipGeneration(gameClip, 0, "P", "/game.wav", "v", referenceSource: ReferenceSource.GameVoice);

        Assert.Equal(new[] { "/game.wav" }, f.Service.GetGameVoiceGenerations().Select(g => g.SavePath).ToArray());
        Assert.Equal(1, f.Service.DeleteGameVoiceGenerations());

        Assert.Empty(f.Service.GetGameVoiceGenerations());
        Assert.NotNull(f.Service.GetVoiceClipGeneration(packClip, 0));
        Assert.Null(f.Service.GetVoiceClipGeneration(gameClip, 0));
        Assert.Equal(0, f.Service.DeleteGameVoiceGenerations());
    }

    [Fact]
    public void VoiceClipManager_DeleteGameVoiceClips_RemovesFilesAndRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ek-gamevoice-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var game = Path.Combine(dir, "game.wav");
            var pack = Path.Combine(dir, "pack.wav");
            File.WriteAllText(game, "x");
            File.WriteAllText(pack, "x");

            var db = new Mock<IDatabaseService>();
            db.Setup(d => d.GetGameVoiceGenerations()).Returns(new System.Collections.Generic.List<VoiceClipGenerationEntity> { new() { SavePath = game, ReferenceSource = 1 } });
            db.Setup(d => d.DeleteGameVoiceGenerations()).Returns(1);
            var svc = new VoiceClipManagerService(db.Object, new Mock<IBackendService>().Object,
                new Mock<IAudioFileService>().Object, new Mock<IAudioPlaybackService>().Object,
                new Mock<INpcDataService>().Object, new Mock<IGameObjectService>().Object,
                new Mock<ILogService>().Object, new Configuration());
            var updated = 0;
            svc.VoiceClipUpdated += () => updated++;

            Assert.Equal(1, svc.DeleteGameVoiceClips());

            Assert.False(File.Exists(game));
            Assert.True(File.Exists(pack));
            db.Verify(d => d.DeleteGameVoiceGenerations(), Times.Once);
            Assert.Equal(1, updated);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ── Google Drive upload exclusion ───────────────────────────────────

    [Theory]
    [InlineData(ReferenceSource.VoicePack, true)]
    [InlineData(ReferenceSource.GameVoice, false)]
    public async Task WriteStreamToFile_UploadsOnlyVoicePackClips(ReferenceSource source, bool uploaded)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ek-gamevoice-upload-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var gameObjects = new Mock<IGameObjectService>();
            gameObjects.Setup(g => g.LocalPlayerName).Returns("");
            var drive = new Mock<IGoogleDriveSyncService>();
            var calls = 0;
            drive.Setup(d => d.UploadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EKEventId>()))
                .Callback(() => System.Threading.Interlocked.Increment(ref calls))
                .ReturnsAsync(true);
            var svc = new AudioFileService(new Mock<ILogService>().Object, gameObjects.Object, drive.Object);
            var message = new VoiceMessage
            {
                OriginalText = "A line.",
                Text = "A line.",
                Speaker = new NpcMapData(ObjectKind.EventNpc) { Name = "Somebody" },
                Source = Echotools.Logging.Enums.TextSource.AddonTalk,
                Language = Dalamud.Game.ClientLanguage.English,
                ReferenceSource = source,
            };

            Assert.True(await svc.WriteStreamToFile(new EKEventId(0, Echotools.Logging.Enums.TextSource.None), message,
                new MemoryStream(new byte[4800]), dir, googleDriveUpload: true));

            // The upload is fire-and-forget; LastUpload is the one it started (none: already complete).
            await svc.LastUpload.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(uploaded ? 1 : 0, calls);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static int SeedClip(DatabaseService db, string text = "Hello there.")
    {
        var character = db.UpsertCharacter(new CharacterEntity
        {
            Name = "Somebody",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male,
        });
        return db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 1,
            TextSource = 2,
            Language = 1,
            OriginalText = text,
        }).Id;
    }
}
