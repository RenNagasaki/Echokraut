using System;
using System.Collections.Generic;
using System.Text;

namespace Echokraut.Helper.Functional;

/// <summary>How a dialogue box ends, seen by <see cref="SentenceContinuation.EndingOf"/>.</summary>
public enum SentenceEnding
{
    /// <summary>Ends with a sentence terminator (<c>. ! ?</c>, <c>。！？</c>, optionally inside a closing quote).</summary>
    Complete,
    /// <summary>Ends with a word, a comma, a colon, a dash … — the sentence goes on in the next box.</summary>
    Open,
    /// <summary>Ends with an ellipsis: a trailing-off sentence OR one that goes on; the next box decides.</summary>
    Ellipsis,
}

/// <summary>Split of a box into the end of the previous box's sentence and what follows it.</summary>
/// <param name="Head">Text up to and including the first sentence terminator (whole text when there is none).</param>
/// <param name="Rest">Everything after <paramref name="Head"/>, trimmed; empty when the box is one sentence.</param>
/// <param name="LeadingEllipsis">The box started with <c>...</c>/<c>…</c> (removed from <paramref name="Head"/>).</param>
/// <param name="Terminated">A sentence terminator was found (false = the whole box is still the open sentence).</param>
public readonly record struct SentenceHead(string Head, string Rest, bool LeadingEllipsis, bool Terminated);

/// <summary>
/// Pure text rules for "a dialogue box ends mid-sentence": when box N stops in the middle of a
/// sentence, the TTS gets the rest of that sentence from box N+1 (the game sheet's next row), and
/// box N+1 is spoken without it. Without this the voice drops its pitch at the box end as if the
/// sentence were over and starts box N+1 like a new one.
/// </summary>
public static class SentenceContinuation
{
    /// <summary>Normalised tail length that identifies a box (<see cref="TailKey"/>).</summary>
    public const int TailChars = 40;

    /// <summary>Shorter normalised boxes say too little to be matched to one sheet row.</summary>
    public const int MinTailChars = 12;

    /// <summary>A continuation longer than this is not "the rest of a sentence" any more.</summary>
    public const int MaxHeadChars = 300;

    private const string Terminators = ".!?…。！？";
    private const string Closers = "\"'”’»)」』】";
    private const string OpenEnders = ",;:、，-–—";

    public static SentenceEnding EndingOf(string? text)
    {
        var t = (text ?? "").TrimEnd();
        if (t.Length == 0) return SentenceEnding.Complete;
        if (t.EndsWith("...", StringComparison.Ordinal) || t.EndsWith('…') || t.EndsWith("……", StringComparison.Ordinal))
            return SentenceEnding.Ellipsis;

        var i = t.Length - 1;
        while (i >= 0 && Closers.Contains(t[i])) i--;
        if (i < 0) return SentenceEnding.Complete;
        var last = t[i];
        if (Terminators.Contains(last)) return SentenceEnding.Complete;
        if (char.IsLetterOrDigit(last) || OpenEnders.Contains(last)) return SentenceEnding.Open;
        return SentenceEnding.Complete;
    }

    /// <summary>The end of the open sentence at the start of <paramref name="text"/>, see <see cref="SentenceHead"/>.</summary>
    public static SentenceHead SplitHead(string? text)
    {
        var t = (text ?? "").Trim();
        var leading = false;
        if (t.StartsWith("...", StringComparison.Ordinal)) { t = t[3..].TrimStart(); leading = true; }
        else if (t.StartsWith('…')) { t = t.TrimStart('…').TrimStart(); leading = true; }

        for (var i = 0; i < t.Length; i++)
        {
            if (!Terminators.Contains(t[i])) continue;
            var end = i;
            while (end + 1 < t.Length && Terminators.Contains(t[end + 1])) end++;
            while (end + 1 < t.Length && Closers.Contains(t[end + 1])) end++;
            // A terminator inside a word ("3.5", "e.g") is not a sentence end; CJK needs no space.
            var atBoundary = end + 1 >= t.Length || char.IsWhiteSpace(t[end + 1]) || IsCjk(t[i]);
            if (!atBoundary) { i = end; continue; }
            // "war ... verstörend." / "it was... odd.": a pause inside the sentence, not its end.
            if (IsEllipsis(t, i, end) && NextWordStartsLowerCase(t, end + 1)) { i = end; continue; }
            return new SentenceHead(t[..(end + 1)].Trim(), t[(end + 1)..].Trim(), leading, true);
        }
        return new SentenceHead(t, "", leading, false);
    }

