using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Echokraut.DataClasses;
using Echokraut.Helper.Data;

namespace Echokraut.Helper.Functional;

internal static class CMDHelper
{
    internal static void CallCMD(EKEventId eventId ,string exePath, string command, string methodExtra)
    {
        try
        {
            var process = new Process();

            if (Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows)
            {
                process.StartInfo.FileName = "cmd.exe"; // oder "bash" unter Linux/macOS
                process.StartInfo.Arguments = @$"/c {exePath} {command}";
            }
            else
            {
                process.StartInfo.FileName = "/bin/bash"; // oder "bash" unter Linux/macOS
                process.StartInfo.Arguments = @$"-c {exePath} {command}";
            }

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name + $" | {methodExtra}", @$"Calling command: '{exePath} {command}'", eventId);
            process.Start();

            while (!process.HasExited)
            {
                string output = process.StandardOutput.ReadLine();
                LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name + $" | {methodExtra}", output, eventId);
            }
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
        }
    }

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
