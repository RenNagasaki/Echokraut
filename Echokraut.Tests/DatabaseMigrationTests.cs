using System.Linq;
using Echokraut.DataClasses.Database;
using Echokraut.Services;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Schema-migration regression tests. Each migration step is exercised individually via
/// the <c>internal</c> <c>EnsureXxx()</c> method on <see cref="DatabaseService"/> — that
/// way a v_n → v_{n+1} test rolls back ONLY what its specific migration introduces and
/// asserts that re-running just that one step restores the expected schema. Tests don't
/// need to know about other migrations in the chain (no cascading DROPs).
///
/// Plus a small set of full-chain checks that guard the integrating
/// <see cref="DatabaseService.RunSchemaMigrations"/> call: fresh-DB shape, current
/// schema_version, idempotency of repeated init.
///
/// Add tests for each new migration when <see cref="DatabaseService.CurrentSchemaVersion"/>
/// bumps. The shared <see cref="MigrationTestFixture"/> wraps the in-memory SQLite + DbContext
/// + DatabaseService setup and exposes the SQLite reflection helpers (TableExists,
/// ColumnExists, IndexExists, …) that every migration test needs.
/// </summary>
public class DatabaseMigrationTests
{
    // ── Full-chain baseline ─────────────────────────────────────────────

    [Fact]
    public void FreshDb_RecordsCurrentSchemaVersion()
    {
        using var f = new MigrationTestFixture();

        Assert.Equal(DatabaseService.CurrentSchemaVersion, f.ReadSchemaVersion());
    }

    [Fact]
    public void FreshDb_HasAllExpectedTables()
    {
        using var f = new MigrationTestFixture();

        // Every entity-backed table the model defines plus the migration-managed
        // schema_version metadata table. Catches silently-removed entities.
        Assert.True(f.TableExists("characters"));
        Assert.True(f.TableExists("character_contexts"));
        Assert.True(f.TableExists("character_instances"));
        Assert.True(f.TableExists("voice_clips"));
        Assert.True(f.TableExists("voice_clip_generations"));
        Assert.True(f.TableExists("voices"));
        Assert.True(f.TableExists("voice_allowed_genders"));
        Assert.True(f.TableExists("voice_allowed_races"));
        Assert.True(f.TableExists("phonetic_corrections"));
        Assert.True(f.TableExists("lodestone_lookups"));
        Assert.True(f.TableExists("character_speaker_aliases"));   // v2
        Assert.True(f.TableExists("schema_version"));
    }

    [Fact]
    public void RunMigrations_IsIdempotent()
    {
        // Calling InitializeDatabase a second time on the same DB must be safe — the user
        // hot-reloads, plugin reloads, etc. Re-instantiating DatabaseService runs the chain
        // again; everything stays consistent.
        using var f = new MigrationTestFixture();

        // Don't dispose this re-instantiated service — its Dispose() would tear down the
        // shared context+connection that the fixture (and our reflection probes) still need.
        _ = new DatabaseService(new Mock<ILogService>().Object, f.Context);

        Assert.Equal(DatabaseService.CurrentSchemaVersion, f.ReadSchemaVersion());
        Assert.True(f.TableExists("character_speaker_aliases"));
        Assert.True(f.ColumnExists("voice_clip_generations", "voice_key"));
        Assert.True(f.ColumnExists("voice_clips", "wav_file_name"));
        Assert.True(f.IndexExists("IX_voice_clips_character_wav_file_name"));
    }

    // ── v1 → v2: character_speaker_aliases ──────────────────────────────

    [Fact]
    public void EnsureSpeakerAliasTable_CreatesTableWhenMissing()
    {
        using var f = new MigrationTestFixture();

        // Roll the table back to its pre-v2 state. No need to touch v3 — this test only
        // exercises the v1 → v2 migration.
        f.Exec("DROP TABLE character_speaker_aliases");
        Assert.False(f.TableExists("character_speaker_aliases"));

        f.Service.EnsureSpeakerAliasTable();

        Assert.True(f.TableExists("character_speaker_aliases"));
    }

    [Fact]
    public void EnsureSpeakerAliasTable_CreatesBothIndexes()
    {
        using var f = new MigrationTestFixture();

        f.Exec("DROP TABLE character_speaker_aliases");
        f.Service.EnsureSpeakerAliasTable();

        // Unique (character_id, language, alias) — backs the upsert dedup in
        // PersistLinkedDialogs. Lookup (language, alias) — backs the live alias resolver
        // in VoiceMessageProcessor. Both critical for harvest + runtime correctness.
        Assert.True(f.IndexExists("IX_character_speaker_aliases_character_language_alias"));
        Assert.True(f.IndexExists("IX_character_speaker_aliases_language_alias"));
    }

