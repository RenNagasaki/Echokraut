using Dalamud.Plugin.Services;
using Echokraut.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public static class ModuleInitializer
    {
        [ModuleInitializer]
        [SuppressMessage("Usage", "CA2255:The \'ModuleInitializer\' attribute should not be used in libraries")]
        public static void Initialize()
        {
            /*
             * Manually pre-load dependencies so that shadow-loading doesn't break our assembly.
             * This invokes AssemblyLoadContext.LoadUnmanagedDll.
             * https://github.com/goatcorp/Dalamud/issues/1238
             * https://learn.microsoft.com/en-us/dotnet/api/System.Runtime.InteropServices.NativeLibrary.Load?view=net-7.0
             */
            var nativeLibraries = new List<nint>();
            LoadLibrary(nativeLibraries, "bass.dll");
            //LoadLibrary(nativeLibraries, "ManagedBass.dll");

            var assemblyLoadContext = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
            if (assemblyLoadContext == null) return;
            nativeLibraries.Reverse();
            assemblyLoadContext.Unloading += _ => { nativeLibraries.ForEach(NativeLibrary.Free); };
        }

        private static void LoadLibrary(ICollection<nint> handles, string assemblyName)
        {
            var handle = NativeLibrary.Load(
                ResolvePath(assemblyName),
                Assembly.GetExecutingAssembly(),
                DllImportSearchPath.AssemblyDirectory);
            handles.Add(handle);
        }

        private static string ResolvePath(string assemblyPath)
        {
            var location = Assembly.GetCallingAssembly().Location;
            var targetLocation = assemblyPath;// Path.Join(location, "..", assemblyPath);
            return targetLocation;
        }
    }
}
