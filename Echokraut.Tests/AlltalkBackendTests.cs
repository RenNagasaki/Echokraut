using Echokraut.Backend;
using Xunit;

namespace Echokraut.Tests;

public class AlltalkBackendTests
{
    [Theory]
    [InlineData("http://127.0.0.1:7851/", "/api/ready", "http://127.0.0.1:7851/api/ready")]
    [InlineData("http://127.0.0.1:7851", "/api/ready", "http://127.0.0.1:7851/api/ready")]
    [InlineData("http://host/", "/api/stop", "http://host/api/stop")]
    [InlineData("http://host//", "/api/stop", "http://host/api/stop")] // multiple trailing slashes collapsed
    public void BuildUrl_JoinsBaseAndPath_WithoutDoubleSlash(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, AlltalkBackend.BuildUrl(baseUrl, path));
    }
}
