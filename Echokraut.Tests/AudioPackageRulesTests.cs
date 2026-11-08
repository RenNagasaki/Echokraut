using System.IO;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The pure rules behind audio package export/import. These decide what leaves a user's install
/// and where a foreign file is allowed to land, so they are covered here rather than left to the
/// service's IO paths.
/// </summary>
public class AudioPackageRulesTests
{
    // ── IsShareable ─────────────────────────────────────────

    [Fact]
    public void IsShareable_ClipWithoutPlaceholder_IsShareable()
    {
        Assert.True(AudioPackageRules.IsShareable(hasPlayerPlaceholder: false, aliasGender: 0));
    }

    [Fact]
    public void IsShareable_PlaceholderClipGeneratedForTheRealPlayer_IsNotShareable()
    {
        // alias_gender 0 on a placeholder clip = the exporter's own character name is spoken.
        Assert.False(AudioPackageRules.IsShareable(hasPlayerPlaceholder: true, aliasGender: 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void IsShareable_AliasVariantsOfAPlaceholderClip_AreShareable(int aliasGender)
    {
        Assert.True(AudioPackageRules.IsShareable(hasPlayerPlaceholder: true, aliasGender));
    }

    // ── RelativeEntryPath ───────────────────────────────────

    [Fact]
    public void RelativeEntryPath_KeepsOnlySpeakerFolderAndFileName()
    {
        var relative = AudioPackageRules.RelativeEntryPath(@"F:\Echokraut\Audio\Alphinaud\hello.wav");
        Assert.Equal("Alphinaud/hello.wav", relative);
    }

    [Fact]
    public void RelativeEntryPath_NormalizesForwardSlashPaths()
    {
        var relative = AudioPackageRules.RelativeEntryPath("/home/user/audio/Y'shtola/line.wav");
        Assert.Equal("Y'shtola/line.wav", relative);
    }

    [Fact]
    public void RelativeEntryPath_NeverLeaksTheSendersRoot()
    {
        var relative = AudioPackageRules.RelativeEntryPath(@"C:\Users\SomeName\Documents\ek\NPC\a.wav");
        Assert.DoesNotContain("SomeName", relative);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RelativeEntryPath_EmptyInput_IsNull(string? savePath)
    {
        Assert.Null(AudioPackageRules.RelativeEntryPath(savePath));
    }

    // ── ResolveImportTarget ─────────────────────────────────

    [Fact]
    public void ResolveImportTarget_PutsTheEntryUnderTheSaveLocation()
    {
        var root = Path.Combine(Path.GetTempPath(), "ek-import-test");
        var target = AudioPackageRules.ResolveImportTarget(root, "Alphinaud/hello.wav");

        Assert.NotNull(target);
        Assert.StartsWith(Path.GetFullPath(root), target!);
        Assert.EndsWith("hello.wav", target);
    }

    [Theory]
    [InlineData("../../evil.wav")]
    [InlineData("Alphinaud/../../../evil.wav")]
    public void ResolveImportTarget_ZipSlipIsRejected(string entryFile)
    {
        var root = Path.Combine(Path.GetTempPath(), "ek-import-test");
        Assert.Null(AudioPackageRules.ResolveImportTarget(root, entryFile));
    }

    [Fact]
    public void ResolveImportTarget_AbsoluteEntryPathIsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "ek-import-test");
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere.wav");
        Assert.Null(AudioPackageRules.ResolveImportTarget(root, absolute));
    }

    [Fact]
    public void ResolveImportTarget_WithoutASaveLocation_IsNull()
    {
        Assert.Null(AudioPackageRules.ResolveImportTarget("", "Alphinaud/hello.wav"));
    }

    // ── ContextTypeFor ──────────────────────────────────────

    [Fact]
    public void ContextTypeFor_PlayerCharacter_IsPlayer()
    {
        var pc = (int)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc;
        var chat = (int)Echotools.Logging.Enums.TextSource.Chat;
        Assert.Equal("player", AudioPackageRules.ContextTypeFor(pc, chat));
    }

    [Fact]
    public void ContextTypeFor_BubbleLineOfAnNpc_IsBubble()
    {
        var npc = (int)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc;
        var bubble = (int)Echotools.Logging.Enums.TextSource.AddonBubble;
        Assert.Equal("bubble", AudioPackageRules.ContextTypeFor(npc, bubble));
    }

    [Fact]
    public void ContextTypeFor_RegularDialogue_IsNpc()
    {
        var npc = (int)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc;
        var talk = (int)Echotools.Logging.Enums.TextSource.AddonTalk;
        Assert.Equal("npc", AudioPackageRules.ContextTypeFor(npc, talk));
    }

    // ── Version gate ────────────────────────────────────────

    [Fact]
    public void IsSupportedVersion_CurrentFormat_IsReadable()
    {
        Assert.True(AudioPackageRules.IsSupportedVersion(Echokraut.DataClasses.AudioPackageFormat.CurrentVersion));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void IsSupportedVersion_UnknownFormat_IsRejected(int version)
    {
        Assert.False(AudioPackageRules.IsSupportedVersion(version));
    }

    [Fact]
    public void ImportedGenerationsCarryNoPlayerId()
    {
        // A package must never bind audio to anybody's character id.
        Assert.Equal(0L, AudioPackageRules.ImportPlayerContentId);
    }
}
