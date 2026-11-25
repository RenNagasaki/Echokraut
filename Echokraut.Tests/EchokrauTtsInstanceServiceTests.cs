using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// EchokrauTtsInstanceService: where StopInstance sends /shutdown. Before the fix it posted to
/// BaseUrl, which after a Local -> Remote switch names the remote server, so the plugin shut down a
/// server it never started.
/// </summary>
public class EchokrauTtsInstanceServiceTests
{
    [Theory]
    [InlineData(8765, "/shutdown", "http://127.0.0.1:8765/shutdown")]
    [InlineData(9000, "shutdown", "http://127.0.0.1:9000/shutdown")]
    public void LocalShutdownUrl_AlwaysLoopback(int port, string path, string expected)
    {
        Assert.Equal(expected, EchokrauTtsInstanceService.LocalShutdownUrl(port, path));
    }
}
