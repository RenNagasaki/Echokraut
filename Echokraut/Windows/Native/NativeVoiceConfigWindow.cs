using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Localization;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

/// <summary>
/// Standalone native window for configuring a single voice's gender, race, and options.
/// Opened from the Voices tab's Configure button.
/// </summary>
public sealed unsafe class NativeVoiceConfigWindow : NativeAddon
{
    private readonly EchokrautVoice _voice;
    private readonly Configuration _config;
    private readonly INpcDataService _npcData;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly IVolumeService _volumeService;
    private readonly IGameObjectService _gameObjects;
    private readonly IClientState _clientState;
    private readonly IBackendService _backend;
    private readonly ILogService _log;
    private readonly Action _onChanged;

    public NativeVoiceConfigWindow(
        EchokrautVoice voice,
        Configuration config,
        INpcDataService npcData,
        IAudioPlaybackService audioPlayback,
        IVolumeService volumeService,
        IGameObjectService gameObjects,
        IClientState clientState,
        IBackendService backend,
        ILogService log,
        Action onChanged)
    {
        _voice = voice;
        _config = config;
        _npcData = npcData;
        _audioPlayback = audioPlayback;
        _volumeService = volumeService;
        _gameObjects = gameObjects;
        _clientState = clientState;
        _backend = backend;
        _log = log;
        _onChanged = onChanged;
    }

    protected override void OnSetup(AtkUnitBase* addon)
    {
        var pos = ContentStartPosition;
        var size = ContentSize;
        var w = size.X;

        var list = new ScrollingListNode
        {
            Position = pos,
            Size = size,
            FitWidth = true,
            ItemSpacing = 4,
        };

        // ── Options ──────────────────────────────────────────────────────────

        var enabledCheck = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = Loc.S("Enabled"),
            IsChecked = _voice.IsEnabled,
            OnClick = v => { _voice.IsEnabled = v; Save(); },
        };

