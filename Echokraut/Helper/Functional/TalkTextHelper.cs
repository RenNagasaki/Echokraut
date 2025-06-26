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
using Dalamud.Game;
using Humanizer;
using System.Globalization;
using Echokraut.Helper.Addons;
using GameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.API;
using Echokraut.Helper.Data;

namespace Echokraut.Helper.Functional
{
    public static partial class TalkTextHelper
    {
        [GeneratedRegex(@"\p{L}+|\p{M}+|\p{N}+|\s+", RegexOptions.Compiled)]
        private static partial Regex SpeakableRegex();

        [GeneratedRegex(@"(?<=\s|^)(\p{L}{1,2})-(?=\1)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
        private static partial Regex StutterRegex();

        [GeneratedRegex(@"(?<=(^|\s)+)([XILMV]+)(?=([ \!\?\.]|$|\n)+)", RegexOptions.Compiled)]
        private static partial Regex RomanNumeralsRegex();

        [GeneratedRegex(@"(\d+)+([\.])+(\d+)", RegexOptions.Compiled)]
        private static partial Regex CurrencyRegex();

        [GeneratedRegex(@"(\d+)", RegexOptions.Compiled)]
        private static partial Regex IntegerRegex();

        [GeneratedRegex(@"(\b(0?[1-9]|[12]\d|30|31)[^\w\d\r\n:](0?[1-9]|1[0-2])[^\w\d\r\n:](\d{4}|\d{2})\b)|(\b(0?[1-9]|1[0-2])[^\w\d\r\n:](0?[1-9]|[12]\d|30|31)[^\w\d\r\n:](\d{4}|\d{2})\b)", RegexOptions.Compiled)]
        private static partial Regex DateRegex();

        [GeneratedRegex(@"(?<=(^|\s|[\.\?\!])+)([0-1]?[0-9]|2[0-3]):[0-5][0-9](?=([ \!\?\.]|$|\n)+)", RegexOptions.Compiled)]
        private static partial Regex TimeRegex();

        [GeneratedRegex("<[^<]*>", RegexOptions.Compiled)]
        private static partial Regex BracketedRegex();

        private static readonly Regex SpeakableRx = SpeakableRegex();
        private static readonly Regex StutterRx = StutterRegex();
        private static readonly Regex RomanNumeralsRx = RomanNumeralsRegex();
        private static readonly Regex CurrencyRx = CurrencyRegex();
        private static readonly Regex IntegerRx = IntegerRegex();
        private static readonly Regex DateRx = DateRegex();
        private static readonly Regex TimeRx = TimeRegex();
        private static readonly Regex BracketedRx = BracketedRegex();

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
                Speaker = ReadTextNode(talkAddon->Speaker),
                Text = ReadTextNode(talkAddon->Text),
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

        public static unsafe string ReadStringNode(Utf8String textNode)
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
            return BracketedRx.Replace(text, "").Trim();
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
                       .Replace("...", " - ")
                       .Replace("–", "-") ??
                   ""; // Hopefully, this one is only in Kan-E-Senna's name? Otherwise, I'm not sure how to parse this correctly.
        }

        public static string ReplacePhonetics(string text, List<PhoneticCorrection> corrections)
        {
            foreach (var correction in corrections) {
                text = text.Replace(correction.OriginalText.ToLower(), correction.CorrectedText.ToLower(), StringComparison.OrdinalIgnoreCase);
            }

            return text;
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
                        world = pp.World.Value.Name.ToString();
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
            name = string.Join("", SpeakableRx.Matches(input.TextValue));
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
                if (!StutterRx.IsMatch(text)) break;
                text = StutterRx.Replace(text, "");
            }

            var isCapitalized = char.IsUpper(text, 0);
            if (startsCapitalized && !isCapitalized)
            {
                text = char.ToUpper(text[0]) + text[1..];
            }

            return text;
        }

        public static string ReplaceEmoticons(EKEventId eventId, string cleanText)
        {
            try
            {
                Regex r = new Regex(string.Join("|", JsonLoaderHelper.Emoticons.Select(s => Regex.Escape(s)).ToArray()));
                cleanText = r.Replace(cleanText, string.Empty);
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", eventId);
            }

            return cleanText;
        }

