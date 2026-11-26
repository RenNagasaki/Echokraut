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

/// <summary>Per-NPC override text field (§11.5).</summary>
public sealed class GameVoiceNpcOverrideTests
{
    [Fact]
    public void RoundTrip()
    {
        var d = new Dictionary<string, string>();
        Assert.Equal("", GameVoiceNpcOverride.Display(d, "DE|x"));
        Assert.True(GameVoiceNpcOverride.Apply(d, "DE|x", " -"));
        Assert.Equal("", d["DE|x"]);
        Assert.Equal("-", GameVoiceNpcOverride.Display(d, "DE|x"));
        Assert.True(GameVoiceNpcOverride.Apply(d, "DE|x", "Mystery Voice"));
        Assert.Equal("mysteryvoice", d["DE|x"]);
        Assert.Equal("MYSTERYVOICE", GameVoiceNpcOverride.Display(d, "DE|x"));
        Assert.False(GameVoiceNpcOverride.Apply(d, "DE|x", "MYSTERYVOICE"));
        Assert.True(GameVoiceNpcOverride.Apply(d, "DE|x", ""));
        Assert.Empty(d);
    }
}
