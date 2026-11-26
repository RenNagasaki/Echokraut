using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest;

/// <summary>PlayerCheck, GameDataCheck, DatabaseCheck, StoryProgressCheck (EnvironmentChecks.cs).</summary>
public class EnvironmentChecksTests
{
    private readonly Mock<IClientState> _clientState = new();
    private readonly Mock<IFramework> _framework = new();
    private readonly Mock<IGameObjectService> _gameObjects = new();

    public EnvironmentChecksTests()
    {
        _clientState.SetupGet(c => c.ClientLanguage).Returns(ClientLanguage.German);
        _framework.Setup(f => f.RunOnFrameworkThread(It.IsAny<Func<(bool, string, ulong)>>()))
            .Returns((Func<(bool, string, ulong)> f) => Task.FromResult(f()));
    }

    private Task<SelfTestResult> Player() =>
        new PlayerCheck(_clientState.Object, _framework.Object, _gameObjects.Object).RunAsync(CancellationToken.None);

    [Fact]
    public async Task Player_NotLoggedIn_Fails()
    {
        Assert.Equal(SelfTestOutcome.Fail, (await Player()).Outcome);
    }

    [Fact]
    public async Task Player_LoggedInWithoutName_Fails()
    {
        _clientState.SetupGet(c => c.IsLoggedIn).Returns(true);
        var result = await Player();
        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        Assert.Contains("name", result.Detail);
    }

    [Fact]
    public async Task Player_Complete_Passes_AfterRefreshingTheCache()
    {
        _clientState.SetupGet(c => c.IsLoggedIn).Returns(true);
        _gameObjects.SetupGet(g => g.LocalPlayerName).Returns("Jon Doe");
        _gameObjects.SetupGet(g => g.LocalPlayerContentId).Returns(123UL);

        var result = await Player();

        Assert.Equal(SelfTestOutcome.Pass, result.Outcome);
        Assert.DoesNotContain("Jon Doe", result.Detail); // the report may be shared
        _gameObjects.Verify(g => g.RefreshLocalPlayer(), Times.Once);
    }

    [Fact]
    public async Task GameData_SheetsAndNames_Pass()
    {
        var sheets = new Mock<IDialogSheetSource>();
        sheets.Setup(s => s.ReadDialogueSheets(ClientLanguage.German))
            .Returns(new List<IReadOnlyList<SheetLine>> { new[] { new SheetLine("", "Hallo.") } });
        var data = new Mock<IGameVoiceDataSource>();
        data.Setup(d => d.GetNpcNames(ClientLanguage.German)).Returns(new Dictionary<uint, string> { [1] = "Tataru" });

        var result = await new GameDataCheck(_clientState.Object, sheets.Object, data.Object).RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, result.Outcome);
    }

    [Fact]
    public async Task GameData_NoSheets_Fails()
    {
        var sheets = new Mock<IDialogSheetSource>();
        sheets.Setup(s => s.ReadDialogueSheets(It.IsAny<ClientLanguage>())).Returns(new List<IReadOnlyList<SheetLine>>());
        var data = new Mock<IGameVoiceDataSource>();
        data.Setup(d => d.GetNpcNames(It.IsAny<ClientLanguage>())).Returns(new Dictionary<uint, string> { [1] = "Tataru" });

        var result = await new GameDataCheck(_clientState.Object, sheets.Object, data.Object).RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
    }

    [Fact]
    public async Task Database_Readable_Passes()
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(d => d.GetVoiceClipCount(null, null, null, null)).Returns(12);
        db.Setup(d => d.GetMutedBaseIds()).Returns(new HashSet<uint> { 1 });

        var result = await new DatabaseCheck(db.Object).RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestResult.Pass("Readable: 12 voice clip(s), 1 muted dialogue(s)."), result);
    }

    [Theory]
    [InlineData("T_VER610", SelfTestOutcome.Pass)]
    [InlineData("", SelfTestOutcome.Pass)]
    public async Task StoryProgress_Known_Passes(string key, SelfTestOutcome expected)
    {
        var progress = new Mock<IStoryProgressService>();
        progress.SetupGet(p => p.ReachedPatchKey).Returns(key);

        var result = await new StoryProgressCheck(progress.Object).RunAsync(CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }
}
