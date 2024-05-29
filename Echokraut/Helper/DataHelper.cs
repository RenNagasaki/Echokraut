using Echokraut.DataClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Echokraut.Helper
{
    public static class DataHelper
    {
        static public NpcMapData getNpcMapData(List<NpcMapData> datas, NpcMapData data)
        {
            NpcMapData result = null;

            foreach (var item in datas)
            {
                if (item.ToString() == data.ToString())
                {
                    result = item;
                    break;
                }
            }

            return result;
        }

        static public string analyzeAndImproveText(string text)
        {
            var resultText = text;

            resultText = Regex.Replace(resultText, "(?<=^|[^/.\\w])[a-zA-Z]+[\\.\\,\\!\\?](?=[a-zA-ZäöüÄÖÜ])", "$& ");

            return resultText;
        }

        static public string cleanUpName(string name)
        {
            name = name.Replace("[a]", "");
            name = Regex.Replace(name, "[^a-zA-Z0-9-' ]+", "");
            name = name.Replace(" ", "+").Replace("'", "=");

            return name;
        }

        static public string unCleanUpName(string name)
        {
            name = name.Replace("+", " ").Replace("=", "'");

            return name;
        }
    }
}
