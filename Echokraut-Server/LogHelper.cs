using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Echokraut_Server
{
    internal static class LogHelper
    {
        static string LogFile = DateTime.Now.ToString("yyyyMMdd_HHmm") + "_Echokraut_Server_";

        internal static void Log(string message, Dispatcher dispatcher, TextBlock textBlock, int instance = 0)
        {
            dispatcher.Invoke(() => {
                textBlock.Text += $"\r\n{message}";
            });

            File.AppendAllText(LogFile + instance + ".log", message);
        }
    }
}
