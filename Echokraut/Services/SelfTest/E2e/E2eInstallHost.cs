using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>Machine-side work of the <c>first-time</c> stage; a mock in the stage tests.</summary>
public interface IE2eInstallHost
{
    /// <summary>
    /// Makes <paramref name="root"/> an empty folder holding only <see cref="E2eRootGuard.MarkerFileName"/>
    /// (D4: every run is a fresh install; marker before the install [M7]). Null on success, else why not.
    /// An existing folder without the marker is never touched.
    /// </summary>
    string? PrepareRoot(string root);

    /// <summary><c>ShareLinesWithCommunityDb</c> of the backed-up <c>Echokraut.json</c>; null when unreadable.</summary>
    bool? BackedUpShareLines(string backupDir);

    /// <summary>
    /// Full path of the executable that serves <c>127.0.0.1:port</c>; null when unknown. A Windows venv
    /// <c>python.exe</c> is only a launcher that starts the base interpreter (uv's, under %APPDATA%) with the
    /// same command line, so when the listener's parent has the same file name the parent's path counts.
    /// </summary>
    string? ListenerExecutable(int port);
}

public sealed class E2eInstallHost : IE2eInstallHost
{
    public const string ConfigFileName = "Echokraut.json";

    public string? PrepareRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                if (!File.Exists(Path.Combine(root, E2eRootGuard.MarkerFileName)))
                    return $"{root} exists without {E2eRootGuard.MarkerFileName} - not emptied.";
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    // A junction inside must not take its target with it: delete the link, not the content.
                    var info = new DirectoryInfo(dir);
                    if (info.LinkTarget != null) info.Delete();
                    else info.Delete(recursive: true);
                }
                foreach (var file in Directory.EnumerateFiles(root))
                    File.Delete(file);
            }
            else
            {
                Directory.CreateDirectory(root);
            }
            File.WriteAllText(Path.Combine(root, E2eRootGuard.MarkerFileName),
                "Echokraut E2E install root. The E2E run empties this folder before every install.\r\n");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not prepare {root}: {ex.Message}";
        }
    }

    public bool? BackedUpShareLines(string backupDir)
    {
        try
        {
            var path = Path.Combine(backupDir, ConfigFileName);
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty(nameof(DataClasses.Configuration.ShareLinesWithCommunityDb), out var v))
                return true; // absent = the Configuration default
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? ListenerExecutable(int port)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var pid = ListenerPid(port);
        if (pid is not { } p) return null;
        var exe = ExecutableOf(p);
        if (exe == null) return null;
        var parent = ParentPid(p) is { } pp ? ExecutableOf(pp) : null;
        return LauncherOrSelf(exe, parent);
    }

    /// <summary>The venv launcher's path when <paramref name="parentExe"/> has the listener's file name, else the listener's.</summary>
    internal static string LauncherOrSelf(string exe, string? parentExe) =>
        parentExe != null && string.Equals(FileName(exe), FileName(parentExe), StringComparison.OrdinalIgnoreCase)
            ? parentExe
            : exe;

    private static string FileName(string path) => path.Replace('\\', '/').Split('/')[^1];

    private static string? ExecutableOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    // ── CreateToolhelp32Snapshot (parent PID) ─────────────────────────────────────────────────────
    private const uint Th32csSnapProcess = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static int? ParentPid(int pid)
    {
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return null;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                if (entry.th32ProcessID == (uint)pid) return (int)entry.th32ParentProcessID;
            return null;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    // ── GetExtendedTcpTable (IPv4 listeners with owning PID) ──────────────────────────────────────
    private const int AfInet = 2;
    private const int TcpTableOwnerPidListener = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

    private static int? ListenerPid(int port)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0) != 0) return null;
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(buffer + 4 + i * rowSize);
                // The port is in network byte order in the low 16 bits.
                var rowPort = (ushort)IPAddress.NetworkToHostOrder((short)(row.LocalPort & 0xFFFF));
                if (rowPort == port) return (int)row.OwningPid;
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
