using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// The reason a wrapper launch ended, handed to the plugin next to the ready file.
/// <para>The plugin only sees the installer process end; without this file the Backend tab fell back
/// from "Starting..." to "Start" with the reason only in <c>EchokrautLocalInstaller.log</c>. The plugin
/// reads it after the installer exited without a ready file, so it must survive the installer's own
/// exit (<c>Dispose</c> → <c>StopInstance</c> keeps it) and is cleared only when a new launch begins
/// or the plugin stops the instance on purpose.</para>
/// <para>Format (<see cref="Constants.ECHOKRAUTTSERRORFILE"/>, UTF-8 JSON):
/// <c>{"key": "boot.unsupported_language", "message": "…"}</c>. <c>message</c> is already in the
/// game language (the wrapper localizes it), <c>key</c> is the wrapper's stable catalog key and empty
/// for an error the installer raised itself.</para>
/// </summary>
internal static class WrapperErrorFile
{
    public static string PathIn(string directory) => Path.Join(directory, Constants.ECHOKRAUTTSERRORFILE);

    /// <summary>Writes the file when <paramref name="ndjsonLine"/> is the wrapper's <c>error</c>
    /// event. Later errors replace earlier ones: the last one before the exit is the reason.</summary>
    /// <returns>Whether the line was an error event.</returns>
    public static bool RecordIfError(string directory, string ndjsonLine)
    {
        JObject obj;
        try { obj = JObject.Parse(ndjsonLine); }
        catch (JsonException) { return false; }

        if ((string?)obj["event"] != "error") return false;
        Write(directory, (string?)obj["key"] ?? string.Empty, (string?)obj["message"] ?? string.Empty);
        return true;
    }

    public static void Write(string directory, string key, string message)
        => File.WriteAllText(PathIn(directory),
            JsonConvert.SerializeObject(new { key, message }), new System.Text.UTF8Encoding(false));

    public static void Clear(string directory)
    {
        var path = PathIn(directory);
        if (File.Exists(path)) File.Delete(path);
    }
}
