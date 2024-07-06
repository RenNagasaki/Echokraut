using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Data.Parsing;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentHousingPlant;
using static System.Windows.Forms.Design.AxImporter;
using Echokraut.Helper;

namespace Echokraut.TextToTalk.Utils
{
    public static partial class TalkUtils
    {
        [GeneratedRegex(@"\p{L}+|\p{M}+|\p{N}+|\s+", RegexOptions.Compiled)]
        private static partial Regex SpeakableRegex();

        [GeneratedRegex(@"(?<=\s|^)(\p{L}{1,2})-(?=\1)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
        private static partial Regex StutterRegex();

        [GeneratedRegex(@"(?<=[ ]+)([XILMV]+)(?=[\.]+)", RegexOptions.Compiled)]
        private static partial Regex RomanNumeralsRegex();

        [GeneratedRegex("<[^<]*>", RegexOptions.Compiled)]
        private static partial Regex BracketedRegex();

        private static readonly Regex Speakable = SpeakableRegex();
        private static readonly Regex Stutter = StutterRegex();
        private static readonly Regex RomanNumerals = StutterRegex();
        private static readonly Regex Bracketed = BracketedRegex();

        public static unsafe AddonTalkText ReadTalkAddon(AddonTalk* talkAddon)
        {
            if (talkAddon is null) return null;
            return new AddonTalkText
            {
                Speaker = ReadTextNode(talkAddon->AtkTextNode220),
                Text = ReadTextNode(talkAddon->AtkTextNode228)
            };
        }

        public static unsafe AddonTalkText ReadTalkAddon(AddonBattleTalk* talkAddon)
        {
            if (talkAddon is null) return null;
            return new AddonTalkText
            {
                Speaker = ReadTextNode(talkAddon->AtkTextNode220),
                Text = ReadTextNode(talkAddon->AtkTextNode228),
            };
        }

        public static unsafe AddonTalkText ReadSelectStringAddon(AddonSelectString* selectStringAddon)
        {
            if (selectStringAddon is null) return null;
            var list = selectStringAddon->PopupMenu.PopupMenu.List;
            if (list is null) return null;
            var selectedItemIndex = list->SelectedItemIndex;
            if (selectedItemIndex < 0 || selectedItemIndex >= list->GetItemCount()) return null;
            var listItemRenderer = list->ItemRendererList[selectedItemIndex].AtkComponentListItemRenderer;
            if (listItemRenderer is null) return null;
            var buttonTextNode = listItemRenderer->AtkComponentButton.ButtonTextNode;
            if (buttonTextNode is null) return null;
            var text = ReadStringNode(buttonTextNode->NodeText);
            return new AddonTalkText
            {
                Speaker = "PLAYER",
                Text = text,
            };
        }

        public static unsafe AddonTalkText ReadCutSceneSelectStringAddon(AddonCutSceneSelectString* cutSceneSelectStringAddon)
        {
            if (cutSceneSelectStringAddon is null) return null;
            var list = cutSceneSelectStringAddon->OptionList;
            if (list is null) return null;
            var selectedItemIndex = list->SelectedItemIndex;
            if (selectedItemIndex < 0 || selectedItemIndex >= list->GetItemCount()) return null;
            var listItemRenderer = list->ItemRendererList[selectedItemIndex].AtkComponentListItemRenderer;
            if (listItemRenderer is null) return null;
            var buttonTextNode = listItemRenderer->AtkComponentButton.ButtonTextNode;
            if (buttonTextNode is null) return null;
            var text = ReadStringNode(buttonTextNode->NodeText);
            return new AddonTalkText
            {
                Speaker = "PLAYER",
                Text = text,
            };
        }

        private static unsafe string ReadTextNode(AtkTextNode* textNode)
        {
            if (textNode == null) return "";

            var textPtr = textNode->NodeText.StringPtr;
            var textLength = textNode->NodeText.BufUsed - 1; // Null-terminated; chop off the null byte
            if (textLength is <= 0 or > int.MaxValue) return "";

            var textLengthInt = Convert.ToInt32(textLength);

            var seString = SeString.Parse(textPtr, textLengthInt);
            return seString.TextValue
                .Trim()
                .Replace("\n", "")
                .Replace("\r", "");
        }

        private static unsafe string ReadStringNode(Utf8String textNode)
        {
            var textPtr = textNode.StringPtr;
            var textLength = textNode.BufUsed - 1; // Null-terminated; chop off the null byte
            if (textLength is <= 0 or > int.MaxValue) return "";

            var textLengthInt = Convert.ToInt32(textLength);

            var seString = SeString.Parse(textPtr, textLengthInt);
            return seString.TextValue
                .Trim()
                .Replace("\n", "")
                .Replace("\r", "");
        }

        public static string StripAngleBracketedText(string text)
        {
            // TextToTalk#17 "<sigh>"
            return Bracketed.Replace(text, "").Trim();
        }

        public static string ReplaceSsmlTokens(string text)
        {
            return text.Replace("&", "and");
        }

        public static string NormalizePunctuation(string? text)
        {
            return text?
                       // TextToTalk#29 emdashes and dashes and whatever else
                       .Replace("─", " - ") // These are not the same character
                       .Replace("—", " - ")
                       .Replace("–", "-") ??
                   ""; // Hopefully, this one is only in Kan-E-Senna's name? Otherwise, I'm not sure how to parse this correctly.
        }

        public static string StripWorldFromNames(SeString message)
        {
            // Remove world from all names in message body
            var world = "";
            var cleanString = new SeStringBuilder();
            foreach (var p in message.Payloads)
            {
                switch (p)
                {
                    case PlayerPayload pp:
                        world = pp.World.Name;
                        break;
                    case TextPayload tp when world != "" && tp.Text != null && tp.Text.Contains(world):
                        cleanString.AddText(tp.Text.Replace(world, ""));
                        break;
                    default:
                        cleanString.Add(p);
                        break;
                }
            }

            return cleanString.Build().TextValue;
        }

        public static bool TryGetEntityName(SeString input, out string name)
        {
            name = string.Join("", Speakable.Matches(input.TextValue));
            foreach (var p in input.Payloads)
            {
                if (p is PlayerPayload pp)
                {
                    // Simplest case; the payload has the raw name
                    name = pp.PlayerName;
                    return true;
                }
            }

            return name != string.Empty;
        }

        /// <summary>
        /// Removes single letters with a hyphen following them, since they aren't read as expected.
        /// </summary>
        /// <param name="text">The input text.</param>
        /// <returns>The cleaned text.</returns>
        public static string RemoveStutters(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var startsCapitalized = char.IsUpper(text, 0);
            while (true)
            {
                if (!Stutter.IsMatch(text)) break;
                text = Stutter.Replace(text, "");
            }

            var isCapitalized = char.IsUpper(text, 0);
            if (startsCapitalized && !isCapitalized)
            {
                text = char.ToUpper(text[0]) + text[1..];
            }

            return text;
        }

        public static string ReplaceRomanNumbers(string text)
        {
            try
            {
                var romanNumerals = RomanNumerals.Match(text);
                if (romanNumerals.Success)
                {
                    var romanNumeralsText = romanNumerals.Value;
                    var value = RomanNumeralsHelper.RomanNumeralsToInt(romanNumeralsText);

                    text = text.Replace(romanNumeralsText, value.ToString());
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}");
            }

            return text;
        }

        public static bool IsSpeakable(string text)
        {
            // TextToTalk#41 Unspeakable text
            return Speakable.Match(text).Success;
        }

        public static string GetPlayerNameWithoutWorld(SeString playerName)
        {
            if (playerName.Payloads.FirstOrDefault(p => p is PlayerPayload) is PlayerPayload player)
            {
                return player.PlayerName;
            }

            return playerName.TextValue;
        }

        public static string ExtractTokens(string text, IReadOnlyDictionary<string, string?> tokenMap)
        {
            // Extract tokens from the longest target values down to the shortest, to e.g.
            // extract full names before first and last names.
            foreach (var (k, v) in tokenMap
                         .Where(kvp => kvp.Value is not null)
                         .OrderByDescending(kvp => kvp.Value?.Length))
            {
                text = text.Replace(v!, k);
            }

            return text;
        }
    }
}