    /// <summary>Lower-case letters and digits only: punctuation, spacing and quote styles differ between sheet and addon text.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>The last <see cref="TailChars"/> normalised characters; null when the box is too short to tell rows apart.</summary>
    public static string? TailKey(string? text)
    {
        var n = Normalize(text);
        if (n.Length < MinTailChars) return null;
        return n.Length <= TailChars ? n : n[^TailChars..];
    }

    /// <summary>Whether box <paramref name="current"/> continues into a box that starts with <paramref name="next"/>.</summary>
    public static bool Continues(SentenceEnding ending, SentenceHead next)
    {
        if (next.Head.Length == 0) return false;
        return ending switch
        {
            SentenceEnding.Open => true,
            // "I thought..." / "...that you knew." goes on; "I thought..." / "Never mind." does not.
            SentenceEnding.Ellipsis => next.LeadingEllipsis || StartsLowerCase(next.Head),
            _ => false,
        };
    }

    /// <summary><paramref name="current"/> followed by <paramref name="head"/>; a trailing ellipsis that only marked the box break is dropped.</summary>
    public static string Join(string current, string head)
    {
        var c = current.TrimEnd();
        if (c.EndsWith("...", StringComparison.Ordinal)) c = c[..^3].TrimEnd();
        else c = c.TrimEnd('…').TrimEnd();
        if (c.Length == 0) return head;
        var separator = IsCjk(c[^1]) ? "" : " ";
        return c + separator + head;
    }

    /// <summary>
    /// <paramref name="text"/> without its start when that start normalises to <paramref name="normalizedHead"/>;
    /// null when it does not (the box is not the one the head was taken from).
    /// </summary>
    public static string? StripHead(string text, string normalizedHead)
    {
        if (string.IsNullOrEmpty(normalizedHead)) return null;
        var matched = 0;
        var i = 0;
        for (; i < text.Length && matched < normalizedHead.Length; i++)
        {
            var c = text[i];
            if (!char.IsLetterOrDigit(c)) continue;
            if (char.ToLowerInvariant(c) != normalizedHead[matched]) return null;
            matched++;
        }
        if (matched < normalizedHead.Length) return null;
        // The box's own word may go on ("going" vs. head "go") — then it is not the same text.
        if (i < text.Length && char.IsLetterOrDigit(text[i])) return null;
        while (i < text.Length && !char.IsLetterOrDigit(text[i]) && !IsOpeningPunctuation(text[i])) i++;
        return text[i..].Trim();
    }

