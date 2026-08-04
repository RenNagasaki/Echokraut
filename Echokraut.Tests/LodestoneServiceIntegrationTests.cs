using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Echokraut.Tests;

/// <summary>
/// Live integration tests against the Lodestone (eu.finalfantasyxiv.com).
/// Opt-in via LODESTONE_TEST_NAME + LODESTONE_TEST_WORLD — set them either as shell env vars
/// or in a repo-root <c>.env</c> file (copy from <c>.example.env</c>). Without values the
/// tests pass trivially so CI without network access doesn't fail.
///
/// Quickest path: copy <c>.example.env</c> to <c>.env</c> at the repo root, fill in name + world,
/// run <c>dotnet test --filter "FullyQualifiedName~LodestoneServiceIntegrationTests"</c>.
/// </summary>
public class LodestoneServiceIntegrationTests : IDisposable
{
    private const string EnvName = "LODESTONE_TEST_NAME";
    private const string EnvWorld = "LODESTONE_TEST_WORLD";

    private readonly DatabaseService _db;
    private readonly EchokrautDbContext _context;
    private readonly LodestoneService _lodestone;
    private readonly ITestOutputHelper _output;
    private readonly SqliteConnection _connection;

    public LodestoneServiceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        TestEnvLoader.EnsureLoaded();

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<EchokrautDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new EchokrautDbContext(options);

        // Pipe LodestoneService warnings/info into xUnit's test output so we can see what
        // happened during a failing live run.
        var log = new Mock<ILogService>();
        log.Setup(x => x.Info(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EchoEventId>()))
            .Callback<string, string, EchoEventId>((m, msg, _) => _output.WriteLine($"INF {m}: {msg}"));
        log.Setup(x => x.Warning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EchoEventId>()))
            .Callback<string, string, EchoEventId>((m, msg, _) => _output.WriteLine($"WRN {m}: {msg}"));
        log.Setup(x => x.Error(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EchoEventId>()))
            .Callback<string, string, EchoEventId>((m, msg, _) => _output.WriteLine($"ERR {m}: {msg}"));

        _db = new DatabaseService(log.Object, _context);
        _lodestone = new LodestoneService(log.Object, _db);
    }

    [Fact]
    public async Task DEBUG_RawHtmlFetch_ShowsWhatLodestoneReturns()
    {
        var env = GetEnv();
        if (env == null)
        {
            _output.WriteLine("Skipping DEBUG: env vars not set.");
            return;
        }

        var (name, world) = env.Value;
        var url = $"https://eu.finalfantasyxiv.com/lodestone/character/?q={HttpUtility.UrlEncode(name)}&worldname={HttpUtility.UrlEncode(world)}";
        _output.WriteLine($"GET {url}");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; Echokraut/1.0; +https://github.com/RenNagasaki/Echokraut)");
        var resp = await http.GetAsync(url);
        _output.WriteLine($"Status: {(int)resp.StatusCode} {resp.StatusCode}");
        var body = await resp.Content.ReadAsStringAsync();
        _output.WriteLine($"Body length: {body.Length}");

        // Print the section of HTML that should contain results
        var entryIdx = body.IndexOf("entry__link", StringComparison.OrdinalIgnoreCase);
        var noResultIdx = body.IndexOf("No matches", StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"'entry__link' index: {entryIdx}");
        _output.WriteLine($"'No matches' index: {noResultIdx}");
        if (entryIdx >= 0)
        {
            var snippet = body.Substring(Math.Max(0, entryIdx - 50), Math.Min(800, body.Length - entryIdx + 50));
            _output.WriteLine($"Snippet around 'entry__link':\n{snippet}");
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private (string name, string world)? GetEnv()
    {
        var name = Environment.GetEnvironmentVariable(EnvName);
        var world = Environment.GetEnvironmentVariable(EnvWorld);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world))
            return null;
        return (name!, world!);
    }

    [Fact]
    public async Task Lodestone_LookupAsync_ResolvesKnownCharacterFromEnv()
    {
        var env = GetEnv();
        if (env == null)
        {
            _output.WriteLine($"Skipping: set {EnvName} + {EnvWorld} env vars to run live test.");
            return;
        }

        var (name, world) = env.Value;
        var result = await _lodestone.LookupAsync(name, world, CancellationToken.None);

        Assert.NotNull(result);
        _output.WriteLine($"Live Lodestone hit: {name} @ {world} → Race={result!.Race}, Gender={result.Gender}");

        // For a real player character, race should resolve to a player race (Hyur/Elezen/Lalafell/Miqote/
        // Roegadyn/AuRa/Hrothgar/Viera) — never Unknown if the scraper works.
        Assert.NotEqual(NpcRaces.Unknown, result.Race);
        // Gender on player characters is always Male or Female on Lodestone.
        Assert.NotEqual(Genders.None, result.Gender);
    }

    [Fact]
    public async Task Lodestone_LookupAsync_NegativeCachesMisses()
    {
        var env = GetEnv();
        if (env == null)
        {
            _output.WriteLine($"Skipping: set {EnvName} + {EnvWorld} env vars to run live test.");
            return;
        }

        var (_, world) = env.Value;
        // A name with random characters that almost certainly doesn't exist.
        var fakeName = $"Zzzz Nonexistent_{Guid.NewGuid():N}".Substring(0, 20);
        var result = await _lodestone.LookupAsync(fakeName, world, CancellationToken.None);

        Assert.Null(result);

        // Verify the miss was negatively cached — second lookup should hit cache, not network.
        var cached = _db.GetLodestoneLookup(fakeName, world);
        Assert.NotNull(cached);
        Assert.False(cached!.Found);
    }

    [Fact]
    public async Task Lodestone_LookupAsync_SecondCallUsesCache()
    {
        var env = GetEnv();
        if (env == null)
        {
            _output.WriteLine($"Skipping: set {EnvName} + {EnvWorld} env vars to run live test.");
            return;
        }

        var (name, world) = env.Value;

        var first = await _lodestone.LookupAsync(name, world, CancellationToken.None);
        Assert.NotNull(first);

        // Second call: cache should be populated, no network round-trip needed.
        var startTicks = Environment.TickCount64;
        var second = await _lodestone.LookupAsync(name, world, CancellationToken.None);
        var elapsed = Environment.TickCount64 - startTicks;

        Assert.NotNull(second);
        Assert.Equal(first!.Race, second!.Race);
        Assert.Equal(first.Gender, second.Gender);
        // Cache lookup is a single SQLite query — 50ms is generous; live HTTP would take seconds.
        Assert.True(elapsed < 200,
            $"Second lookup took {elapsed}ms — cache likely missed. Expected < 200ms.");
    }
}
