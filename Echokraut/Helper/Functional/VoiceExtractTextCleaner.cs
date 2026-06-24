using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Stateless cleanup for FFXIV voice-line text. Ported from Echokraut Tools' WorkText /
/// CleanUpLine / ReplaceGenderText (see <c>Echokraut Tools/GetScdHelper.cs</c>) and adapted
/// for Echokraut's runtime context.
///
/// <para>Returns up to two cleaned strings: index 0 = male variant (or only variant when no
/// gender split), index 1 = female variant (only present when the source contained an
/// <c>&lt;If(PlayerParameter(4))…&gt;</c> branch). Empty strings indicate that the line is
/// effectively empty after cleanup and should be skipped.</para>
/// </summary>
public static class VoiceExtractTextCleaner
{
    /// <summary>
    /// Top-level entry point. Mirrors Tools' <c>WorkText</c> shape but takes already-trimmed
    /// raw text (no surrounding quotes assumed).
    /// </summary>
    /// <returns>Length-1 or length-2 array of cleaned variants, or null if the input was
    /// blank after cleanup.</returns>
    public static string[]? Clean(string rawText, ClientLanguage language)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        var text = CleanLine(rawText, language);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var male = text;
        var female = text;

        // Tools strips a specific GC-rank if-block before any other expansion.
        const string gcRankBlock =
            "<if(PLYR: 53 ? 1 ) {<if( 5 ) {<ref:GCRankLimsaFemaleText>} else {<ref:GCRankLimsaMaleText>}>}" +
            " else {<if(PLYR: 54 ? 1 ) {<if( 5 ) {<ref:GCRankGridaniaFemaleText>} else {<ref:GCRankGridaniaMaleText>}>}" +
            " else {<if(PLYR: 55 ? 1 ) {<if( 5 ) {<ref:GCRankUldahFemaleText>} else {<ref:GCRankUldahMaleText>}>} else {}>}>}>";
        if (male.Contains(gcRankBlock))
        {
            male = male.Replace(gcRankBlock, "");
            female = female.Replace(gcRankBlock, "");
        }

        // <If(PlayerParameter(4))…<Else/>…</If> = male/female split. Tools uses textual span
        // matching (not a real parser) — we keep the same heuristic since it has handled
        // FFXIV's voice text reliably for years.
        if (male.Contains("<If(PlayerParameter(4))"))
        {
            var (m, f) = ExpandGenderBranch(male);
            male = m;
            female = f;
        }

        // Collapsed garbage left over from partial if-blocks Tools observed in the wild.
        const string uldahLeftover =
            "<if(PLYR: 55 ? 1 ) {<if(PLYR: 55 ) { 128 } else {<if(PLYR: 55 ? 1 ) {<ref:GCRankUldahMaleText>} else {}> }>} else {";
        const string uldahLeftoverFemale =
            "<if(PLYR: 55 ? 1 ) {<if(PLYR: 55 ) { 128 } else {<if(PLYR: 55 ? 1 ) {<ref:GCRankUldahFemaleText>} else {}> }>} else {";
        const string ifPlyr12Leftover = "<if(PLYR: 12 ) { 13 } else {<if(PLYR: 12 ) { 5 } else {";
        male = male.Replace(uldahLeftover, "");
        female = female.Replace(uldahLeftoverFemale, "");
        male = male.Replace(ifPlyr12Leftover, "");
        female = female.Replace(ifPlyr12Leftover, "");

        // Strip remaining if-blocks and tags. Greedy regex from Tools.
        male = Regex.Replace(male, "<if.*?}>}>", "");
        female = Regex.Replace(female, "<if.*?}>}>", "");
        male = Regex.Replace(male, "<if.*?}>", "");
        female = Regex.Replace(female, "<if.*?}>", "");
        male = Regex.Replace(male, "<.*?>", "");
        female = Regex.Replace(female, "<.*?>", "");

        male = male.Replace("}>}>", "").Replace("}>", "").Replace("} else {", "");
        female = female.Replace("}>}>", "").Replace("}>", "").Replace("} else {", "");

