using System;
using System.Collections.Generic;
using Dalamud.Game;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Unit tests for the deterministic units of the voice-sample extractor pipeline.
/// SCD decoding and BASS resampling are not testable here (need game files / runtime).
/// </summary>
public class VoiceSampleExtractorTests
{
    // ── VoiceExtractKey.TryParse ─────────────────────────────────────────────

    [Theory]
    [InlineData("TEXT_GAIUSA308_00740_BUSCARRON_000_058", true, "058", "vo_gaiusa308_00740_buscarron_000")]
    [InlineData("TEXT_VOICEMAN_00100_010_001_ALPHINAUD", true, "alphinaud", "vo_voiceman_00100_010_001")]
    [InlineData("TEXT_NOT_ENOUGH_PARTS", false, "", "")]
    [InlineData("VOICE_GAIUSA308_00740_BUSCARRON_000_058", false, "", "")] // doesn't start with TEXT
    [InlineData("", false, "", "")]
    public void TryParse_HandlesValidAndInvalidShapes(string textKey, bool ok, string speaker, string audioBase)
    {
        var success = VoiceExtractKey.TryParse(textKey, out var s, out var a);
        Assert.Equal(ok, success);
        if (ok)
        {
            Assert.Equal(speaker, s);
            Assert.Equal(audioBase, a);
        }
    }

    // ── VoiceExtractKey.Normalize ────────────────────────────────────────────

