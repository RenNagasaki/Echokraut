using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Localization;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Opt-in rules of plan game-voice-reference §5.2/§5.3.</summary>
public class GameVoiceConsentTests
{
    /// <summary>
    /// The text the user confirmed is pinned per version. If this goes red you changed the consent
    /// text: bump <see cref="GameVoiceConsent.CurrentVersion"/> (everyone is asked again) and
    /// update the pair below — never just update the hash.
    /// </summary>
    [Fact]
    public void ConsentText_ChangeRequiresVersionBump()
    {
        var pinned = new Dictionary<int, string>
        {
            [1] = "231918708AE54E14EAB15226A87C7CC04095A4686A704CCDAD1CAD45BDA7AC82",
        };
        Assert.True(pinned.ContainsKey(GameVoiceConsent.CurrentVersion), "pin the hash of the new version");
        Assert.Equal(pinned[GameVoiceConsent.CurrentVersion], GameVoiceConsent.TextHash());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(GameVoiceConsent.CurrentVersion, true)]
    [InlineData(GameVoiceConsent.CurrentVersion - 1, false)]
    [InlineData(GameVoiceConsent.CurrentVersion + 1, false)]
    public void IsGranted_OnlyForTheCurrentText(int stored, bool expected)
    {
        Assert.Equal(expected, GameVoiceConsent.IsGranted(stored));
        Assert.Equal(!expected, GameVoiceConsent.NeedsDialog(stored));
    }

    [Theory]
    [InlineData(TTSBackends.EchokrauTTS, AlltalkInstanceType.Remote, true)]
    [InlineData(TTSBackends.EchokrauTTS, AlltalkInstanceType.Local, false)]
    [InlineData(TTSBackends.EchokrauTTS, AlltalkInstanceType.None, false)]
    [InlineData(TTSBackends.Alltalk, AlltalkInstanceType.Remote, false)]
    public void RemoteHint_OnlyForRemoteEchokrauTts(TTSBackends backend, AlltalkInstanceType type, bool expected)
        => Assert.Equal(expected, GameVoiceConsent.ShowRemoteHint(backend, type));

    [Fact]
    public void AvailabilityText_DistinctForEveryState()
    {
        var texts = Enum.GetValues<GameVoiceAvailability>().Select(GameVoiceConsent.AvailabilityText).ToList();
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    [Fact]
    public void ConsentText_NamesTheOwnPrivateInstanceRule()
    {
        // §5.3: the opt-in itself must say it, not only the settings page.
        Assert.Contains(GameVoiceConsent.Paragraphs, p => p.Contains("own private EchokrauTTS instance"));
        Assert.Contains(GameVoiceConsent.Paragraphs, p => p.Contains("Do not publish, stream, record or share"));
    }

    /// <summary>Every user-visible string of the feature has DE, FR and JP (CLAUDE.md › Localization).</summary>
    [Fact]
    public void AllStrings_AreTranslated()
    {
        var keys = new List<string>
        {
            GameVoiceConsent.Title, GameVoiceConsent.Accept, GameVoiceConsent.Decline,
            GameVoiceConsent.RemoteHint, GameVoiceConsent.ExportHint,
            GameVoiceConsent.DisableTitle, GameVoiceConsent.DisableBody,
            GameVoiceConsent.DisableDelete, GameVoiceConsent.DisableKeep,
            "Game voice reference", "Game voice", "Build index for all voiced NPCs",
            "Check the best clips with Whisper (slower, better)",
            "Also detect voice-actor changes (suggestions for the maintainers, best with XTTS)",
            "Every voiced NPC has a current game-voice index.",
            "{0} voiced speaker(s) without a current index (new game patch or first run).",
            "{0} left", "Index not built: {0}",
            "Index built: {0} ranked ({1} with a game voice), {2} up to date, {3} unmatched (report in the harvest folder).",
            "Cancelled: {0} ranked ({1} with a game voice), {2} up to date, {3} unmatched (report in the harvest folder). A new run continues.",
            "Use game voices as reference (EchokrauTTS)",
            "Rank the current NPC when a conversation starts",
            "Building game-voice index...", "Deleting clips made with game voices...",
            "Deleted {0} clip(s) made with game voices.",
            "Index build failed, see log.",
        };
        keys.AddRange(GameVoiceConsent.Paragraphs);
        keys.AddRange(Enum.GetValues<GameVoiceAvailability>().Select(GameVoiceConsent.AvailabilityText));

        var field = typeof(Loc).GetField("Translations", BindingFlags.NonPublic | BindingFlags.Static)!;
        var table = (Dictionary<string, Dictionary<ClientLanguage, string>>)field.GetValue(null)!;
        foreach (var key in keys)
        {
            Assert.True(table.TryGetValue(key, out var map), $"missing Loc entry: {key}");
            foreach (var lang in new[] { ClientLanguage.German, ClientLanguage.French, ClientLanguage.Japanese })
                Assert.True(map!.TryGetValue(lang, out var t) && !string.IsNullOrWhiteSpace(t), $"{lang} missing for: {key}");
        }
    }

    // ── Configuration.MigrateGameVoiceConsent ───────────────────────────────

    [Fact]
    public void Migrate_EnabledWithoutCurrentConsent_TurnsOff()
    {
        var config = new Configuration { GameVoiceReferenceEnabled = true, GameVoiceConsentVersion = 0 };
        config.MigrateGameVoiceConsent();
        Assert.False(config.GameVoiceReferenceEnabled);
    }

    [Fact]
    public void Migrate_EnabledWithCurrentConsent_StaysOn()
    {
        var config = new Configuration { GameVoiceReferenceEnabled = true, GameVoiceConsentVersion = GameVoiceConsent.CurrentVersion };
        config.MigrateGameVoiceConsent();
        config.MigrateGameVoiceConsent(); // idempotent
        Assert.True(config.GameVoiceReferenceEnabled);
    }

    [Fact]
    public void Default_IsOff()
    {
        var config = new Configuration();
        Assert.False(config.GameVoiceReferenceEnabled);
        Assert.Equal(0, config.GameVoiceConsentVersion);
    }
}
