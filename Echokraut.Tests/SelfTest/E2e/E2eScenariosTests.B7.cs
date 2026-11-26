using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.Text;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>The B7 sub-scenarios (plan › Build plan): phonetics, voices, narrator, chat channels, local save, volume, bubble, harvest, AllTalk switch-off.</summary>
public sealed partial class E2eScenariosTests
{
    private static CancellationToken Soon() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    // ── phonetics ───────────────────────────────────────────────────────────────────────────────

    private readonly List<PhoneticCorrection> _corrections = [];

    /// <summary>The service + pipeline the way they work: corrections stored, applied to the spoken text.</summary>
    private void CorrectionsLikeTheService(bool applied = true)
    {
        _npcData.Setup(n => n.GetPhoneticCorrections()).Returns(() => _corrections.ToList());
        _npcData.Setup(n => n.UpsertPhoneticCorrection(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string o, string c) =>
            {
                _corrections.RemoveAll(x => string.Equals(x.OriginalText, o, StringComparison.OrdinalIgnoreCase));
                _corrections.Add(new PhoneticCorrection(o, c));
            });
        _npcData.Setup(n => n.DeletePhoneticCorrection(It.IsAny<string>()))
            .Callback((string o) => _corrections.RemoveAll(x => string.Equals(x.OriginalText, o, StringComparison.OrdinalIgnoreCase)));
        _process = (e, t) => new SelfTestSpeech(Enqueue(e.TextSource, applied ? TalkTextHelper.ReplacePhonetics(t, _corrections) : t, _npc), _npc);
    }

    private PhoneticsScenario Phonetics() => new(_config, _npcData.Object, _playback.Object, Speaker());

    [Theory]
    [InlineData("How much wood would a woodchuck chuck", "much")]
    [InlineData("In der Stadt gab es", "Stadt")]
    [InlineData("この竹垣", "この竹垣")]
    [InlineData("竹", "竹")]
    public void Phonetics_WordOf_TakesTheFirstWordOfFourLetters(string text, string word) =>
        Assert.Equal(word, PhoneticsScenario.WordOf(text));

    [Fact]
    public async Task Phonetics_AddChangeDelete_Passes_AndLeavesNoCorrection()
    {
        CorrectionsLikeTheService();
        var r = await Phonetics().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Empty(_corrections);
    }

    [Fact]
    public async Task Phonetics_CorrectionNotApplied_Fails()
    {
        CorrectionsLikeTheService(applied: false);
        var r = await Phonetics().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains($"'{PhoneticsScenario.FirstReplacement}' not in the queued text", r.Detail);
        Assert.Empty(_corrections);
    }

    [Fact]
    public async Task Phonetics_ExistingCorrectionForTheWord_IsPutBack()
    {
        CorrectionsLikeTheService();
        _corrections.Add(new PhoneticCorrection("much", "mutch"));
        var r = await Phonetics().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        var kept = Assert.Single(_corrections);
        Assert.Equal("mutch", kept.CorrectedText);
    }

    // ── voice-reassign / reload-voices ──────────────────────────────────────────────────────────

    private readonly Mock<IBackendService> _backend = new();

    private static EchokrautVoice Voice(string key, bool isDefault = false, bool enabled = true) =>
        new() { BackendVoice = key, VoiceName = key, IsDefault = isDefault, IsEnabled = enabled };

    private List<EchokrautVoice> _voices = [Voice("Narrator.wav", isDefault: true), Voice("Male_All_NPC001.wav"), Voice("Male_All_NPC002.wav")];

    private void VoicesLikeTheService()
    {
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns(() => _voices);
        _npc.Voices = _voices;
        _npcData.Setup(n => n.SaveVoice(It.IsAny<EchokrautVoice>()));
        _backend.Setup(b => b.IsVoiceFittingForNpc(It.IsAny<EchokrautVoice?>(), It.IsAny<NpcMapData>())).Returns(true);
    }

    [Fact]
    public void VoiceReassign_Other_SkipsCurrentNarratorDisabledAndUnfitting()
    {
        var voices = new[] { Voice("a"), Voice("n", isDefault: true), Voice("off", enabled: false), Voice("misfit"), Voice("b") };
        var other = VoiceReassignScenario.Other(voices, _npc, "a", "n", (v, _) => v.BackendVoice != "misfit");
        Assert.Equal("b", other?.BackendVoice);
        Assert.Null(VoiceReassignScenario.Other(voices[..3], _npc, "a", "n", (_, _) => true));
    }

