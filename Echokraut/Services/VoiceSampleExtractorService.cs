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
using Echokraut.Localization;
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
    /// <summary>Counter for OGG-encoded SCD entries skipped during the current run. Voice
    /// content under cut/*/sound/ is overwhelmingly MS-ADPCM (per Ioncannon/xivapi research:
    /// "FFXIV uses OGG for music, MS-ADPCM for sound"); we don't bundle an OGG decoder, so
    /// any OGG entries we hit are counted and reported once at the end.</summary>
    private int _oggSkipCount;
    /// <summary>Cache of speaker shortnames already warned about for multi-NPC ambiguity, so
    /// the same warning isn't emitted hundreds of times during a single run.</summary>
    private readonly HashSet<string> _multiNpcWarned = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>0 until the first SCD lookup actually resolves; logs the working path once.</summary>
    private int _firstHitLogged;
    /// <summary>Per-run aggregate counters for SCD lookups: how many candidate clips found
    /// at least one resolvable path vs. how many were exhausted across all expansions/suffixes.</summary>
    private int _scdResolveCount;
    private int _scdMissCount;

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
        _oggSkipCount = 0;
        _multiNpcWarned.Clear();
        _firstHitLogged = 0;
        _scdResolveCount = 0;
        _scdMissCount = 0;
        var outputRoot = _config.LocalSaveLocation;
        Directory.CreateDirectory(Path.Combine(outputRoot, "FF14-Voices"));

        // ── Phase 1+2: harvest text-key → speaker-shortname, then resolve to NpcId ──
        ProgressChanged?.Invoke(Loc.S("Building NPC name index"), 0, 1);
        var npcNames = LoadEnglishNpcNames(language);
        var nameIndex = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        _log.Info(nameof(RunInternal),
            $"NPC name index: {nameIndex.Count} entries (across {npcNames.Count} NPCs)",
            eventId);

        ProgressChanged?.Invoke(Loc.S("Scanning voice text sheets"), 0, 1);
        var perNpcKeys = new Dictionary<uint, List<VoiceClipCandidate>>();
        var unmatched = new Dictionary<string, UnmatchedShortName>(StringComparer.OrdinalIgnoreCase);
        var totalKeys = 0;
        // Per-source diagnostic counters — surface where keys are coming from and how many
        // pass TryParse vs. are dropped. Helps spot column-index or sheet-name regressions.
        var sheetTotal = new Dictionary<string, int>();
        var sheetParsed = new Dictionary<string, int>();
        var sheetSampleKey = new Dictionary<string, string>();

        foreach (var (sheetName, textKey, text) in IterateVoiceTextRows(language, ct))
        {
            ct.ThrowIfCancellationRequested();
            totalKeys++;
            sheetTotal.TryGetValue(sheetName, out var st);
            sheetTotal[sheetName] = st + 1;
            if (!sheetSampleKey.ContainsKey(sheetName))
                sheetSampleKey[sheetName] = textKey;
            if (totalKeys % 5000 == 0)
                ProgressChanged?.Invoke(string.Format(Loc.S("Scanning voice text {0} keys"), totalKeys), totalKeys, 0);

            if (!VoiceExtractKey.TryParse(textKey, out var shortName, out var audioBase)) continue;
            sheetParsed.TryGetValue(sheetName, out var sp);
            sheetParsed[sheetName] = sp + 1;
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
            if (resolved.Count > 1 && _multiNpcWarned.Add(shortName))
                _log.Debug(nameof(RunInternal),
                    $"Shortname '{shortName}' maps to {resolved.Count} NPC instances, " +
                    $"using first ({npcId}). All instances share the same character " +
                    "(name/gender/race) and only differ by spawn location.",
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

        // Per-sheet diagnostic: top 10 sources by row count, with TryParse hit rate and a
        // sample key. If hit rate is 0% with a non-empty sheet, the sample reveals whether the
        // column index is wrong (e.g. column 0 returns a balloon ID instead of a TEXT_ key).
        var topSheets = sheetTotal.OrderByDescending(kvp => kvp.Value).Take(10);
        foreach (var (sheet, total) in topSheets)
        {
            sheetParsed.TryGetValue(sheet, out var parsed);
            sheetSampleKey.TryGetValue(sheet, out var sample);
            var truncated = sample is { Length: > 80 } ? sample.Substring(0, 80) + "…" : sample;
            _log.Info(nameof(RunInternal),
                $"  sheet '{sheet}': {parsed}/{total} parsed (sample col0='{truncated}')", eventId);
        }
        var cutSceneCount = sheetTotal.Keys.Count(k => k.StartsWith("cut_scene/", StringComparison.OrdinalIgnoreCase));
        _log.Info(nameof(RunInternal),
            $"  cut_scene sheets producing rows: {cutSceneCount}", eventId);

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
            ProgressChanged?.Invoke(string.Format(Loc.S("Extracting samples for NPC {0}"), npcId), npcsProcessed, npcCount);

            // Decode all candidates to PCM. Skips candidates whose SCD can't be resolved,
            // fails to decode, or is OGG (counted globally — overwhelmingly absent in voice paths).
            var decoded = new List<DecodedClip>();
            foreach (var c in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var pcm = TryDecodeSingleClip(c, language, eventId);
                if (pcm == null || pcm.Samples.Length == 0) continue;
                if (pcm.Seconds <= 0) continue;
                decoded.Add(new DecodedClip { Candidate = c, Pcm = pcm, Seconds = pcm.Seconds });
            }
            if (decoded.Count == 0) continue;

            // Length filter (with closest-fallback if no clip in 6—12s).
            var filtered = VoiceExtractSampleSelector.ApplyLengthFilter(
                decoded, c => c.Seconds, MinSeconds, MaxSeconds);
            if (filtered.Count == 0) continue;

            // Pick N deterministically (seeded by NpcId).
            var picked = VoiceExtractSampleSelector.PickN(filtered, samplesPerNpc, seed: (int)npcId);

            // Resolve gender first — the localized name normalizer needs it to resolve
            // German [a]/[p] declension tags into their grammatically-correct form.
            var (gender, race) = ResolveGenderRace(npcId);
            var locName = ResolveDisplayName(npcId, language, gender) ?? $"NPC_{npcId}";

            // Resample each picked clip once (downmix → 22050 mono → PCM-WAV bytes), then
            // write to the named folder and (when eligible) the random-voice catalog.
            var resampledBytes = new byte[picked.Count][];
            for (var i = 0; i < picked.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                resampledBytes[i] = ResampleToMonoWavBytes(picked[i].Pcm);
            }

            for (var i = 0; i < picked.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var path = VoiceExtractFileNames.GetNamedTargetPath(
                    outputRoot, gender, race, locName, i + 1, picked.Count);
                WriteWavToFile(resampledBytes[i], path, eventId);
                totalClipsWritten++;
            }

            // Write the catalog version when this NPC is below the 20-clip threshold.
            if (catalogIdByNpc.TryGetValue(npcId, out var catalogId))
            {
                for (var i = 0; i < picked.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = VoiceExtractFileNames.GetCatalogTargetPath(
                        outputRoot, gender, catalogId, i + 1, picked.Count);
                    WriteWavToFile(resampledBytes[i], path, eventId);
                    totalClipsWritten++;
                }
            }
        }

        _log.Info(nameof(RunInternal),
            $"Voice starter set complete: {totalClipsWritten} files written across " +
            $"{npcsProcessed} NPCs ({catalogEligible.Count} also in catalog). " +
            $"SCD lookups: {_scdResolveCount} resolved, {_scdMissCount} exhausted.",
            eventId);
        if (_oggSkipCount > 0)
            _log.Info(nameof(RunInternal),
                $"Skipped {_oggSkipCount} OGG-encoded SCD entries (decoder supports MS-ADPCM only; " +
                "voice content is overwhelmingly MS-ADPCM, but a few OGG entries exist).",
                eventId);
        ProgressChanged?.Invoke(string.Format(Loc.S("Done — {0} files written"), totalClipsWritten), npcsProcessed, npcCount);
    }

    /// <summary>
    /// Convert a freshly-decoded PCM clip to the AllTalk target format: mono int16 at 22050 Hz,
    /// wrapped in a standard PCM RIFF/WAVE container. Downmixes any multi-channel input first.
    /// </summary>
    private byte[] ResampleToMonoWavBytes(MsAdpcmDecoder.DecodedPcm pcm)
    {
        var mono = pcm.Channels == 1
            ? pcm.Samples
            : WavResampler.DownmixToMono(pcm.Samples, pcm.Channels);
        var resampled = WavResampler.Resample(mono, pcm.SampleRate, TargetSampleRate);
        return PcmWavWriter.Build(resampled, TargetSampleRate, channels: 1);
    }

    /// <summary>
    /// Iterate raw rows from voice-text-bearing sheets. Yields <c>(sheetName, textKey, text)</c>
    /// tuples — caller filters / parses. Reads <c>Balloon</c>, <c>InstanceContentTextData</c>,
    /// <c>ManFst</c>, plus every <c>cut_scene/*</c> sheet Lumina knows about (filtered through
    /// <see cref="IDataManager.Excel"/>'s <c>SheetNames</c>). The cut_scene set carries the bulk
    /// of voice acting (Tools' equivalent uses SaintCoinach's <c>AvailableSheets</c>).
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

        // All cut_scene/* sheets discovered via Lumina's actual sheet list. FFXIV ships
        // cut_scene voice-text sheets with names like cut_scene/<expFolder>/VoiceMan_<NNNNN>;
        // the exact numbering varies per expansion so we don't try to compose names.
        IReadOnlyList<string>? sheetNames = null;
        try { sheetNames = _dataManager.Excel.SheetNames; } catch { /* best-effort */ }
        if (sheetNames != null)
        {
            foreach (var name in sheetNames)
            {
                ct.ThrowIfCancellationRequested();
                if (!name.StartsWith("cut_scene/", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var row in EnumerateRawSheet(name, language))
                    yield return row;
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
    /// first MS-ADPCM entry to int16 PCM and return it. OGG entries are counted into
    /// <see cref="_oggSkipCount"/> and reported once at end-of-run — voice content is
    /// overwhelmingly MS-ADPCM (per Ioncannon/xivapi research) so OGG skips are expected
    /// to be near-zero in practice.
    /// </summary>
    private MsAdpcmDecoder.DecodedPcm? TryDecodeSingleClip(VoiceClipCandidate c, ClientLanguage language, EKEventId eventId)
    {
        // Build a list of candidate SCD paths. FFXIV organizes cutscene voice files under
        // cut/<expansion>/sound/... but the exact folder layout differs per voice family.
        // We try multiple plausible structures since reverse-engineering docs disagree.
        var lang = LanguageCodeForScd(language);
        var audioBase = c.AudioFileBase;
        if (audioBase.Length < 14) return null;

        var paths = BuildCandidateScdPaths(audioBase, lang);
        if (paths.Count == 0) return null;

        foreach (var path in paths)
        {
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

            // We resolved! Log the layout that worked so subsequent runs / users can confirm.
            if (System.Threading.Interlocked.CompareExchange(ref _firstHitLogged, 1, 0) == 0)
                _log.Info(nameof(TryDecodeSingleClip),
                    $"First SCD hit — layout works: '{path}'", eventId);

            try
            {
                var scd = new ScdFile(raw);
                foreach (var entry in scd.Entries)
                {
                    if (entry == null) continue;
                    if (entry.Header.Codec == ScdCodec.OGG)
                    {
                        _oggSkipCount++;
                        continue;
                    }
                    if (entry.Header.Codec != ScdCodec.MSADPCM) continue;

                    var encodedWav = entry.GetDecoded();
                    if (encodedWav == null || encodedWav.Length == 0) continue;
                    var pcm = MsAdpcmDecoder.Decode(encodedWav);
                    if (pcm != null && pcm.Samples.Length > 0)
                    {
                        _scdResolveCount++;
                        return pcm;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(TryDecodeSingleClip),
                    $"SCD decode failed for {path}: {ex.Message}", eventId);
            }
        }

        _scdMissCount++;
        return null;
    }

    /// <summary>
    /// Build the list of candidate SCD paths for an audio file base. The proven layout
    /// (verified against live game data) is Tools' substring math:
    /// <c>cut/{exp}/sound/{base[3..6]}/{base[3..14]}/{base}_{gender}_{lang}.scd</c> — for
    /// "vo_voiceman_06006_000010" that becomes
    /// <c>cut/{exp}/sound/voicem/voiceman_06006/vo_voiceman_06006_000010_m_de.scd</c>. The
    /// gender marker is empty (single underscore separator) for non-gender-branching lines.
    /// We iterate all expansions because the same cutscene-id range can be referenced from
    /// any expansion's archive; first hit wins.
    /// </summary>
    private List<string> BuildCandidateScdPaths(string audioBase, string lang)
    {
        var result = new List<string>(18);
        if (audioBase.Length < 17) return result;

        var folder6 = audioBase.Substring(3, 6);
        var folder14 = audioBase.Substring(3, 14);
        // ManFst entries use a 9-char inner folder and the file path drops trailing tokens
        // (truncated base of 12 chars); we keep that legacy layout as a fallback.
        var isManFst = audioBase.Contains("manfst");

        foreach (var exp in ExpansionKeys)
        {
            // Gender marker has a LEADING underscore — real filenames are
            // <base>_m_<lang>.scd / <base>_f_<lang>.scd / <base>_<lang>.scd.
            foreach (var suffix in new[] { "_m_", "_", "_f_" })
            {
                if (isManFst && audioBase.Length >= 12)
                {
                    var folder9 = audioBase.Substring(3, 9);
                    result.Add($"cut/{exp}/sound/{folder6}/{folder9}/{audioBase.Substring(0, 12)}{suffix}{lang}.scd");
                }
                else
                {
                    result.Add($"cut/{exp}/sound/{folder6}/{folder14}/{audioBase}{suffix}{lang}.scd");
                }
            }
        }
        return result;
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

    /// <summary>
    /// Localized display name with FFXIV grammatical gender tags resolved (German <c>[a]</c>/
    /// <c>[p]</c>, French <c>[a]</c>/<c>[p]</c>). Without this step German NPC names like
    /// <c>"Soldatin[p] der Befreiungsarmee"</c> would land literally in the output filename.
    /// </summary>
    private string? ResolveDisplayName(uint npcId, ClientLanguage language, string gender)
    {
        try
        {
            var sheet = _dataManager.GetExcelSheet<ENpcResident>(language);
            var row = sheet?.GetRowOrDefault(npcId);
            var s = row?.Singular.ExtractText();
            if (string.IsNullOrEmpty(s)) return null;

            // Pronoun is German-only (decides whether [p] adds "in" or stays empty for
            // feminine NPCs whose name stem is already feminine). Pull the German row
            // explicitly because the client language might be French / English / Japanese.
            var dePronoun = 0;
            if (s.Contains('['))
            {
                try
                {
                    var deSheet = _dataManager.GetExcelSheet<ENpcResident>(ClientLanguage.German);
                    var deRow = deSheet?.GetRowOrDefault(npcId);
                    if (deRow.HasValue) dePronoun = deRow.Value.Pronoun;
                }
                catch { /* best-effort — fall back to pronoun=0 */ }
            }

            var langCode = LangCode(language);
            var isFemale = string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase);
            return NpcNameNormalizer.Resolve(s, langCode, isFemale, dePronoun);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve gender + race for an NPC. Both are forced to fixed English so the starter set
    /// filenames stay locale-stable (a German client and an English client produce identical
    /// <c>Gender_Race_*</c> tokens). Race comes from the English <see cref="Race"/> sheet
    /// directly, then runs through <see cref="VoiceExtractFileNames.NormalizeRace"/> which
    /// maps <c>"Hyuran"→"Hyur"</c> / <c>"Au Ra"→"AuRa"</c> / <c>"Miqo'te"→"Miqote"</c> onto
    /// the canonical NpcRaces enum tokens used by voice resolution.
    /// </summary>
    private (string gender, string race) ResolveGenderRace(uint npcId)
    {
        try
        {
            // Gender comes from a numeric byte (0=Male, 1=Female) so it's already locale-free.
            var baseSheet = _dataManager.GetExcelSheet<ENpcBase>();
            if (baseSheet == null) return ("None", "Unknown");
            var npcBase = baseSheet.GetRowOrDefault(npcId);
            if (npcBase == null) return ("None", "Unknown");
            var gender = npcBase.Value.Gender == 1 ? "Female" : npcBase.Value.Gender == 0 ? "Male" : "None";

            // Race must come from the English Race sheet — the client-language Masculine
            // form is e.g. "Hyuraner" (DE) which doesn't match the canonical "Hyur" enum token.
            var race = "Unknown";
            var raceRowId = npcBase.Value.Race.RowId;
            if (raceRowId != 0)
            {
                var enRaceSheet = _dataManager.GetExcelSheet<Race>(ClientLanguage.English);
                var enRace = enRaceSheet?.GetRowOrDefault(raceRowId);
                var raw = enRace?.Masculine.ExtractText();
                if (!string.IsNullOrEmpty(raw)) race = raw;
            }
            return (gender, race);
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
        public required MsAdpcmDecoder.DecodedPcm Pcm { get; init; }
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