    /// <summary>
    /// Index right after the <paramref name="normalizedChars"/>-th letter or digit of <paramref name="text"/>
    /// (0 for 0); -1 when the text has fewer. Maps a <see cref="Normalize"/> position back onto the text.
    /// </summary>
    public static int PositionAfter(string text, int normalizedChars)
    {
        if (normalizedChars <= 0) return 0;
        var seen = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLetterOrDigit(text[i])) continue;
            if (++seen == normalizedChars) return i + 1;
        }
        return -1;
    }

    /// <summary>
    /// Where box <paramref name="box"/> ends inside a row (normalised <paramref name="rowNormalized"/>)
    /// when it starts at normalised position <paramref name="offset"/>; null when the box is not that part of the row.
    /// </summary>
    public static int? EndInRow(string rowNormalized, int offset, string? box)
    {
        var n = Normalize(box);
        if (n.Length == 0 || offset < 0 || offset + n.Length > rowNormalized.Length) return null;
        return string.CompareOrdinal(rowNormalized, offset, n, 0, n.Length) == 0 ? offset + n.Length : null;
    }

    /// <summary>
    /// The game split one sheet row over several Talk boxes and the box shown last ends after
    /// <paramref name="spokenChars"/> normalised characters of <paramref name="row"/>: the rest of the open
    /// sentence from the row itself. Null when the row ends there, the box ends a sentence (or an ellipsis the
    /// row does not go on from) or the rest is too long. The row's own punctuation decides, not the box's:
    /// the live text has <c>...</c> already turned into <c>.</c>.
    /// </summary>
    public static RowContinuation? ContinueInRow(string row, int spokenChars)
    {
        var p = PositionAfter(row, spokenChars);
        if (p < 0 || Normalize(row[p..]).Length == 0) return null;

        // The box broke inside a word ("Vor-" / "bereitungen"): the head goes on without a space.
        var midWord = p > 0 && char.IsLetterOrDigit(row[p]);
        var ending = SentenceEnding.Open;
        if (!midWord)
        {
            // The punctuation behind the box's last word belongs to that box ("zusammen." / "Freunde ...").
            while (p < row.Length && !char.IsLetterOrDigit(row[p]) && !IsOpeningPunctuation(row[p])) p++;
            ending = EndingOf(row[..p]);
            if (ending == SentenceEnding.Complete) return null;
        }

        var head = SplitHead(row[p..]);
        if (!midWord && !Continues(ending, head)) return null;
        if (head.Head.Length == 0 || head.Head.Length > MaxHeadChars) return null;
        return new RowContinuation(head.Head, midWord, spokenChars + Normalize(head.Head).Length);
    }

    /// <summary><see cref="Join"/>, or for a box that broke inside a word: the word put back together (hyphen dropped).</summary>
    public static string Join(string current, string head, bool midWord)
    {
        if (!midWord) return Join(current, head);
        return current.TrimEnd().TrimEnd('-', '­', '‐') + head;
    }

    private static bool IsEllipsis(string t, int from, int to)
    {
        var dots = 0;
        for (var k = from; k <= to; k++)
        {
            if (t[k] == '…') return true;
            if (t[k] == '.') dots++;
        }
        return dots >= 3;
    }

    private static bool NextWordStartsLowerCase(string t, int from)
    {
        var k = from;
        while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
        return k < t.Length && char.IsLetter(t[k]) && char.IsLower(t[k]);
    }

    private static bool StartsLowerCase(string text)
    {
        foreach (var c in text)
            if (char.IsLetter(c)) return char.IsLower(c);
        return false;
    }

    private static bool IsOpeningPunctuation(char c) => c is '"' or '“' or '„' or '«' or '(' or '「' or '『' or '¿' or '¡';

    private static bool IsCjk(char c) => c >= '　' && c <= '鿿' || c >= '＀' && c <= '￯';
}

/// <summary>Rest of the open sentence inside one sheet row, see <see cref="SentenceContinuation.ContinueInRow"/>.</summary>
/// <param name="Head">Text to append (placeholders kept).</param>
/// <param name="MidWord">The box broke inside a word: join without a space.</param>
/// <param name="SpokenChars">Normalised characters of the row spoken once the head is appended.</param>
public sealed record RowContinuation(string Head, bool MidWord, int SpokenChars);

/// <summary>Two consecutive boxes: <paramref name="First"/> ends mid-sentence, <paramref name="Head"/> (the start of <paramref name="Second"/>) ends that sentence.</summary>
public sealed record ContinuationPair(string First, string Second, string Head);

/// <summary>One text row of a game dialogue sheet, in sheet order.</summary>
/// <param name="Key">Text key (column 0), e.g. <c>TEXT_CLSGLA001_00177_THANCRED_000_10</c>; empty when the sheet has none.</param>
/// <param name="Text">Text as <see cref="GameSheetText.ExtractTextWithPlayerName"/> returns it (placeholders kept).</param>
public readonly record struct SheetLine(string Key, string Text);

