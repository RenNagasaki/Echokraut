using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class EchokrauTtsWrapperHttpTests
{
    [Fact]
    public void ParseHealth_ReadsDeviceAndTheCacheCounters()
    {
        var h = EchokrauTtsWrapperHttp.ParseHealth("""
            {"status":"ok","backend":"xtts","device":"cuda","rate_limit":{},
             "response_cache":{"enabled":true,"minutes":5.0,"max_mb":128.0,"entries":3,"mb":1.2,"running":1,
                               "hits":4,"shared":2,"misses":5,"bypassed":1}}
            """)!;
        Assert.Equal("cuda", h.Device);
        Assert.Equal("xtts", h.Backend);
        Assert.Equal(new E2eCacheStats(true, 3, 1, 4, 2, 5, 1), h.Cache);
    }

    [Fact]
    public void ParseHealth_OldWrapperWithoutCache_HasNoCacheStats()
    {
        var h = EchokrauTtsWrapperHttp.ParseHealth("""{"status":"ok","backend":"xtts","device":"cpu"}""")!;
        Assert.Null(h.Cache);
        Assert.False(EchokrauTtsWrapperHttp.ParseHealth("""{"response_cache":{"enabled":false}}""")!.Cache!.Enabled);
    }

    [Fact]
    public void ParseHealth_NoJsonObject_IsNull()
    {
        Assert.Null(EchokrauTtsWrapperHttp.ParseHealth("not json"));
        Assert.Null(EchokrauTtsWrapperHttp.ParseHealth("[1,2]"));
    }

    [Fact]
    public void Minus_GivesTheCounterChange()
    {
        var d = new E2eCacheStats(true, 4, 0, 6, 3, 7, 2).Minus(new E2eCacheStats(true, 3, 1, 4, 2, 5, 1));
        Assert.Equal(new E2eCacheStats(true, 1, -1, 2, 1, 2, 1), d);
    }
}
