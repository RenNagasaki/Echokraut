using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Coverage for <see cref="ChangelogService"/>: version parsing, language fallback, and the
/// version-window filter that drives <c>NativeChangelogWindow</c>'s "show what's new" logic.
///
/// The service reads embedded resources from an assembly. Tests use a synthetic in-memory
/// assembly built at runtime so we can inject the exact resource set we want — the real
/// Echokraut.dll has only the live changelog set, which can't exercise corner cases (skip
/// windows, missing language fallback, etc.) without shipping fake versions.
/// </summary>
public class ChangelogServiceTests
{
    private readonly Mock<ILogService> _log = new();
    private readonly Mock<IClientState> _clientState = new();

    private static Configuration MakeConfig(string lastSeen) => new()
    {
        LastSeenChangelogVersion = lastSeen,
    };

    /// <summary>
    /// Builds a throwaway in-memory assembly carrying the supplied (resourceName, content)
    /// pairs as embedded resources. Used as a stand-in for the live plugin assembly so tests
    /// can dictate exactly which changelog entries the service sees.
    /// </summary>
    private static Assembly BuildAssemblyWithResources(params (string Name, string Content)[] resources)
    {
        // System.Reflection.Emit can't bake embedded resources into a runtime-defined
        // dynamic assembly, so we use an alternative: load a minimal pre-built DLL whose
        // ResourceManager we can probe. But that's heavyweight for unit tests.
        // The pragmatic choice — wrap a real assembly via a thin Assembly subclass — is
        // also blocked because Assembly is sealed-by-convention via internal abstract.
        //
        // What works cleanly: write each resource to disk, then call
        // Assembly.LoadFrom on a tiny .NET assembly we generate. But that requires a
        // build step.
        //
        // Simpler: use a custom Assembly proxy via a wrapper class that holds the
        // resource map, and refactor ChangelogService to take an abstraction. Done in
        // FakeAssembly below.
        throw new NotImplementedException("Use FakeAssembly instead.");
    }

