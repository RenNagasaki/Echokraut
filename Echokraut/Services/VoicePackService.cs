using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

/// <summary>
/// Downloads the curated, freely-licensed voice pack (see <see cref="IVoicePackService"/>) and
/// unpacks it into the target voice folder.
/// </summary>
public sealed class VoicePackService : IVoicePackService, IDisposable
{
    /// <summary>Progress label granularity for the download phase (in percent steps).</summary>
    private const int DownloadReportStepPercent = 2;

    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IRemoteUrlService _remoteUrls;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    private int _running;

    public VoicePackService(ILogService log, Configuration config, IRemoteUrlService remoteUrls)
        : this(log, config, remoteUrls, new HttpClient { Timeout = TimeSpan.FromMinutes(30) }, ownsHttp: true)
    {
    }

    public VoicePackService(ILogService log, Configuration config, IRemoteUrlService remoteUrls,
        HttpClient http, bool ownsHttp = false)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ownsHttp = ownsHttp;
    }

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public event Action<string, int, int>? ProgressChanged;

    public async Task DownloadAsync(CancellationToken ct, string? outputRootOverride = null,
        string outputSubfolder = "Voices")
    {
        // Interlocked instead of a plain bool: the manual UI run and the install flow can race.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            _log.Warning(nameof(DownloadAsync), "Voice pack download already running — ignoring request.",
                new EKEventId(0, TextSource.None));
            return;
        }

        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            await RunInternalAsync(eventId, ct, outputRootOverride, outputSubfolder).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task RunInternalAsync(EKEventId eventId, CancellationToken ct,
        string? outputRootOverride, string outputSubfolder)
    {
        var url = _remoteUrls.Urls.VoicePackUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            // Recoverable: the pack URL is remote config, so an empty value just means "not
            // published yet". The user keeps whatever voices are already installed.
            _log.Warning(nameof(RunInternalAsync),
                "No voice pack URL configured — skipping voice pack download.", eventId);
            Report("No voice pack available", 0, 1);
            return;
        }

        var root = string.IsNullOrWhiteSpace(outputRootOverride) ? _config.LocalSaveLocation : outputRootOverride!;
        var targetDir = Path.Join(root, outputSubfolder);
        var tempZip = Path.Combine(Path.GetTempPath(), $"echokraut-voicepack-{Guid.NewGuid():N}.zip");

        try
        {
            Report("Downloading voice pack", 0, 1);
            await DownloadToFileAsync(url, tempZip, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // Matches the old install semantics: an explicit output root means "this is a fresh
            // engine install, give it a clean voice folder". A manual run keeps existing files.
            if (!string.IsNullOrWhiteSpace(outputRootOverride) && Directory.Exists(targetDir))
            {
                _log.Info(nameof(RunInternalAsync), $"Clearing {targetDir} before unpacking voice pack.", eventId);
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.CreateDirectory(targetDir);
            var written = ExtractZip(tempZip, targetDir, ct);
            WarnAboutUnparsableNames(targetDir, eventId);

            _log.Info(nameof(RunInternalAsync),
                $"Voice pack ready: {written} files written to {targetDir}.", eventId);
            Report($"Done — {written} files written", written, written);
        }
        finally
        {
            try
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(RunInternalAsync),
                    $"Could not delete temporary voice pack file {tempZip}: {ex.Message}", eventId);
            }
        }
    }

    private async Task DownloadToFileAsync(string url, string destination, CancellationToken ct)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Content-Length is absent on chunked responses; fall back to an indeterminate bar.
        var total = response.Content.Headers.ContentLength ?? 0L;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write,
            FileShare.None, 64 * 1024, useAsync: true);

        var buffer = new byte[64 * 1024];
        long received = 0;
        var lastReportedPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;

            if (total <= 0)
                continue;

            var percent = (int)(received * 100 / total);
            if (percent - lastReportedPercent < DownloadReportStepPercent)
                continue;

            lastReportedPercent = percent;
            Report("Downloading voice pack", (int)(received / 1024), (int)(total / 1024));
        }
    }

    /// <summary>
    /// Unpacks <paramref name="zipPath"/> into <paramref name="targetDir"/> and returns the number
    /// of files written. Entries escaping the target directory are skipped (zip-slip guard).
    /// </summary>
    private int ExtractZip(string zipPath, string targetDir, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var fullTarget = Path.GetFullPath(targetDir);
        var total = archive.Entries.Count;
        var written = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // Directory entries have an empty name; the file entries below create the dirs.
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destination = Path.GetFullPath(Path.Combine(fullTarget, entry.FullName));
            if (!destination.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                _log.Warning(nameof(ExtractZip),
                    $"Skipping voice pack entry outside the target folder: {entry.FullName}",
                    new EKEventId(0, TextSource.None));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            written++;
            Report("Unpacking voice pack", written, total);
        }

        return written;
    }

    /// <summary>
    /// Post-unpack sanity check on the sample filenames. The plugin derives a voice's allowed
    /// genders, allowed races and body type PURELY from the filename
    /// (<c>NpcDataService.ReSetVoiceGenders</c> / <c>ReSetVoiceRaces</c> split on <c>_</c> then
    /// <c>-</c>), so a pack that ships e.g. <c>voice_m_07.wav</c> installs fine but ends up with
    /// an empty race/gender filter and is never auto-assigned to anyone. That failure is
    /// completely silent at runtime, hence this explicit warning.
    /// <para>Required grammar: <c>Gender_RacePool[-BodyType]_Name.wav</c>, e.g.
    /// <c>Male_All_M01.wav</c>, <c>Female_All-Child_C03.wav</c>.</para>
    /// </summary>
    private void WarnAboutUnparsableNames(string targetDir, EKEventId eventId)
    {
        try
        {
            var malformed = new List<string>();
            var notPooled = new List<string>();
            foreach (var wav in Directory.EnumerateFiles(targetDir, "*.wav", SearchOption.AllDirectories))
            {
                var baseName = Path.GetFileNameWithoutExtension(wav);
                if (!IsWellFormedVoiceName(baseName))
                    malformed.Add(Path.GetFileName(wav));
                else if (!IsRandomPoolName(baseName))
                    notPooled.Add(Path.GetFileName(wav));
            }

            if (malformed.Count > 0)
                _log.Warning(nameof(WarnAboutUnparsableNames),
                    $"{malformed.Count} voice pack sample(s) don't follow the " +
                    $"Gender_RacePool[-BodyType]_NPCnnn naming convention and will get no " +
                    $"gender/race filter (they will never be auto-assigned to an NPC). " +
                    $"Examples: {string.Join(", ", malformed.Take(5))}", eventId);

            if (notPooled.Count > 0)
                _log.Warning(nameof(WarnAboutUnparsableNames),
                    $"{notPooled.Count} voice pack sample(s) have no \"NPC\" in the name, so " +
                    $"UseAsRandom stays false and they are only selectable manually. " +
                    $"Examples: {string.Join(", ", notPooled.Take(5))}", eventId);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(WarnAboutUnparsableNames),
                $"Could not verify voice pack filenames: {ex.Message}", eventId);
        }
    }

    /// <summary>
    /// True when the first segment parses as a <see cref="Genders"/> value and the second
    /// segment's leading token is a race, the <c>All</c> race pool, or a body type — i.e. the
    /// name carries the metadata the auto-assignment needs. Mirrors the parsing in
    /// <c>NpcDataService.ReSetVoiceGenders</c>/<c>ReSetVoiceRaces</c>.
    /// <para>Purely numeric name segments are rejected on purpose: <c>Enum.TryParse</c> happily
    /// accepts <c>"01"</c> as the enum value 1, so <c>Male_01_Foo</c> would silently be filed as
    /// race Hyur instead of failing loudly.</para>
    /// </summary>
    internal static bool IsWellFormedVoiceName(string baseName)
    {
        var segments = baseName.Split('_');
        if (segments.Length < 3)
            return false;

        if (IsNumeric(segments[0]) || !Enum.TryParse<Genders>(segments[0], ignoreCase: true, out _))
            return false;

        var raceToken = segments[1].Split('-')[0];
        if (IsNumeric(raceToken))
            return false;

        return raceToken.Equals("All", StringComparison.OrdinalIgnoreCase)
               || raceToken.Equals("Child", StringComparison.OrdinalIgnoreCase)
               || raceToken.Equals("Elder", StringComparison.OrdinalIgnoreCase)
               || raceToken.Equals("Adult", StringComparison.OrdinalIgnoreCase)
               || Enum.TryParse<NpcRaces>(raceToken, ignoreCase: true, out _);
    }

    /// <summary>
    /// True when the name marks the voice as part of the random pool. <c>BackendService.MapVoices</c>
    /// sets <c>UseAsRandom = voiceName.Contains("NPC")</c> — <b>case-sensitive substring</b> — and
    /// <c>EchokrautVoice.FitsNpcData</c> requires <c>UseAsRandom</c>, so a pack voice without
    /// <c>NPC</c> in its name is never picked automatically for any NPC.
    /// </summary>
    internal static bool IsRandomPoolName(string baseName)
        => baseName.Contains("NPC", StringComparison.Ordinal);

    private static bool IsNumeric(string segment)
        => segment.Length > 0 && segment.All(char.IsDigit);

    private void Report(string label, int current, int total)
    {
        try
        {
            ProgressChanged?.Invoke(label, current, total);
        }
        catch (Exception ex)
        {
            // A misbehaving subscriber must never abort the download.
            _log.Warning(nameof(Report), $"Voice pack progress subscriber threw: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
