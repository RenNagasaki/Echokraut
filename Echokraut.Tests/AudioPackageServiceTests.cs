using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// End-to-end coverage of the audio package: a sending install exports its generated audio, a
/// second (empty) install imports it and ends up with the character, the dialog line and a
/// generation row pointing at the extracted file.
/// </summary>
public class AudioPackageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly Install _sender;
    private readonly Install _receiver;

    public AudioPackageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ek-package-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _sender = new Install(Path.Combine(_root, "sender"));
        _receiver = new Install(Path.Combine(_root, "receiver"));
    }

    public void Dispose()
    {
        _sender.Dispose();
        _receiver.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { /* temp dir, best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>One plugin installation: its own database plus its own audio folder.</summary>
    private sealed class Install : IDisposable
    {
        public readonly SqliteConnection Connection;
        public readonly EchokrautDbContext Context;
        public readonly DatabaseService Db;
        public readonly Configuration Config;
        public readonly AudioPackageService Service;

        public Install(string saveLocation)
        {
            Directory.CreateDirectory(saveLocation);
            Connection = new SqliteConnection("DataSource=:memory:");
            Connection.Open();
            Context = new EchokrautDbContext(new DbContextOptionsBuilder<EchokrautDbContext>()
                .UseSqlite(Connection).Options);
            Db = new DatabaseService(new Mock<ILogService>().Object, Context);
            Config = new Configuration { LocalSaveLocation = saveLocation };
            Service = new AudioPackageService(Db, Config, new Mock<ILogService>().Object);
        }

        public void Dispose()
        {
            Db.Dispose();
            Connection.Dispose();
        }
    }

    /// <summary>
    /// Seeds a character + clip + generation and writes the matching (dummy) WAV to disk, exactly
    /// as a real generation would have left it.
    /// </summary>
    private static int SeedGeneratedClip(Install install, string npcName, string text,
        string voiceKey = "Male_All_NPC001.wav", bool hasPlaceholder = false, int aliasGender = 0,
        int language = 1)
    {
        var character = install.Db.UpsertCharacter(new CharacterEntity
        {
            Name = npcName,
            Gender = (int)Genders.Male,
            Race = (int)NpcRaces.Hyur,
            RaceStr = "Hyur",
            ObjectKind = (int)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc,
            Language = language,
        });

        var clip = install.Db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 4242,
            TextSource = (int)TextSource.AddonTalk,
            Language = language,
            VoiceKey = voiceKey,
            OriginalText = text,
            CleanedText = text,
            HasPlayerPlaceholder = hasPlaceholder,
            QuestType = 1,
        });

        var folder = Path.Combine(install.Config.LocalSaveLocation, npcName);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{Math.Abs(text.GetHashCode())}_{aliasGender}.wav");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });

        install.Db.LogVoiceClipGeneration(clip.Id, aliasGender == 0 && hasPlaceholder ? 12345L : 0L,
            "Tester", file, voiceKey, aliasGender);
        return clip.Id;
    }

    // ── Export ──────────────────────────────────────────────

    [Fact]
    public async Task Export_WritesManifestAndAudioIntoTheZip()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met, friend.");
        var package = Path.Combine(_root, $"pack{AudioPackageFormat.Extension}");

        var result = await _sender.Service.ExportAsync(package, null);

        Assert.Null(result.Error);
        Assert.Equal(1, result.Exported);
        Assert.True(File.Exists(package));

        using var zip = ZipFile.OpenRead(package);
        Assert.NotNull(zip.GetEntry(AudioPackageFormat.ManifestName));
        Assert.Single(zip.Entries.Where(e => e.FullName.StartsWith($"{AudioPackageFormat.AudioFolder}/")));
    }

    [Fact]
    public async Task Export_SkipsGenerationsThatSpeakTheExportersOwnName()
    {
        // Placeholder clip generated for the real player — carries the sender's character name.
        SeedGeneratedClip(_sender, "Alphinaud", "Well met, -PlayerFirstName-.", hasPlaceholder: true);
        var package = Path.Combine(_root, $"personal{AudioPackageFormat.Extension}");

        var result = await _sender.Service.ExportAsync(package, null);

        Assert.Equal(0, result.Exported);
        Assert.Equal(1, result.SkippedPersonal);
    }

    [Fact]
    public async Task Export_IncludesTheShareableAliasVariantOfAPlaceholderClip()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met, -PlayerFirstName-.",
            hasPlaceholder: true, aliasGender: 1);
        var package = Path.Combine(_root, $"alias{AudioPackageFormat.Extension}");

        var result = await _sender.Service.ExportAsync(package, null);

        Assert.Equal(1, result.Exported);
        Assert.Equal(0, result.SkippedPersonal);
    }

    [Fact]
    public async Task Export_LanguageFilter_OnlyPacksThatLanguage()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met.", language: 1);
        SeedGeneratedClip(_sender, "Alphinaud", "Sei gegrüßt.", language: 2);
        var package = Path.Combine(_root, $"de{AudioPackageFormat.Extension}");

        var result = await _sender.Service.ExportAsync(package, language: 2);

        Assert.Equal(1, result.Exported);
    }

    [Fact]
    public async Task Export_CountsRowsWhoseAudioFileIsGone()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met.");
        foreach (var file in Directory.EnumerateFiles(_sender.Config.LocalSaveLocation, "*.wav",
                     SearchOption.AllDirectories))
            File.Delete(file);

        var result = await _sender.Service.ExportAsync(
            Path.Combine(_root, $"gone{AudioPackageFormat.Extension}"), null);

        Assert.Equal(0, result.Exported);
        Assert.Equal(1, result.SkippedMissingFile);
    }

    // ── Import ──────────────────────────────────────────────

    [Fact]
    public async Task Import_CreatesCharacterClipGenerationAndFile()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met, friend.");
        var package = Path.Combine(_root, $"roundtrip{AudioPackageFormat.Extension}");
        await _sender.Service.ExportAsync(package, null);

        var result = await _receiver.Service.ImportAsync(package);

        Assert.Null(result.Error);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.CreatedCharacters);
        Assert.Equal(1, result.CreatedClips);
        Assert.Equal(0, result.Failed);

        var character = _receiver.Db.FindCharacter("Alphinaud", Genders.Male, NpcRaces.Hyur, 1);
        Assert.NotNull(character);

        var clips = _receiver.Db.GetVoiceClipsForCharacter(character!.Id);
        var clip = Assert.Single(clips);
        Assert.Equal("Well met, friend.", clip.OriginalText);

        var generation = _receiver.Db.GetVoiceClipGeneration(clip.Id, 0L);
        Assert.NotNull(generation);
        Assert.Equal("Male_All_NPC001.wav", generation!.VoiceKey);
        Assert.True(File.Exists(generation.SavePath));
        Assert.StartsWith(_receiver.Config.LocalSaveLocation, generation.SavePath);
    }

    [Fact]
    public async Task Import_DoesNotCarryOverThePlayerContentId()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met, -PlayerFirstName-.",
            hasPlaceholder: true, aliasGender: 2);
        var package = Path.Combine(_root, $"aliasimport{AudioPackageFormat.Extension}");
        await _sender.Service.ExportAsync(package, null);

        await _receiver.Service.ImportAsync(package);

        var character = _receiver.Db.FindCharacter("Alphinaud", Genders.Male, NpcRaces.Hyur, 1);
        var clip = Assert.Single(_receiver.Db.GetVoiceClipsForCharacter(character!.Id));
        Assert.NotNull(_receiver.Db.GetVoiceClipGeneration(clip.Id, 0L, aliasGender: 2));
    }

    [Fact]
    public async Task Import_KeepsExistingLocalAudioAndStillRecordsTheClip()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met, friend.");
        var package = Path.Combine(_root, $"collide{AudioPackageFormat.Extension}");
        await _sender.Service.ExportAsync(package, null);

        // Receiver already has a file at exactly that relative path, with different content.
        using (var zip = ZipFile.OpenRead(package))
        {
            var audioEntry = zip.Entries.First(e => e.FullName.StartsWith($"{AudioPackageFormat.AudioFolder}/"));
            var relative = audioEntry.FullName[(AudioPackageFormat.AudioFolder.Length + 1)..];
            var target = Path.Combine(_receiver.Config.LocalSaveLocation,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, new byte[] { 9, 9, 9, 9, 9, 9 });
        }

        var result = await _receiver.Service.ImportAsync(package);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.SkippedExistingFile);

        var character = _receiver.Db.FindCharacter("Alphinaud", Genders.Male, NpcRaces.Hyur, 1);
        var clip = Assert.Single(_receiver.Db.GetVoiceClipsForCharacter(character!.Id));
        var generation = _receiver.Db.GetVoiceClipGeneration(clip.Id, 0L);
        Assert.NotNull(generation);
        // The receiver's own audio survived untouched.
        Assert.Equal(6, new FileInfo(generation!.SavePath).Length);
    }

    [Fact]
    public async Task Import_TwiceIsIdempotent()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met, friend.");
        var package = Path.Combine(_root, $"twice{AudioPackageFormat.Extension}");
        await _sender.Service.ExportAsync(package, null);

        await _receiver.Service.ImportAsync(package);
        var second = await _receiver.Service.ImportAsync(package);

        Assert.Equal(1, second.Imported);
        Assert.Equal(0, second.CreatedCharacters);
        Assert.Equal(0, second.CreatedClips);

        var character = _receiver.Db.FindCharacter("Alphinaud", Genders.Male, NpcRaces.Hyur, 1);
        Assert.Single(_receiver.Db.GetVoiceClipsForCharacter(character!.Id));
    }

    [Fact]
    public async Task Import_UnknownFile_ReportsAnError()
    {
        var result = await _receiver.Service.ImportAsync(Path.Combine(_root, "does-not-exist.ekpack"));

        Assert.NotNull(result.Error);
        Assert.Equal(0, result.Imported);
    }

    [Fact]
    public async Task Import_ZipWithoutManifest_ReportsAnError()
    {
        var notAPackage = Path.Combine(_root, "plain.zip");
        using (var fs = new FileStream(notAPackage, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        var result = await _receiver.Service.ImportAsync(notAPackage);

        Assert.NotNull(result.Error);
        Assert.Contains(AudioPackageFormat.ManifestName, result.Error);
    }

    [Fact]
    public async Task Import_NewerFormatVersion_IsRefusedInsteadOfGuessed()
    {
        var future = Path.Combine(_root, $"future{AudioPackageFormat.Extension}");
        using (var fs = new FileStream(future, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(AudioPackageFormat.ManifestName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{\"formatVersion\":99,\"entries\":[]}");
        }

        var result = await _receiver.Service.ImportAsync(future);

        Assert.NotNull(result.Error);
        Assert.Contains("99", result.Error);
    }

    [Fact]
    public async Task Import_WithoutASaveLocation_ReportsAnError()
    {
        SeedGeneratedClip(_sender, "Alphinaud", "Well met, friend.");
        var package = Path.Combine(_root, $"nosave{AudioPackageFormat.Extension}");
        await _sender.Service.ExportAsync(package, null);

        _receiver.Config.LocalSaveLocation = "";
        var result = await _receiver.Service.ImportAsync(package);

        Assert.NotNull(result.Error);
        Assert.Equal(0, result.Imported);
    }
}
