using System;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

internal class VoiceTestService : IVoiceTestService
{
    private readonly ILogService _log;
    private readonly IVolumeService _volumeService;
    private readonly IBackendService _backend;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly IClientState _clientState;
    private readonly IGameObjectService _gameObjects;
    private readonly Configuration _config;

    public EchokrautVoice? TestingVoice { get; private set; }
    public bool IsPlaying => _audioPlayback.IsPlaying;

    public event Action? TestStateChanged;

    public VoiceTestService(
        Echotools.Logging.Services.ILogService log,
        IVolumeService volumeService,
        IBackendService backend,
        IAudioPlaybackService audioPlayback,
        IClientState clientState,
        IGameObjectService gameObjects,
        Configuration config)
    {
        _log = log;
        _volumeService = volumeService;
        _backend = backend;
        _audioPlayback = audioPlayback;
        _clientState = clientState;
        _gameObjects = gameObjects;
        _config = config;

        _audioPlayback.CurrentMessageChanged += OnCurrentMessageChanged;
    }

    public bool IsTestingVoice(EchokrautVoice voice) => TestingVoice == voice;

    public void TestVoice(EchokrautVoice voice)
    {
        StopVoice();
        TestingVoice = voice;

        var eventId = _log.Start(nameof(TestVoice), TextSource.AddonTalk);
        _log.Debug(nameof(TestVoice), $"Testing voice: {voice}", eventId);

        var volume = _volumeService.GetVoiceVolume(eventId) * voice.Volume;
        var speaker = new NpcMapData(ObjectKind.None)
        {
            Gender = voice.AllowedGenders.Count > 0 ? voice.AllowedGenders[0] : Genders.Male,
            Race = voice.AllowedRaces.Count > 0 ? voice.AllowedRaces[0] : NpcRaces.Hyur,
            Name = voice.VoiceName,
            Voices = _config.EchokrautVoices,
            Voice = voice
        };

        var text = GetTestText();
        var voiceMessage = new VoiceMessage
        {
            SpeakerObj = null,
            Source = TextSource.VoiceTest,
            Speaker = speaker,
            Text = text,
            OriginalText = text,
            Language = _clientState.ClientLanguage,
            EventId = eventId,
            SpeakerFollowObj = _gameObjects.LocalPlayer,
            Volume = volume
        };

        if (volume > 0)
        {
            _backend.ProcessVoiceMessage(voiceMessage);
        }
        else
        {
            _log.Debug(nameof(TestVoice), "Skipping voice inference. Volume is 0", eventId);
            _log.End(nameof(TestVoice), eventId);
            TestingVoice = null;
        }

        TestStateChanged?.Invoke();
    }

    public void StopVoice()
    {
        TestingVoice = null;
        if (DialogState.CurrentVoiceMessage != null)
            _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
        _log.End(nameof(StopVoice), new EKEventId(0, TextSource.AddonTalk));
        TestStateChanged?.Invoke();
    }

    private void OnCurrentMessageChanged(VoiceMessage? message)
    {
        if (message == null && TestingVoice != null)
        {
            TestingVoice = null;
            TestStateChanged?.Invoke();
        }
    }

    private string GetTestText() => _clientState.ClientLanguage switch
    {
        ClientLanguage.German => Constants.TESTMESSAGEDE,
        ClientLanguage.French => Constants.TESTMESSAGEFR,
        ClientLanguage.Japanese => Constants.TESTMESSAGEJP,
        _ => Constants.TESTMESSAGEEN,
    };
}
