using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Echokraut_Server
{
    internal static class SoftwareHelper
    {
        internal static bool IsCudaSoftwareInstalled(Dispatcher dispatcher, TextBlock textBlock)
        {
            LogHelper.Log("Checking for Cuda Install:", dispatcher, textBlock);
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = "/c nvcc --version";
            p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            p.Start();
            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.
            string output = p.StandardOutput.ReadToEnd();
            output += p.StandardError.ReadToEnd();
            LogHelper.Log(output, dispatcher, textBlock);
            p.WaitForExit();

            if (output.Contains("Cuda compilation tools, release 12.1"))
                return true;
                        
            return false;
        }
        internal static void InstallEspeak(Dispatcher dispatcher, TextBlock textBlock)
        {
            LogHelper.Log("Installing Espeak:", dispatcher, textBlock);
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = "msiexec";
            p.StartInfo.Arguments = "/i alltalk_tts_1\\alltalk_tts\\system\\espeak-ng\\espeak-ng-X64.msi /l*v msilog.txt /qn";
            p.StartInfo.Verb = "runas";
            p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            p.Start();
            string output = "\r\n" + p.StandardOutput.ReadToEnd();
            output += "\r\n" + p.StandardError.ReadToEnd();
            LogHelper.Log(output, dispatcher, textBlock);
            p.WaitForExit();
        }

        internal static void CreateHardlinks(string instancePath, string dataPath, Dispatcher dispatcher, TextBlock textBlock)
        {
            LogHelper.Log("Creating Hardlinks:", dispatcher, textBlock);
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = $"/c mklink /D \"{instancePath}\\models\" \"{dataPath}\\models\"";
            p.StartInfo.Verb = "runas";
            p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            p.Start();
            string output = "\r\n" + p.StandardOutput.ReadToEnd();
            output += "\r\n" + p.StandardError.ReadToEnd();
            LogHelper.Log(output, dispatcher, textBlock);
            p.WaitForExit();
            p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = $"/c mklink /D \"{instancePath}\\voices\" \"{dataPath}\\voices\"";
            p.StartInfo.Verb = "runas";
            p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            p.Start();
            output = "\r\n" + p.StandardOutput.ReadToEnd();
            output += "\r\n" + p.StandardError.ReadToEnd();
            LogHelper.Log(output, dispatcher, textBlock);
            p.WaitForExit();
        }

        internal static void InstallAlltalk(string instancePath, Dispatcher dispatcher, TextBlock textBlock)
        {
            LogHelper.Log("Installing Alltalk:", dispatcher, textBlock);
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = true;
            p.StartInfo.FileName = $"{instancePath}\\atsetup.bat";
            p.StartInfo.Arguments = "-silent";
            p.StartInfo.Verb = "runas";
            p.Start();
            while (!p.HasExited && !MainWindow.closing)
            {
                Thread.Sleep(1000);
            }

            if (MainWindow.closing)
                p.Close();
        }

        internal static Process CreateInstance(string instancePath, Dispatcher dispatcher, TextBlock textBlock)
        {
            LogHelper.Log("Starting Alltalk Instance:", dispatcher, textBlock);
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.FileName = $"{instancePath}\\start_alltalk.bat";

            return p;
        }

        internal static void OpenUrl(string url, Dispatcher dispatcher, TextBlock textBlock)
        {
            LogHelper.Log("Opening Cuda URL:", dispatcher, textBlock);
            Process p = new Process();

            try
            {
                p.StartInfo.UseShellExecute = true;
                p.StartInfo.FileName = url;
                p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                p.Start();
            }
            catch (Exception e)
            {
                string output = "\r\n" + p.StandardOutput.ReadToEnd();
                output += "\r\n" + p.StandardError.ReadToEnd();
                LogHelper.Log(output, dispatcher, textBlock);
            }
        }
    }
}
