using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Helper.Functional.Scd;
using Echotools.Logging.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Echokraut.Services;

/// <summary>
/// Builds a per-NPC voice sample folder from FFXIV's built-in voice acting (.scd files).
/// See <c>plans/game-data-tools-window.md</c> for the full design and phase list.
/// </summary>
public class VoiceSampleExtractorService : IVoiceSampleExtractorService
{
    private const double MinSeconds = 6.0;
    private const double MaxSeconds = 12.0;
    private const int CatalogClipThreshold = 20;
    private const int TargetSampleRate = 22050;

    /// <summary>FFXIV expansion sub-paths used in <c>cut/{exp}/sound/...</c>. Tools mirrors
    /// this. We try each in order until the first SCD resolves.</summary>
    private static readonly string[] ExpansionKeys =
    {
        "ffxiv", "ex1", "ex2", "ex3", "ex4", "ex5",
    };

    private readonly IDataManager _dataManager;
    private readonly IClientState _clientState;
    private readonly ILogService _log;
    private readonly IJsonDataService _jsonData;
    private readonly Configuration _config;

    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;
    public event Action<string, int, int>? ProgressChanged;

    public VoiceSampleExtractorService(
        IDataManager dataManager,
        IClientState clientState,
        ILogService log,
        IJsonDataService jsonData,
        Configuration config)
    {
        _dataManager = dataManager;
        _clientState = clientState;
        _log = log;
        _jsonData = jsonData;
        _config = config;
    }

