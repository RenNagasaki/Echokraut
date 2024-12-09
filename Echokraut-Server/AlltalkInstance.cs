using Echokraut.DataClasses;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Management;

namespace Echokraut_Server
{
    internal class AlltalkInstance
    {
        internal Process process;
        internal DateTime lastUse = DateTime.Now;
        internal bool ready = false;
        internal int instanceNumber;
        internal int port;
        
        internal AlltalkInstance(int instanceNumber, int port, Process process)
        {
            this.instanceNumber = instanceNumber;
            this.port = port;
            this.process = process;
            lastUse = DateTime.Now;
            ready = false;
        }

        internal void Start()
        {
            this.process.Start();
        }

        internal void Stop()
        {
            KillProcessAndChildren(this.process.Id);
        }

        private static void KillProcessAndChildren(int pid)
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher
              ("Select * From Win32_Process Where ParentProcessID=" + pid);
            ManagementObjectCollection moc = searcher.Get();
            foreach (ManagementObject mo in moc)
            {
                KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
            }
            try
            {
                Process proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }
    }
}
