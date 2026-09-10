using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

/// <inheritdoc cref="IAudioPackageService"/>
public class AudioPackageService : IAudioPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IDatabaseService _db;
    private readonly Configuration _config;
    private readonly ILogService _log;

    public bool IsRunning { get; private set; }
    public event Action<string, int, int>? ProgressChanged;

    public AudioPackageService(IDatabaseService db, Configuration config, ILogService log)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    // ── Export ──────────────────────────────────────────────

    public Task<AudioPackageExportResult> ExportAsync(string packagePath, int? language,
        CancellationToken ct = default)
        => RunAsync(() => ExportCore(packagePath, language, ct),
            () => new AudioPackageExportResult { PackagePath = packagePath, Error = "busy" });

    private AudioPackageExportResult ExportCore(string packagePath, int? language, CancellationToken ct)
    {
        var eventId = new EKEventId(0, TextSource.None);
        var result = new AudioPackageExportResult { PackagePath = packagePath };

        var rows = _db.GetAudioPackageExportRows(language);
        _log.Info(nameof(ExportCore),
            $"Exporting audio package to {packagePath} ({rows.Count} generation rows, language={language?.ToString() ?? "all"})",
            eventId);

        var manifest = new AudioPackageManifest
        {
            CreatedBy = $"Echokraut {Plugin.PluginVersion}",
            CreatedUtc = DateTime.UtcNow,
            Language = language,
        };

        // Same source file can back several manifest entries (a clip and its alias variants share
        // a speaker folder, and a re-import of somebody else's package can point two clips at one
        // file). The zip gets each file once; the manifest may reference it repeatedly.
        var entryNameBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var takenEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var directory = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        try
        {
            using (var fs = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var processed = 0;
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    processed++;
                    if (processed % 25 == 0 || processed == rows.Count)
                        ProgressChanged?.Invoke("Exporting audio package", processed, rows.Count);

                    ExportOne(zip, row, entryNameBySource, takenEntryNames, manifest, result);
                }

                var manifestEntry = zip.CreateEntry(AudioPackageFormat.ManifestName, CompressionLevel.Optimal);
                using var manifestStream = manifestEntry.Open();
                JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
            }

            _log.Info(nameof(ExportCore),
                $"Audio package written: {result.Exported} entries, {result.SkippedMissingFile} missing files, " +
                $"{result.SkippedPersonal} personal generations skipped", eventId);
        }
        catch (OperationCanceledException)
        {
            DeletePartialPackage(packagePath, eventId);
            result.Error = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            DeletePartialPackage(packagePath, eventId);
            _log.Error(nameof(ExportCore), $"Audio package export failed: {ex}", eventId);
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Adds one generation row to the package: the file itself (once per source path) plus its
    /// manifest entry. Everything that disqualifies a row is counted on <paramref name="result"/>.
    /// </summary>
    private static void ExportOne(ZipArchive zip, AudioPackageExportRow row,
        Dictionary<string, string> entryNameBySource, HashSet<string> takenEntryNames,
        AudioPackageManifest manifest, AudioPackageExportResult result)
    {
        if (!AudioPackageRules.IsShareable(row.HasPlayerPlaceholder, row.AliasGender))
        {
            result.SkippedPersonal++;
            return;
        }

        if (string.IsNullOrWhiteSpace(row.SavePath) || !File.Exists(row.SavePath))
        {
            result.SkippedMissingFile++;
            return;
        }

        var entryName = ResolveEntryName(row.SavePath, entryNameBySource, takenEntryNames);
        if (entryName == null)
        {
            result.SkippedMissingFile++;
            return;
        }

        if (entryNameBySource.TryAdd(row.SavePath, entryName))
        {
            var zipEntry = zip.CreateEntry($"{AudioPackageFormat.AudioFolder}/{entryName}",
                CompressionLevel.Optimal);
            using var source = File.OpenRead(row.SavePath);
            using var target = zipEntry.Open();
            source.CopyTo(target);
            result.BytesWritten += source.Length;
        }

        manifest.Entries.Add(ToEntry(row, entryName));
        result.Exported++;
    }

    /// <summary>
    /// Package-relative name for a source file, stable per source path. Two different sources that
    /// collapse to the same speaker/file name get a numeric suffix instead of silently overwriting
    /// each other inside the zip.
    /// </summary>
    private static string? ResolveEntryName(string savePath,
        Dictionary<string, string> entryNameBySource, HashSet<string> taken)
    {
        if (entryNameBySource.TryGetValue(savePath, out var known)) return known;

        var candidate = AudioPackageRules.RelativeEntryPath(savePath);
        if (candidate == null) return null;

        if (taken.Add(candidate)) return candidate;

        var withoutExtension = candidate.Substring(0, candidate.Length - Path.GetExtension(candidate).Length);
        var extension = Path.GetExtension(candidate);
        for (var i = 2; i < 10000; i++)
        {
            var alternative = $"{withoutExtension}_{i}{extension}";
            if (taken.Add(alternative)) return alternative;
        }
        return null;
    }

    private static AudioPackageEntry ToEntry(AudioPackageExportRow row, string entryName) => new()
    {
        File = entryName,
        NpcName = row.NpcName,
        Gender = row.Gender,
        Race = row.Race,
        RaceStr = row.RaceStr,
        ObjectKind = row.ObjectKind,
        Language = row.Language,
        NpcBaseId = row.NpcBaseId,
        TextSource = row.TextSource,
        QuestType = row.QuestType,
        BodyType = row.BodyType,
        OriginalText = row.OriginalText,
        CleanedText = row.CleanedText,
        HasPlayerPlaceholder = row.HasPlayerPlaceholder,
        WavFileName = row.WavFileName,
        ZoneName = row.ZoneName,
        MapX = row.MapX,
        MapY = row.MapY,
        VoiceKey = row.VoiceKey,
        AliasGender = row.AliasGender,
    };

    private void DeletePartialPackage(string packagePath, EKEventId eventId)
    {
        try
        {
            if (File.Exists(packagePath)) File.Delete(packagePath);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(DeletePartialPackage),
                $"Could not remove partial package {packagePath}: {ex.Message}", eventId);
        }
    }

    // ── Import ──────────────────────────────────────────────

    public Task<AudioPackageImportResult> ImportAsync(string packagePath, CancellationToken ct = default)
        => RunAsync(() => ImportCore(packagePath, ct),
            () => new AudioPackageImportResult { Error = "busy" });

    private AudioPackageImportResult ImportCore(string packagePath, CancellationToken ct)
    {
        var eventId = new EKEventId(0, TextSource.None);
        var result = new AudioPackageImportResult();

        if (!File.Exists(packagePath))
        {
            result.Error = $"Package not found: {packagePath}";
            _log.Warning(nameof(ImportCore), result.Error, eventId);
            return result;
        }

        var saveLocation = _config.LocalSaveLocation;
        if (string.IsNullOrWhiteSpace(saveLocation))
        {
            result.Error = "No local save location configured — set it in Settings → Storage first.";
            _log.Warning(nameof(ImportCore), result.Error, eventId);
            return result;
        }

        // Bulk mode: the per-row cache refresh is O(N) and would make a 20k-entry package
        // quadratic. Caches are rebuilt once at the end (also in the failure path).
        var previousBulk = _db.BulkMode;
        var previousSuppress = _db.SuppressEvents;

        try
        {
            using var zip = ZipFile.OpenRead(packagePath);

            var manifest = ReadManifest(zip, eventId);
            if (manifest == null)
            {
                result.Error = $"{AudioPackageFormat.ManifestName} missing or unreadable — not an Echokraut package.";
                return result;
            }

            if (!AudioPackageRules.IsSupportedVersion(manifest.FormatVersion))
            {
                result.Error = $"Package format version {manifest.FormatVersion} is newer than this plugin supports " +
                               $"(max {AudioPackageFormat.MaxSupportedVersion}). Update Echokraut.";
                _log.Warning(nameof(ImportCore), result.Error, eventId);
                return result;
            }

            _log.Info(nameof(ImportCore),
                $"Importing audio package {packagePath}: {manifest.Entries.Count} entries, created by " +
                $"'{manifest.CreatedBy}' at {manifest.CreatedUtc:u}", eventId);

            _db.BulkMode = true;
            _db.SuppressEvents = true;

            var known = new ImportCache();
            var processed = 0;
            foreach (var entry in manifest.Entries)
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                if (processed % 25 == 0 || processed == manifest.Entries.Count)
                    ProgressChanged?.Invoke("Importing audio package", processed, manifest.Entries.Count);

                try
                {
                    ImportOne(zip, entry, saveLocation, known, result);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    _log.Warning(nameof(ImportCore),
                        $"Entry '{entry.File}' failed: {ex.Message}", eventId);
                }
            }

            _log.Info(nameof(ImportCore),
                $"Audio package imported: {result.Imported} clips, {result.CreatedCharacters} new characters, " +
                $"{result.CreatedClips} new dialog lines, {result.SkippedExistingFile} files already present, " +
                $"{result.Failed} failed", eventId);
        }
        catch (OperationCanceledException)
        {
            result.Error = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(ImportCore), $"Audio package import failed: {ex}", eventId);
            result.Error = ex.Message;
        }
        finally
        {
            _db.BulkMode = previousBulk;
            _db.SuppressEvents = previousSuppress;
            _db.FlushChanges();
            _db.RefreshCaches();
            _db.NotifyVoiceClipLogged();
        }

        return result;
    }

    /// <summary>
    /// Per-run memo so a package with thousands of entries doesn't re-query the same character's
    /// clip list once per entry. Keyed by character identity; the clip keys are seeded from the
    /// database on first sight and kept current as entries are inserted.
    /// </summary>
    private sealed class ImportCache
    {
        public readonly Dictionary<string, int> CharacterIds = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, HashSet<string>> ClipKeys = new();
        public readonly HashSet<string> EnsuredContexts = new(StringComparer.Ordinal);

        /// <summary>
        /// Separator for the composite keys below: a character no NPC name or dialog text can
        /// contain, so "Ab"+"c" and "A"+"bc" can never collapse into one key.
        /// </summary>
        private const string Sep = "\u0001";

        public static string CharacterKey(AudioPackageEntry e)
            => $"{e.NpcName}{Sep}{e.Gender}{Sep}{e.Race}{Sep}{e.Language}";

        public static string ClipKey(long npcBaseId, string originalText)
            => $"{npcBaseId}{Sep}{originalText}";
    }

    private void ImportOne(ZipArchive zip, AudioPackageEntry entry, string saveLocation,
        ImportCache known, AudioPackageImportResult result)
    {
        var target = AudioPackageRules.ResolveImportTarget(saveLocation, entry.File);
        if (target == null)
        {
            result.Failed++;
            return;
        }

        var zipEntry = zip.GetEntry($"{AudioPackageFormat.AudioFolder}/{entry.File}");
        if (zipEntry == null)
        {
            result.Failed++;
            return;
        }

        // Never overwrite audio the receiving install generated itself — their file is at least as
        // valid as ours, and the DB row below makes the clip count as generated either way.
        if (File.Exists(target))
        {
            result.SkippedExistingFile++;
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            zipEntry.ExtractToFile(target, overwrite: false);
        }

        var characterKey = ImportCache.CharacterKey(entry);
        if (!known.CharacterIds.TryGetValue(characterKey, out var characterId))
        {
            var existedBefore = _db.FindCharacter(entry.NpcName, (Genders)entry.Gender,
                (NpcRaces)entry.Race, entry.Language) != null;

            var character = _db.UpsertCharacter(new CharacterEntity
            {
                Name = entry.NpcName,
                Gender = entry.Gender,
                Race = entry.Race,
                RaceStr = entry.RaceStr,
                ObjectKind = entry.ObjectKind,
                Language = entry.Language,
                BodyType = entry.BodyType,
                // Never import a voice onto the character row: which voice a receiver's NPC speaks
                // with is their setting. The generation row below records what the audio was made
                // with, which is what playback needs.
                VoiceKey = "",
            });

            characterId = character.Id;
            if (!existedBefore) result.CreatedCharacters++;

            var seed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in _db.GetVoiceClipsForCharacter(characterId, int.MaxValue))
                seed.Add(ImportCache.ClipKey(candidate.NpcBaseId, candidate.OriginalText));

            known.CharacterIds[characterKey] = characterId;
            known.ClipKeys[characterId] = seed;
        }

        // Per (character, context) — one character can appear with dialogue lines AND bubbles, and
        // the Voice Clip Manager lists them by context, so both rows have to exist.
        var contextType = AudioPackageRules.ContextTypeFor(entry.ObjectKind, entry.TextSource);
        if (known.EnsuredContexts.Add($"{characterId}|{contextType}"))
            _db.EnsureContext(characterId, contextType);

        var clipKeys = known.ClipKeys[characterId];
        var clipExisted = !clipKeys.Add(ImportCache.ClipKey(entry.NpcBaseId, entry.OriginalText));

        var clip = _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = characterId,
            NpcBaseId = entry.NpcBaseId,
            TextSource = entry.TextSource,
            Language = entry.Language,
            VoiceKey = entry.VoiceKey,
            OriginalText = entry.OriginalText,
            CleanedText = entry.CleanedText,
            BodyType = entry.BodyType,
            HasPlayerPlaceholder = entry.HasPlayerPlaceholder,
            WavFileName = entry.WavFileName,
            QuestType = entry.QuestType,
            ZoneName = entry.ZoneName,
            MapX = entry.MapX,
            MapY = entry.MapY,
            SavedToDisk = true,
            SavePath = target,
        });
        if (!clipExisted) result.CreatedClips++;

        // Batch mode defers SaveChanges, so a freshly added clip has no id yet — the generation
        // row needs one, hence the flush before writing it.
        if (clip.Id == 0)
        {
            _db.FlushChanges();
        }

        if (clip.Id == 0)
        {
            result.Failed++;
            return;
        }

        _db.LogVoiceClipGeneration(clip.Id, AudioPackageRules.ImportPlayerContentId,
            "", target, entry.VoiceKey, entry.AliasGender);

        result.Imported++;
    }

    private AudioPackageManifest? ReadManifest(ZipArchive zip, EKEventId eventId)
    {
        var manifestEntry = zip.GetEntry(AudioPackageFormat.ManifestName);
        if (manifestEntry == null) return null;

        try
        {
            using var stream = manifestEntry.Open();
            return JsonSerializer.Deserialize<AudioPackageManifest>(stream, JsonOptions);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(ReadManifest), $"Manifest could not be parsed: {ex.Message}", eventId);
            return null;
        }
    }

    // ── Shared run gate ─────────────────────────────────────

    private Task<T> RunAsync<T>(Func<T> body, Func<T> busyResult)
    {
        if (IsRunning) return Task.FromResult(busyResult());

        IsRunning = true;
        return Task.Run(() =>
        {
            try
            {
                return body();
            }
            finally
            {
                IsRunning = false;
            }
        });
    }
}