/// <summary>
/// Lookup "box ends mid-sentence → rest of the sentence" built from the game's dialogue sheets.
/// Only boxes that end open are stored (a small fraction of all rows), keyed by
/// <see cref="SentenceContinuation.TailKey"/>. A tail that leads to different continuations is
/// dropped: guessing wrong would make the NPC say a sentence that is not on screen.
/// </summary>
public sealed class SentenceContinuationIndex
{
    /// <summary>Longest pair (both boxes together) <see cref="Sample"/> takes: a short pair keeps the E2E run's generation fast.</summary>
    internal const int MaxSampleChars = 400;

    private const int MaxSampleCandidates = 64;

    /// <summary>
    /// Shorter rows always fit into one Talk box (a German box holds ~260 characters), so the game
    /// never splits them. Measured 2026-11-22: ~44k of 529k quest/cut_scene rows are longer (DE).
    /// </summary>
    internal const int MinPagedRowChars = 200;

    private readonly Dictionary<string, string?> _heads;

    // Start of a long row (first TailChars normalised chars, as hash) -> rows, UTF-8: ~12 MB instead of ~25 MB (DE).
    private readonly Dictionary<int, List<byte[]>> _pagedRows;

    private SentenceContinuationIndex(Dictionary<string, string?> heads, Dictionary<int, List<byte[]>> pagedRows,
        int pagedRowCount, ContinuationPair? sample)
    {
        _heads = heads;
        _pagedRows = pagedRows;
        PagedRowCount = pagedRowCount;
        Sample = sample;
    }

    public static SentenceContinuationIndex Empty { get; } =
        new(new Dictionary<string, string?>(), new Dictionary<int, List<byte[]>>(), 0, null);

    /// <summary>Rows long enough for the game to split them over several boxes (<see cref="PagedRowFor"/>).</summary>
    public int PagedRowCount { get; }

    /// <summary>
    /// One joinable pair of consecutive boxes for the E2E run's continuation scenario: no player or
    /// gender placeholder in either box, the second box goes on after the head (it is not swallowed
    /// whole), the first box's tail is unambiguous. Null when the sheets have none.
    /// </summary>
    public ContinuationPair? Sample { get; }

    /// <summary>Usable entries (ambiguous tails excluded).</summary>
    public int Count
    {
        get
        {
            var n = 0;
            foreach (var v in _heads.Values) if (v != null) n++;
            return n;
        }
    }

    /// <param name="sheets">Each sheet's rows in sheet order; pairs never cross a sheet.</param>
    public static SentenceContinuationIndex Build(IEnumerable<IReadOnlyList<SheetLine>> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        var heads = new Dictionary<string, string?>(StringComparer.Ordinal);
        var candidates = new List<(string TailKey, ContinuationPair Pair)>();
        var pagedRows = new Dictionary<int, List<byte[]>>();
        var pagedRowCount = 0;
        foreach (var rows in sheets)
        {
            foreach (var row in rows)
                if (AddPagedRow(pagedRows, row.Text)) pagedRowCount++;

            for (var r = 0; r + 1 < rows.Count; r++)
            {
                var (a, b) = (rows[r], rows[r + 1]);
                if (string.IsNullOrWhiteSpace(a.Text) || string.IsNullOrWhiteSpace(b.Text)) continue;
                if (!SameSpeaker(a.Key, b.Key)) continue;

                var ending = SentenceContinuation.EndingOf(a.Text);
                if (ending == SentenceEnding.Complete) continue;
                var head = SentenceContinuation.SplitHead(b.Text);
                // The next box is open itself: one hop only, otherwise it would be swallowed whole.
                if (!head.Terminated || head.Head.Length > SentenceContinuation.MaxHeadChars) continue;
                if (!SentenceContinuation.Continues(ending, head)) continue;
                // A gender branch near the end cannot be matched against the resolved addon text.
                if (TalkTextHelper.ContainsGenderPlaceholder(TailRegion(a.Text))) continue;
                if (SentenceContinuation.TailKey(a.Text) is not { } key) continue;

                if (heads.TryGetValue(key, out var known))
                {
                    if (known != null && !string.Equals(known, head.Head, StringComparison.Ordinal))
                        heads[key] = null;
                }
                else heads[key] = head.Head;

                if (candidates.Count < MaxSampleCandidates && IsSampleCandidate(a.Text, b.Text, head))
                    candidates.Add((key, new ContinuationPair(a.Text, b.Text, head.Head)));
            }
        }
        // A tail can turn ambiguous after its first pair was seen: pick only from tails that stayed unique.
        ContinuationPair? sample = null;
        foreach (var (tail, pair) in candidates)
        {
            if (!heads.TryGetValue(tail, out var known) || known != pair.Head) continue;
            sample = pair;
            break;
        }
        return new SentenceContinuationIndex(heads, pagedRows, pagedRowCount, sample);
    }