        return male == female ? new[] { male } : new[] { male, female };
    }

    /// <summary>
    /// First-pass cleanup: strips game-text tags, encoded entities, garbage placeholders,
    /// and Japanese stutter markers (in non-JP locales). Whitespace-collapsed and trimmed.
    /// </summary>
    public static string CleanLine(string line, ClientLanguage language)
    {
        line = line
            .Replace("<br>", " ")
            .Replace("<i>", "")
            .Replace("</i>", "")
            .Replace(" <forename>", "")
            .Replace(" <surname>", "")
            .Replace(" <forename surname>", "")
            .Replace("<forename>", "")
            .Replace("<surname>", "")
            .Replace("<forename surname>", "")
            .Replace("<?0x32>", "Handwerker")
            .Replace(",,", ",")
            .Replace("（★未使用／削除予定★）", "")
            .Replace("__", "_")
            .Replace("", "")
            .Replace("", "")
            .Replace("\\u0003", "")
            .Replace("\\u0005", "")
            .Replace("\\u00e1", "á")
            .Replace("\\u00e9", "é")
            .Replace("\\u00ed", "í")
            .Replace("\\u00f3", "ó")
            .Replace("\\u00fa", "ú")
            .Replace("\\u00f1", "ñ")
            .Replace("\\u00e0", "à")
            .Replace("\\u00e8", "è")
            .Replace("\\u00ec", "ì")
            .Replace("\\u00f2", "ò")
            .Replace("+", "plus ")
            .Replace("=", "gleicht")
            .Replace("F20223", "")
            .Replace("ALT1:", "")
            .Replace("ALT2:", "")
            .Replace("ALT:", "");

        // Garbled control-char remnants Tools observed in localized text dumps.
        string[] garbled = { "�V", "�3", "�E", "�:", "�2", "�>", "�A", "�@", "�G" };
        foreach (var g in garbled) line = line.Replace(g, "");

        // Japanese stutter / placeholder phrases in non-JP locales (these appear when a
        // localizer left an untranslated marker in the row).
        if (language != ClientLanguage.Japanese)
        {
            string[] jpStutters =
            {
                "うわぁぁぁっ！！", "ぎゃああ！", "(仮)にぎやかし381_森",
                "ハァ……ハァ……", "ハァ……", "スゥー", "ぜぇ……ぜぇ……",
                "ゴホッ……ゴホッ", "うん",
                "「XXXX]で「イベントアイテムB」を使い、 しばらく影で待機するんだ。 そうすれば、お目当ての敵がやってくるからね。",
                "さぁ、いっておいで。 「XXXX]で「イベントアイテムB」を使い、しばらく影で待機するんだ。 そうすれば、お目当ての敵がやってくるからね。",
                "EItem：クエスト：GaiUsa912_02を使ってBnpc_GaiUsa912_00を倒す",
                "（コスタの柱にアイテムを使うとアンモがPOP*5）",
            };
            foreach (var s in jpStutters) line = line.Replace(s, "");
        }

        // "...word" → "... word" so TTS phrases pause naturally on ellipses.
        line = Regex.Replace(line, @"(\.{3})(\w)", "$1 $2");

        // Curly quotes → straight, then alternate-pair them back as “ ”.
        line = Regex.Replace(line, "[“”]", "\"");
        var openingQuote = true;
        line = Regex.Replace(line, "\"", _ =>
        {
            var s = openingQuote ? "“" : "”";
            openingQuote = !openingQuote;
            return s;
        });

        line = Regex.Replace(line, "  ", " ");
        line = Regex.Replace(line, @"\(-.*?-\)", "");  // strip (-Speaker-) prefixes
        line = Regex.Replace(line, @"\[.*?\]", "");    // strip [tags]

        if (line.StartsWith(","))
            line = line.Length > 2 ? line.Substring(2) : string.Empty;
        line = line.Trim();

        // Drop leading punctuation (".!?") that ended up at the start after stripping tags.
        while (line.Length > 0 && Regex.IsMatch(line.Substring(0, 1), "[!?.,]"))
            line = line.Substring(1);

        return line.Trim();
    }

    // ── Speech-content filter ────────────────────────────────────────────────
    // Drops non-speech voice candidates (laughs, grunts, single-word exclamations,
    // onomatopoeia, bracketed sound descriptions) before they reach the starter-set /
    // dataset candidate list. Pure + allocation-light; operates on already-Clean()ed text.

    /// <summary>Minimum word count for a Latin-script line to count as speech.
    /// "Yes." / "I see." / "Hmph." are non-speech; "Thank you for coming." qualifies.</summary>
    public const int EnglishMinWords = 4;

    /// <summary>German tends to compound words, so 4 EN words ≈ 3 DE words.</summary>
    public const int GermanMinWords = 3;

    /// <summary>French structure is close to English.</summary>
    public const int FrenchMinWords = 4;

    /// <summary>Japanese uses a char count instead of word count — whitespace splitting is
    /// unreliable for CJK. Kanji density means ~8 chars ≈ 3-4 EN words of content.</summary>
    public const int JapaneseMinChars = 8;

    // A single letter immediately repeated 4+ times in a row (Noooo, Aaaah, Wheee, ははは).
    private static readonly Regex OnomatopoeiaCharRun =
        new(@"(\p{L})\1{3,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // The whole (whitespace-collapsed) string is a 1-3 char unit repeated (Hahaha, Heehee, "Ho ho ho").
    private static readonly Regex OnomatopoeiaReduplication =
        new(@"^(\p{L}{1,3})\1+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Bracketed / asterisked sound descriptions: *sigh*, [laughter]. (CleanLine strips most
    // [..] already; this is belt-and-suspenders plus the *..* form it doesn't touch.)
    private static readonly Regex BracketedSound =
        new(@"^(\*.*\*|\[.*\])$", RegexOptions.Compiled);

    // Outer punctuation stripped before analysis so "..." / "!?" / "……" / "。。。" collapse to
    // empty and word-count runs on bare content. Includes common JP punctuation.
    private static readonly char[] OuterPunctuation =
    {
        ' ', '\t', '\n', '\r', '.', '…', '!', '?', ',', ';', ':', '"', '“', '”', '\'',
        '-', '—', '~', '〜', '★', '。', '、', '！', '？', '「', '」', '『', '』', '・',
    };

    /// <summary>
    /// True when <paramref name="cleanedText"/> looks like real spoken dialogue rather than a
    /// non-speech vocalization (laugh, grunt, single-word exclamation, onomatopoeia, sound
    /// description). Operates on already-<see cref="Clean"/>ed text — callers pass
    /// <c>cleaned[0]</c> (the male variant is sufficient; both gender variants share the same
    /// structural content type, so if one is non-speech the other is too).
    ///
    /// <para>Thresholds (<see cref="EnglishMinWords"/> etc.) are starting estimates; per the
    /// plan's Open Question #1 they should be re-tuned against a real SCD text dump if the drop
    /// rate looks off for any language.</para>
    /// </summary>
    public static bool IsSpeech(string cleanedText, ClientLanguage language)
    {
        if (string.IsNullOrWhiteSpace(cleanedText))
            return false;

        var core = cleanedText.Trim();

        // Bracketed / asterisked sound description → not speech.
        if (BracketedSound.IsMatch(core))
            return false;

        // Strip surrounding punctuation; pure-punctuation lines collapse to empty here.
        var stripped = core.Trim(OuterPunctuation);
        if (stripped.Length == 0)
            return false;

        // Whitespace-collapsed form so spaced-out laughs ("Ha ha ha" → "hahaha") are caught
        // by the reduplication check; also the char count used for Japanese.
        var collapsed = new string(stripped.Where(c => !char.IsWhiteSpace(c)).ToArray());

        // Reduplicated-syllable noises (Hahaha, Heehee, "Ho ho ho") — match the collapsed form
        // since the whole token must be a repeated unit. Char-run cries (Noooo, Aaaah) match the
        // spaced form so runs are never fabricated across word boundaries ("see eels").
        if (OnomatopoeiaReduplication.IsMatch(collapsed))
            return false;
        if (OnomatopoeiaCharRun.IsMatch(stripped))
            return false;

        if (language == ClientLanguage.Japanese)
            return collapsed.Length >= JapaneseMinChars;

        var minWords = language switch
        {
            ClientLanguage.German => GermanMinWords,
            ClientLanguage.French => FrenchMinWords,
            _ => EnglishMinWords,
        };
        var words = stripped.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Length;
        return words >= minWords;
    }

    /// <summary>
    /// Splits an <c>&lt;If(PlayerParameter(4))…&lt;Else/&gt;…&lt;/If&gt;</c> branch into
    /// male and female variants. Mirrors Tools' textual-span approach so the behaviour
    /// stays identical.
    /// </summary>
    internal static (string male, string female) ExpandGenderBranch(string text)
    {
        var male = text;
        var female = text;
        while (male.Contains("<If(PlayerParameter(4))"))
        {
            var startIndexCode = male.IndexOf("<If(PlayerParameter(4))");
            var ifBeginText = male.Substring(startIndexCode);
            var endIndexCode = ifBeginText.IndexOf("If>") + 3;
            if (endIndexCode <= 3) break; // malformed — bail out
            var codeSubstring = male.Substring(startIndexCode, endIndexCode);

            var femaleStartIndex = codeSubstring.IndexOf(")>") + 2;
            var femaleEndIndex = codeSubstring.IndexOf("<Else");
            if (femaleEndIndex < femaleStartIndex) break;
            var femaleSubstring = codeSubstring.Substring(femaleStartIndex, femaleEndIndex - femaleStartIndex);
            female = female.Replace(codeSubstring, femaleSubstring);

            var maleStartIndex = codeSubstring.IndexOf("Else/>", femaleEndIndex) + 6;
            var maleEndIndex = codeSubstring.IndexOf("</If>", femaleEndIndex);
            if (maleStartIndex < 6 || maleEndIndex < maleStartIndex) break;
            var maleSubstring = codeSubstring.Substring(maleStartIndex, maleEndIndex - maleStartIndex);
            male = male.Replace(codeSubstring, maleSubstring);
        }
        return (male, female);
    }
}
