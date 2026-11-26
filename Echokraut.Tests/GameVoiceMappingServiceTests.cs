using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Layering + repo export of the central mapping files (§11.5).</summary>
public sealed class GameVoiceMappingServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ek-gvm-" + Guid.NewGuid().ToString("N"));
    private readonly ILogService _log = new Mock<ILogService>().Object;

    public GameVoiceMappingServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private const string RemoteAliases =
        "{ \"version\": 1, \"aliases\": [ { \"ShortName\": \"ICEHEART\", \"NpcName\": \"Remote Ysayle\" }, { \"ShortName\": \"NEWONE\", \"NpcName\": \"Somebody\" } ] }";

    private const string RemoteSplits =
        "{ \"version\": 2, \"splits\": [ { \"npcName\": \"Y'shtola\", \"language\": \"DE\", \"boundaryPatches\": [\"06100\"] } ] }";

    private GameVoiceMappingService Create(Func<string, CancellationToken, Task<string>>? fetch, string? aliasesUrl = "a", string? splitsUrl = "s") =>
        new(_log, _dir, aliasesUrl, splitsUrl, fetch);

    private static string OwnerOf(GameVoiceMappingService m, string token) =>
        m.Aliases.Single(a => a.ShortName == token).NpcName!;

    [Fact]
    public async Task Order_EmbeddedThenRemoteThenLocal()
    {
        File.WriteAllText(Path.Combine(_dir, GameVoiceMappingService.AliasOverrideFile),
            "{ \"version\": 1, \"aliases\": [ { \"ShortName\": \"NEWONE\", \"NpcName\": \"Local Wins\" } ] }");
        using var m = Create((url, _) => Task.FromResult(url == "a" ? RemoteAliases : RemoteSplits));
        var changed = 0;
        m.Changed += () => changed++;

        Assert.Equal("Ysayle", OwnerOf(m, "iceheart")); // embedded until the remote copy arrives
        await m.RemoteLoad;

        Assert.Equal("Remote Ysayle", OwnerOf(m, "iceheart"));
        Assert.Equal("Local Wins", OwnerOf(m, "newone"));
        Assert.True(m.Splits.HasSplit("Y'shtola", "DE"));
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task RemoteFailure_KeepsTheEmbeddedCopy()
    {
        using var m = Create((_, _) => Task.FromException<string>(new HttpRequestExceptionStub()));
        await m.RemoteLoad;
        Assert.Equal("Ysayle", OwnerOf(m, "iceheart"));
    }

    [Fact]
    public void NoUrls_NoFetch()
    {
        var called = false;
        using var m = Create((_, _) => { called = true; return Task.FromResult(""); }, null, null);
        Assert.False(called);
        Assert.Contains(m.Aliases, a => a.ShortName == "mysteryvoice");
    }

    [Fact]
    public void SetLocalAlias_SavesRaisesAndWins_RemoveFallsBack()
    {
        using var m = Create(null);
        var changed = 0;
        m.Changed += () => changed++;

        m.SetLocalAlias(new GameVoiceAliasEntry { ShortName = "ICEHEART", Disabled = true });
        Assert.True(m.Aliases.Single(a => a.ShortName == "iceheart").Disabled);
        Assert.Contains("\"Disabled\": true", File.ReadAllText(Path.Combine(_dir, GameVoiceMappingService.AliasOverrideFile)));

        m.RemoveLocalAlias("iceheart");
        Assert.False(m.Aliases.Single(a => a.ShortName == "iceheart").Disabled);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void ExportForRepo_SortedDeduplicatedInTheResourceFormat()
    {
        using var m = Create(null);
        m.SetLocalAlias(new GameVoiceAliasEntry { ShortName = "AAAFIRST", NpcName = "First" });
        m.SetLocalAlias(new GameVoiceAliasEntry { ShortName = "iceheart", NpcName = "Ysayle Dangoulain" });
        var path = Path.Combine(_dir, "harvest", "game_voice_aliases_for_repo.json");

        var count = m.ExportAliasesForRepo(path);

        var file = System.Text.Json.JsonSerializer.Deserialize<GameVoiceAliasFile>(File.ReadAllText(path))!;
        Assert.Equal(count, file.Aliases.Count);
        Assert.Equal(1, file.Version);
        Assert.Equal("aaafirst", file.Aliases[0].ShortName);
        Assert.Equal(file.Aliases.Select(a => a.ShortName).OrderBy(x => x, StringComparer.Ordinal), file.Aliases.Select(a => a.ShortName));
        Assert.Single(file.Aliases, a => a.ShortName == "iceheart");
        Assert.Equal("Ysayle Dangoulain", file.Aliases.Single(a => a.ShortName == "iceheart").NpcName);
        Assert.DoesNotContain("\"NpcId\"", File.ReadAllText(path));   // unset fields stay out, like the resource
        Assert.DoesNotContain("\"Disabled\"", File.ReadAllText(path));
    }

    [Fact]
    public void ShippedSplits_LoadWithoutWarnings_AndUseRealTokens()
    {
        var log = new Mock<ILogService>();
        using var m = new GameVoiceMappingService(log.Object, _dir, null, null, null);
        log.Verify(l => l.Warning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EKEventId>()), Times.Never);
        Assert.True(m.Splits.HasSplit("Y'shtola", "DE"));
        Assert.Equal("Post06100", m.Splits.ResolveEpoch("Y'shtola", "DE", "vo_voiceman_06100_000010"));
        Assert.Equal("Pre06100", m.Splits.ResolveEpoch("Y'shtola", "DE", "vo_voiceman_06006_000010"));
    }

    private sealed class HttpRequestExceptionStub : Exception { }
}
