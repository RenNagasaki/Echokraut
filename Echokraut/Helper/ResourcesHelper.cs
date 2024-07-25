using Echokraut.DataClasses;
using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper
{
    public static class ResourcesHelper
    {

        public static string ReadResourceEmbedded(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Failed to load resource: {resourceName}", new EKEventId(0, TextSource.None));
                    return null;
                }
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
