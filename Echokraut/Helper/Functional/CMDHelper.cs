using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Echokraut.Helper.Functional;

internal static class CMDHelper
{
    internal static string CleanAnsi(string input)
    {
        return Regex.Replace(input, @"\x1B\[[0-9;]*[mK]", "").Replace(" ", "  ");
    }

    internal static void OpenUrl(string url)
    {
        if (Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows)
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
    }
}