    [Fact]
    public void EnsureSpeakerAliasTable_IsIdempotentWhenTableAlreadyPresent()
    {
        using var f = new MigrationTestFixture();
        // Table is already there from EnsureCreated. Calling the migration again must be
        // a no-op — no exception, no duplicate index errors.
        var ex = Record.Exception(() => f.Service.EnsureSpeakerAliasTable());
        Assert.Null(ex);
        Assert.True(f.TableExists("character_speaker_aliases"));
    }

    // ── v2 → v3: voice_clip_generations.voice_key ──────────────────────

    [Fact]
    public void EnsureVoiceClipGenerationVoiceKey_AddsColumnWhenMissing()
    {
        using var f = new MigrationTestFixture();

        f.Exec("ALTER TABLE voice_clip_generations DROP COLUMN voice_key");
        Assert.False(f.ColumnExists("voice_clip_generations", "voice_key"));

        f.Service.EnsureVoiceClipGenerationVoiceKey();

        Assert.True(f.ColumnExists("voice_clip_generations", "voice_key"));
    }

    [Fact]
    public void EnsureVoiceClipGenerationVoiceKey_IsIdempotentWhenColumnAlreadyPresent()
    {
        using var f = new MigrationTestFixture();
        // Column is already there from EnsureCreated. The migration probes via
        // PRAGMA table_info first and must skip the ALTER — otherwise SQLite throws on
        // duplicate column names.
        var ex = Record.Exception(() => f.Service.EnsureVoiceClipGenerationVoiceKey());
        Assert.Null(ex);
        Assert.True(f.ColumnExists("voice_clip_generations", "voice_key"));
    }

    // ── v3 → v4: voice_clips.wav_file_name + index ────────────────────

    [Fact]
    public void EnsureVoiceClipWavFileName_AddsColumnWhenMissing()
    {
        using var f = new MigrationTestFixture();

        // The index has to be dropped first — SQLite's ALTER TABLE DROP COLUMN refuses
        // when an index references the column.
        f.Exec("DROP INDEX IF EXISTS IX_voice_clips_character_wav_file_name");
        f.Exec("ALTER TABLE voice_clips DROP COLUMN wav_file_name");
        Assert.False(f.ColumnExists("voice_clips", "wav_file_name"));

        f.Service.EnsureVoiceClipWavFileName();

        Assert.True(f.ColumnExists("voice_clips", "wav_file_name"));
        Assert.True(f.IndexExists("IX_voice_clips_character_wav_file_name"));
    }

    [Fact]
    public void EnsureVoiceClipWavFileName_IsIdempotentWhenColumnAlreadyPresent()
    {
        using var f = new MigrationTestFixture();
        // Both column and index are already there from EnsureCreated + the chain run by the
        // fixture. PRAGMA-probe must skip the ALTER, IF NOT EXISTS must skip the index.
        var ex = Record.Exception(() => f.Service.EnsureVoiceClipWavFileName());
        Assert.Null(ex);
        Assert.True(f.ColumnExists("voice_clips", "wav_file_name"));
        Assert.True(f.IndexExists("IX_voice_clips_character_wav_file_name"));
    }

    [Fact]
    public void EnsureVoiceClipWavFileName_BackfillsIndexWhenColumnExistsButIndexMissing()
    {
        // Defensive: an interrupted v3 → v4 migration could leave the column without
        // its companion index. Re-running the migration must add the index even though
        // the column-add itself is a no-op.
        using var f = new MigrationTestFixture();

        f.Exec("DROP INDEX IF EXISTS IX_voice_clips_character_wav_file_name");
        Assert.False(f.IndexExists("IX_voice_clips_character_wav_file_name"));
        Assert.True(f.ColumnExists("voice_clips", "wav_file_name"));

        f.Service.EnsureVoiceClipWavFileName();

        Assert.True(f.IndexExists("IX_voice_clips_character_wav_file_name"));
    }

