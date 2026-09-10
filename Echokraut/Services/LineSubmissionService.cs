using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

/// <inheritdoc cref="ILineSubmissionService"/>
public sealed class LineSubmissionService : ILineSubmissionService, IDisposable
{
    /// <summary>Send when this many lines are buffered, without waiting for the timer.</summary>
    private const int BatchSize = 50;

    /// <summary>Hard cap the server enforces per live request; a larger body comes back as 400.</summary>
    private const int MaxLinesPerRequest = 200;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);

    /// <summary>Server cap for the bulk endpoint; a bigger chunk comes back as 400.</summary>
    private const int ImportChunkSize = 1000;

    /// <summary>
    /// Gap between import chunks. The server allows only a handful of import requests per minute and
    /// says why: a harvest should take hours, not minutes. Pacing ourselves is friendlier than being
    /// throttled, and a 429 would cost the chunk anyway.
    /// </summary>
    private static readonly TimeSpan ImportPacing = TimeSpan.FromSeconds(11);

    /// <summary>
    /// One client for the process. A <c>new HttpClient</c> per call has exhausted sockets twice in
    /// this repo already (the backend probe and the AllTalk streaming client).
    /// </summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IRemoteUrlService _remoteUrls;
    private readonly IGameObjectService _gameObjects;
    private readonly IAudioFileService _audioFiles;
    private readonly IDatabaseService _db;
    private readonly string _sentFilePath;

    private readonly ConcurrentQueue<EcholinesLine> _pending = new();

    /// <summary>
    /// Lines this installation has already sent. NOT "lines we know" — whether the line exists
    /// locally or has audio is irrelevant to reporting; this only stops us sending the same line
    /// over and over. Persisted so a restart does not re-send a session's worth of lines.
    /// </summary>
    private readonly HashSet<string> _alreadySent = new(StringComparer.Ordinal);
    private readonly object _sentLock = new();

    /// <summary>
    /// Cancelled on dispose. The bulk import runs for about an hour; without this it would keep
    /// walking pages and writing config after the plugin is gone.
    /// </summary>
    private readonly CancellationTokenSource _lifetime = new();

    private readonly Timer _timer;
    private int _flushing;
    private volatile bool _stopped;

    public LineSubmissionService(ILogService log, Configuration config, IRemoteUrlService remoteUrls,
        IGameObjectService gameObjects, IAudioFileService audioFiles, IDatabaseService db,
        string configDirectory)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        _audioFiles = audioFiles ?? throw new ArgumentNullException(nameof(audioFiles));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _sentFilePath = Path.Combine(configDirectory, "echolines-sent.txt");

        LoadSentKeys();

        // A timer, not a frame hook: reporting must never touch the frame thread, and a 60 s cadence
        // has no business being driven by the render loop.
        _timer = new Timer(_ => FlushSafely(), null, FlushInterval, FlushInterval);
    }

    /// <inheritdoc/>
    public void Report(NpcMapData speaker, uint npcBaseId, string originalText, TextSource source,
        ClientLanguage language)
    {
        try
        {
            if (_stopped) return;
            if (!_config.ShareLinesWithCommunityDb) return;
            if (!EcholinesRules.IsReportableSource(source)) return;
            if (string.IsNullOrWhiteSpace(_remoteUrls.Urls.EcholinesUrl)) return;
            if (speaker == null || string.IsNullOrWhiteSpace(originalText)) return;

            // Tokenise BEFORE anything else touches the text: the player's name must not reach the
            // suppression key either, or the same line would be a different key on every character.
            var text = _audioFiles.RemovePlayerNameInText(originalText);
            if (!EcholinesRules.IsSendableText(text, _gameObjects.LocalPlayerName)) return;

            var lang = EcholinesRules.LanguageCode(language);
            var npc = speaker.Name ?? string.Empty;
            var hash = TalkTextHelper.VoiceMessageToFileName(text);
            var key = EcholinesRules.SuppressionKey(lang, npc, hash);

            lock (_sentLock)
            {
                if (!_alreadySent.Add(key)) return;
            }

            _pending.Enqueue(EcholinesLine.Create(lang, npc, speaker.Gender.ToString(),
                speaker.Race.ToString(), npcBaseId, text, source.ToString()));

            if (_pending.Count >= BatchSize)
                ThreadPool.QueueUserWorkItem(_ => FlushSafely());
        }
        catch (Exception ex)
        {
            // Reporting is a side channel. It must never take a dialogue line down with it.
            _log.Warning(nameof(Report), $"Line reporting failed, dropping the line: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }

    /// <inheritdoc/>
    public void FlushNow() => FlushSafely();

    private void FlushSafely()
    {
        // One flush at a time: the timer and the batch-size trigger can fire together, and sending
        // the same buffer twice would spend the installation's rate budget on duplicates.
        if (Interlocked.Exchange(ref _flushing, 1) == 1) return;

        try
        {
            Flush();
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(FlushSafely), $"Sending lines failed: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }
    }

    private void Flush()
    {
        var baseUrl = _remoteUrls.Urls.EcholinesUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) return;

        while (true)
        {
            var batch = new List<EcholinesLine>(MaxLinesPerRequest);
            while (batch.Count < MaxLinesPerRequest && _pending.TryDequeue(out var line))
                batch.Add(line);

            if (batch.Count == 0) return;

            var request = new EcholinesRequest
            {
                InstallId = EnsureInstallId(),
                PluginVersion = Plugin.PluginVersion,
                Origin = "live",
                Lines = batch,
            };

            LogBatch("live", batch);

            if (!Send(baseUrl.TrimEnd('/') + "/v1/lines", request))
            {
                // Failed sends are dropped rather than retried: their keys are already in the
                // suppression set, and a retry queue would grow without bound during an outage. The
                // database is built from many installations, so one lost report costs nothing.
                _log.Warning(nameof(Flush), $"Dropped {batch.Count} unreported lines.",
                    new EKEventId(0, TextSource.None));
                return;
            }

            PersistSentKeys(batch);
        }
    }

    /// <summary>
    /// Writes out exactly what is about to leave the machine, at <b>Debug</b> level.
    ///
    /// <para>Not a nicety: the promises this feature makes to the user — no chat, no character name —
    /// are otherwise unverifiable from outside. Without this a tester can only confirm that
    /// <i>something</i> was sent, not that the right thing was. Debug level keeps it off in normal
    /// play, where it would be thousands of lines a session.</para>
    /// </summary>
    private void LogBatch(string what, List<EcholinesLine> batch)
    {
        var eventId = new EKEventId(0, TextSource.None);
        _log.Debug(nameof(LogBatch), $"Echolines {what}: sending {batch.Count} line(s).", eventId);

        foreach (var line in batch)
            _log.Debug(nameof(LogBatch), $"  [{line.Source}] {line.Lang} | {line.Npc} | {line.Text}", eventId);
    }

    /// <summary>Blocking wrapper for the live path, which already runs on a timer thread.</summary>
    private bool Send(string url, EcholinesRequest request)
        => SendAsync(url, request, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// The one place that builds and sends a request, so the header, the user agent and the error
    /// handling cannot drift apart between the live path and the bulk import.
    /// </summary>
    private async Task<bool> SendAsync(string url, EcholinesRequest request, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            // Also as a header, not only in the body: the rate limiter runs before the body is read,
            // so without this the budget is per IP and everyone behind one NAT shares it.
            message.Headers.TryAddWithoutValidation("X-Install-Id", request.InstallId);
            message.Headers.TryAddWithoutValidation("User-Agent", "Echokraut/" + Plugin.PluginVersion);

            using var response = await Http.SendAsync(message, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return true;

            // 400 (malformed or oversized) and 429 (too fast) are both "our problem, try later" and
            // never reach the user.
            _log.Warning(nameof(SendAsync), $"Echolines answered {(int)response.StatusCode}.",
                new EKEventId(0, TextSource.None));
            return false;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the caller's business, not a transport failure.
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(SendAsync), $"Echolines unreachable: {ex.Message}",
                new EKEventId(0, TextSource.None));
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task SubmitHarvestAsync(ClientLanguage language, CancellationToken ct)
    {
        var baseUrl = _remoteUrls.Urls.EcholinesUrl;
        if (!_config.ShareLinesWithCommunityDb || string.IsNullOrWhiteSpace(baseUrl)) return;

        var lang = EcholinesRules.LanguageCode(language);
        var eventId = new EKEventId(0, TextSource.None);

        // Linked, so both the caller's cancellation and plugin shutdown stop the run.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        ct = linked.Token;

        try
        {
            var total = _db.GetVoiceClipCountForLanguage((int)language);
            if (total == 0) return;

            var chunkCount = EcholinesRules.ChunkCount(total, ImportChunkSize);
            var saved = _config.EcholinesImportProgress.TryGetValue(lang, out var stored) ? stored : 0;
            var startChunk = EcholinesRules.ResumeChunk(saved, chunkCount);

            _log.Info(nameof(SubmitHarvestAsync),
                $"Submitting harvest for '{lang}': {total} lines, starting at chunk {startChunk + 1}/{chunkCount}.",
                eventId);

            var url = baseUrl.TrimEnd('/') + "/v1/lines/import";

            for (var chunk = startChunk; chunk < chunkCount; chunk++)
            {
                ct.ThrowIfCancellationRequested();

                var page = _db.GetVoiceClipsForLanguage((int)language, chunk * ImportChunkSize, ImportChunkSize);
                if (page.Count == 0) break;

                var lines = BuildImportLines(page, lang);
                if (lines.Count > 0)
                {
                    var request = new EcholinesRequest
                    {
                        InstallId = EnsureInstallId(),
                        PluginVersion = Plugin.PluginVersion,
                        Origin = "harvest",
                        Chunk = chunk,
                        ChunkCount = chunkCount,
                        Lines = lines,
                    };

                    LogBatch($"harvest chunk {chunk + 1}/{chunkCount}", lines);

                    if (!await SendAsync(url, request, ct).ConfigureAwait(false))
                    {
                        // Stop rather than push on: progress stays where it is, so the next run
                        // resumes at this chunk instead of losing the hour already spent.
                        _log.Warning(nameof(SubmitHarvestAsync),
                            $"Import for '{lang}' stopped at chunk {chunk + 1}/{chunkCount}; will resume later.",
                            eventId);
                        return;
                    }
                }

                SaveImportProgress(lang, chunk + 1);
                await Task.Delay(ImportPacing, ct).ConfigureAwait(false);
            }

            // Done: drop the marker so a later harvest of the same language starts clean.
            _config.EcholinesImportProgress.Remove(lang);
            _config.Save();
            _log.Info(nameof(SubmitHarvestAsync), $"Harvest import for '{lang}' complete.", eventId);
        }
        catch (OperationCanceledException)
        {
            _log.Info(nameof(SubmitHarvestAsync), $"Harvest import for '{lang}' cancelled; progress kept.", eventId);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(SubmitHarvestAsync), $"Harvest import for '{lang}' failed: {ex.Message}", eventId);
        }
    }

    /// <summary>
    /// Turns one page of stored clips into payload lines, dropping anything that must not be sent.
    /// Same tokenisation and same guard as the live path: a harvested row can still carry a real
    /// character name if it was recorded before the tokeniser knew the player's name.
    /// </summary>
    private List<EcholinesLine> BuildImportLines(List<DataClasses.Database.VoiceClipEntity> page, string lang)
    {
        var playerName = _gameObjects.LocalPlayerName;
        var lines = new List<EcholinesLine>(page.Count);

        foreach (var clip in page)
        {
            var source = (TextSource)clip.TextSource;
            if (!EcholinesRules.IsReportableSource(source)) continue;

            var text = _audioFiles.RemovePlayerNameInText(clip.OriginalText ?? string.Empty);
            if (!EcholinesRules.IsSendableText(text, playerName)) continue;

            lines.Add(EcholinesLine.Create(
                lang,
                clip.Character?.Name ?? string.Empty,
                ((Enums.Genders)(clip.Character?.Gender ?? 0)).ToString(),
                ((Enums.NpcRaces)(clip.Character?.Race ?? 0)).ToString(),
                clip.NpcBaseId > 0 ? (uint)clip.NpcBaseId : 0u,
                text,
                source.ToString()));
        }

        return lines;
    }

    private void SaveImportProgress(string lang, int nextChunk)
    {
        _config.EcholinesImportProgress[lang] = nextChunk;
        _config.Save();
    }

    /// <summary>
    /// The installation id is created on first use, not in the config initialiser — an existing
    /// config should not silently gain one before the user has had a chance to see the switch.
    /// </summary>
    private string EnsureInstallId()
    {
        if (!string.IsNullOrWhiteSpace(_config.EcholinesInstallId))
            return _config.EcholinesInstallId;

        _config.EcholinesInstallId = Guid.NewGuid().ToString("D");
        _config.Save();
        return _config.EcholinesInstallId;
    }

    private void LoadSentKeys()
    {
        try
        {
            if (!File.Exists(_sentFilePath)) return;

            foreach (var line in File.ReadLines(_sentFilePath))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    _alreadySent.Add(line);
            }
        }
        catch (Exception ex)
        {
            // An unreadable file only means we may report some lines twice, which the server
            // deduplicates anyway. Not worth failing startup over.
            _log.Warning(nameof(LoadSentKeys), $"Could not read the sent-lines file: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }

    /// <summary>
    /// Appends the keys of a successfully sent batch. Append-only and written after the send, so a
    /// crash mid-session costs at most a re-report, never a silently swallowed line.
    /// </summary>
    private void PersistSentKeys(List<EcholinesLine> batch)
    {
        try
        {
            var keys = new List<string>(batch.Count);
            foreach (var line in batch)
                keys.Add(EcholinesRules.SuppressionKey(line.Lang, line.Npc, line.Hash));

            File.AppendAllLines(_sentFilePath, keys);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(PersistSentKeys), $"Could not record sent lines: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }

    public void Dispose()
    {
        if (_stopped) return;

        try { _lifetime.Cancel(); } catch { /* shutting down */ }
        try { _timer.Dispose(); } catch { /* shutting down */ }

        // Send what is still buffered before going away, so a session's last minute is not lost.
        // _stopped is set AFTER this, because it also gates Report and would block the flush path.
        try { FlushSafely(); } catch { /* shutting down */ }

        _stopped = true;

        try { _lifetime.Dispose(); } catch { /* shutting down */ }
    }
}
