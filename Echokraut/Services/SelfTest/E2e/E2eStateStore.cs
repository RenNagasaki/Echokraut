using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// The files of an E2E run in <c>pluginConfigs\Echokraut-e2e\</c> — next to, not inside, the plugin's
/// config folder, because the reset (build step 2) wipes <c>pluginConfigs\Echokraut\</c>.
/// </summary>
public sealed class E2eStateStore
{
    public const string RequestFileName = "request.json";
    public const string StateFileName = "state.json";

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public E2eStateStore(string directory) => Directory = directory;

    /// <summary>The E2E folder, e.g. <c>%APPDATA%\XIVLauncher\pluginConfigs\Echokraut-e2e</c>.</summary>
    public string Directory { get; }

    public string RequestPath => Path.Combine(Directory, RequestFileName);
    public string StatePath => Path.Combine(Directory, StateFileName);
    public string ReportPath(string id) => Path.Combine(Directory, $"e2e-{id}.json");

    /// <summary>Derives the E2E folder from the plugin's config folder (<c>…\pluginConfigs\Echokraut</c>).</summary>
    public static string DirectoryFor(string pluginConfigDirectory) =>
        Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(pluginConfigDirectory))!, "Echokraut-e2e");

    public bool RequestExists => File.Exists(RequestPath);
    public bool StateExists => File.Exists(StatePath);

    /// <summary>
    /// Reads the request and deletes it at once, so a crash while the run starts can never trigger it
    /// a second time. Null when there is none or it cannot be read (still deleted then).
    /// </summary>
    public string? TakeRequest()
    {
        if (!RequestExists) return null;
        try
        {
            return File.ReadAllText(RequestPath);
        }
        catch (IOException)
        {
            return null; // still being written: next poll
        }
        finally
        {
            try { File.Delete(RequestPath); } catch { /* next poll retries */ }
        }
    }

    /// <summary>The saved state; null when there is none or it is unreadable.</summary>
    public E2eState? Load()
    {
        try
        {
            return StateExists ? JsonSerializer.Deserialize<E2eState>(File.ReadAllText(StatePath), JsonOpts) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes state.json atomically: a temp file next to it, then a replacing move.</summary>
    public void Save(E2eState state)
    {
        System.IO.Directory.CreateDirectory(Directory);
        WriteAtomic(StatePath, JsonSerializer.Serialize(state, JsonOpts));
    }

    /// <summary>Writes the report atomically and returns its path.</summary>
    public string WriteReport(string json, string id)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = ReportPath(id);
        WriteAtomic(path, json);
        return path;
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