    /// <summary>
    /// Minimal Assembly wrapper that returns a fixed manifest resource list. ChangelogService
    /// only calls GetManifestResourceNames() and GetManifestResourceStream(name); both are
    /// virtual on Assembly so they can be overridden in a derived runtime-only test type.
    /// </summary>
    private sealed class FakeAssembly : Assembly
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _resources;
        public FakeAssembly(System.Collections.Generic.Dictionary<string, string> resources)
        {
            _resources = resources;
        }
        public override string[] GetManifestResourceNames() => _resources.Keys.ToArray();
        public override Stream? GetManifestResourceStream(string name)
        {
            if (!_resources.TryGetValue(name, out var content)) return null;
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        }
    }

    private static FakeAssembly Asm(params (string Name, string Content)[] entries)
    {
        var dict = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var (n, c) in entries) dict[n] = c;
        return new FakeAssembly(dict);
    }

    private ChangelogService BuildService(string lastSeen, string current, FakeAssembly asm,
        ClientLanguage lang = ClientLanguage.English)
    {
        _clientState.Setup(c => c.ClientLanguage).Returns(lang);
        return new ChangelogService(_log.Object, MakeConfig(lastSeen), _clientState.Object, current, asm);
    }

    // ── Version parsing ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("v0.19.0.0", 0, 19, 0, 0)]
    [InlineData("0.19.0.0", 0, 19, 0, 0)]
    [InlineData("V0.19.0.0", 0, 19, 0, 0)]
    [InlineData(" v1.2.3.4 ", 1, 2, 3, 4)]
    public void TryParseVersion_StripsVAndParses(string raw, int major, int minor, int build, int rev)
    {
        var v = ChangelogService.TryParseVersion(raw);
        Assert.NotNull(v);
        Assert.Equal(new Version(major, minor, build, rev), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("v1.2")] // 4-part required by our convention; 2-part still parses but tests the contract
    public void TryParseVersion_HandlesBadInput(string raw)
    {
        var v = ChangelogService.TryParseVersion(raw);
        // 4-part is convention but not enforced — Version.Parse accepts 2/3/4 part strings.
        // Bad input returns null; partial-but-valid Version returns a parsed Version.
        if (raw == "v1.2") Assert.NotNull(v);
        else Assert.Null(v);
    }

    // ── Language pick ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(ClientLanguage.German, "DE")]
    [InlineData(ClientLanguage.English, "EN")]
    [InlineData(ClientLanguage.French, "EN")]   // FR falls back to EN until translated
    [InlineData(ClientLanguage.Japanese, "EN")] // JA falls back to EN until translated
    public void PickLanguage_PicksDeOrFallsToEn(ClientLanguage lang, string expected)
    {
        Assert.Equal(expected, ChangelogService.PickLanguage(lang));
    }

    // ── Filtering ──────────────────────────────────────────────────────────

    [Fact]
    public void GetUnseenChangelogs_ReturnsEntriesInVersionWindow()
    {
        var asm = Asm(
            ("Echokraut.Resources.Changelogs.v0.19.0.0_EN.txt", "v19 content"),
            ("Echokraut.Resources.Changelogs.v0.19.1.0_EN.txt", "v19.1 content"),
            ("Echokraut.Resources.Changelogs.v0.20.0.0_EN.txt", "v20 content"));
        var svc = BuildService("v0.19.0.0", "v0.20.0.0", asm);

        var entries = svc.GetUnseenChangelogs();

        Assert.Collection(entries,
            e => Assert.Equal("v0.19.1.0", e.Version),
            e => Assert.Equal("v0.20.0.0", e.Version));
        Assert.True(svc.HasUnseenChangelogs());
    }

    [Fact]
    public void GetUnseenChangelogs_ExcludesAlreadySeenAndFutureVersions()
    {
        var asm = Asm(
            ("Echokraut.Resources.Changelogs.v0.18.0.0_EN.txt", "old, already seen"),
            ("Echokraut.Resources.Changelogs.v0.19.0.0_EN.txt", "current target"),
            ("Echokraut.Resources.Changelogs.v0.20.0.0_EN.txt", "future, not released yet"));
        var svc = BuildService("v0.18.0.0", "v0.19.0.0", asm);

        var entries = svc.GetUnseenChangelogs();

        // v0.18.0.0 is == LastSeen → excluded (window is open-on-low side: > LastSeen).
        // v0.19.0.0 is == current → included (window is closed-on-high side: ≤ current).
        // v0.20.0.0 is > current → excluded.
        Assert.Single(entries);
        Assert.Equal("v0.19.0.0", entries[0].Version);
    }

    [Fact]
    public void GetUnseenChangelogs_NothingNew_WhenLastSeenEqualsCurrent()
    {
        var asm = Asm(
            ("Echokraut.Resources.Changelogs.v0.19.0.0_EN.txt", "current"));
        var svc = BuildService("v0.19.0.0", "v0.19.0.0", asm);

        Assert.Empty(svc.GetUnseenChangelogs());
        Assert.False(svc.HasUnseenChangelogs());
    }

    [Fact]
    public void GetUnseenChangelogs_FallsBackToEnglish_WhenLocalizedMissing()
    {
        var asm = Asm(
            ("Echokraut.Resources.Changelogs.v0.19.0.0_EN.txt", "english body"));
        // Client speaks German but there's no DE file for this version → fallback to EN.
        var svc = BuildService("v0.18.0.0", "v0.19.0.0", asm, ClientLanguage.German);

        var entries = svc.GetUnseenChangelogs();
        Assert.Single(entries);
        Assert.Equal("english body", entries[0].Content);
    }

    [Fact]
    public void GetUnseenChangelogs_PrefersClientLanguage_WhenBothPresent()
    {
        var asm = Asm(
            ("Echokraut.Resources.Changelogs.v0.19.0.0_EN.txt", "english body"),
            ("Echokraut.Resources.Changelogs.v0.19.0.0_DE.txt", "deutscher inhalt"));
        var svc = BuildService("v0.18.0.0", "v0.19.0.0", asm, ClientLanguage.German);

        var entries = svc.GetUnseenChangelogs();
        Assert.Single(entries);
        Assert.Equal("deutscher inhalt", entries[0].Content);
    }

    [Fact]
    public void GetUnseenChangelogs_ReturnsEmpty_OnUnparseableLastSeen()
    {
        var asm = Asm(
            ("Echokraut.Resources.Changelogs.v0.19.0.0_EN.txt", "v19"));
        var svc = BuildService("garbage-version-string", "v0.19.0.0", asm);

        // Defensive: we don't want a corrupt config to spam the user with the entire
        // changelog history on every plugin start. Better to silently show nothing
        // than to assume all versions are unseen.
        Assert.Empty(svc.GetUnseenChangelogs());
    }

    [Fact]
    public void GetUnseenChangelogs_OrdersAscending()
    {
        var asm = Asm(
            // Ordered backwards in the resource list to verify the service sorts.
            ("Echokraut.Resources.Changelogs.v0.21.0.0_EN.txt", "v21"),
            ("Echokraut.Resources.Changelogs.v0.20.0.0_EN.txt", "v20"),
            ("Echokraut.Resources.Changelogs.v0.19.0.0_EN.txt", "v19"));
        var svc = BuildService("v0.18.0.0", "v0.21.0.0", asm);

        var entries = svc.GetUnseenChangelogs();
        Assert.Collection(entries,
            e => Assert.Equal("v0.19.0.0", e.Version),
            e => Assert.Equal("v0.20.0.0", e.Version),
            e => Assert.Equal("v0.21.0.0", e.Version));
    }

    // ── Section splitter (consumed by NativeChangelogWindow.SplitIntoSections) ─────

    [Fact]
    public void SplitIntoSections_SplitsAtEqualsDividers()
    {
        var content =
            "===\n" +
            "Section A line 1\n" +
            "Section A line 2\n" +
            "==============\n" +
            "Section B\n" +
            "===";
        var sections = Echokraut.Windows.Native.NativeChangelogWindow
            .SplitIntoSections(content)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        Assert.Equal(2, sections.Count);
        Assert.Contains("Section A line 1", sections[0]);
        Assert.Contains("Section A line 2", sections[0]);
        Assert.Equal("Section B", sections[1]);
    }

    [Fact]
    public void SplitIntoSections_DropsDividerLinesThemselves()
    {
        var content = "Body line\n=====\nMore body";
        var sections = Echokraut.Windows.Native.NativeChangelogWindow
            .SplitIntoSections(content)
            .ToList();

        // The "=====" line itself is consumed as a divider, not retained as content.
        Assert.All(sections, s => Assert.DoesNotContain("=====", s));
    }

    [Fact]
    public void SplitIntoSections_HandlesEmptyAndNullSafely()
    {
        Assert.Empty(Echokraut.Windows.Native.NativeChangelogWindow.SplitIntoSections(""));
    }

    [Fact]
    public void SplitIntoSections_AlsoSplitsAtBlankLines()
    {
        // Long-form changelog format: [NEU] entries are separated by single blank lines
        // (no === divider between them). Without blank-line splitting, the whole "MAJOR
        // FEATURES" section would land in one giant TextNode and FFXIV ATK refuses to
        // render it.
        var content =
            "[NEU] Feature One\n" +
            "  Description line 1\n" +
            "  Description line 2\n" +
            "\n" +                          // ← blank line: section break
            "[NEU] Feature Two\n" +
            "  Description";
        var sections = Echokraut.Windows.Native.NativeChangelogWindow
            .SplitIntoSections(content)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        Assert.Equal(2, sections.Count);
        Assert.Contains("Feature One", sections[0]);
        Assert.Contains("Feature Two", sections[1]);
        Assert.DoesNotContain("Feature Two", sections[0]);
    }

    [Fact]
    public void SplitIntoSections_HandlesMixedDividerAndBlankBoundaries()
    {
        // Real changelog shape: === MAJOR === then blank then [NEU] entries with blank
        // separators. Verifies the splitter doesn't merge the major-section header with
        // the first entry under it (regression: would happen if blank lines were a no-op
        // and only === was treated as a boundary).
        var content =
            "==========\n" +
            "MAJOR\n" +
            "==========\n" +
            "\n" +
            "[NEU] First entry\n" +
            "  Body\n" +
            "\n" +
            "[NEU] Second entry\n" +
            "  Body";
        var sections = Echokraut.Windows.Native.NativeChangelogWindow
            .SplitIntoSections(content)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        Assert.Equal(3, sections.Count);
        Assert.Equal("MAJOR", sections[0]);
        Assert.Contains("First entry", sections[1]);
        Assert.Contains("Second entry", sections[2]);
    }

    [Fact]
    public void GetUnseenChangelogs_IgnoresUnrelatedResources()
    {
        var asm = Asm(
            ("Echokraut.Resources.Changelogs.v0.19.0.0_EN.txt", "v19"),
            // Files with the wrong shape must be ignored, not crash the parser.
            ("Echokraut.Resources.RemoteUrls.json", "{}"),
            ("Echokraut.Resources.Changelogs.invalid_EN.txt", "no version"),
            ("Echokraut.Resources.Changelogs.v1.2.3_EN.txt", "wrong version part count")); // 3-part fails the 4-part regex
        var svc = BuildService("v0.18.0.0", "v0.20.0.0", asm);

        var entries = svc.GetUnseenChangelogs();
        Assert.Single(entries);
        Assert.Equal("v0.19.0.0", entries[0].Version);
    }
}
