using System;
using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
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
    // Real-world VOICEMAN keys: TEXT_VOICEMAN_<cutscene>_<line>_<character> (5 tokens).
    [InlineData("TEXT_VOICEMAN_06006_000010_YSHTOLA", true, "yshtola", "vo_voiceman_06006_000010")]
    [InlineData("TEXT_VOICEMAN_07000_000010_WUKLAMAT", true, "wuklamat", "vo_voiceman_07000_000010")]
    // Real-world MANFST keys follow the same 5-token shape.
    [InlineData("TEXT_MANFST_00100_000010_ALPHINAUD", true, "alphinaud", "vo_manfst_00100_000010")]
    // System markers — speaker name carries underscores → 7 tokens → reject (Tools also skips).
    [InlineData("TEXT_VOICEMAN_06005_000010_SYSTEM_NONE_VOICE", false, "", "")]
    // Older 6-token quest-name keys whose trailing token is a line number, not a speaker — reject.
    [InlineData("TEXT_GAIUSA308_00740_BUSCARRON_000_058", false, "", "")]
    [InlineData("TEXT_NOT_ENOUGH", false, "", "")]
    [InlineData("VOICE_VOICEMAN_06006_000010_YSHTOLA", false, "", "")] // doesn't start with TEXT
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

    [Fact]
    public void Resolve_PrefixOfDifferentName_DoesNotMatch()
    {
        // Regression: speaker token "ABEL" must NOT resolve to the unrelated "Abelie" just
        // because "abelie" starts with "abel". The old greedy prefix match did exactly that,
        // mislabelling a male character's line as Female_Elezen_Abelie.
        var npcNames = new Dictionary<uint, Dictionary<string, string>>
        {
            [100] = new() { ["en"] = "Abelie" },
        };
        var index = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        Assert.Null(VoiceExtractKey.Resolve("abel", index));
        // The real name still resolves exactly.
        Assert.Equal(100u, VoiceExtractKey.Resolve("abelie", index)![0]);
    }

    [Fact]
    public void Resolve_FirstNameToken_ResolvesViaFirstNameIndex()
    {
        // "alisaie" is the given name of "Alisaie Leveilleur" — resolves via the first-name
        // index, but "alis" (a non-name prefix) does not.
        var npcNames = new Dictionary<uint, Dictionary<string, string>>
        {
            [7] = new() { ["en"] = "Alisaie Leveilleur" },
        };
        var index = VoiceExtractKey.BuildNormalizedNameIndex(npcNames);
        Assert.Equal(7u, VoiceExtractKey.Resolve("alisaie", index)![0]);
        Assert.Null(VoiceExtractKey.Resolve("alis", index));
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

    // ── VoiceExtractTextCleaner.IsSpeech ─────────────────────────────────────
    // Pure static predicate — no game runtime / Lumina needed. Thresholds are the
    // plan's starting estimates (EN/FR 4 words, DE 3 words, JP 8 chars); Open Question #1
    // tracks empirical re-tuning. Test names document the chosen boundary values.

    [Theory]
    [InlineData("...")]
    [InlineData("!?")]
    [InlineData("……")]
    [InlineData("。。。")]
    [InlineData("?!?!")]
    public void IsSpeech_RejectsPurePunctuation(string text)
    {
        Assert.False(VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.English));
        Assert.False(VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.Japanese));
    }

    [Theory]
    [InlineData("Hahaha!")]     // reduplication (Ha)+
    [InlineData("Wahaha")]      // single token, 1 word < EN min 4
    [InlineData("Heeheehee")]   // reduplication (Hee)+
    [InlineData("Noooo")]       // char-run: o repeated 4×
    [InlineData("Aaaah!")]      // char-run: a repeated 4×
    [InlineData("haha")]        // reduplication (ha)+
    public void IsSpeech_RejectsRepeatedCharOrSyllableNoises(string text)
    {
        Assert.False(VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.English));
    }

    [Theory]
    [InlineData("*sigh*")]
    [InlineData("[laughter]")]
    public void IsSpeech_RejectsBracketedSoundDescription(string text)
    {
        Assert.False(VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.English));
    }

    [Theory]
    [InlineData("Ha ha ha ha")]   // 4 "words" but collapses to a reduplication
    [InlineData("Ho ho ho!")]
    [InlineData("He he he")]
    public void IsSpeech_RejectsSpacedOutLaughs(string text)
    {
        Assert.False(VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.English));
    }

    [Theory]
    [InlineData("Hmph.")]
    [InlineData("Yes.")]
    [InlineData("I see.")]          // 2 words < EN min 4
    [InlineData("I trust you.")]    // 3 words < EN min 4 — boundary case, documented as non-speech
    public void IsSpeech_RejectsShortLines_EN(string text)
    {
        Assert.False(VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.English));
    }

    [Theory]
    [InlineData("Thank you for coming.")]                          // exactly 4 words → accepted
    [InlineData("She speaks so softly, yet carries great resolve.")]
    [InlineData("Ha, that was unexpected.")]                       // contains "Ha" but not a pure repeat run
    public void IsSpeech_AcceptsRealLines_EN(string text)
    {
        Assert.True(VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.English));
    }

    [Theory]
    [InlineData("Ich verstehe.", false)]            // 2 words < DE min 3
    [InlineData("Ich verstehe das.", true)]         // 3 words → accepted (DE threshold)
    [InlineData("Das ist eine gute Idee.", true)]
    public void IsSpeech_GermanWordThreshold(string text, bool expected)
    {
        Assert.Equal(expected, VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.German));
    }

    [Theory]
    [InlineData("あいうえお", false)]              // 5 chars < JP min 8
    [InlineData("あいうえおかき", false)]          // 7 chars < JP min 8
    [InlineData("あいうえおかきく", true)]         // 8 chars → accepted (JP boundary)
    [InlineData("これは普通の会話の文章です。", true)]
    public void IsSpeech_JapaneseCharThreshold(string text, bool expected)
    {
        Assert.Equal(expected, VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.Japanese));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSpeech_RejectsBlank(string text)
    {
        Assert.False(VoiceExtractTextCleaner.IsSpeech(text, ClientLanguage.English));
    }

    [Fact]
    public void IsSpeech_AgreesAcrossGenderVariants()
    {
        // The extractor only checks cleaned[0] (male). This locks in the assumption that the
        // female variant yields the same verdict — gender expansion swaps tokens, not structure.
        var result = VoiceExtractTextCleaner.Clean(
            "<If(PlayerParameter(4))>my dear lady<Else/>my good sir</If>, you are most welcome here.",
            ClientLanguage.English);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Length);
        Assert.Equal(
            VoiceExtractTextCleaner.IsSpeech(result[0], ClientLanguage.English),
            VoiceExtractTextCleaner.IsSpeech(result[1], ClientLanguage.English));
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

    [Theory]
    // Alias map (matches DialogHarvestService.RaceNameMap so voice resolution agrees).
    [InlineData("Hyuran", "Hyur")]
    [InlineData("Au Ra", "AuRa")]
    [InlineData("Miqo'te", "Miqote")]
    // Strip-separator fallback for races without an alias entry.
    [InlineData("Hyur", "Hyur")]
    [InlineData("Hrothgar", "Hrothgar")]
    [InlineData("Lalafell", "Lalafell")]
    [InlineData("Hyur-Highlander", "HyurHighlander")]
    [InlineData("", "")]
    public void NormalizeRace_MapsAliasesAndStripsSeparators(string input, string expected)
    {
        Assert.Equal(expected, VoiceExtractFileNames.NormalizeRace(input));
    }

    [Fact]
    public void CanonicalNamePart_NormalizesRaceButKeepsNameSpaces()
    {
        var part = VoiceExtractFileNames.CanonicalNamePart("Female", "Au Ra", "Adult", "Y'shtola Rhul");
        Assert.Equal("Female_AuRa_Y'shtola Rhul", part);
    }

    [Fact]
    public void CanonicalNamePart_AppendsBodyTypeSuffix_ForChild()
    {
        // Child / Elder NPCs get a "-Child" / "-Elder" suffix on the race segment so AllTalk's
        // age-based voice picker can match them. Suffix is glued to the race with "-".
        var part = VoiceExtractFileNames.CanonicalNamePart("Female", "Hyur", "Child", "Tataru");
        Assert.Equal("Female_Hyur-Child_Tataru", part);
    }

    [Fact]
    public void CanonicalNamePart_AppendsBodyTypeSuffix_ForElder()
    {
        var part = VoiceExtractFileNames.CanonicalNamePart("Male", "Lalafell", "Elder", "Old Sage");
        Assert.Equal("Male_Lalafell-Elder_Old Sage", part);
    }

    [Theory]
    [InlineData("Adult")]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("garbage")]
    public void CanonicalNamePart_AdultAndUnknownProduceNoSuffix(string bodyType)
    {
        // Adult is the implicit default — no filename token. Unknown / garbage values fall
        // through to the same "no suffix" branch so callers can pass whatever they have.
        var part = VoiceExtractFileNames.CanonicalNamePart("Female", "Hyur", bodyType, "Tataru");
        Assert.Equal("Female_Hyur_Tataru", part);
    }

    [Theory]
    [InlineData("Hyur")]
    [InlineData("Elezen")]
    [InlineData("Miqote")]
    [InlineData("Roegadyn")]
    [InlineData("Lalafell")]
    [InlineData("Viera")]
    [InlineData("AuRa")]
    [InlineData("Hrothgar")]
    // Aliases / un-normalized forms also resolve to player-race.
    [InlineData("Hyuran")]
    [InlineData("Au Ra")]
    [InlineData("Miqo'te")]
    public void IsPlayerRace_ReturnsTrueForAllEightPlayerRaces(string race)
    {
        Assert.True(VoiceExtractFileNames.IsPlayerRace(race));
    }

    [Theory]
    [InlineData("Sylph")]
    [InlineData("Goblin")]
    [InlineData("Amaljaa")]
    [InlineData("Moogle")]
    [InlineData("Loporrit")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void IsPlayerRace_ReturnsFalseForBeastTribesAndUnknown(string race)
    {
        Assert.False(VoiceExtractFileNames.IsPlayerRace(race));
    }

    [Fact]
    public void GetNamedTargetPath_SingleSample_IsFlat()
    {
        var path = VoiceExtractFileNames.GetNamedTargetPath(@"C:\save", "Female", "Hyur", "Adult", "Tataru", 1, 1);
        Assert.Contains("FF14-Voices", path);
        Assert.EndsWith("Female_Hyur_Tataru.wav", path);
        Assert.DoesNotContain("Tataru" + System.IO.Path.DirectorySeparatorChar, path);
    }

    [Fact]
    public void GetNamedTargetPath_NormalizesRaceInFilenameButNotInSubfolder()
    {
        // Race "Miqo'te" → "Miqote" in the canonical filename. Name "Y'shtola Rhul" keeps
        // its apostrophe + space in both the filename and the subfolder.
        var path = VoiceExtractFileNames.GetNamedTargetPath(
            @"C:\save", "Female", "Miqo'te", "Adult", "Y'shtola Rhul", 2, 3);
        Assert.EndsWith("Female_Miqote_Y'shtola Rhul_2.wav", path);
        Assert.Contains("Y'shtola Rhul" + System.IO.Path.DirectorySeparatorChar, path);
        Assert.DoesNotContain("Miqo'te", path);
    }

    [Fact]
    public void GetNamedTargetPath_MultiSample_HasSubfolder()
    {
        var path = VoiceExtractFileNames.GetNamedTargetPath(@"C:\save", "Female", "Hyur", "Adult", "Tataru", 3, 5);
        Assert.Contains("FF14-Voices", path);
        Assert.Contains("Tataru" + System.IO.Path.DirectorySeparatorChar, path);
        Assert.EndsWith("Female_Hyur_Tataru_3.wav", path);
    }

    [Fact]
    public void GetNamedTargetPath_AppendsBodyTypeSuffix_ForChild()
    {
        var path = VoiceExtractFileNames.GetNamedTargetPath(
            @"C:\save", "Female", "Hyur", "Child", "Tataru", 1, 1);
        Assert.EndsWith("Female_Hyur-Child_Tataru.wav", path);
    }

    [Fact]
    public void GetNamedTargetPath_AppendsEpochSuffix_SingleSample()
    {
        var path = VoiceExtractFileNames.GetNamedTargetPath(
            @"C:\save", "Female", "Hyur", "Adult", "Iceheart", 1, 1, "FF14-Voices", "Pre06010");
        Assert.EndsWith("Female_Hyur_Iceheart_Pre06010.wav", path);
    }

    [Fact]
    public void GetNamedTargetPath_AppendsEpochSuffix_MultiSample()
    {
        var path = VoiceExtractFileNames.GetNamedTargetPath(
            @"C:\save", "Female", "Hyur", "Adult", "Iceheart", 2, 3, "FF14-Voices", "Post06010");
        var sep = System.IO.Path.DirectorySeparatorChar;
        Assert.EndsWith("Female_Hyur_Iceheart_Post06010_2.wav", path);
        // Subfolder stays keyed on the name alone so both epochs group together.
        Assert.Contains("Iceheart" + sep, path);
    }

    [Fact]
    public void GetNamedTargetPath_EmptyEpoch_UnchangedFilename()
    {
        var path = VoiceExtractFileNames.GetNamedTargetPath(
            @"C:\save", "Female", "Hyur", "Adult", "Tataru", 1, 1, "FF14-Voices", "");
        Assert.EndsWith("Female_Hyur_Tataru.wav", path);
    }

    [Fact]
    public void GetCatalogTargetPath_PlayerRace_CollapsesToAll()
    {
        // Hyur is a player race → race token is "All".
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Male", "Hyur", "Adult", 7, sampleIndex: 2, totalSamplesPerNpc: 5);
        var sep = System.IO.Path.DirectorySeparatorChar;
        Assert.EndsWith("Male_All_NPC007_2.wav", path);
        Assert.Contains($"FF14-Voices{sep}NPC007{sep}", path);
        Assert.DoesNotContain($"FF14-Voices{sep}NPC{sep}", path);
    }

    [Fact]
    public void GetCatalogTargetPath_NonPlayerRace_KeepsSpecificRaceToken()
    {
        // Sylph is NOT a player race — filename keeps the specific race token instead of "All",
        // so the random-voice catalog won't pool sylph voices with player races.
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Female", "Sylph", "Adult", 42, sampleIndex: 1, totalSamplesPerNpc: 1);
        var sep = System.IO.Path.DirectorySeparatorChar;
        Assert.EndsWith($"FF14-Voices{sep}Female_Sylph_NPC042.wav", path);
        Assert.DoesNotContain("All", path);
    }

    [Fact]
    public void GetCatalogTargetPath_NonPlayerRace_NormalizesRaceToken()
    {
        // "Au Ra" would be a player race anyway, so use a hyphenated non-player race for the
        // normalization check — Mamool Ja → MamoolJa (no spaces).
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Male", "Mamool Ja", "Adult", 9, sampleIndex: 1, totalSamplesPerNpc: 1);
        Assert.EndsWith("Male_MamoolJa_NPC009.wav", path);
        Assert.DoesNotContain("Mamool Ja", path);
    }

    [Fact]
    public void GetCatalogTargetPath_PlayerRace_ChildAppendsBodyTypeSuffix()
    {
        // Child Lalafell NPCs (e.g. miner kids) → "All-Child" so AllTalk picks an
        // age-appropriate voice. Body suffix is appended right after the race token.
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Female", "Lalafell", "Child", 3, sampleIndex: 1, totalSamplesPerNpc: 1);
        Assert.EndsWith("Female_All-Child_NPC003.wav", path);
    }

    [Fact]
    public void GetCatalogTargetPath_NonPlayerRace_ElderAppendsBodyTypeSuffix()
    {
        // Elder Sylph → keeps the race token AND appends "-Elder".
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Female", "Sylph", "Elder", 11, sampleIndex: 1, totalSamplesPerNpc: 1);
        Assert.EndsWith("Female_Sylph-Elder_NPC011.wav", path);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void GetCatalogTargetPath_UnknownRace_CollapsesToAll(string? race)
    {
        // Race=Unknown (or empty / null) is treated like a player race and collapses to "All".
        // Pinning it as "Unknown" in the filename would create a dead bucket no voice ever fits.
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Female", race!, "Adult", 5, sampleIndex: 1, totalSamplesPerNpc: 1);
        Assert.EndsWith("Female_All_NPC005.wav", path);
    }

    [Fact]
    public void GetCatalogTargetPath_UnknownRace_StillAppendsBodyTypeSuffix()
    {
        // Even when race collapses to "All" via the Unknown-fallback, the body-type suffix is
        // still applied — Child/Elder is independent of the race-pool decision.
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Male", "Unknown", "Child", 8, sampleIndex: 1, totalSamplesPerNpc: 1);
        Assert.EndsWith("Male_All-Child_NPC008.wav", path);
    }

    [Fact]
    public void GetCatalogTargetPath_SingleSample_FlatNoSubfolder_NoIndexSuffix()
    {
        // totalSamplesPerNpc == 1 → flat filename directly under FF14-Voices/, no per-NPC
        // subfolder and no _1 suffix. Mirrors GetNamedTargetPath's single-sample behavior.
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Female", "Hyur", "Adult", 12, sampleIndex: 1, totalSamplesPerNpc: 1);
        var sep = System.IO.Path.DirectorySeparatorChar;
        Assert.EndsWith($"FF14-Voices{sep}Female_All_NPC012.wav", path);
        Assert.DoesNotContain("NPC012_", path);
        Assert.DoesNotContain($"NPC012{sep}", path);
    }

    [Fact]
    public void GetCatalogTargetPath_AutoWidens_To4DigitsAt1000()
    {
        var path = VoiceExtractFileNames.GetCatalogTargetPath(
            @"C:\save", "Male", "Hyur", "Adult", 1234, sampleIndex: 1, totalSamplesPerNpc: 3);
        var sep = System.IO.Path.DirectorySeparatorChar;
        Assert.EndsWith("Male_All_NPC1234_1.wav", path);
        Assert.Contains($"FF14-Voices{sep}NPC1234{sep}", path);
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

    // ── VoiceExtractSampleSelector.PickDiverse ───────────────────────────────

    [Fact]
    public void PickDiverse_IncludesShortestAndLongest()
    {
        var clips = new[] { 10.0, 1.0, 5.0, 8.0, 3.0, 7.0 };
        var picked = VoiceExtractSampleSelector.PickDiverse(clips, 3, x => x);
        Assert.Equal(3, picked.Count);
        Assert.Equal(1.0, picked[0]);    // shortest first (sorted ascending)
        Assert.Equal(10.0, picked[^1]);  // longest last
    }

    [Fact]
    public void PickDiverse_SinglePick_ReturnsMedianNotExtreme()
    {
        var clips = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
        var picked = VoiceExtractSampleSelector.PickDiverse(clips, 1, x => x);
        Assert.Single(picked);
        Assert.Equal(3.0, picked[0]); // middle of the sorted set, not 1.0 or 5.0
    }

    [Fact]
    public void PickDiverse_ReturnsAllSorted_WhenCountAtMostN()
    {
        var clips = new[] { 5.0, 1.0, 3.0 };
        var picked = VoiceExtractSampleSelector.PickDiverse(clips, 5, x => x);
        Assert.Equal(new[] { 1.0, 3.0, 5.0 }, picked);
    }

    [Fact]
    public void PickDiverse_Deterministic()
    {
        var clips = new[] { 9.0, 2.0, 7.0, 4.0, 1.0, 6.0, 3.0, 8.0, 5.0, 10.0 };
        var a = VoiceExtractSampleSelector.PickDiverse(clips, 4, x => x);
        var b = VoiceExtractSampleSelector.PickDiverse(clips, 4, x => x);
        Assert.Equal(a, b);
    }

    [Fact]
    public void PickDiverse_EmptyInput_ReturnsEmpty()
    {
        var picked = VoiceExtractSampleSelector.PickDiverse(Array.Empty<double>(), 3, x => x);
        Assert.Empty(picked);
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

    // ── VoiceScdPaths ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ClientLanguage.English, "en")]
    [InlineData(ClientLanguage.German, "de")]
    [InlineData(ClientLanguage.French, "fr")]
    [InlineData(ClientLanguage.Japanese, "ja")]
    public void VoiceScdPaths_LanguageCodeForScd_MapsAllFourClientLanguages(ClientLanguage lang, string expected)
    {
        Assert.Equal(expected, VoiceScdPaths.LanguageCodeForScd(lang));
    }

    [Fact]
    public void VoiceScdPaths_Build_ProducesAllExpansionAndGenderVariants()
    {
        // Standard VOICEMAN base — 6 expansions × 3 gender suffixes = 18 candidate paths.
        var paths = VoiceScdPaths.Build("vo_voiceman_06006_000010", "de");
        Assert.Equal(18, paths.Count);
        // Spot-check a known-good combo.
        Assert.Contains("cut/ffxiv/sound/voicem/voiceman_06006/vo_voiceman_06006_000010_m_de.scd", paths);
        Assert.Contains("cut/ex3/sound/voicem/voiceman_06006/vo_voiceman_06006_000010_f_de.scd", paths);
        Assert.Contains("cut/ex5/sound/voicem/voiceman_06006/vo_voiceman_06006_000010_de.scd", paths);
    }

    [Fact]
    public void VoiceScdPaths_Build_ManFstUsesNineCharInnerFolderAnd12CharBase()
    {
        // ManFst entries have a different folder structure — 9-char inner folder, base
        // truncated to 12 chars. Real-world example shape:
        //   cut/{exp}/sound/{6}/{9}/<base[0..12]>_<gender>_<lang>.scd
        var paths = VoiceScdPaths.Build("vo_manfst_00100_000010", "en");
        Assert.NotEmpty(paths);
        Assert.Contains(paths, p => p.Contains("manfst") && p.Contains("vo_manfst_00") && p.EndsWith("_m_en.scd"));
    }

    [Fact]
    public void VoiceScdPaths_Build_ShortInputReturnsEmpty()
    {
        // Base must be at least 17 chars to satisfy the substring offsets — anything
        // shorter is malformed and returns no candidate paths.
        Assert.Empty(VoiceScdPaths.Build("too_short", "en"));
        Assert.Empty(VoiceScdPaths.Build("", "en"));
    }

    // ── VoiceSampleExtractorService.ApplyVoiceAliasEntries ───────────────────
    // Loader-side I/O (embedded resource read, HTTP fetch, file read) is not testable here
    // without spinning up the full plugin runtime, so we focus on the deterministic merge
    // logic — the same code path that decides "does an entry land in the result map and with
    // which NPC ID". Constructed via a lean mock-only path because the helpers under test
    // never touch DataManager / ClientState / Config / RemoteUrls.

    private static VoiceSampleExtractorService BuildExtractorForAliasTests()
    {
        var log = new Mock<ILogService>();
        var dataManager = new Mock<IDataManager>();
        var clientState = new Mock<IClientState>();
        var jsonData = new Mock<IJsonDataService>();
        var remoteUrls = new Mock<IRemoteUrlService>();
        return new VoiceSampleExtractorService(
            dataManager.Object,
            clientState.Object,
            log.Object,
            jsonData.Object,
            new Echokraut.DataClasses.Configuration(),
            remoteUrls.Object);
    }

    private static Dictionary<string, List<uint>> NameIndexFor(params (uint id, string name)[] entries)
    {
        var dict = new Dictionary<uint, Dictionary<string, string>>();
        foreach (var (id, name) in entries)
            dict[id] = new() { ["en"] = name };
        return VoiceExtractKey.BuildNormalizedNameIndex(dict);
    }

    [Fact]
    public void ApplyVoiceAliasEntries_ExplicitNpcId_WinsOverNameLookup()
    {
        var svc = BuildExtractorForAliasTests();
        var nameIndex = NameIndexFor((100, "Alphinaud Leveilleur"));
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var file = new VoiceExtractAliasFile
        {
            Aliases = new()
            {
                // NpcId is set → name is irrelevant, even if it doesn't resolve.
                new() { ShortName = "BUSCARRON", NpcId = 1003876, NpcName = "Made Up Name" }
            }
        };

        svc.ApplyVoiceAliasEntries(file, "test", result, nameIndex, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None));

        Assert.True(result.ContainsKey("BUSCARRON"));
        Assert.Equal(1003876u, result["BUSCARRON"]);
    }

    [Fact]
    public void ApplyVoiceAliasEntries_NameLookup_ResolvesViaIndex()
    {
        var svc = BuildExtractorForAliasTests();
        var nameIndex = NameIndexFor((42, "Tataru Taru"));
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var file = new VoiceExtractAliasFile
        {
            Aliases = new()
            {
                new() { ShortName = "OLDTATARUKEY", NpcName = "Tataru Taru" }
            }
        };

        svc.ApplyVoiceAliasEntries(file, "test", result, nameIndex, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None));

        Assert.Equal(42u, result["OLDTATARUKEY"]);
    }

    [Fact]
    public void ApplyVoiceAliasEntries_LaterLayerOverwritesEarlier()
    {
        var svc = BuildExtractorForAliasTests();
        var nameIndex = NameIndexFor();
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        var embedded = new VoiceExtractAliasFile
        {
            Aliases = new() { new() { ShortName = "KEY", NpcId = 1u } }
        };
        var local = new VoiceExtractAliasFile
        {
            Aliases = new() { new() { ShortName = "KEY", NpcId = 999u } }
        };

        var ev = new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None);
        svc.ApplyVoiceAliasEntries(embedded, "embedded", result, nameIndex, ev);
        svc.ApplyVoiceAliasEntries(local, "local", result, nameIndex, ev);

        // Local wins — embedded entry got overwritten on the second apply.
        Assert.Equal(999u, result["KEY"]);
    }

    [Fact]
    public void ApplyVoiceAliasEntries_KeyComparisonIsCaseInsensitive()
    {
        var svc = BuildExtractorForAliasTests();
        var nameIndex = NameIndexFor();
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var file = new VoiceExtractAliasFile
        {
            Aliases = new()
            {
                new() { ShortName = "buscarron", NpcId = 100u }
            }
        };

        svc.ApplyVoiceAliasEntries(file, "test", result, nameIndex, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None));

        // Stored key is uppercase-invariant so RunInternal's lookup hits regardless of how the
        // text key in the SCD sheet is cased. Both lookups must succeed.
        Assert.Equal(100u, result["BUSCARRON"]);
        Assert.Equal(100u, result["buscarron"]);
    }

    [Fact]
    public void ApplyVoiceAliasEntries_UnknownName_NotResolved_AndSkipped()
    {
        var svc = BuildExtractorForAliasTests();
        var nameIndex = NameIndexFor((1, "Alphinaud"));
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var file = new VoiceExtractAliasFile
        {
            Aliases = new()
            {
                new() { ShortName = "MYSTERY", NpcName = "NotInGame" }
            }
        };

        svc.ApplyVoiceAliasEntries(file, "test", result, nameIndex, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None));

        // Unknown name → entry skipped, key absent. Logger receives a warning but that's not
        // asserted here (would couple the test to log message wording).
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyVoiceAliasEntries_EmptyShortName_Skipped()
    {
        var svc = BuildExtractorForAliasTests();
        var nameIndex = NameIndexFor((1, "Alphinaud"));
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var file = new VoiceExtractAliasFile
        {
            Aliases = new()
            {
                new() { ShortName = "", NpcId = 1u }
            }
        };

        svc.ApplyVoiceAliasEntries(file, "test", result, nameIndex, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None));
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyVoiceAliasEntries_NullFile_NoOp()
    {
        var svc = BuildExtractorForAliasTests();
        var nameIndex = NameIndexFor();
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        svc.ApplyVoiceAliasEntries(null, "missing", result, nameIndex, new Echokraut.DataClasses.EKEventId(0, Echotools.Logging.Enums.TextSource.None));
        Assert.Empty(result);
    }
}