    private static bool AddPagedRow(Dictionary<int, List<byte[]>> pagedRows, string? text)
    {
        if (text == null || text.Length < MinPagedRowChars) return false;
        var n = SentenceContinuation.Normalize(text);
        if (n.Length <= SentenceContinuation.TailChars) return false;
        var key = StartKey(n);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (!pagedRows.TryGetValue(key, out var list)) pagedRows[key] = list = new List<byte[]>(1);
        foreach (var known in list)
            if (known.AsSpan().SequenceEqual(bytes)) return false;   // same line in several sheets
        list.Add(bytes);
        return true;
    }

    private static int StartKey(string normalized) =>
        string.GetHashCode(normalized.AsSpan(0, SentenceContinuation.TailChars), StringComparison.Ordinal);

    /// <summary>
    /// The sheet row this box is the first page of (the game split the row over several boxes): the
    /// box text is a real start of the row. Null when no row starts like that, the box is the whole
    /// row, or two rows with different texts start like that (guessing would speak the wrong one).
    /// </summary>
    public string? PagedRowFor(string? boxText)
    {
        var n = SentenceContinuation.Normalize(boxText);
        if (n.Length < SentenceContinuation.TailChars || !_pagedRows.TryGetValue(StartKey(n), out var list)) return null;
        string? found = null;
        string? foundNormalized = null;
        foreach (var bytes in list)
        {
            var row = Encoding.UTF8.GetString(bytes);
            var rn = SentenceContinuation.Normalize(row);
            if (rn.Length <= n.Length || !rn.StartsWith(n, StringComparison.Ordinal)) continue;
            if (found == null) { found = row; foundNormalized = rn; }
            else if (rn != foundNormalized) return null;
        }
        return found;
    }

    private static bool IsSampleCandidate(string first, string second, SentenceHead head) =>
        head.Rest.Length > 0
        && first.Length + second.Length <= MaxSampleChars
        && !TalkTextHelper.ContainsPlayerPlaceholder(first) && !TalkTextHelper.ContainsPlayerPlaceholder(second)
        && !TalkTextHelper.ContainsGenderPlaceholder(first) && !TalkTextHelper.ContainsGenderPlaceholder(second);

    /// <summary>The rest of the sentence for a box with this text (player name tokenised), null when none or ambiguous.</summary>
    public string? HeadFor(string? boxText) =>
        SentenceContinuation.TailKey(boxText) is { } key && _heads.TryGetValue(key, out var head) ? head : null;

    /// <summary>Text key without its numeric segments: same speaker in the same scene → same group.</summary>
    internal static string SpeakerGroup(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        var parts = key.Split('_');
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (p.Length == 0 || IsDigits(p)) continue;
            if (sb.Length > 0) sb.Append('_');
            sb.Append(p);
        }
        return sb.ToString();
    }

    private static bool SameSpeaker(string a, string b) =>
        string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || SpeakerGroup(a) == SpeakerGroup(b);

    private static bool IsDigits(string s)
    {
        foreach (var c in s) if (c < '0' || c > '9') return false;
        return true;
    }

    private static string TailRegion(string text) => text.Length <= 80 ? text : text[^80..];
}
