using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using EchoScd;

namespace Echokraut.Services;

/// <inheritdoc cref="IGameVoiceMappingService"/>
public sealed class GameVoiceMappingService : IGameVoiceMappingService, IDisposable
{
    internal const string AliasResource = "Echokraut.Resources.GameVoiceAliases.json";
    internal const string SplitsResource = "Echokraut.Resources.GameVoiceActorSplits.json";
    internal const string AliasOverrideFile = "game_voice_aliases.json";
    internal const string SplitsOverrideFile = "game_voice_actor_splits.json";

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly ILogService _log;
    private readonly string _configDirectory;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();

    private List<GameVoiceAliasEntry> _embeddedAliases = new();
    private List<GameVoiceAliasEntry> _remoteAliases = new();
    private List<GameVoiceAliasEntry> _localAliases = new();
    private List<VoiceActorSplitEntry> _embeddedSplits = new();
    private List<VoiceActorSplitEntry> _remoteSplits = new();
    private List<VoiceActorSplitEntry> _localSplits = new();

    private IReadOnlyList<GameVoiceAliasEntry> _aliases = Array.Empty<GameVoiceAliasEntry>();
    private VoiceActorSplits _splits = VoiceActorSplits.Build(new VoiceActorSplitsFile { Version = 2 }, out _);

    public event Action? Changed;

    /// <summary>Remote copies are fetched in the background; until then embedded + local apply.</summary>
    public GameVoiceMappingService(ILogService log, string configDirectory, string? remoteAliasesUrl, string? remoteSplitsUrl)
        : this(log, configDirectory, remoteAliasesUrl, remoteSplitsUrl, DefaultFetch)
    {
    }

    internal GameVoiceMappingService(ILogService log, string configDirectory, string? remoteAliasesUrl, string? remoteSplitsUrl,
        Func<string, CancellationToken, Task<string>>? fetch)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _configDirectory = configDirectory ?? "";

        _embeddedAliases = ReadEmbedded<GameVoiceAliasFile>(AliasResource)?.Aliases ?? new();
        _localAliases = ReadLocal<GameVoiceAliasFile>(AliasOverrideFile)?.Aliases ?? new();
        _embeddedSplits = ReadEmbedded<VoiceActorSplitsFile>(SplitsResource)?.Splits ?? new();
        _localSplits = ReadLocal<VoiceActorSplitsFile>(SplitsOverrideFile)?.Splits ?? new();
        Rebuild(raise: false);

        if (fetch != null && (!string.IsNullOrWhiteSpace(remoteAliasesUrl) || !string.IsNullOrWhiteSpace(remoteSplitsUrl)))
            RemoteLoad = Task.Run(() => LoadRemoteAsync(fetch, remoteAliasesUrl, remoteSplitsUrl, _cts.Token));
    }

    /// <summary>The background fetch (tests await it); completed when there is none.</summary>
    internal Task RemoteLoad { get; } = Task.CompletedTask;

    public IReadOnlyList<GameVoiceAliasEntry> Aliases { get { lock (_lock) return _aliases; } }

    public VoiceActorSplits Splits { get { lock (_lock) return _splits; } }

    public IReadOnlyList<GameVoiceAliasEntry> LocalAliases { get { lock (_lock) return _localAliases.ToList(); } }

    public void SetLocalAlias(GameVoiceAliasEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock) _localAliases = GameVoiceAliasMerge.Upsert(_localAliases, entry);
        SaveLocalAliases();
        Rebuild(raise: true);
    }

    public void RemoveLocalAlias(string token)
    {
        lock (_lock) _localAliases = GameVoiceAliasMerge.Remove(_localAliases, token);
        SaveLocalAliases();
        Rebuild(raise: true);
    }

    public int ExportAliasesForRepo(string path)
    {
        List<GameVoiceAliasEntry> merged;
        lock (_lock) merged = GameVoiceAliasMerge.Merge(_embeddedAliases, _remoteAliases, _localAliases);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new GameVoiceAliasFile { Version = 1, Aliases = merged }, WriteOpts));
        return merged.Count;
    }

    private async Task LoadRemoteAsync(Func<string, CancellationToken, Task<string>> fetch, string? aliasesUrl, string? splitsUrl, CancellationToken ct)
    {
        var changed = false;
        var aliases = await FetchAsync<GameVoiceAliasFile>(fetch, aliasesUrl, ct).ConfigureAwait(false);
        var splits = await FetchAsync<VoiceActorSplitsFile>(fetch, splitsUrl, ct).ConfigureAwait(false);
        lock (_lock)
        {
            if (aliases?.Aliases != null) { _remoteAliases = aliases.Aliases; changed = true; }
            if (splits?.Splits != null) { _remoteSplits = splits.Splits; changed = true; }
        }
        if (changed) Rebuild(raise: true);
    }

    private async Task<T?> FetchAsync<T>(Func<string, CancellationToken, Task<string>> fetch, string? url, CancellationToken ct) where T : class
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var json = await fetch(url, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, ReadOpts);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            // Never critical: the embedded copy still applies.
            _log.Warning(nameof(GameVoiceMappingService), $"Remote {url} unavailable, using the embedded copy: {ex.Message}", Event);
            return null;
        }
    }

    private void Rebuild(bool raise)
    {
        lock (_lock)
        {
            _aliases = GameVoiceAliasMerge.Merge(_embeddedAliases, _remoteAliases, _localAliases);
            // Later layer wins per name + language. Deduplicated here, not in VoiceActorSplits.Build:
            // that one warns on every duplicate, and the remote copy repeats the embedded one.
            var byKey = new Dictionary<string, VoiceActorSplitEntry>(StringComparer.Ordinal);
            foreach (var e in _embeddedSplits.Concat(_remoteSplits).Concat(_localSplits))
                if (e != null) byKey[(e.NpcName ?? "").Trim().ToLowerInvariant() + "|" + (e.Language ?? "").Trim().ToUpperInvariant()] = e;
            var merged = new VoiceActorSplitsFile { Version = 2, Splits = byKey.Values.ToList() };
            _splits = VoiceActorSplits.Build(merged, out var warnings);
            foreach (var w in warnings) _log.Warning(nameof(GameVoiceMappingService), w, Event);
        }
        if (raise) Changed?.Invoke();
    }

    private void SaveLocalAliases()
    {
        if (string.IsNullOrEmpty(_configDirectory)) return;
        try
        {
            List<GameVoiceAliasEntry> local;
            lock (_lock) local = _localAliases.ToList();
            Directory.CreateDirectory(_configDirectory);
            File.WriteAllText(Path.Combine(_configDirectory, AliasOverrideFile),
                JsonSerializer.Serialize(new GameVoiceAliasFile { Version = 1, Aliases = local }, WriteOpts));
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(SaveLocalAliases), ex.Message, Event);
        }
    }

    private T? ReadEmbedded<T>(string resource) where T : class
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            return stream == null ? null : JsonSerializer.Deserialize<T>(stream, ReadOpts);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(ReadEmbedded), $"Embedded {resource} unreadable: {ex.Message}", Event);
            return null;
        }
    }

    private T? ReadLocal<T>(string file) where T : class
    {
        if (string.IsNullOrEmpty(_configDirectory)) return null;
        var path = Path.Combine(_configDirectory, file);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), ReadOpts); }
        catch (Exception ex)
        {
            _log.Warning(nameof(ReadLocal), $"{path} unreadable, ignored: {ex.Message}", Event);
            return null;
        }
    }

    private static async Task<string> DefaultFetch(string url, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return await http.GetStringAsync(url, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private static readonly EKEventId Event = new(0, TextSource.None);
}
