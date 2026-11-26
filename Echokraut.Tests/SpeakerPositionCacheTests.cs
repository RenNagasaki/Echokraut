using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

public class SpeakerPositionCacheTests
{
    private static Mock<IGameObject> Speaker(Vector3 pos, bool valid = true)
    {
        var m = new Mock<IGameObject>();
        m.Setup(o => o.IsValid()).Returns(valid);
        m.Setup(o => o.Position).Returns(pos);
        return m;
    }

    private static KeyValuePair<Guid, IGameObject?> Entry(Guid id, IGameObject? obj) => new(id, obj);

    [Fact]
    public void Get_BeforeRefresh_IsNull()
    {
        Assert.Null(new SpeakerPositionCache().Get(Guid.NewGuid()));
    }

    [Fact]
    public void Refresh_StoresPosition_GetReturnsIt()
    {
        var cache = new SpeakerPositionCache();
        var id = Guid.NewGuid();
        cache.Refresh([Entry(id, Speaker(new Vector3(1, 2, 3)).Object)]);
        var p = cache.Get(id);
        Assert.NotNull(p);
        Assert.Equal((1f, 2f, 3f), (p!.X, p.Y, p.Z));
    }

    [Fact]
    public void Refresh_InvalidOrMissingSpeaker_KeepsLastKnownPosition_WithoutReadingIt()
    {
        var cache = new SpeakerPositionCache();
        var id = Guid.NewGuid();
        cache.Refresh([Entry(id, Speaker(new Vector3(4, 5, 6)).Object)]);

        var gone = Speaker(new Vector3(9, 9, 9), valid: false);
        cache.Refresh([Entry(id, gone.Object)]);
        cache.Refresh([Entry(id, null)]);

        Assert.Equal(4f, cache.Get(id)!.X);
        gone.Verify(o => o.Position, Times.Never);
    }

    [Fact]
    public void Refresh_DropsStreamsThatStoppedPlaying()
    {
        var cache = new SpeakerPositionCache();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        cache.Refresh([Entry(a, Speaker(Vector3.One).Object), Entry(b, Speaker(Vector3.One).Object)]);

        cache.Refresh([Entry(b, Speaker(Vector3.One).Object)]);

        Assert.Null(cache.Get(a));
        Assert.NotNull(cache.Get(b));
    }
}
