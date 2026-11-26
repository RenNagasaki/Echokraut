using Echokraut.DataClasses;
using Echokraut.Enums;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Post-migration tests for <see cref="AlltalkData"/>. Behaviour around the legacy boolean
/// fields and the migration to <see cref="AlltalkInstanceType"/> lives in
/// <see cref="AlltalkDataMigrationTests"/>.
/// </summary>
public class AlltalkDataTests
{
    [Theory]
    [InlineData("http://localhost:7851/", "http://localhost:7851")]
    [InlineData("http://localhost:7851", "http://localhost:7851")]
    [InlineData("https://example.com///", "https://example.com")]
    public void BaseUrl_TrimEnd_RemovesTrailingSlashes(string input, string expected)
    {
        Assert.Equal(expected, input.TrimEnd('/'));
    }
}