    public async Task RunAsync(ClientLanguage language, int samplesPerNpc, CancellationToken ct)
    {
        if (_isRunning) return;
        _isRunning = true;
        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            await Task.Run(() => RunInternal(language, samplesPerNpc, ct, eventId), ct);
        }
        catch (OperationCanceledException)
        {
            _log.Info(nameof(RunAsync), "Voice sample extraction cancelled by user", eventId);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(RunAsync), $"Voice sample extraction failed: {ex.Message}", eventId);
        }
        finally
        {
            _isRunning = false;
        }
    }

    private void RunInternal(ClientLanguage language, int samplesPerNpc, CancellationToken ct, EKEventId eventId)
    {
        samplesPerNpc = Math.Clamp(samplesPerNpc, 1, 5);
        var outputRoot = _config.LocalSaveLocation;
        Directory.CreateDirectory(Path.Combine(outputRoot, "FF14-Voices"));

        // ── Phase 1+2: harvest text-key → speaker-shortname, then resolve to NpcId ──
        ProgressChanged?.Invoke("Building NPC name index", 0, 1);
        var npcNames = LoadEnglishNpcNames(language);
        var nameIndex = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        _log.Info(nameof(RunInternal),
            $"NPC name index: {nameIndex.Count} entries (across {npcNames.Count} NPCs)",
            eventId);

        ProgressChanged?.Invoke("Scanning voice text sheets", 0, 1);
        var perNpcKeys = new Dictionary<uint, List<VoiceClipCandidate>>();
        var unmatched = new Dictionary<string, UnmatchedShortName>(StringComparer.OrdinalIgnoreCase);
        var totalKeys = 0;

        foreach (var (sheetName, textKey, text) in IterateVoiceTextRows(language, ct))
        {
            ct.ThrowIfCancellationRequested();
            totalKeys++;
            if (totalKeys % 5000 == 0)
                ProgressChanged?.Invoke($"Scanning voice text  {totalKeys} keys", totalKeys, 0);

            if (!VoiceExtractKey.TryParse(textKey, out var shortName, out var audioBase)) continue;
            var cleaned = VoiceExtractTextCleaner.Clean(text, language);
            if (cleaned == null || cleaned.Length == 0 || string.IsNullOrWhiteSpace(cleaned[0])) continue;

            var resolved = VoiceExtractKey.Resolve(shortName, nameIndex);
            if (resolved == null || resolved.Count == 0)
            {
                if (!unmatched.TryGetValue(shortName, out var unm))
                    unmatched[shortName] = unm = new UnmatchedShortName { ShortName = shortName };
                unm.Count++;
                if (unm.Examples.Count < 3)
                    unm.Examples.Add(new UnmatchedExample { TextKey = textKey, Text = cleaned[0] });
                continue;
            }

            var npcId = resolved[0];
            if (resolved.Count > 1)
                _log.Debug(nameof(RunInternal),
                    $"Shortname '{shortName}' maps to {resolved.Count} NPCs " +
                    $"[{string.Join(",", resolved)}], using first ({npcId})",
                    eventId);

            if (!perNpcKeys.TryGetValue(npcId, out var list))
                perNpcKeys[npcId] = list = new List<VoiceClipCandidate>();
            list.Add(new VoiceClipCandidate
            {
                TextKey = textKey,
                AudioFileBase = audioBase,
                Sheet = sheetName,
                MaleText = cleaned[0],
                FemaleText = cleaned.Length > 1 ? cleaned[1] : null,
            });
        }

        _log.Info(nameof(RunInternal),
            $"Voice scan: {totalKeys} keys, {perNpcKeys.Count} matched NPCs, " +
            $"{unmatched.Count} unmatched shortnames",
            eventId);

        WriteUnmatchedJson(outputRoot, unmatched, eventId);

        // ── Phases 3-7: per NPC, decode + length filter + resample + pick + write ──
        var npcCount = perNpcKeys.Count;
        var npcsProcessed = 0;
        var totalClipsWritten = 0;

        // Catalog-eligible NPCs sorted deterministically (English name asc, NpcId tiebreak).
        var catalogEligible = perNpcKeys
            .Where(kvp => kvp.Value.Count < CatalogClipThreshold)
            .OrderBy(kvp => npcNames.TryGetValue(kvp.Key, out var n)
                && n.TryGetValue("en", out var en) ? en : string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(kvp => kvp.Key)
            .Select(kvp => kvp.Key)
            .ToList();
        var catalogIdByNpc = new Dictionary<uint, int>();
        for (var i = 0; i < catalogEligible.Count; i++)
            catalogIdByNpc[catalogEligible[i]] = i + 1; // 1-indexed: NPC001…
        if (catalogEligible.Count > 800)
            _log.Warning(nameof(RunInternal),
                $"Catalog has {catalogEligible.Count} NPCs — approaching the 999 cap; " +
                "filenames will auto-widen to NPC0000+ once we cross it.",
                eventId);

        foreach (var (npcId, candidates) in perNpcKeys)
        {
            ct.ThrowIfCancellationRequested();
            npcsProcessed++;
            ProgressChanged?.Invoke($"Extracting samples for NPC {npcId}", npcsProcessed, npcCount);

            // Decode all candidates → WAV bytes + duration. Skips candidates whose SCD can't
            // be resolved or fails to decode.
            var decoded = new List<DecodedClip>();
            foreach (var c in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var wav = TryDecodeSingleClip(c, language, eventId);
                if (wav == null) continue;
                using var ms = new MemoryStream(wav);
                var seconds = WavInspector.GetDurationSeconds(ms);
                if (seconds <= 0) continue;
                decoded.Add(new DecodedClip { Candidate = c, WavBytes = wav, Seconds = seconds });
            }
            if (decoded.Count == 0) continue;

            // Length filter (with closest-fallback if no clip in 6—12s).
            var filtered = VoiceExtractSampleSelector.ApplyLengthFilter(
                decoded, c => c.Seconds, MinSeconds, MaxSeconds);
            if (filtered.Count == 0) continue;

            // Pick N deterministically (seeded by NpcId).
            var picked = VoiceExtractSampleSelector.PickN(filtered, samplesPerNpc, seed: (int)npcId);

            // Resolve display strings: gender / race / localized name. Pull from npcNames
            // index: language-specific name comes from the row in the current client locale.
            var locName = ResolveDisplayName(npcId, language) ?? $"NPC_{npcId}";
            var (gender, race) = ResolveGenderRace(npcId);

            // Write the named-folder version.
            for (var i = 0; i < picked.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var path = VoiceExtractFileNames.GetNamedTargetPath(
                    outputRoot, gender, race, locName, i + 1, picked.Count);
                WriteWavToFile(picked[i].WavBytes, path, eventId);
                totalClipsWritten++;
            }

            // Write the catalog version when this NPC is below the 20-clip threshold.
            if (catalogIdByNpc.TryGetValue(npcId, out var catalogId))
            {
                for (var i = 0; i < picked.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = VoiceExtractFileNames.GetCatalogTargetPath(
                        outputRoot, gender, catalogId, i + 1);
                    WriteWavToFile(picked[i].WavBytes, path, eventId);
                    totalClipsWritten++;
                }
            }
        }

        _log.Info(nameof(RunInternal),
            $"Voice starter set complete: {totalClipsWritten} files written across " +
            $"{npcsProcessed} NPCs ({catalogEligible.Count} also in catalog).",
            eventId);
        ProgressChanged?.Invoke($"Done — {totalClipsWritten} files written", npcsProcessed, npcCount);
    }

    /// <summary>
    /// Iterate raw rows from voice-text-bearing sheets. Yields <c>(sheetName, textKey, text)</c>
    /// tuples — caller filters / parses. Currently reads <c>cut_scene/&lt;exp&gt;/VoiceMan_*</c>
    /// (best-effort enumeration) plus <c>Balloon</c> / <c>InstanceContentTextData</c> /
    /// <c>ManFst</c> if Lumina exposes them. <b>Wiring TODO:</b> the cut_scene enumeration is
    /// brute-force across known number ranges — replace with reflective sheet listing once
    /// Lumina API is confirmed.
    /// </summary>
    private IEnumerable<(string sheet, string textKey, string text)> IterateVoiceTextRows(
        ClientLanguage language, CancellationToken ct)
    {
        // Single-named sheets first.
        foreach (var name in new[] { "Balloon", "InstanceContentTextData", "ManFst" })
        {
            ct.ThrowIfCancellationRequested();
            foreach (var row in EnumerateRawSheet(name, language))
                yield return row;
        }

        // cut_scene/<NNN>/VoiceMan_<NNNNN> — brute-force scan over known number ranges.
        // Tools' equivalent enumeration uses SaintCoinach's AvailableSheets list which we
        // don't have at runtime. Range up to 999 covers EW + DT comfortably.
        for (var folder = 0; folder < 1000; folder++)
        {
            ct.ThrowIfCancellationRequested();
            for (var index = 0; index < 100; index++)
            {
                var sheetName = $"cut_scene/{folder:D3}/VoiceMan_{folder:D3}{index:D2}";
                var any = false;
                foreach (var row in EnumerateRawSheet(sheetName, language))
                {
                    any = true;
                    yield return row;
                }
                // Best-effort optimization: if folder/00 is empty, the rest of the indexes
                // are usually empty too. Don't scan more in this folder.
                if (!any && index == 0) break;
            }
        }
    }

    /// <summary>
    /// Read all rows from a named raw sheet. Yields <c>(sheetName, textKey, text)</c> for
    /// each non-empty row. Quietly returns nothing if the sheet doesn't exist or has no
    /// usable text columns.
    /// </summary>
    private IEnumerable<(string sheet, string textKey, string text)> EnumerateRawSheet(
        string sheetName, ClientLanguage language)
    {
        ExcelSheet<RawRow>? sheet;
        try
        {
            sheet = _dataManager.GetExcelSheet<RawRow>(language, sheetName);
        }
        catch
        {
            yield break;
        }
        if (sheet == null) yield break;

        foreach (var row in sheet)
        {
            // Heuristic: scan columns for a TEXT_… key in column 0, text in column 1.
            // Schema varies across sheets, but the dump format Tools relies on (the CSV
            // shape <id, "TEXT_xxx", "english text">) maps to RawRow columns 0 and 1 in
            // most cases. If it doesn't, the consumer's TryParse simply yields a no-match.
            string keyStr;
            string textStr;
            try
            {
                keyStr = row.ReadStringColumn(0).ExtractText();
                textStr = row.ReadStringColumn(1).ExtractText();
            }
            catch
            {
                continue;
            }
            if (string.IsNullOrEmpty(keyStr) || string.IsNullOrEmpty(textStr)) continue;
            yield return (sheetName, keyStr, textStr);
        }
    }

    /// <summary>
    /// Build the SCD path family for one candidate, fetch the bytes from Lumina, decode the
    /// first ADPCM/OGG entry, return the raw decoded WAV bytes (unresampled — TODO: resample
    /// to 22050 Hz via BASS).
    /// </summary>
    private byte[]? TryDecodeSingleClip(VoiceClipCandidate c, ClientLanguage language, EKEventId eventId)
    {
        // Path family from Tools (GetScdHelper.AddLine):
        //   cut/{expansion}/sound/{folder6}/{folder14}/<base>_<lang>.scd
        // For ManFst-prefixed entries, the path is shorter:
        //   cut/{expansion}/sound/{folder6}/{folder9}/<truncatedBase>.scd
        var lang = LanguageCodeForScd(language);
        var audioBase = c.AudioFileBase;
        if (audioBase.Length < 14) return null;

        var folder6 = audioBase.Substring(3, 6);
        foreach (var exp in ExpansionKeys)
        {
            string path;
            if (audioBase.Contains("manfst"))
            {
                if (audioBase.Length < 12) continue;
                var folder9 = audioBase.Substring(3, 9);
                path = $"cut/{exp}/sound/{folder6}/{folder9}/{audioBase.Substring(0, 12)}_{lang}.scd";
            }
            else
            {
                var folder14 = audioBase.Substring(3, 14);
                path = $"cut/{exp}/sound/{folder6}/{folder14}/{audioBase}_{lang}.scd";
            }

            byte[]? raw;
            try
            {
                var file = _dataManager.GetFile(path);
                raw = file?.Data;
            }
            catch
            {
                raw = null;
            }
            if (raw == null) continue;

            try
            {
                var scd = new ScdFile(raw);
                foreach (var entry in scd.Entries)
                {
                    if (entry == null) continue;
                    var decoded = entry.GetDecoded();
                    if (decoded != null && decoded.Length > 0)
                    {
                        // TODO: resample to TargetSampleRate via BASS once the in-process
                        // resample API is wired. For now we hand back the native rate WAV.
                        return decoded;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(TryDecodeSingleClip),
                    $"SCD decode failed for {path}: {ex.Message}", eventId);
            }
        }
        return null;
    }

    private static string LanguageCodeForScd(ClientLanguage language) => language switch
    {
        ClientLanguage.English => "en",
        ClientLanguage.German => "de",
        ClientLanguage.French => "fr",
        ClientLanguage.Japanese => "ja",
        _ => "en",
    };

    private void WriteWavToFile(byte[] wavBytes, string path, EKEventId eventId)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, wavBytes);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(WriteWavToFile), $"Failed to write {path}: {ex.Message}", eventId);
        }
    }

    private void WriteUnmatchedJson(string outputRoot, Dictionary<string, UnmatchedShortName> unmatched, EKEventId eventId)
    {
        if (unmatched.Count == 0) return;
        try
        {
            var dir = Path.Combine(outputRoot, "FF14-Voices");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "voice_extract_unmatched.json");
            var json = JsonSerializer.Serialize(unmatched.Values.OrderByDescending(u => u.Count),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(WriteUnmatchedJson),
                $"Failed to write unmatched shortnames JSON: {ex.Message}", eventId);
        }
    }

    /// <summary>Loads English NPC names — used for the canonical name index.</summary>
    private Dictionary<uint, Dictionary<string, string>> LoadEnglishNpcNames(ClientLanguage clientLanguage)
    {
        var result = new Dictionary<uint, Dictionary<string, string>>();
        try
        {
            // English for the index, plus the client language for display naming later.
            var en = _dataManager.GetExcelSheet<ENpcResident>(ClientLanguage.English);
            var loc = _dataManager.GetExcelSheet<ENpcResident>(clientLanguage);
            if (en == null) return result;
            foreach (var row in en)
            {
                var enName = row.Singular.ExtractText();
                if (string.IsNullOrEmpty(enName)) continue;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = enName };
                if (loc != null)
                {
                    var locRow = loc.GetRowOrDefault(row.RowId);
                    var locName = locRow?.Singular.ExtractText() ?? enName;
                    d[LangCode(clientLanguage)] = string.IsNullOrEmpty(locName) ? enName : locName;
                }
                result[row.RowId] = d;
            }
        }
        catch { /* best-effort — empty index falls through, every name becomes unmatched */ }
        return result;
    }

    private string? ResolveDisplayName(uint npcId, ClientLanguage language)
    {
        try
        {
            var sheet = _dataManager.GetExcelSheet<ENpcResident>(language);
            var row = sheet?.GetRowOrDefault(npcId);
            var s = row?.Singular.ExtractText();
            return string.IsNullOrEmpty(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }

    private (string gender, string race) ResolveGenderRace(uint npcId)
    {
        // ENpcResident → ENpcBase → race / gender. ENpcBase has Race + Gender columns.
        try
        {
            var residentSheet = _dataManager.GetExcelSheet<ENpcResident>();
            var baseSheet = _dataManager.GetExcelSheet<ENpcBase>();
            if (residentSheet == null || baseSheet == null) return ("None", "Unknown");
            var resident = residentSheet.GetRowOrDefault(npcId);
            if (resident == null) return ("None", "Unknown");
            var npcBase = baseSheet.GetRowOrDefault(npcId);
            if (npcBase == null) return ("None", "Unknown");
            var gender = npcBase.Value.Gender == 1 ? "Female" : npcBase.Value.Gender == 0 ? "Male" : "None";
            var race = npcBase.Value.Race.ValueNullable?.Masculine.ExtractText() ?? "Unknown";
            return (gender, string.IsNullOrEmpty(race) ? "Unknown" : race);
        }
        catch
        {
            return ("None", "Unknown");
        }
    }

    private static string LangCode(ClientLanguage language) => language switch
    {
        ClientLanguage.English => "en",
        ClientLanguage.German => "de",
        ClientLanguage.French => "fr",
        ClientLanguage.Japanese => "ja",
        _ => "en",
    };

    // ── DTOs ────────────────────────────────────────────────────────────────

    private sealed class VoiceClipCandidate
    {
        public string TextKey { get; set; } = string.Empty;
        public string AudioFileBase { get; set; } = string.Empty;
        public string Sheet { get; set; } = string.Empty;
        public string MaleText { get; set; } = string.Empty;
        public string? FemaleText { get; set; }
    }

    private sealed class DecodedClip
    {
        public required VoiceClipCandidate Candidate { get; init; }
        public required byte[] WavBytes { get; init; }
        public double Seconds { get; init; }
    }

    private sealed class UnmatchedShortName
    {
        public string ShortName { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<UnmatchedExample> Examples { get; set; } = new();
    }

    private sealed class UnmatchedExample
    {
        public string TextKey { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
