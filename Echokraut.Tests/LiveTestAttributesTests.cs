using Xunit;

namespace Echokraut.Tests;

public class LiveTestAttributesTests
{
    [Fact]
    public void SkipReason_MissingEnvVar_NamesIt()
    {
        const string missing = "ECHOKRAUT_TEST_SURELY_UNSET_7F3A";
        var reason = Live.SkipReason(new[] { missing });
        Assert.NotNull(reason);
        Assert.Contains(missing, reason);
    }

    [Fact]
    public void LiveFact_MissingEnvVar_SkipsWithTheReasonNamingIt()
    {
        const string missing = "ECHOKRAUT_TEST_SURELY_UNSET_7F3A";
        Assert.Equal(Live.SkipReason(new[] { missing }), new LiveFactAttribute(missing).Skip);
    }
}
