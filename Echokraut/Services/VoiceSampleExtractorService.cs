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

    private readonly IDataManager _dataManager;
    private readonly IClientState _clientState;
    private readonly ILogService _log;
    private readonly IJsonDataService _jsonData;
    private readonly Configuration _config;
    private readonly IRemoteUrlService _remoteUrls;

    private volatile bool _isRunning;
    /// <summary>Counter for OGG-encoded SCD entries skipped during the current run. Voice
    /// content under cut/*/sound/ is overwhelmingly MS-ADPCM (per Ioncannon/xivapi research:
    /// "FFXIV uses OGG for music, MS-ADPCM for sound"); we don't bundle an OGG decoder, so
    /// any OGG entries we hit are counted and reported once at the end.</summary>
    private int _oggSkipCount;
    /// <summary>Counter for candidates dropped by the speech-content filter
    /// (<see cref="VoiceExtractTextCleaner.IsSpeech"/>) during the current run. Surfaced in the
    /// run summary so the per-language thresholds can be re-tuned against real data (Open
    /// Question #1 in docs/plans/voice-sample-improvements.md).</summary>
    private int _nonSpeechSkipCount;
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
        Configuration config,
        IRemoteUrlService remoteUrls)
    {
        _dataManager = dataManager;
        _clientState = clientState;
        _log = log;
        _jsonData = jsonData;
        _config = config;
        _remoteUrls = remoteUrls;
    }

    public async Task RunAsync(ClientLanguage language, int samplesPerNpc, CancellationToken ct,
        string? outputRootOverride = null, string outputSubfolder = "FF14-Voices")
    {
        if (_isRunning) return;
        _isRunning = true;
        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            await Task.Run(() => RunInternal(language, samplesPerNpc, ct, eventId, outputRootOverride, outputSubfolder), ct);
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

    private void RunInternal(ClientLanguage language, int samplesPerNpc, CancellationToken ct, EKEventId eventId,
        string? outputRootOverride = null, string outputSubfolder = "FF14-Voices")
    {
        samplesPerNpc = Math.Clamp(samplesPerNpc, 1, 5);
        _oggSkipCount = 0;
        _nonSpeechSkipCount = 0;
        _multiNpcWarned.Clear();
        _firstHitLogged = 0;
        _scdResolveCount = 0;
        _scdMissCount = 0;
        // Allow callers (specifically the First-Time install flow) to redirect output into
        // e.g. the AllTalk install folder instead of LocalSaveLocation. Empty / null falls
        // back to the user's configured save location.
        var hasOverride = !string.IsNullOrWhiteSpace(outputRootOverride);
        var outputRoot = hasOverride ? outputRootOverride! : _config.LocalSaveLocation;
        var fullVoicesDir = Path.Combine(outputRoot, outputSubfolder);
        // When the caller specifies an override (install flow), wipe the target subfolder
        // before extracting — the old voices.zip flow extracted over a fresh dir, and we
        // mirror that semantic so leftover files from a previous extraction don't haunt
        // AllTalk's voice list. Default Game-Data-Tools usage (no override) leaves the
        // existing FF14-Voices/ contents in place; same-named files are overwritten by the
        // per-file WriteAllBytes downstream.
        if (hasOverride && Directory.Exists(fullVoicesDir))
        {
            try { Directory.Delete(fullVoicesDir, recursive: true); }
            catch (Exception ex)
            {
                _log.Warning(nameof(RunInternal),
                    $"Could not wipe target voices dir {fullVoicesDir}: {ex.Message}. " +
                    $"Continuing with extraction over existing files.", eventId);
            }
        }
        Directory.CreateDirectory(fullVoicesDir);

        // ── Phase 1+2: harvest text-key → speaker-shortname, then resolve to NpcId ──
        ProgressChanged?.Invoke(Loc.S("Building NPC name index"), 0, 1);
        var npcNames = LoadEnglishNpcNames(language);
        var nameIndex = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        _log.Info(nameof(RunInternal),
            $"NPC name index: {nameIndex.Count} entries (across {npcNames.Count} NPCs)",
            eventId);

        // Layered short-name → NPC override map (embedded ← remote ← user-local). Consulted
        // ONLY when the normal name-index resolve returns nothing — so name-index hits never
        // get overruled accidentally. Built once before the iteration loop.
        var aliasMap = LoadVoiceExtractAliases(nameIndex, eventId);

        // Voice-actor splits (embedded + optional local override). For voices whose dub actor
        // changed mid-game, this partitions the clips into per-epoch sample sets so a cloned
        // voice doesn't blend two actors. Voices without an entry behave exactly as before.
        var splits = LoadVoiceActorSplits(eventId);
        var langCode = VoiceScdPaths.LanguageCodeForScd(language);

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

            // Speech-content filter: drop laughs / grunts / single-word exclamations /
            // onomatopoeia before they reach the candidate list. cleaned[0] (male variant)
            // is sufficient — both gender variants share the same structural content type.
            if (!VoiceExtractTextCleaner.IsSpeech(cleaned[0], language)) { _nonSpeechSkipCount++; continue; }

            var resolved = VoiceExtractKey.Resolve(shortName, nameIndex);
            uint npcId;
            if (resolved == null || resolved.Count == 0)
            {
                // Last-chance: layered alias overrides (embedded ← remote ← user-local).
                // The user pulls entries from voice_extract_unmatched.json, looks the speaker
                // up in-game, and writes them into voice_extract_aliases.json — re-running the
                // extract then resolves them through this map instead of dropping them.
                if (aliasMap.TryGetValue(shortName, out var aliasNpcId))
                {
                    npcId = aliasNpcId;
                }
                else
                {
                    if (!unmatched.TryGetValue(shortName, out var unm))
                        unmatched[shortName] = unm = new UnmatchedShortName { ShortName = shortName };
                    unm.Count++;
                    if (unm.Examples.Count < 3)
                        unm.Examples.Add(new UnmatchedExample { TextKey = textKey, Text = cleaned[0] });
                    continue;
                }
            }
            else
            {
                npcId = resolved[0];
                if (resolved.Count > 1 && _multiNpcWarned.Add(shortName))
                    _log.Debug(nameof(RunInternal),
                        $"Shortname '{shortName}' maps to {resolved.Count} NPC instances, " +
                        $"using first ({npcId}). All instances share the same character " +
                        "(name/gender/race) and only differ by spawn location.",
                        eventId);
            }

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
            $"{unmatched.Count} unmatched shortnames, " +
            $"{_nonSpeechSkipCount} non-speech lines dropped",
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

        WriteUnmatchedJson(outputRoot, outputSubfolder, unmatched, eventId);

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

        var skippedUnknownRace = 0;
        var skippedNoName = 0;
        foreach (var (npcId, candidates) in perNpcKeys)
        {
            ct.ThrowIfCancellationRequested();
            npcsProcessed++;
            ProgressChanged?.Invoke(string.Format(Loc.S("Extracting samples for NPC {0}"), npcId), npcsProcessed, npcCount);

            // Skip NPCs the voice picker can't make sensible use of, BEFORE the expensive
            // decode + resample work runs:
            //   • Race=Unknown → no race-bucket fits the file in AllTalk, sample sits unused.
            //   • No resolvable display name → filename collapses to "NPC_<rawId>" which
            //     gives nothing the user can recognize and clutters the voices folder.
            // Both produce the "Male_Unknown_NPC_1043638"-style entries the user reported
            // as useless.
            var (gender, race, bodyType) = ResolveGenderRaceBody(npcId);
            if (string.Equals(race, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                skippedUnknownRace++;
                continue;
            }
            var locName = ResolveDisplayName(npcId, language, gender);
            if (string.IsNullOrEmpty(locName))
            {
                skippedNoName++;
                continue;
            }

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

            // Voice-actor split: write one named sample set per epoch (epoch encoded in the
            // filename, e.g. _Pre06010 / _Post06010) so pre/post-actor-change voices stay
            // distinct. Split voices are main characters (far more than the catalog's 20-clip
            // threshold), so they never produce catalog entries — handled in the branch.
            var voiceKey = VoiceExtractFileNames.CanonicalNamePart(gender, race, bodyType, locName);
            if (splits.HasSplit(voiceKey, langCode))
            {
                var target = new NamedOutputTarget(outputRoot, gender, race, bodyType, locName, outputSubfolder);
                totalClipsWritten += WriteSplitVoiceSets(
                    decoded, ab => splits.ResolveEpoch(voiceKey, langCode, ab), samplesPerNpc, target, ct, eventId);
                continue;
            }

            // Length filter (with closest-fallback if no clip in 6—12s).
            var filtered = VoiceExtractSampleSelector.ApplyLengthFilter(
                decoded, c => c.Seconds, MinSeconds, MaxSeconds);
            if (filtered.Count == 0) continue;

            // Pick a duration-diverse sample set (short/medium/long) so the cloned voice sees
            // varied prosody instead of a random length cluster. Deterministic, no RNG.
            var picked = VoiceExtractSampleSelector.PickDiverse(filtered, samplesPerNpc, c => c.Seconds);

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
                    outputRoot, gender, race, bodyType, locName, i + 1, picked.Count, outputSubfolder);
                WriteWavToFile(resampledBytes[i], path, eventId);
                totalClipsWritten++;
            }

            // Write the catalog version when this NPC is below the 20-clip threshold.
            // Player-race NPCs collapse to "All" inside GetCatalogTargetPath; non-player races
            // (beast tribes etc.) keep their specific race token so their distinctive voices
            // don't get mixed into the generic player-race pool.
            if (catalogIdByNpc.TryGetValue(npcId, out var catalogId))
            {
                for (var i = 0; i < picked.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = VoiceExtractFileNames.GetCatalogTargetPath(
                        outputRoot, gender, race, bodyType, catalogId, i + 1, picked.Count, outputSubfolder);
                    WriteWavToFile(resampledBytes[i], path, eventId);
                    totalClipsWritten++;
                }
            }
        }

        _log.Info(nameof(RunInternal),
            $"Voice starter set complete: {totalClipsWritten} files written across " +
            $"{npcsProcessed} NPCs ({catalogEligible.Count} also in catalog). " +
            $"Skipped: {skippedUnknownRace} Race=Unknown, {skippedNoName} no resolvable name. " +
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
    /// Write the per-epoch named sample sets for a voice that has a configured actor split.
    /// Partitions the decoded clips by epoch, then length-filters + duration-diverse-picks +
    /// resamples each epoch independently, tagging the epoch onto the filename
    /// (e.g. <c>_Pre06010</c> / <c>_Post06010</c>). Returns the number of files written.
    /// Catalog output is intentionally skipped — split voices are main characters well above
    /// the catalog's clip threshold, so they never produced catalog entries anyway.
    /// </summary>
    private int WriteSplitVoiceSets(
        List<DecodedClip> decoded, Func<string, string> resolveEpoch, int samplesPerNpc,
        NamedOutputTarget target, CancellationToken ct, EKEventId eventId)
    {
        var byEpoch = new Dictionary<string, List<DecodedClip>>();
        foreach (var d in decoded)
        {
            var epoch = resolveEpoch(d.Candidate.AudioFileBase);
            if (!byEpoch.TryGetValue(epoch, out var list)) byEpoch[epoch] = list = new List<DecodedClip>();
            list.Add(d);
        }

        var written = 0;
        foreach (var (epoch, epochClips) in byEpoch)
        {
            ct.ThrowIfCancellationRequested();
            var filtered = VoiceExtractSampleSelector.ApplyLengthFilter(
                epochClips, c => c.Seconds, MinSeconds, MaxSeconds);
            if (filtered.Count == 0) continue;
            var picked = VoiceExtractSampleSelector.PickDiverse(filtered, samplesPerNpc, c => c.Seconds);
            for (var i = 0; i < picked.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = ResampleToMonoWavBytes(picked[i].Pcm);
                var path = VoiceExtractFileNames.GetNamedTargetPath(
                    target.OutputRoot, target.Gender, target.Race, target.BodyType, target.LocName,
                    i + 1, picked.Count, target.OutputSubfolder, epoch);
                WriteWavToFile(bytes, path, eventId);
                written++;
            }
        }
        _log.Debug(nameof(WriteSplitVoiceSets),
            $"Split voice '{target.LocName}': {byEpoch.Count} epoch(s), {written} files written", eventId);
        return written;
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
        // VoiceScdPaths centralizes the substring math so the harvest's "is this voiced?"
        // check sees the same set of paths.
        var lang = VoiceScdPaths.LanguageCodeForScd(language);
        var audioBase = c.AudioFileBase;
        if (audioBase.Length < 14) return null;

        var paths = VoiceScdPaths.Build(audioBase, lang);
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

    // ── Voice-extract aliases: embedded ← remote ← user-local (last wins) ────
    /// <summary>
    /// Build the short-name → NPC ID alias map from up to three layered sources. Mirrors the
    /// pattern in <c>DialogHarvestService.LoadUserAliases</c> so users have one mental model:
    /// embedded baseline ships with the plugin, remote URL adds community-curated entries,
    /// user-local file in <c>FF14-Voices/voice_extract_aliases.json</c> wins over both.
    ///
    /// Each entry resolves either by explicit <see cref="VoiceExtractAliasEntry.NpcId"/>
    /// (wins when set and &gt; 0) or by <see cref="VoiceExtractAliasEntry.NpcName"/>, which
    /// goes through the same <c>VoiceExtractKey.Normalize</c> as the runtime shortname index
    /// (lowercase, strip spaces / apostrophes / hyphens). Unresolvable / empty entries are
    /// logged at warning level and skipped — they don't break the whole alias load.
    /// </summary>
    private Dictionary<string, uint> LoadVoiceExtractAliases(
        Dictionary<string, List<uint>> nameIndex, EKEventId eventId)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        ApplyVoiceAliasEntries(LoadVoiceAliasesFromEmbedded(eventId), "embedded", result, nameIndex, eventId);
        ApplyVoiceAliasEntries(LoadVoiceAliasesFromRemote(eventId), "remote", result, nameIndex, eventId);
        var localFile = LoadVoiceAliasesFromLocal(eventId, out var localPath);
        ApplyVoiceAliasEntries(localFile, $"local ({localPath})", result, nameIndex, eventId);

        _log.Info(nameof(LoadVoiceExtractAliases),
            $"Voice extract aliases loaded: {result.Count} effective entries (embedded+remote+local merged)",
            eventId);
        return result;
    }

    private VoiceExtractAliasFile? LoadVoiceAliasesFromEmbedded(EKEventId eventId)
    {
        try
        {
            using var stream = typeof(VoiceSampleExtractorService).Assembly
                .GetManifestResourceStream("Echokraut.Resources.VoiceExtractAliases.json");
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<VoiceExtractAliasFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadVoiceExtractAliases),
                $"Failed to read embedded VoiceExtractAliases.json: {ex.Message}", eventId);
            return null;
        }
    }

    private VoiceExtractAliasFile? LoadVoiceAliasesFromRemote(EKEventId eventId)
    {
        var url = _remoteUrls.Urls.VoiceExtractAliasesUrl;
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = http.GetStringAsync(url).GetAwaiter().GetResult();
            return JsonSerializer.Deserialize<VoiceExtractAliasFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadVoiceExtractAliases),
                $"Failed to fetch remote voice extract aliases ({url}): {ex.Message} — using embedded+local only",
                eventId);
            return null;
        }
    }

    /// <summary>
    /// Local override file lives next to <c>voice_extract_unmatched.json</c> (same
    /// <c>FF14-Voices/</c> folder) so users can copy entries from one to the other in their
    /// editor without changing directories. Missing file = no local overrides; not an error.
    /// </summary>
    private VoiceExtractAliasFile? LoadVoiceAliasesFromLocal(EKEventId eventId, out string path)
    {
        var baseDir = string.IsNullOrEmpty(_config.LocalSaveLocation) ? @"C:\alltalk_tts" : _config.LocalSaveLocation;
        path = Path.Combine(baseDir, "FF14-Voices", "voice_extract_aliases.json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<VoiceExtractAliasFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadVoiceExtractAliases),
                $"Failed to parse {path}: {ex.Message} — local aliases ignored", eventId);
            return null;
        }
    }

    /// <summary>
    /// Merge one layer's entries into the running result. Later layers overwrite earlier ones
    /// for the same uppercase short-name key — that's how user-local wins over remote which
    /// wins over embedded. Resolution mirrors quest-alias semantics: explicit NpcId wins,
    /// otherwise NpcName goes through the canonical name index. Empty/unknown → skipped + log.
    /// </summary>
    internal void ApplyVoiceAliasEntries(
        VoiceExtractAliasFile? file,
        string sourceLabel,
        Dictionary<string, uint> result,
        Dictionary<string, List<uint>> nameIndex,
        EKEventId eventId)
    {
        if (file?.Aliases == null) return;

        var loaded = 0;
        var overridden = 0;
        var skippedUnknown = 0;
        foreach (var entry in file.Aliases)
        {
            if (string.IsNullOrEmpty(entry.ShortName)) continue;

            uint? resolved = null;
            if (entry.NpcId.HasValue && entry.NpcId.Value > 0)
            {
                resolved = entry.NpcId.Value;
            }
            else if (!string.IsNullOrEmpty(entry.NpcName))
            {
                var ids = VoiceExtractKey.Resolve(entry.NpcName, nameIndex);
                if (ids != null && ids.Count > 0)
                {
                    // First match wins — same reasoning as quest aliases: NPCs that share
                    // (name, gender, race, language) collapse to one DB row, so any spawn id
                    // works for voice attribution.
                    resolved = ids[0];
                    if (ids.Count > 1)
                    {
                        _log.Debug(nameof(LoadVoiceExtractAliases),
                            $"[{sourceLabel}] Alias '{entry.ShortName}' → name '{entry.NpcName}' " +
                            $"matches {ids.Count} NPCs [{string.Join(",", ids)}], using first ({ids[0]})",
                            eventId);
                    }
                }
                else
                {
                    _log.Warning(nameof(LoadVoiceExtractAliases),
                        $"[{sourceLabel}] Alias '{entry.ShortName}' → name '{entry.NpcName}' " +
                        $"not found in English NPC name index — entry ignored (use the English name " +
                        "or set NpcId explicitly)", eventId);
                    skippedUnknown++;
                }
            }

            if (resolved.HasValue)
            {
                var key = entry.ShortName.ToUpperInvariant();
                if (result.ContainsKey(key)) overridden++;
                result[key] = resolved.Value;
                loaded++;
            }
        }

        _log.Info(nameof(LoadVoiceExtractAliases),
            $"[{sourceLabel}] {loaded} voice extract aliases applied " +
            $"({overridden} overrides, {skippedUnknown} unknown)",
            eventId);
    }

    // ── Voice-actor splits: embedded baseline, optional local override (replaces) ──
    /// <summary>
    /// Load the voice-actor split config. Two layers: the embedded
    /// <c>Resources/VoiceActorSplits.json</c> baseline, and an optional user override at
    /// <c>&lt;localSaveLocation&gt;/FF14-Voices/voice_actor_splits.json</c> that, when present and
    /// readable, **fully replaces** the embedded file (no merge, no remote layer). Validation
    /// warnings (bad boundary tokens, non-ascending lists) are logged; invalid entries are
    /// dropped and the rest still load.
    /// </summary>
    private VoiceActorSplits LoadVoiceActorSplits(EKEventId eventId)
    {
        var baseDir = string.IsNullOrEmpty(_config.LocalSaveLocation) ? @"C:\alltalk_tts" : _config.LocalSaveLocation;
        var localPath = Path.Combine(baseDir, "FF14-Voices", "voice_actor_splits.json");
        string? json = null;
        var source = "embedded";

        if (File.Exists(localPath))
        {
            try
            {
                json = File.ReadAllText(localPath);
                source = $"local ({localPath})";
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(LoadVoiceActorSplits),
                    $"Failed to read {localPath}: {ex.Message} — using embedded splits", eventId);
            }
        }

        if (json == null)
        {
            try
            {
                using var stream = typeof(VoiceSampleExtractorService).Assembly
                    .GetManifestResourceStream("Echokraut.Resources.VoiceActorSplits.json");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    json = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(LoadVoiceActorSplits),
                    $"Failed to read embedded VoiceActorSplits.json: {ex.Message}", eventId);
            }
        }

        var splits = VoiceActorSplits.Parse(json, out var warnings);
        foreach (var w in warnings)
            _log.Warning(nameof(LoadVoiceActorSplits), w, eventId);
        _log.Info(nameof(LoadVoiceActorSplits),
            $"Voice actor splits loaded from {source}: {(splits.HasAnySplits ? "active" : "none configured")}",
            eventId);
        return splits;
    }

    private void WriteUnmatchedJson(string outputRoot, string outputSubfolder,
        Dictionary<string, UnmatchedShortName> unmatched, EKEventId eventId)
    {
        if (unmatched.Count == 0) return;
        try
        {
            var dir = Path.Combine(outputRoot, outputSubfolder);
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
    /// <c>[p]</c>, French <c>[a]</c>/<c>[p]</c>) and folded onto the canonical voice name.
    /// Without the tag-resolution step German NPC names like <c>"Soldatin[p] der Befreiungsarmee"</c>
    /// would land literally in the output filename.
    ///
    /// Final step: pipe the localized name through <see cref="IJsonDataService.GetNpcName"/>
    /// so all "speaker variants" of one character collapse onto a single voice file. Example:
    /// <c>"Ysayle"</c> and <c>"Iceheart"</c> are both speakers of the VoiceMap entry
    /// <c>{voiceName: "Iceheart", speakers: ["Ysayle", "Iceheart"]}</c> in
    /// <c>VoiceNamesEN.json</c> — the extractor writes both under the same
    /// <c>Female_Hyur_Iceheart.wav</c>, so users end up with one voice per character instead
    /// of one per dialog tag. <c>GetNpcName</c> returns the input unchanged when no VoiceMap
    /// matches, so plain NPCs are unaffected.
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
            var resolved = NpcNameNormalizer.Resolve(s, langCode, isFemale, dePronoun);
            if (string.IsNullOrEmpty(resolved)) return null;

            // Collapse speaker variants of the same character onto a single canonical voice
            // name (see VoiceMap example in the doc summary). GetNpcName falls through to
            // the input when no VoiceMap matches, so this is a safe no-op for plain NPCs.
            return _jsonData.GetNpcName(resolved);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve gender + race + body type for an NPC. Gender and race are forced to fixed
    /// English so the starter set filenames stay locale-stable (a German client and an
    /// English client produce identical <c>Gender_Race[-BodyType]_*</c> tokens). Race comes
    /// from the English <see cref="Race"/> sheet directly, then runs through
    /// <see cref="VoiceExtractFileNames.NormalizeRace"/> at filename time which maps
    /// <c>"Hyuran"→"Hyur"</c> / <c>"Au Ra"→"AuRa"</c> / <c>"Miqo'te"→"Miqote"</c> onto the
    /// canonical NpcRaces enum tokens used by voice resolution.
    ///
    /// Body type uses ENpcBase's raw byte: <c>4=Child</c>, <c>3=Elder</c>, anything else =
    /// Adult (the implicit default which carries no filename suffix). Mirrors the switch in
    /// <see cref="VoiceMessageProcessor"/> that drives the live <see cref="BodyType"/> enum.
    /// </summary>
    private (string gender, string race, string bodyType) ResolveGenderRaceBody(uint npcId)
    {
        try
        {
            // Gender comes from a numeric byte (0=Male, 1=Female) so it's already locale-free.
            var baseSheet = _dataManager.GetExcelSheet<ENpcBase>();
            if (baseSheet == null) return ("None", "Unknown", "Adult");
            var npcBase = baseSheet.GetRowOrDefault(npcId);
            if (npcBase == null) return ("None", "Unknown", "Adult");
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

            var bodyType = npcBase.Value.BodyType switch
            {
                4 => "Child",
                3 => "Elder",
                _ => "Adult",
            };

            return (gender, race, bodyType);
        }
        catch
        {
            return ("None", "Unknown", "Adult");
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

    /// <summary>Bundles the per-NPC output identity passed to the split-voice writer so the
    /// helper stays under the parameter-count limit.</summary>
    private readonly record struct NamedOutputTarget(
        string OutputRoot, string Gender, string Race, string BodyType, string LocName, string OutputSubfolder);

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