    private VoiceReassignScenario Reassign() => new(_config, _backend.Object, _npcData.Object, _playback.Object, Speaker());

    [Fact]
    public async Task VoiceReassign_NextLineAndAfterReloadUseTheNewVoice_Passes()
    {
        VoicesLikeTheService();
        _npc.voice = "Male_All_NPC001.wav";
        var r = await Reassign().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("'Male_All_NPC001.wav' -> 'Male_All_NPC002.wav'", r.Detail);
        _backend.Verify(b => b.SetBackendType(_config.BackendSelection), Times.Once);
        _db.Verify(d => d.DeleteCharacter(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task VoiceReassign_UsesASourceWithACharacterCache()
    {
        VoicesLikeTheService();
        _npc.voice = "Male_All_NPC001.wav";
        // Like NpcDataService.ListForSource: VoiceTest has no cache, each line is a fresh speaker with a random voice.
        _process = (e, t) =>
        {
            var speaker = e.TextSource == TextSource.VoiceTest
                ? new NpcMapData(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.None) { Name = _npc.Name, voice = "Narrator.wav", Voices = _voices }
                : _npc;
            return new SelfTestSpeech(Enqueue(e.TextSource, t, speaker), speaker);
        };
        var r = await Reassign().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
    }

    [Fact]
    public async Task VoiceReassign_ReloadPutsTheOldVoiceBack_Fails()
    {
        VoicesLikeTheService();
        _npc.voice = "Male_All_NPC001.wav";
        _backend.Setup(b => b.SetBackendType(It.IsAny<TTSBackends>())).Callback(() => _npc.voice = "Male_All_NPC001.wav");
        var r = await Reassign().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("after 'reload voices' uses 'Male_All_NPC001.wav' instead of 'Male_All_NPC002.wav'", r.Detail);
    }

    [Fact]
    public async Task VoiceReassign_PicksOnlyFromTheNpcsSelectableList()
    {
        VoicesLikeTheService();
        _npc.Voices = [_voices[0], _voices[1]];
        _npc.voice = "Male_All_NPC001.wav";
        var r = await Reassign().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Equal("Male_All_NPC001.wav", _npc.voice);
    }

    [Fact]
    public async Task VoiceReassign_NoSecondFittingVoice_IsSkipped()
    {
        VoicesLikeTheService();
        _voices = [Voice("Narrator.wav", isDefault: true), Voice("Male_All_NPC001.wav")];
        _npc.Voices = _voices;
        _npc.voice = "Male_All_NPC001.wav";
        var r = await Reassign().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
    }

    private ReloadVoicesScenario ReloadVoices() => new(_config, _backend.Object, _npcData.Object);

    [Fact]
    public async Task ReloadVoices_SameKeys_Passes()
    {
        VoicesLikeTheService();
        var r = await ReloadVoices().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("3 voices before and after", r.Detail);
        _backend.Verify(b => b.NotifyCharacterMapped(), Times.Once);
    }

    [Fact]
    public async Task ReloadVoices_VoiceLostAndOneDuplicated_Fails()
    {
        VoicesLikeTheService();
        _backend.Setup(b => b.SetBackendType(It.IsAny<TTSBackends>()))
            .Callback(() => _voices = [Voice("Narrator.wav", isDefault: true), Voice("Male_All_NPC001.wav"), Voice("Male_All_NPC001.wav")]);
        var r = await ReloadVoices().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("lost Male_All_NPC002.wav", r.Detail);
        Assert.Contains("twice Male_All_NPC001.wav", r.Detail);
    }

    // ── narrator ────────────────────────────────────────────────────────────────────────────────

    private readonly NpcMapData _narratorRow = new(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.None) { Name = "" };

    /// <summary>Like EnsureFittingVoice: a nameless line gets the current narrator (pick, else the flagged default).</summary>
    private void NarratesLikeThePipeline(bool honoursPick = true)
    {
        VoicesLikeTheService();
        _processor.Setup(p => p.ProcessSelfTestSpeechAsync(It.IsAny<EKEventId>(), "", It.IsAny<string>()))
            .Returns((EKEventId e, string _, string t) =>
            {
                _narratorRow.voice = NarratorVoicePolicy.Resolve(_voices, honoursPick ? _config.NarratorVoice : null)!.BackendVoice;
                return Task.FromResult(new SelfTestSpeech(Enqueue(e.TextSource, t, _narratorRow), _narratorRow));
            });
    }

    private NarratorScenario Narrator() => new(_config, _npcData.Object, _playback.Object, Speaker(), null, null);

    [Fact]
    public async Task Narrator_PickUnpickDisable_Passes_AndPutsEverythingBack()
    {
        NarratesLikeThePipeline();
        _config.NarratorVoice = "previous.wav";
        var r = await Narrator().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("'Male_All_NPC001.wav' narrates", r.Detail);
        Assert.Equal("previous.wav", _config.NarratorVoice);
        Assert.All(_voices, v => Assert.True(v.IsEnabled));
        Assert.Empty(_state.Marks.Keys.Where(k => k.StartsWith(NarratorScenario.PhaseMark)));
    }

    [Fact]
    public async Task Narrator_PipelineIgnoresThePick_Fails()
    {
        NarratesLikeThePipeline(honoursPick: false);
        var r = await Narrator().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("picked: narrator line uses 'Narrator.wav' instead of 'Male_All_NPC001.wav'", r.Detail);
    }

    [Fact]
    public async Task Narrator_AfterTheReload_PickLost_Fails_AndRestoresTheOriginal()
    {
        NarratesLikeThePipeline();
        _state.Marks[NarratorScenario.PhaseMark] = NarratorScenario.Picked;
        _state.Marks[NarratorScenario.PickMark] = "Male_All_NPC001.wav";
        _state.Marks[NarratorScenario.OriginalMark] = "";
        _config.NarratorVoice = "";
        var r = await Narrator().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("after the reload the pick is '' instead of 'Male_All_NPC001.wav'", r.Detail);
        Assert.Equal("", _config.NarratorVoice);
        Assert.False(_state.Marks.ContainsKey(NarratorScenario.PhaseMark));
    }

    [Fact]
    public async Task Narrator_NoFlaggedDefault_ChecksThePick_ThenFailsNamingIt()
    {
        NarratesLikeThePipeline();
        _voices = [Voice("Male_All_NPC001.wav"), Voice("Male_All_NPC002.wav")];
        var r = await Narrator().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal("pick and no reload wired ok, but no flagged default narrator (Narrator.wav) among the voices - unpicked/switched off cannot fall back.", r.Detail);
        Assert.Equal("", _config.NarratorVoice);
    }

    // ── chat-channels ───────────────────────────────────────────────────────────────────────────

    /// <summary>Like ChatTalkHelper's switch: a channel is voiced only with its option on.</summary>
    private void ChannelsLikeTheChatHelper(XivChatType? alwaysVoiced = null)
    {
        _chatGui.Setup(c => c.Print(It.IsAny<XivChatEntry>()))
            .Callback((XivChatEntry e) =>
            {
                var option = ChatChannelsScenario.Channels.Single(c => c.Type == e.Type).Option;
                var on = (bool)typeof(Configuration).GetProperty(option)!.GetValue(_config)!;
                if (_config.VoiceChat && _config.VoiceChatPlayer && (on || e.Type == alwaysVoiced))
                    Enqueue(TextSource.Chat, e.Message.TextValue, _player, clipId: 9);
            });
    }

    private ChatChannelsScenario ChatChannels() => new(_config, Chat(), _playback.Object, Speaker());

    [Fact]
    public async Task ChatChannels_EachChannelFollowsItsOption_Passes_AndRestoresEverything()
    {
        ChannelsLikeTheChatHelper();
        var r = await ChatChannels().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains($"{ChatChannelsScenario.Channels.Count} channels", r.Detail);
        Assert.Empty(_state.TouchedOptions);
        Assert.Empty(_state.CreatedIds);
        Assert.False(_config.VoiceChat);
        Assert.False(_config.VoiceChatSay);
        _db.Verify(d => d.DeleteVoiceClip(9), Times.Once);
    }

    [Fact]
    public async Task ChatChannels_ChannelVoicedWhileOff_Fails()
    {
        ChannelsLikeTheChatHelper(alwaysVoiced: XivChatType.Say);
        var r = await ChatChannels().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal("Say off: queued anyway.", r.Detail);
        Assert.Empty(_state.CreatedIds);
    }

    [Fact]
    public async Task ChatChannels_ChannelNeverVoiced_FailsNamingIt()
    {
        _chatGui.Setup(c => c.Print(It.IsAny<XivChatEntry>()))
            .Callback((XivChatEntry e) =>
            {
                if (e.Type != XivChatType.Yell && _config.VoiceChatSay | _config.VoiceChatShout | _config.VoiceChatTell | _config.VoiceChatParty
                    | _config.VoiceChatAlliance | _config.VoiceChatLinkshell | _config.VoiceChatCrossLinkshell
                    | _config.VoiceChatFreeCompany | _config.VoiceChatNoviceNetwork)
                    Enqueue(TextSource.Chat, e.Message.TextValue, _player);
            });
        var r = await ChatChannels().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal("Yell on: not queued.", r.Detail);
    }

    // ── local-save ──────────────────────────────────────────────────────────────────────────────

    private LocalSaveScenario LocalSave() => new(_config, Chat(), _queue, _playback.Object, Speaker());

    /// <summary>Generation writes the WAV once played; a second line finds it (TryLoadCachedAudio) unless <paramref name="cacheWorks"/> is false.</summary>
    private string LocalSaveLikeThePipeline(bool cacheWorks = true)
    {
        var wav = Path.Combine(Path.GetTempPath(), $"ek-e2e-save-{Guid.NewGuid():N}.wav");
        _config.SaveToLocal = true;
        _config.LocalSaveLocation = Path.GetTempPath();
        _audioFiles.Setup(a => a.GetLocalAudioPath(It.IsAny<string>(), It.IsAny<VoiceMessage>())).Returns(wav);
        _chatGui.Setup(c => c.Print(It.IsAny<XivChatEntry>()))
            .Callback((XivChatEntry e) =>
            {
                var cached = cacheWorks && File.Exists(wav);
                var message = Enqueue(TextSource.Chat, e.Message.TextValue, _player, clipId: 9);
                message.LoadedLocally = cached;
                if (!cached) File.WriteAllText(wav, "x");
            });
        return wav;
    }

    [Fact]
    public async Task LocalSave_SecondLineFromTheWav_Passes_AndDeletesIt()
    {
        var wav = LocalSaveLikeThePipeline();
        StartPlayer();
        var r = await LocalSave().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.False(File.Exists(wav));
        Assert.Empty(_state.CreatedIds);
        _db.Verify(d => d.DeleteVoiceClip(9), Times.Once);
    }

    [Fact]
    public async Task LocalSave_SecondLineGeneratedAgain_Fails()
    {
        var wav = LocalSaveLikeThePipeline(cacheWorks: false);
        StartPlayer();
        var r = await LocalSave().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("generated again", r.Detail);
        Assert.False(File.Exists(wav));
    }

    [Fact]
    public async Task LocalSave_SavingOff_IsSkipped()
    {
        _config.SaveToLocal = false;
        var r = await LocalSave().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
    }

    // ── volume ──────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeSound : IE2eGameSound
    {
        public uint? VoiceVolume { get; set; } = 73;
        public bool Muted { get; set; }
        public Task SetVoiceVolumeAsync(uint value)
        {
            VoiceVolume = value;
            return Task.CompletedTask;
        }
    }

    private readonly FakeSound _sound = new();
    private float _lastVolume;

    /// <summary>The line's volume follows the slider; BASS reports <paramref name="bass"/> (default: the line's volume).</summary>
    private void VolumeLikeThePipeline(Func<float, float>? bass = null)
    {
        _process = (e, t) =>
        {
            var message = Enqueue(e.TextSource, t, _npc);
            message.Volume = _sound.VoiceVolume!.Value / 100f * 0.8f;
            _lastVolume = message.Volume;
            return new SelfTestSpeech(message, _npc);
        };
        _playback.Setup(p => p.ChannelVolume(It.IsAny<Guid>())).Returns(() => (bass ?? (v => v))(_lastVolume));
    }

    private VolumeScenario Volume() => new(_config, _sound, _queue, _playback.Object, Speaker());

    [Fact]
    public async Task Volume_ChannelFollowsTheSlider_Passes_AndRestoresTheSlider()
    {
        VolumeLikeThePipeline();
        StartPlayer(hold: true);
        var r = await Volume().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("ratio 0.50", r.Detail);
        Assert.Equal(73u, _sound.VoiceVolume);
        Assert.Empty(_state.CreatedIds);
    }

    [Fact]
    public async Task Volume_StreamCreatedAfterTheEntryPlays_StillReadsTheChannel()
    {
        VolumeLikeThePipeline();
        var calls = 0;
        _playback.Setup(p => p.ChannelVolume(It.IsAny<Guid>())).Returns(() => ++calls == 1 ? null : _lastVolume);
        StartPlayer(hold: true);
        var r = await Volume().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
    }

    [Fact]
    public async Task Volume_BassIgnoresTheLineVolume_Fails()
    {
        VolumeLikeThePipeline(_ => 1f);
        StartPlayer(hold: true);
        var r = await Volume().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("BASS channel volume 1 but the line's volume is 0.8", r.Detail);
        Assert.Equal(73u, _sound.VoiceVolume);
    }

    [Fact]
    public async Task Volume_Muted_IsSkipped_SliderUntouched()
    {
        _sound.Muted = true;
        var r = await Volume().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Equal(73u, _sound.VoiceVolume);
        Assert.Empty(_state.CreatedIds);
    }

    [Theory]
    [InlineData(0.8f, 0.4f, SelfTestOutcome.Pass)]
    [InlineData(0.8f, 0.8f, SelfTestOutcome.Fail)]
    [InlineData(0.8f, 0.2f, SelfTestOutcome.Fail)]
    [InlineData(0f, 0f, SelfTestOutcome.Fail)]
    public void Volume_Judge_RatioMustFollowTheSlider(float high, float low, SelfTestOutcome expected) =>
        Assert.Equal(expected, VolumeScenario.Judge(high, low).Outcome);

    [Theory]
    [InlineData("gamecfg:SoundVoice=55", 55u)]
    [InlineData("gamecfg:SoundVoice=0", 0u)]
    [InlineData("gamecfg:SoundVoice=101", null)]
    [InlineData("gamecfg:SoundVoice=x", null)]
    [InlineData("clip:5", null)]
    public void GameSoundIds_Parse(string id, uint? expected) => Assert.Equal(expected, E2eGameSoundIds.Parse(id));

    // ── bubble ──────────────────────────────────────────────────────────────────────────────────

    private BubbleScenario Bubble() => new(_config, _queue);

    [Fact]
    public async Task Bubble_ABubbleLinePlays_Passes_AndRestoresTheOptions()
    {
        StartPlayer();
        var run = Bubble().RunAsync(Ctx(), Soon());
        await Task.Run(() => SpinWait.SpinUntil(() => _config.VoiceBubble, TimeSpan.FromSeconds(5)));
        Enqueue(TextSource.AddonBubble, "Kupo!", _npc);
        var r = await run;
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.False(_config.VoiceBubble);
        Assert.Empty(_state.TouchedOptions);
    }

    [Fact]
    public async Task Bubble_NoBubble_IsSkipped()
    {
        Enqueue(TextSource.AddonTalk, "Not a bubble.", _npc);
        var r = await Bubble().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.False(_config.VoiceBubblesInCity);
    }

    // ── harvest ─────────────────────────────────────────────────────────────────────────────────

    private readonly Mock<IDialogHarvestService> _harvest = new();

    private HarvestScenario Harvest() => new(_harvest.Object, _db.Object, () => ClientLanguage.German);

    [Fact]
    public async Task Harvest_AddsRows_PassesWithTheNonQuestFilter()
    {
        var rows = 10;
        _db.Setup(d => d.GetVoiceClipCountForLanguage((int)ClientLanguage.German)).Returns(() => rows);
        _harvest.Setup(h => h.RunAsync(ClientLanguage.German, It.IsAny<CancellationToken>(), (int)QuestType.None))
            .Returns(() => { rows = 25; return Task.CompletedTask; });
        var r = await Harvest().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("added 15 clip rows", r.Detail);
    }

    [Fact]
    public async Task Harvest_NoRows_FailsWithTheLastProgress()
    {
        _db.Setup(d => d.GetVoiceClipCountForLanguage(It.IsAny<int>())).Returns(4);
        _harvest.Setup(h => h.RunAsync(It.IsAny<ClientLanguage>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Returns(() =>
            {
                _harvest.Raise(h => h.ProgressChanged += null, "Error: sheet missing");
                return Task.CompletedTask;
            });
        var r = await Harvest().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("last progress: Error: sheet missing", r.Detail);
    }

    [Fact]
    public async Task Harvest_AlreadyRunning_IsSkipped()
    {
        _harvest.SetupGet(h => h.IsRunning).Returns(true);
        var r = await Harvest().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        _harvest.Verify(h => h.RunAsync(It.IsAny<ClientLanguage>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()), Times.Never);
    }

    // ── alltalk-off ─────────────────────────────────────────────────────────────────────────────

    private readonly Mock<ITtsVoiceSyncService> _voiceSync = new();
    private readonly Mock<IEchokrauTtsInstanceService> _engine = new();
    private readonly Mock<IE2eGpuProbe> _gpu = new();
    private bool _engineRunning = true;
    private bool _portOpen = true;

    /// <summary>The switch the way it works: leaving EchokrauTTS stops it (process + port), the VRAM follows.</summary>
    private void SwitchLikeTheService(bool stops = true, long vramAfterStop = 2000)
    {
        _config.EchokrauTts.InstanceType = AlltalkInstanceType.Local;
        _engine.SetupGet(e => e.InstanceRunning).Returns(() => _engineRunning);
        _engine.SetupGet(e => e.CurrentInstallStatus).Returns("");
        _engine.Setup(e => e.StartInstance()).Callback(() => { _engineRunning = true; _portOpen = true; });
        _backend.Setup(b => b.IsBackendReachableAsync()).ReturnsAsync(true);
        _voiceSync.Setup(v => v.SwitchEngine(It.IsAny<TTSBackends>())).Callback((TTSBackends to) =>
        {
            if (to != TTSBackends.EchokrauTTS && stops) { _engineRunning = false; _portOpen = false; }
            _config.BackendSelection = to;
        });
        _gpu.Setup(g => g.SampleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new E2eGpuSample(_engineRunning ? 6000 : vramAfterStop, 12000, 0));
    }

    private AllTalkOffScenario AllTalkOff(string device = "cuda:0") => new(_config, _voiceSync.Object, _engine.Object,
        new E2eEngineWait(_engine.Object, _backend.Object, _npcData.Object, TimeSpan.FromMilliseconds(1)),
        () => [8765], (_, _) => Task.FromResult(_portOpen), _gpu.Object,
        _ => Task.FromResult<EchokrauTtsHealthResponse?>(new EchokrauTtsHealthResponse { device = device }));

    [Fact]
    public async Task AllTalkOff_StopsTheEngine_FreesVram_AndComesBack_Passes()
    {
        SwitchLikeTheService();
        var r = await AllTalkOff().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("VRAM 6000 -> 2000 MiB", r.Detail);
        Assert.Equal(TTSBackends.EchokrauTTS, _config.BackendSelection);
        Assert.True(_engineRunning);
        _engine.Verify(e => e.StartInstance(), Times.Once);
    }

    [Fact]
    public async Task AllTalkOff_EngineKeepsRunning_Fails_AndIsBackOnEchokrauTts()
    {
        SwitchLikeTheService(stops: false);
        var r = await AllTalkOff().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("EchokrauTTS still running", r.Detail);
        Assert.Contains("port 8765 still open", r.Detail);
        Assert.Equal(TTSBackends.EchokrauTTS, _config.BackendSelection);
    }

    [Fact]
    public async Task AllTalkOff_VramKept_FailsOnCuda_NotJudgedOnCpu()
    {
        SwitchLikeTheService(vramAfterStop: 5900);
        var cuda = await AllTalkOff().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Fail, cuda.Outcome);
        Assert.Contains($"VRAM dropped by less than {AllTalkOffScenario.MinVramDropMiB} MiB", cuda.Detail);

        var cpu = await AllTalkOff("cpu").RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Pass, cpu.Outcome);
        Assert.Contains("VRAM not judged (device cpu)", cpu.Detail);
    }

    [Fact]
    public async Task AllTalkOff_NoLocalEngine_IsSkipped()
    {
        SwitchLikeTheService();
        _config.EchokrauTts.InstanceType = AlltalkInstanceType.Remote;
        var r = await AllTalkOff().RunAsync(Ctx(), Soon());
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        _voiceSync.Verify(v => v.SwitchEngine(It.IsAny<TTSBackends>()), Times.Never);
    }
}
