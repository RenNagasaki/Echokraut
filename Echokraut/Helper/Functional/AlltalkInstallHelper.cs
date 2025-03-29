using Echokraut.DataClasses;
using Echokraut.Helper.Data;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Echokraut.Helper.Functional
{
    public static class AlltalkInstallHelper
    {
        public static async void Install(string alltalkUrl, string installDir, string modelUrl, string voicesUrl, EKEventId eventId)
        {
            try
            {
                var installFile = $"{installDir}\\alltalk_tts.zip";
                var installFolder = $"{installDir}\\alltalk_tts";
                var isWindows = System.Runtime.InteropServices.RuntimeInformation
                                       .IsOSPlatform(OSPlatform.Windows);

                var thread = new Thread(() =>
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Downloading alltalk_tts.zip", eventId);
                    using(var client = new HttpClient())
                    {
                        var response = client.GetByteArrayAsync(alltalkUrl);
                        File.WriteAllBytes(installFile, response.Result);
                    }

                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Extracting alltalk_tts.zip", eventId);
                    System.IO.Compression.ZipFile.ExtractToDirectory(installFile, installFolder);

                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Starting install process", eventId);
                    Process p = new Process();
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.CreateNoWindow=true;
                    p.StartInfo.FileName = $"{installFolder}\\atsetup" + (isWindows ? ".bat" : ".sh");
                    p.StartInfo.ArgumentList.Add("-silent");
                    p.Start();

                    while (!p.HasExited)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, output, eventId);
                        Thread.Sleep(200);
                    }
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Done!", eventId);
                });
                thread.IsBackground = true;
                thread.Start();
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while installing alltalk locally: {ex}", eventId);
            }
        }
    }
}
