using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Running the EchokrauTTS wrapper natively on Linux while this installer runs inside Wine.
/// <para>On Linux the game, the plugin and this exe all live in Wine; the wrapper (torch + CUDA) has to
/// run natively. Wine starts a Linux binary only through <c>start.exe /unix</c> and hands back no
/// process handle for it, so <see cref="System.Diagnostics.Process"/> can neither wait for it nor read
/// its output — that is what made every Linux run die with "No process is associated with this
/// object". The native side is therefore <c>launch_linux.sh</c> (embedded resource), and the two
/// sides talk only through files in <see cref="RunFolderName"/>; see the script header for the
/// protocol.</para>
/// </summary>
static class WineLaunch
{
    public const string RunFolderName = ".linux-run";
    public const string ScriptResourceName = "launch_linux.sh";
    public const string ArgsFileName = "args";
    public const string OutputFileName = "out.log";
    public const string PidFileName = "pid";
    public const string HeartbeatFileName = "heartbeat";
    public const string StopFileName = "stop";
    public const string ExitFileName = "exit";

    /// <summary>Files of one run; cleared before the next run starts.</summary>
    public static readonly string[] RunFiles =
        { ArgsFileName, OutputFileName, PidFileName, HeartbeatFileName, StopFileName, ExitFileName };

    /// <summary>How often the installer touches the heartbeat. The script gives up after 30 s.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound for the script to react to <see cref="StopFileName"/>: one 2 s poll plus
    /// its 10 s TERM grace period, with margin.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Time the script gets to create <see cref="OutputFileName"/> before the launch counts as failed.</summary>
    public static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Content of the <see cref="ArgsFileName"/> file: the install script, then its arguments, one per
    /// line and LF-terminated (bash reads it with <c>mapfile</c>). A line break inside an argument would
    /// silently split it in two, so that is rejected instead.
    /// </summary>
    public static string BuildArgsFile(string installScriptUnixPath, IEnumerable<string> bootstrapArgs)
    {
        var lines = new[] { installScriptUnixPath }.Concat(bootstrapArgs).ToList();
        if (lines.Any(l => l.Contains('\n') || l.Contains('\r')))
            throw new ArgumentException("Launch arguments must not contain line breaks.", nameof(bootstrapArgs));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Arguments for Wine's <c>start.exe</c>: run <c>/bin/bash script runFolder</c> natively.</summary>
    public static IReadOnlyList<string> StartArguments(string scriptUnixPath, string runUnixPath)
        => new[] { "/unix", "/bin/bash", scriptUnixPath, runUnixPath };

    /// <summary>Exit code from the <see cref="ExitFileName"/> file, or null while it is still being written.</summary>
    public static int? ParseExitCode(string content)
        => int.TryParse(content.Trim(), out var code) ? code : null;

    /// <summary>Bash refuses CRLF scripts; the embedded resource may carry them after a Windows checkout.</summary>
    public static string ToLf(string text) => text.Replace("\r\n", "\n");

    /// <summary>
    /// Splits the complete lines off a chunk of tailed output. Only bytes up to the last <c>\n</c> are
    /// consumed — the rest is a line still being written, and cutting there could also split a UTF-8
    /// sequence in half. <paramref name="consumed"/> is how far the caller may advance its position.
    /// </summary>
    public static IReadOnlyList<string> SplitCompleteLines(ReadOnlySpan<byte> chunk, out int consumed)
    {
        var lastNewline = chunk.LastIndexOf((byte)'\n');
        if (lastNewline < 0)
        {
            consumed = 0;
            return Array.Empty<string>();
        }

        consumed = lastNewline + 1;
        return Encoding.UTF8.GetString(chunk[..lastNewline])
                       .Split('\n')
                       .Select(l => l.TrimEnd('\r'))
                       .ToList();
    }

    /// <summary>
    /// Translates a Windows path (as this exe sees it inside Wine) into the Linux path, e.g.
    /// <c>C:\LocalTTS</c> → <c>/home/u/.xlcore/wineprefix/drive_c/LocalTTS</c>. Uses Wine's own
    /// export instead of guessing the prefix layout. Null when not running under Wine.
    /// </summary>
    public static string? ToUnixPath(string windowsPath)
    {
        IntPtr ptr;
        try
        {
            ptr = wine_get_unix_file_name(windowsPath);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        if (ptr == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            HeapFree(GetProcessHeap(), 0, ptr);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr wine_get_unix_file_name(string dosPath);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcessHeap();

    [DllImport("kernel32.dll")]
    private static extern bool HeapFree(IntPtr heap, uint flags, IntPtr mem);
}
