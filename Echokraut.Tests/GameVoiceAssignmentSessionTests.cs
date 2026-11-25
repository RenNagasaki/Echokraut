using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>The "Assign game voices" walk through the batch report (§11.5).</summary>
public sealed class GameVoiceAssignmentSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ek-gva-" + Guid.NewGuid().ToString("N"));
    private readonly GameVoiceMappingService _mapping;

    public GameVoiceAssignmentSessionTests()
    {
        Directory.CreateDirectory(_dir);
        _mapping = new GameVoiceMappingService(new Mock<ILogService>().Object, _dir, null, null, null);
    }

    public void Dispose()
    {
        _mapping.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static GameVoiceUnmatchedToken Entry(string token, params string[] suggestions) => new()
    {
        Token = token, Clips = 5, Samples = new List<string> { "a line" },
        Suggestions = suggestions.Select(s => new GameVoiceMatchCandidate(s, 0.9)).ToList(),
    };

    private static GameVoiceUnmatchedReport Report() => new()
    {
        Unmatched = { Entry("spheneyoung", "Sphene"), Entry("strangehooded"), Entry("iceheart", "Ysayle") },
        Ambiguous = { Entry("alphinaud", "Alphinaud Leveilleur") },
    };

    [Fact]
    public void AlreadyCoveredTokens_AreLeftOut()
    {
        // iceheart is in the embedded GameVoiceAliases.json.
        var s = new GameVoiceAssignmentSession(_mapping, Report());
        Assert.Equal(3, s.Count);
        Assert.Equal("spheneyoung", s.Current!.Token);
        Assert.Equal("Sphene", s.SuggestedName);
    }

    [Fact]
    public void Assign_Disable_Skip_Back_WriteTheLocalFile()
    {
        var s = new GameVoiceAssignmentSession(_mapping, Report(), () => new DateTime(2026, 11, 15));

        Assert.True(s.Assign("Sphene"));
        Assert.True(s.Disable());
        s.Back();
        Assert.Equal("strangehooded", s.Current!.Token);
        s.Skip();
        Assert.Equal("alphinaud", s.Current!.Token);
        Assert.False(s.Assign("  "));                 // empty name does nothing
        s.Skip();
        Assert.Null(s.Current);

        var local = _mapping.LocalAliases.ToDictionary(a => a.ShortName);
        Assert.Equal("Sphene", local["spheneyoung"].NpcName);
        Assert.Contains("2026-11-15", local["spheneyoung"].Comment);
        Assert.True(local["strangehooded"].Disabled);
        Assert.Equal((1, 1), (s.Assigned, s.Disabled));
    }

    [Fact]
    public void Load_MissingOrBrokenFile_IsNull()
    {
        Assert.Null(GameVoiceAssignmentSession.Load(Path.Combine(_dir, "nope.json")));
        var broken = Path.Combine(_dir, "broken.json");
        File.WriteAllText(broken, "{ not json");
        Assert.Null(GameVoiceAssignmentSession.Load(broken));
    }
}
