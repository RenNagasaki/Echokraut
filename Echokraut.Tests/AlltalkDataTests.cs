using Echokraut.DataClasses;
using Echokraut.Enums;
using Xunit;

namespace Echokraut.Tests;

public class AlltalkDataTests
{
    // ── InstanceType getter — migration from legacy booleans ─────────────────

    [Fact]
    public void InstanceType_LocalInstance_ReturnsLocal()
    {
        var data = new AlltalkData { LocalInstance = true, RemoteInstance = false, NoInstance = false };
        Assert.Equal(AlltalkInstanceType.Local, data.InstanceType);
    }

    [Fact]
    public void InstanceType_RemoteInstance_ReturnsRemote()
    {
        var data = new AlltalkData { LocalInstance = false, RemoteInstance = true, NoInstance = false };
        Assert.Equal(AlltalkInstanceType.Remote, data.InstanceType);
    }

    [Fact]
    public void InstanceType_NoInstance_ReturnsNone()
    {
        var data = new AlltalkData { LocalInstance = false, RemoteInstance = false, NoInstance = true };
        Assert.Equal(AlltalkInstanceType.None, data.InstanceType);
    }

    [Fact]
    public void InstanceType_AllFalse_ReturnsNone()
    {
        var data = new AlltalkData { LocalInstance = false, RemoteInstance = false, NoInstance = false };
        Assert.Equal(AlltalkInstanceType.None, data.InstanceType);
    }

    [Fact]
    public void InstanceType_LocalTakesPrecedenceOverRemote()
    {
        // If both are set (shouldn't happen, but defensive), Local wins
        var data = new AlltalkData { LocalInstance = true, RemoteInstance = true, NoInstance = false };
        Assert.Equal(AlltalkInstanceType.Local, data.InstanceType);
    }

    // ── InstanceType setter — updates all three legacy booleans ──────────────

    [Fact]
    public void InstanceType_SetLocal_UpdatesBooleans()
    {
        var data = new AlltalkData();
        data.InstanceType = AlltalkInstanceType.Local;

        Assert.True(data.LocalInstance);
        Assert.False(data.RemoteInstance);
        Assert.False(data.NoInstance);
    }

    [Fact]
    public void InstanceType_SetRemote_UpdatesBooleans()
    {
        var data = new AlltalkData();
        data.InstanceType = AlltalkInstanceType.Remote;

        Assert.False(data.LocalInstance);
        Assert.True(data.RemoteInstance);
        Assert.False(data.NoInstance);
    }

    [Fact]
    public void InstanceType_SetNone_UpdatesBooleans()
    {
        var data = new AlltalkData();
        data.InstanceType = AlltalkInstanceType.None;

        Assert.False(data.LocalInstance);
        Assert.False(data.RemoteInstance);
        Assert.True(data.NoInstance);
    }

    [Fact]
    public void InstanceType_SetLocal_ThenSetRemote_ClearsLocal()
    {
        var data = new AlltalkData();
        data.InstanceType = AlltalkInstanceType.Local;
        data.InstanceType = AlltalkInstanceType.Remote;

        Assert.False(data.LocalInstance);
        Assert.True(data.RemoteInstance);
        Assert.False(data.NoInstance);
    }

    // ── Roundtrip ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AlltalkInstanceType.Local)]
    [InlineData(AlltalkInstanceType.Remote)]
    [InlineData(AlltalkInstanceType.None)]
    public void InstanceType_Roundtrip(AlltalkInstanceType type)
    {
        var data = new AlltalkData();
        data.InstanceType = type;
        Assert.Equal(type, data.InstanceType);
    }

    // ── BaseUrl trailing slash ────────────────────────────────────────────────

    [Theory]
    [InlineData("http://localhost:7851/", "http://localhost:7851")]
    [InlineData("http://localhost:7851", "http://localhost:7851")]
    [InlineData("https://example.com///", "https://example.com")]
    public void BaseUrl_TrimEnd_RemovesTrailingSlashes(string input, string expected)
    {
        Assert.Equal(expected, input.TrimEnd('/'));
    }
}