    [Fact]
    public void EnsureVoiceClipWavFileName_PreservesExistingRows()
    {
        // Same data-preservation guarantee as the v2 → v3 migration: existing voice_clips
        // rows survive the ALTER TABLE ADD COLUMN unchanged, with the new column defaulting
        // to empty string for legacy data.
        using var f = new MigrationTestFixture();

        var character = new CharacterEntity { Name = "Test", Language = 1, Gender = 0, Race = 0 };
        f.Context.Characters.Add(character);
        f.Context.SaveChanges();

        f.Context.VoiceClips.Add(new VoiceClipEntity
        {
            CharacterId = character.Id,
            OriginalText = "hello world",
            CleanedText = "hello world",
            VoiceKey = "VoiceA",
        });
        f.Context.SaveChanges();
        f.Context.ChangeTracker.Clear();

        f.Exec("DROP INDEX IF EXISTS IX_voice_clips_character_wav_file_name");
        f.Exec("ALTER TABLE voice_clips DROP COLUMN wav_file_name");
        f.Service.EnsureVoiceClipWavFileName();

        var rows = f.Context.VoiceClips.AsNoTracking().ToList();
        Assert.Single(rows);
        Assert.Equal("hello world", rows[0].OriginalText);
        Assert.Equal("VoiceA", rows[0].VoiceKey);
        Assert.Equal("", rows[0].WavFileName);
    }

    [Fact]
    public void EnsureVoiceClipGenerationVoiceKey_PreservesExistingRows()
    {
        // Real upgrade scenario: v2 install with logged generations, on next plugin start
        // the migration must add the voice_key column WITHOUT losing any rows. Guards against
        // someone "fixing" the migration by dropping+recreating the table — SQLite's
        // ALTER TABLE ADD COLUMN does in-place augmentation; we want to keep it that way.
        using var f = new MigrationTestFixture();

        var character = new CharacterEntity { Name = "Test", Language = 1, Gender = 0, Race = 0 };
        f.Context.Characters.Add(character);
        f.Context.SaveChanges();

        var clip = new VoiceClipEntity
        {
            CharacterId = character.Id,
            OriginalText = "hello",
            CleanedText = "hello",
            VoiceKey = "OriginalVoice",
        };
        f.Context.VoiceClips.Add(clip);
        f.Context.SaveChanges();

        f.Context.VoiceClipGenerations.Add(new VoiceClipGenerationEntity
        {
            VoiceClipId = clip.Id,
            PlayerContentId = 0,
            PlayerName = "Player",
            SavePath = "/legacy.wav",
        });
        f.Context.SaveChanges();
        f.Context.ChangeTracker.Clear();

        f.Exec("ALTER TABLE voice_clip_generations DROP COLUMN voice_key");
        f.Service.EnsureVoiceClipGenerationVoiceKey();

        var rows = f.Context.VoiceClipGenerations.AsNoTracking().ToList();
        Assert.Single(rows);
        Assert.Equal("/legacy.wav", rows[0].SavePath);
        // Pre-existing row gets the empty-string default — distinguishes "we don't know what
        // voice produced this on-disk file" from a real captured voice key.
        Assert.Equal("", rows[0].VoiceKey);
    }
}

/// <summary>
/// Wraps the in-memory SQLite + DbContext + DatabaseService setup used by migration tests,
/// plus the small reflection helpers (PRAGMA-driven table / column / index probes) that let
/// assertions read the actual SQLite schema rather than the EF model.
/// </summary>
internal sealed class MigrationTestFixture : System.IDisposable
{
    public SqliteConnection Connection { get; }
    public EchokrautDbContext Context { get; }
    public DatabaseService Service { get; }

    public MigrationTestFixture()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var options = new DbContextOptionsBuilder<EchokrautDbContext>()
            .UseSqlite(Connection)
            .Options;
        Context = new EchokrautDbContext(options);

        Service = new DatabaseService(new Mock<ILogService>().Object, Context);
    }

    public void Dispose()
    {
        Service.Dispose();
        Context.Dispose();
        Connection.Dispose();
    }

    public void Exec(string sql) => Context.Database.ExecuteSqlRaw(sql);

    public int ReadSchemaVersion()
    {
        using var cmd = Context.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
        return System.Convert.ToInt32(cmd.ExecuteScalar());
    }

    public bool TableExists(string name)
    {
        using var cmd = Context.Database.GetDbConnection().CreateCommand();
        // Names come from the test source — no injection surface, so plain interpolation
        // beats wiring SqliteCommand parameters for a one-shot read.
        cmd.CommandText = $"SELECT 1 FROM sqlite_master WHERE type='table' AND name='{name}'";
        return cmd.ExecuteScalar() != null;
    }

    public bool IndexExists(string name)
    {
        using var cmd = Context.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM sqlite_master WHERE type='index' AND name='{name}'";
        return cmd.ExecuteScalar() != null;
    }

    public bool ColumnExists(string table, string column)
    {
        using var cmd = Context.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
