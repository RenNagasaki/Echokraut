using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Echokraut.Utils
{
    public class UngenderedOverrideManager
    {
        private readonly IReadOnlyDictionary<int, bool> ungenderedMap;

        public UngenderedOverrideManager()
        {
            ungenderedMap = ReadAssemblyOverridesFile();
        }

        public UngenderedOverrideManager(string overrideData)
        {
            ungenderedMap = ParseOverridesFile(overrideData);
        }

        public bool IsUngendered(int modelId)
        {
            return ungenderedMap.TryGetValue(modelId, out _);
        }

        private static IReadOnlyDictionary<int, bool> ReadAssemblyOverridesFile()
        {
            using var fileData = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Echokraut.Utils.overridenModelIds.txt");
            if (fileData == null) throw new FileNotFoundException("Failed to load ungendered model overrides file!");
            using var sr = new StreamReader(fileData);
            return ParseOverridesFile(sr.ReadToEnd());
        }

        private static IReadOnlyDictionary<int, bool> ParseOverridesFile(string fileData)
        {
            return fileData.Split('\r', '\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToDictionary(line =>
            {
                line = line.Split(';')[0].Trim(); // Remove comments

                try
                {
                    return int.Parse(line);
                }
                catch (Exception e)
                {
                    throw new AggregateException($"Failed to parse model ID \"{line}\"!", e);
                }
            }, _ => true);
        }
    }
}
