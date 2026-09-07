using System.IO;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Path composition for the shared TTS install root (both engines).</summary>
public class TtsPathsTests
{
    private const string Root = @"C:\alltalk_tts";
    private static readonly char S = Path.DirectorySeparatorChar;

    [Fact]
    public void AllTalkRoot_AndVoices()
    {
        Assert.Equal($@"{Root}{S}alltalk_tts", TtsPaths.AllTalkRoot(Root));
        Assert.Equal($@"{Root}{S}alltalk_tts{S}voices", TtsPaths.AllTalkVoices(Root));
    }

    [Fact]
    public void EchokrauTtsRoot_AndSamples()
    {
        Assert.Equal($@"{Root}{S}echokrautts", TtsPaths.EchokrauTtsRoot(Root));
        Assert.Equal($@"{Root}{S}echokrautts{S}samples", TtsPaths.EchokrauTtsSamples(Root));
    }

    [Fact]
    public void AllTalkAndEchokrauTts_AreSiblingsUnderTheSameRoot()
    {
        Assert.Equal(TtsPaths.EchokrauTtsRoot(Root), Path.Combine(Root, "echokrautts"));
        Assert.Equal(Path.GetDirectoryName(TtsPaths.AllTalkRoot(Root)),
                     Path.GetDirectoryName(TtsPaths.EchokrauTtsRoot(Root)));
    }
}