    [Theory]
    [InlineData("Y'shtola Rhul", "yshtolarhul")]
    [InlineData("Au Ra", "aura")]
    [InlineData("Hyur-Highlander", "hyurhighlander")]
    [InlineData("ALISAIE", "alisaie")]
    [InlineData("", "")]
    public void Normalize_StripsSpacesApostrophesHyphensAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, VoiceExtractKey.Normalize(input));
    }

    // ── VoiceExtractKey.Resolve ──────────────────────────────────────────────

    [Fact]
    public void Resolve_DirectMatch_ReturnsSingleId()
    {
        var npcNames = new Dictionary<uint, Dictionary<string, string>>
        {
            [100] = new() { ["en"] = "Alphinaud Leveilleur" },
            [200] = new() { ["en"] = "Tataru" },
        };
        var index = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        var ids = VoiceExtractKey.Resolve("tataru", index);
        Assert.NotNull(ids);
        Assert.Single(ids);
        Assert.Equal(200u, ids![0]);
    }

    [Fact]
    public void Resolve_SubstringMatch_FindsLongerName()
    {
        var npcNames = new Dictionary<uint, Dictionary<string, string>>
        {
            [100] = new() { ["en"] = "Alphinaud Leveilleur" },
        };
        var index = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        var ids = VoiceExtractKey.Resolve("alphinaud", index);
        Assert.NotNull(ids);
        Assert.Contains(100u, ids!);
    }

    [Fact]
    public void Resolve_VeryShortName_DoesNotSubstringMatch()
    {
        var npcNames = new Dictionary<uint, Dictionary<string, string>>
        {
            [100] = new() { ["en"] = "Alphinaud" },
        };
        var index = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        // 'alp' is too short — substring fallback skipped to avoid noise matches
        var ids = VoiceExtractKey.Resolve("alp", index);
        Assert.Null(ids);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        var npcNames = new Dictionary<uint, Dictionary<string, string>>
        {
            [100] = new() { ["en"] = "Alphinaud Leveilleur" },
        };
        var index = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        Assert.Null(VoiceExtractKey.Resolve("redbellygutter", index));
    }

    // ── VoiceExtractTextCleaner ──────────────────────────────────────────────

    [Fact]
    public void Clean_StripsForenameTags()
    {
        var result = VoiceExtractTextCleaner.Clean("Hello, <forename>! Good morning.", ClientLanguage.English);
        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Contains("Hello,", result![0]);
        Assert.DoesNotContain("forename", result[0]);
    }

    [Fact]
    public void Clean_ExpandsGenderBranchIntoTwoVariants()
    {
        var input = "<If(PlayerParameter(4))>my dear lady<Else/>my good sir</If>, welcome.";
        var result = VoiceExtractTextCleaner.Clean(input, ClientLanguage.English);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Length);
        Assert.Contains("good sir", result[0]);    // male
        Assert.Contains("dear lady", result[1]);   // female
    }

    [Fact]
    public void Clean_ReturnsNullForBlankInput()
    {
        Assert.Null(VoiceExtractTextCleaner.Clean("", ClientLanguage.English));
        Assert.Null(VoiceExtractTextCleaner.Clean("   ", ClientLanguage.English));
    }

    [Fact]
    public void Clean_StripsParenSpeakerPrefix()
    {
        var result = VoiceExtractTextCleaner.Clean("(-???-)Who goes there?", ClientLanguage.English);
        Assert.NotNull(result);
        Assert.DoesNotContain("(-", result![0]);
        Assert.Contains("Who goes there?", result[0]);
    }

    // ── VoiceExtractFileNames ────────────────────────────────────────────────

    [Theory]
    [InlineData("Y'shtola/Rhul", "Y'shtola_Rhul")]
    [InlineData("File*Name?", "File_Name_")]
    [InlineData("Normal Name", "Normal Name")]
    public void Sanitize_ReplacesFsIllegalChars(string input, string expected)
    {
        Assert.Equal(expected, VoiceExtractFileNames.Sanitize(input));
    }

    [Fact]
    public void GetNamedTargetPath_SingleSample_IsFlat()
    {
        var path = VoiceExtractFileNames.GetNamedTargetPath(@"C:\save", "Female", "Hyur", "Tataru", 1, 1);
        Assert.Contains("FF14-Voices", path);
        Assert.EndsWith("Female_Hyur_Tataru.wav", path);
        Assert.DoesNotContain("Tataru" + System.IO.Path.DirectorySeparatorChar, path);
    }

    [Fact]
    public void GetNamedTargetPath_MultiSample_HasSubfolder()
    {
        var path = VoiceExtractFileNames.GetNamedTargetPath(@"C:\save", "Female", "Hyur", "Tataru", 3, 5);
        Assert.Contains("FF14-Voices", path);
        Assert.Contains("Tataru" + System.IO.Path.DirectorySeparatorChar, path);
        Assert.EndsWith("Female_Hyur_Tataru_3.wav", path);
    }

    [Fact]
    public void GetCatalogTargetPath_FormatsId_With3Digits()
    {
        var path = VoiceExtractFileNames.GetCatalogTargetPath(@"C:\save", "Male", 7, 2);
        Assert.EndsWith("Male_All_NPC007_2.wav", path);
        Assert.Contains("FF14-Voices" + System.IO.Path.DirectorySeparatorChar + "NPC", path);
    }

    [Fact]
    public void GetCatalogTargetPath_AutoWidens_To4DigitsAt1000()
    {
        var path = VoiceExtractFileNames.GetCatalogTargetPath(@"C:\save", "Male", 1234, 1);
        Assert.EndsWith("Male_All_NPC1234_1.wav", path);
    }

    // ── VoiceExtractSampleSelector.ApplyLengthFilter ─────────────────────────

    [Fact]
    public void ApplyLengthFilter_KeepsOnlyInWindowClips()
    {
        var clips = new[] { 4.0, 6.5, 9.0, 13.0 };
        var result = VoiceExtractSampleSelector.ApplyLengthFilter(clips, x => x, 6.0, 12.0);
        Assert.Equal(2, result.Count);
        Assert.Contains(6.5, result);
        Assert.Contains(9.0, result);
    }

    [Fact]
    public void ApplyLengthFilter_FallbackPicksClosestWhenWindowIsEmpty()
    {
        // All clips are < 6s. Closest to window = 5.5s (smallest gap of 0.5).
        var clips = new[] { 2.0, 5.5, 4.0 };
        var result = VoiceExtractSampleSelector.ApplyLengthFilter(clips, x => x, 6.0, 12.0);
        Assert.Single(result);
        Assert.Equal(5.5, result[0]);
    }

    [Fact]
    public void ApplyLengthFilter_FallbackPicksClosestForOversizedClips()
    {
        // All clips are > 12s. Closest = 13s (gap of 1).
        var clips = new[] { 20.0, 13.0, 15.0 };
        var result = VoiceExtractSampleSelector.ApplyLengthFilter(clips, x => x, 6.0, 12.0);
        Assert.Single(result);
        Assert.Equal(13.0, result[0]);
    }

    [Fact]
    public void ApplyLengthFilter_EmptyInput_ReturnsEmpty()
    {
        var result = VoiceExtractSampleSelector.ApplyLengthFilter(Array.Empty<double>(), x => x, 6.0, 12.0);
        Assert.Empty(result);
    }

    // ── VoiceExtractSampleSelector.PickN ─────────────────────────────────────

    [Fact]
    public void PickN_KeepsAll_WhenInputSmallerThanN()
    {
        var clips = new[] { 1, 2, 3 };
        var picked = VoiceExtractSampleSelector.PickN(clips, 5, seed: 42);
        Assert.Equal(3, picked.Count);
    }

    [Fact]
    public void PickN_Deterministic_SameSeedSameOutput()
    {
        var clips = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var a = VoiceExtractSampleSelector.PickN(clips, 3, seed: 42);
        var b = VoiceExtractSampleSelector.PickN(clips, 3, seed: 42);
        Assert.Equal(a, b);
    }

    [Fact]
    public void PickN_DifferentSeeds_DifferentSelections()
    {
        var clips = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var a = VoiceExtractSampleSelector.PickN(clips, 3, seed: 1);
        var b = VoiceExtractSampleSelector.PickN(clips, 3, seed: 2);
        Assert.NotEqual(a, b);
    }

    // ── WavInspector ─────────────────────────────────────────────────────────

    [Fact]
    public void WavInspector_ReadsDurationFromMinimalPcmWav()
    {
        // 1 second of 16-bit 44100 Hz mono → 88200 bytes data
        var sampleRate = 44100;
        var bitsPerSample = (short)16;
        var channels = (short)1;
        var dataBytes = sampleRate * channels * bitsPerSample / 8;

        using var ms = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write(0x46464952); // "RIFF"
            bw.Write(36 + dataBytes);
            bw.Write(0x45564157); // "WAVE"
            bw.Write(0x20746D66); // "fmt "
            bw.Write(16);          // chunk size
            bw.Write((short)1);    // PCM
            bw.Write(channels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * channels * bitsPerSample / 8); // bytesPerSec
            bw.Write((short)(channels * bitsPerSample / 8));      // block align
            bw.Write(bitsPerSample);
            bw.Write(0x61746164); // "data"
            bw.Write(dataBytes);
            bw.Write(new byte[dataBytes]);
        }
        ms.Position = 0;
        var seconds = WavInspector.GetDurationSeconds(ms);
        Assert.Equal(1.0, seconds, 2);
    }

    [Fact]
    public void WavInspector_ReturnsZeroForBlobThatIsntWav()
    {
        using var ms = new System.IO.MemoryStream(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.Equal(0, WavInspector.GetDurationSeconds(ms));
    }
}