        var randomCheck = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = Loc.S("Use as random NPC voice"),
            IsChecked = _voice.UseAsRandom,
            OnClick = v => { _voice.UseAsRandom = v; Save(); },
        };

        var childCheck = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = Loc.S("Child voice"),
            IsChecked = _voice.IsChildVoice,
            OnClick = v => { _voice.IsChildVoice = v; Save(); },
        };

        // Note
        var noteInput = new TextInputNode
        {
            Size = new Vector2(w, 28),
            MaxCharacters = 80,
            PlaceholderString = Loc.S("Note"),
            String = _voice.Note,
        };
        noteInput.OnInputReceived = s => { _voice.Note = s.ToString(); Save(); };

        // Volume (0..2)
        var volSlider = new SliderNode
        {
            Size = new Vector2(w, 20),
            Range = 0..200,
            DecimalPlaces = 2,
            Value = (int)(_voice.Volume * 100),
        };
        volSlider.OnValueChanged = v => { _voice.Volume = v / 100.0f; Save(); };

        // Play/Stop
        TextButtonNode? playBtn = null;
        playBtn = new TextButtonNode { Size = new Vector2(80, 24), String = Loc.S("Play") };
        var playMaxW = new[] { Loc.S("Play"), Loc.S("Stop") }
            .Max(s => playBtn.LabelNode.GetTextDrawSize(s).X) + 36;
        if (playMaxW > 80) playBtn.Size = new Vector2(playMaxW, 24);
        playBtn.OnClick = () =>
        {
            if (_audioPlayback.IsPlaying)
            {
                if (DialogState.CurrentVoiceMessage != null)
                    _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
                playBtn.String = Loc.S("Play");
            }
            else
            {
                TestVoice();
                playBtn.String = Loc.S("Stop");
            }
        };

        list.AddNode(enabledCheck);
        list.AddNode(randomCheck);
        list.AddNode(childCheck);
        list.AddNode(noteInput);
        list.AddNode(volSlider);
        list.AddNode(playBtn);

        list.AddNode(new HorizontalLineNode { Size = new Vector2(w, 4) });

        // ── Genders ──────────────────────────────────────────────────────────

        var genderLabel = new TextNode
        {
            Size = new Vector2(w, 18),
            String = Loc.S("Allowed genders"),
            FontType = FontType.Axis,
            FontSize = 14,
        };
        list.AddNode(genderLabel);

        foreach (var gender in Constants.GENDERLIST)
        {
            var g = gender;
            var gCheck = new CheckboxNode
            {
                Size = new Vector2(w, 24),
                String = Loc.S(g.ToString()),
                IsChecked = _voice.AllowedGenders.Contains(g),
                OnClick = v =>
                {
                    if (v && !_voice.AllowedGenders.Contains(g))
                        _voice.AllowedGenders.Add(g);
                    else if (!v && _voice.AllowedGenders.Contains(g))
                        _voice.AllowedGenders.Remove(g);
                    _npcData.RefreshSelectables(_config.EchokrautVoices);
                    Save();
                },
            };
            list.AddNode(gCheck);
        }

        var resetGenderLabel = Loc.S("Reset genders");
        var resetGenderBtn = new TextButtonNode { Size = new Vector2(120, 24), String = resetGenderLabel };
        var rgW = resetGenderBtn.LabelNode.GetTextDrawSize(resetGenderLabel).X + 36;
        if (rgW > 120) resetGenderBtn.Size = new Vector2(rgW, 24);
        resetGenderBtn.OnClick = () =>
        {
            _npcData.ReSetVoiceGenders(_voice);
            Save();
            Close();
        };
        list.AddNode(resetGenderBtn);

        list.AddNode(new HorizontalLineNode { Size = new Vector2(w, 4) });

        // ── Races ────────────────────────────────────────────────────────────

        var raceLabel = new TextNode
        {
            Size = new Vector2(w, 18),
            String = Loc.S("Allowed races"),
            FontType = FontType.Axis,
            FontSize = 14,
        };
        list.AddNode(raceLabel);

        // All checkbox
        var allRaces = _voice.AllowedRaces.Count == Constants.RACELIST.Count;
        var allCheck = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = Loc.S("All"),
            IsChecked = allRaces,
            OnClick = v =>
            {
                if (v)
                    foreach (var r in Constants.RACELIST)
                    {
                        if (!_voice.AllowedRaces.Contains(r))
                            _voice.AllowedRaces.Add(r);
                    }
                else
                    _voice.AllowedRaces.Clear();
                _npcData.RefreshSelectables(_config.EchokrautVoices);
                Save();
            },
        };
        list.AddNode(allCheck);

        // Individual race checkboxes — absolute grid positioning for uniform columns
        const int cols = 4;
        const float rowH = 26f;
        var raceList = Constants.RACELIST;
        var colW = w / cols;
        var rows = (int)Math.Ceiling(raceList.Count / (float)cols);
        var gridHeight = rows * rowH;

        var raceGrid = new SimpleComponentNode { Size = new Vector2(w, gridHeight) };
        for (var i = 0; i < raceList.Count; i++)
        {
            var race = raceList[i];
            var col = i % cols;
            var row = i / cols;
            var rCheck = new CheckboxNode
            {
                Size = new Vector2(colW, 24),
                Position = new Vector2(col * colW, row * rowH),
                String = Loc.S(race.ToString()),
                IsChecked = _voice.AllowedRaces.Contains(race),
                OnClick = v =>
                {
                    if (v && !_voice.AllowedRaces.Contains(race))
                        _voice.AllowedRaces.Add(race);
                    else if (!v && _voice.AllowedRaces.Contains(race))
                        _voice.AllowedRaces.Remove(race);
                    _npcData.RefreshSelectables(_config.EchokrautVoices);
                    Save();
                },
            };
            rCheck.AttachNode(raceGrid);
        }
        list.AddNode(raceGrid);

        var resetRaceLabel = Loc.S("Reset races");
        var resetRaceBtn = new TextButtonNode { Size = new Vector2(120, 24), String = resetRaceLabel };
        var rrW = resetRaceBtn.LabelNode.GetTextDrawSize(resetRaceLabel).X + 36;
        if (rrW > 120) resetRaceBtn.Size = new Vector2(rrW, 24);
        resetRaceBtn.OnClick = () =>
        {
            _npcData.ReSetVoiceRaces(_voice);
            Save();
            Close();
        };
        list.AddNode(resetRaceBtn);

        AddNode(list);
    }

    private void Save()
    {
        _config.Save();
    }

    protected override void OnHide(AtkUnitBase* addon)
    {
        // Don't rebuild the voices list — data is saved, visual catches up on next filter or tab revisit
    }

    private void TestVoice()
    {
        if (DialogState.CurrentVoiceMessage != null)
            _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);

        var eventId = _log.Start(nameof(TestVoice), TextSource.AddonTalk);
        var volume = _volumeService.GetVoiceVolume(eventId) * _voice.Volume;

        var speaker = new NpcMapData(ObjectKind.None)
        {
            Gender = _voice.AllowedGenders.Count > 0 ? _voice.AllowedGenders[0] : Genders.Male,
            Race = _voice.AllowedRaces.Count > 0 ? _voice.AllowedRaces[0] : NpcRaces.Hyur,
            Name = _voice.VoiceName,
        };
        speaker.Voices = _config.EchokrautVoices;
        speaker.Voice = _voice;

        var text = _clientState.ClientLanguage switch
        {
            ClientLanguage.German => Constants.TESTMESSAGEDE,
            ClientLanguage.French => Constants.TESTMESSAGEFR,
            ClientLanguage.Japanese => Constants.TESTMESSAGEJP,
            _ => Constants.TESTMESSAGEEN,
        };

        var msg = new VoiceMessage
        {
            SpeakerObj = null,
            Source = TextSource.VoiceTest,
            Speaker = speaker,
            Text = text,
            OriginalText = text,
            Language = _clientState.ClientLanguage,
            EventId = eventId,
            SpeakerFollowObj = _gameObjects.LocalPlayer,
            Volume = volume,
        };

        if (volume > 0)
            _backend.ProcessVoiceMessage(msg);
        else
            _log.End(nameof(TestVoice), eventId);
    }
}
