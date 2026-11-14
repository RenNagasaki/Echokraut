using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>One row of a <c>cut_scene/*</c> text sheet as the data source hands it over.</summary>
/// <param name="TextKey">Column 0, e.g. <c>TEXT_VOICEMAN_06006_000010_YSHTOLA</c>.</param>
/// <param name="Text">Column 1 as plain text.</param>
/// <param name="HasMacro">True when the text contained a non-text payload (player name, gender
/// switch, …). Such a line's plain text no longer matches what the actor says, so it is useless
/// as <c>ref_text</c> and is dropped.</param>
public readonly record struct CutsceneTextRow(string TextKey, string Text, bool HasMacro);

/// <summary>A voiced cutscene line that may serve as a reference: speaker token, clip id, game text.</summary>
public sealed record GameVoiceLine(string SpeakerToken, string AudioBase, string Text);

/// <summary>
/// Text-only index <c>speaker token → voiced lines</c> over the cutscene sheets of ONE language
/// (plan game-voice-reference §2.2). Reads no audio: whether a clip actually exists is decided
/// later, when it is decoded. Tokens are stored normalized (<see cref="VoiceExtractKey.Normalize"/>).
/// </summary>
public sealed class GameVoiceTextIndex
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly Dictionary<string, List<GameVoiceLine>> _byToken;

    private GameVoiceTextIndex(Dictionary<string, List<GameVoiceLine>> byToken, int lineCount)
    {
        _byToken = byToken;
        LineCount = lineCount;
    }

    public static readonly GameVoiceTextIndex Empty = new(new Dictionary<string, List<GameVoiceLine>>(), 0);

    public int LineCount { get; }

    public IEnumerable<string> Tokens => _byToken.Keys;

    /// <summary>
    /// Builds the index. Dropped: keys <see cref="VoiceExtractKey.TryParse"/> rejects, keys
    /// carrying FFXIV's <c>_NONE_VOICE</c> marker (explicitly not voice acted), rows with a
    /// macro, empty text. The first row per audio base wins (sheets list each line once; the
    /// guard keeps a duplicate from being ranked twice).
    /// </summary>
    public static GameVoiceTextIndex Build(IEnumerable<CutsceneTextRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var byToken = new Dictionary<string, List<GameVoiceLine>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var row in rows)
        {
            if (row.HasMacro || string.IsNullOrWhiteSpace(row.Text) || string.IsNullOrEmpty(row.TextKey)) continue;
            if (row.TextKey.EndsWith(VoiceExtractKey.UnvoicedMarkerSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!VoiceExtractKey.TryParse(row.TextKey, out var shortName, out var audioBase)) continue;
            var token = VoiceExtractKey.Normalize(shortName);
            if (token.Length == 0 || !seen.Add(audioBase)) continue;

            if (!byToken.TryGetValue(token, out var list))
                byToken[token] = list = new List<GameVoiceLine>();
            list.Add(new GameVoiceLine(token, audioBase, CleanText(row.Text)));
            count++;
        }
        return new GameVoiceTextIndex(byToken, count);
    }

    /// <summary>Lines of one speaker token (normalized or not); empty when unknown.</summary>
    public IReadOnlyList<GameVoiceLine> LinesFor(string token)
    {
        var key = VoiceExtractKey.Normalize(token);
        return _byToken.TryGetValue(key, out var list) ? list : Array.Empty<GameVoiceLine>();
    }

    /// <summary>Line breaks and runs of whitespace become one space — the text is read aloud as one line.</summary>
    public static string CleanText(string text) => Whitespace.Replace(text ?? "", " ").Trim();
}
