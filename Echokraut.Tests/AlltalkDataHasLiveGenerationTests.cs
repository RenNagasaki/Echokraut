using Echokraut.DataClasses;
using Echokraut.Enums;
using Xunit;

namespace Echokraut.Tests;

public class AlltalkDataHasLiveGenerationTests
{
    [Fact]
    public void None_HasNoLiveGeneration()
    {
        var data = new AlltalkData { InstanceType = AlltalkInstanceType.None };
        Assert.False(data.HasLiveGeneration);
    }

    [Fact]
    public void Local_HasLiveGeneration()
    {
        var data = new AlltalkData { InstanceType = AlltalkInstanceType.Local };
        Assert.True(data.HasLiveGeneration);
    }

    [Fact]
    public void Remote_HasLiveGeneration()
    {
        var data = new AlltalkData { InstanceType = AlltalkInstanceType.Remote };
        Assert.True(data.HasLiveGeneration);
    }

    [Fact]
    public void DefaultIsNone()
    {
        // Sanity: a freshly constructed AlltalkData defaults to None mode, so a fresh user
        // gets the safest configuration (no accidental backend calls).
        Assert.Equal(AlltalkInstanceType.None, new AlltalkData().InstanceType);
        Assert.False(new AlltalkData().HasLiveGeneration);
    }
}
