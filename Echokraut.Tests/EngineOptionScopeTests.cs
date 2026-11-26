using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class EngineOptionScopeTests
{
    [Theory]
    [InlineData(EngineOptionGroup.Xtts, "xtts", true)]
    [InlineData(EngineOptionGroup.Xtts, "XTTS", true)]
    [InlineData(EngineOptionGroup.Xtts, "f5", false)]
    [InlineData(EngineOptionGroup.Xtts, "pocket", false)]
    [InlineData(EngineOptionGroup.Qwen, "qwen-0.6b", true)]
    [InlineData(EngineOptionGroup.Qwen, "qwen-1.7b", true)]
    [InlineData(EngineOptionGroup.Qwen, "xtts", false)]
    [InlineData(EngineOptionGroup.Pocket, "pocket", true)]
    [InlineData(EngineOptionGroup.Pocket, "qwen-0.6b", false)]
    [InlineData(EngineOptionGroup.Pocket, null, false)]
    [InlineData(EngineOptionGroup.None, "f5", true)]
    [InlineData(EngineOptionGroup.None, null, true)]
    public void AppliesTo_MatchesOnlyTheOwningEngine(EngineOptionGroup group, string? engine, bool expected)
        => Assert.Equal(expected, EngineOptionScope.AppliesTo(group, engine));
}
