using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Echokraut.DataClasses
{
    public static class NativeLibraryLoader
    {
        /// <summary>
        /// Pre-loads native libraries from the plugin's assembly directory so that
        /// shadow-loading doesn't break P/Invoke resolution.
        /// Call this from the Plugin constructor before any ManagedBass code runs.
        /// </summary>
        /// <param name="assemblyDir">
        /// The directory containing the plugin DLL and its native dependencies
        /// (e.g. <c>Path.GetDirectoryName(pluginInterface.AssemblyLocation.FullName)</c>).
        /// </param>
        public static void Initialize(string assemblyDir)
        {
            var nativeLibraries = new List<nint>();
            var handle = NativeLibrary.Load(Path.Combine(assemblyDir, "bass.dll"));
            nativeLibraries.Add(handle);

            var assemblyLoadContext = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
            if (assemblyLoadContext == null) return;
            nativeLibraries.Reverse();
            assemblyLoadContext.Unloading += _ => { nativeLibraries.ForEach(NativeLibrary.Free); };
        }
    }
}