        public static string ReplaceDate(EKEventId eventId, string cleanText, ClientLanguage language)
        {
            try
            {
                var culture = new CultureInfo("en-US");
                switch (language)
                {
                    case ClientLanguage.English:
                        culture = CultureInfo.CreateSpecificCulture("en-US");
                        break;
                    case ClientLanguage.German:
                        culture = CultureInfo.CreateSpecificCulture("de-DE");
                        break;
                    case ClientLanguage.Japanese:
                        culture = CultureInfo.CreateSpecificCulture("ja-JP");
                        break;
                    case ClientLanguage.French:
                        culture = CultureInfo.CreateSpecificCulture("fr-FR");
                        break;
                }
                var formats = new[] { "M-d-yyyy", "dd-MM-yyyy", "MM-dd-yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "M.d.yyyy", "dd.MM.yyyy" }
                        .Union(culture.DateTimeFormat.GetAllDateTimePatterns()).ToArray();
                var dateRxResult = DateRx.Match(cleanText);
                int i = 0;
                while (dateRxResult.Success)
                {
                    var dateString = dateRxResult.Value;
                    var date = DateTime.ParseExact(dateString, formats, culture, DateTimeStyles.AssumeLocal);
                    var value = date.ToOrdinalWords();

                    var regex = new Regex(Regex.Escape(dateString));
                    cleanText = regex.Replace(cleanText, value.ToString(), 1);
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Replaced '{dateString}' with '{value}'", eventId);

                    dateRxResult = DateRx.Match(cleanText);
                    i++;

                    if (i > 50)
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", eventId);
            }

            return cleanText;
        }

        public static string ReplaceTime(EKEventId eventId, string cleanText, ClientLanguage language)
        {
            try
            {
                CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");
                switch (language)
                {
                    case ClientLanguage.English:
                        culture = CultureInfo.CreateSpecificCulture("en-US");
                        break;
                    case ClientLanguage.German:
                        culture = CultureInfo.CreateSpecificCulture("de-DE");
                        break;
                    case ClientLanguage.Japanese:
                        culture = CultureInfo.CreateSpecificCulture("ja-JP");
                        break;
                    case ClientLanguage.French:
                        culture = CultureInfo.CreateSpecificCulture("fr-FR");
                        break;
                }

                var timeRxResult = TimeRx.Match(cleanText);
                int i = 0;
                while (timeRxResult.Success)
                {                    
                    var timeString = timeRxResult.Value;

                    if (language == ClientLanguage.German)
                    {
                        var time = TimeOnly.ParseExact(timeString, ["HH:mm", "H:mm"], culture, System.Globalization.DateTimeStyles.None);
                        var value = time.Hour.ToWords(culture) + " Uhr " + (time.Minute == 0 ? "" : time.Minute.ToWords(culture));

                        var oldCleanText = cleanText;
                        var regex = new Regex(Regex.Escape(timeString + " (Uhr)"));
                        cleanText = regex.Replace(cleanText, value.ToString(), 1); 
                        regex = new Regex(Regex.Escape(timeString + " Uhr"));
                        cleanText = regex.Replace(cleanText, value.ToString(), 1);
                        if (cleanText == oldCleanText)
                        {
                            regex = new Regex(Regex.Escape(timeString));
                            cleanText = regex.Replace(cleanText, value.ToString(), 1);
                        }
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Replaced '{timeString}' with '{value}'", eventId);
                    }
                    else
                    {
                        var time = TimeOnly.ParseExact(timeString, ["HH:mm", "H:mm"], culture, System.Globalization.DateTimeStyles.None);
                        var value = time.ToClockNotation();

                        var regex = new Regex(Regex.Escape(timeString));
                        cleanText = regex.Replace(cleanText, value.ToString(), 1);
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Replaced '{timeString}' with '{value}'", eventId);
                    }

                    timeRxResult = TimeRx.Match(cleanText);
                    i++;

                    if (i > 50)
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", eventId);
            }

            return cleanText;
        }

        public static string ReplaceRomanNumbers(EKEventId eventId, string cleanText)
        {
            try
            {
                var romanNumerals = RomanNumeralsRx.Match(cleanText);
                int i = 0;
                while (romanNumerals.Success)
                {
                    var romanNumeralsText = romanNumerals.Value;

                    var value = "i";
                    if (romanNumeralsText != "I")
                        value = romanNumeralsText.FromRoman().ToString();

                    var regex = new Regex(Regex.Escape(romanNumeralsText));
                    cleanText = regex.Replace(cleanText, value.ToString(), 1);
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name,
                                    $"Replaced '{romanNumeralsText}' with '{value}'", eventId);

                    romanNumerals = RomanNumeralsRx.Match(cleanText);

                    i++;
                    if (i > 50)
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", eventId);
            }

            return cleanText;
        }

        public static string ReplaceCurrency(EKEventId eventId, string cleanText)
        {
            try
            {
                var currencyNumerals = CurrencyRx.Match(cleanText);
                int i = 0;
                while (currencyNumerals.Success)
                {
                    var currencyNumeralsText = currencyNumerals.Value;
                    var value = currencyNumeralsText.Replace(".", "");

                    var regex = new Regex(Regex.Escape(currencyNumeralsText));
                    cleanText = regex.Replace(cleanText, value.ToString(), 1);
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Replaced '{currencyNumeralsText}' with '{value}'", eventId);

                    currencyNumerals = CurrencyRx.Match(cleanText);
                    i++;

                    if (i > 50)
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", eventId);
            }

            return cleanText;
        }

        internal static string ReplaceIntWithVerbal(EKEventId eventId, string cleanText, ClientLanguage language)
        {
            try
            {
                CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");
                switch (language)
                {
                    case ClientLanguage.English:
                        culture = CultureInfo.CreateSpecificCulture("en-US");
                        break;
                    case ClientLanguage.German:
                        culture = CultureInfo.CreateSpecificCulture("de-DE");
                        break;
                    case ClientLanguage.Japanese:
                        culture = CultureInfo.CreateSpecificCulture("ja-JP");
                        break;
                    case ClientLanguage.French:
                        culture = CultureInfo.CreateSpecificCulture("fr-FR");
                        break;
                }

                var integer = IntegerRx.Match(cleanText);
                int i = 0;
                while (integer.Success)
                {
                    var integerValue = Convert.ToInt32(integer.Value);
                    var value = "";

                    if (cleanText.Length >= (cleanText.IndexOf(integer.Value) + integer.Value.Length + 1) &&
                        cleanText.Substring(cleanText.IndexOf(integer.Value) + integer.Value.Length, 1) == ".")
                    {
                        value = integerValue.ToOrdinalWords(culture);
                        var regex = new Regex(Regex.Escape(integer.Value + "."));
                        cleanText = regex.Replace(cleanText, value.ToString(), 1);
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Replaced '{integer.Value}' with '{value}'", eventId);
                    }
                    else
                    {
                        value = integerValue.ToWords(culture);
                        var regex = new Regex(Regex.Escape(integer.Value));
                        cleanText = regex.Replace(cleanText, value.ToString(), 1);
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Replaced '{integer.Value}' with '{value}'", eventId);
                    }

                    integer = IntegerRx.Match(cleanText);
                    i++;

                    if (i > 50)
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", eventId);
            }

            return cleanText;
        }

        public static bool IsSpeakable(string text)
        {
            // TextToTalk#41 Unspeakable text
            return SpeakableRx.Match(text).Success;
        }

        public static string AnalyzeAndImproveText(string text)
        {
            var resultText = text;

            resultText = Regex.Replace(resultText, @"(?<=^|[^/.\w])[a-zA-ZäöüÄÖÜ]+[\.\,\!\?](?=[a-zA-ZäöüÄÖÜ])", "$& ");

            return resultText;
        }

        public static string CleanUpName(string name)
        {
            name = name.Replace("[a]", "");
            name = Regex.Replace(name, "[^a-zA-Z0-9-äöüÄÖÜ' ]+", "");

            return name;
        }

        public static string GetPlayerNameWithoutWorld(SeString playerName)
        {
            if (playerName.Payloads.FirstOrDefault(p => p is PlayerPayload) is PlayerPayload player)
            {
                return player.PlayerName;
            }

            return playerName.TextValue;
        }

        public static unsafe string GetBubbleName(GameObject? speaker, string text)
        {
            var territory = LuminaHelper.GetTerritory();
            var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
            var modelData = charaStruct->ModelContainer.ModelSkeletonId;
            var modelData2 = charaStruct->ModelContainer.ModelSkeletonId_2;

            var activeData = modelData;
            if (activeData == -1)
                activeData = modelData2;

            text = AudioFileHelper.VoiceMessageToFileName(text);
            var textSubstring = text.Length > 20 ? text.Substring(0, 20) : text;
            return $"BB-{territory.Value.PlaceName.Value.Name.ToString()}-{activeData}-{textSubstring}";
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
