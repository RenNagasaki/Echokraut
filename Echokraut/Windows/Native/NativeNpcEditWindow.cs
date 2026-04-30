using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

/// <summary>
/// Popup that lets the user override an NPC's (or player's) Race, Gender, Voice, Volume, and Mute
/// after the fact. Used from the Voice Clip Manager when auto-detected/Lodestone values are wrong
/// or when fine-tuning per-NPC.
/// </summary>
public sealed unsafe class NativeNpcEditWindow : NativeAddon
{
    private readonly NpcMapData _npc;
    private readonly INpcDataService _npcData;
    private readonly ILogService _log;
    private readonly Action _onSaved;

    // Pending values — TextDropDownNode crashes when its label is updated mid-OnOptionSelected,
    // so we capture pending values and apply them on Save click.
    private NpcRaces _pendingRace;
    private Genders _pendingGender;
    private string _pendingVoiceKey;
    private float _pendingVolume;
    private bool _pendingEnabled;

    // Voices filtered for this NPC's identity. Built once at OnSetup against the *original*
    // race/gender/bodyType — the user re-opens the window after a race/gender change to refresh.
    private List<EchokrautVoice> _selectableVoices = new();

    public NativeNpcEditWindow(
        NpcMapData npc,
        INpcDataService npcData,
        ILogService log,
        Action onSaved)
    {
        _npc = npc ?? throw new ArgumentNullException(nameof(npc));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _onSaved = onSaved ?? throw new ArgumentNullException(nameof(onSaved));

        _pendingRace = _npc.Race;
        _pendingGender = _npc.Gender;
        _pendingVoiceKey = _npc.voice ?? "";
        _pendingVolume = _npc.Volume;
        _pendingEnabled = _npc.IsEnabled;
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        var pos = ContentStartPosition;
        var size = ContentSize;
        var w = size.X;

        // Layout offsets (relative to pos.Y). NPC name is shown in the window title.
        var y = 0f;

        // ── Race dropdown ────────────────────────────────────────────
        AddNode(new TextNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 18),
            String = Loc.S("Race"),
            FontType = FontType.Axis,
            FontSize = 13,
        });
        y += 20;

        var raceDropDown = new TextDropDownNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 26),
            Options = [],
        };
        raceDropDown.OptionListNode.Options = new List<string>(Constants.RACENAMESLIST);
        var currentRaceName = _npc.Race.ToString();
        raceDropDown.OptionListNode.SelectedOption = currentRaceName;
        if (raceDropDown.LabelNode.Node != null)
            raceDropDown.LabelNode.String = currentRaceName;
        raceDropDown.OnOptionSelected = option =>
        {
            if (Enum.TryParse<NpcRaces>(option, out var race))
                _pendingRace = race;
        };
        AddNode(raceDropDown);
        y += 36;

        // ── Gender dropdown ──────────────────────────────────────────
        AddNode(new TextNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 18),
            String = Loc.S("Gender"),
            FontType = FontType.Axis,
            FontSize = 13,
        });
        y += 20;

        var genderDropDown = new TextDropDownNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 26),
            Options = [],
        };
        genderDropDown.OptionListNode.Options = new List<string>(Constants.GENDERNAMESLIST);
        var currentGenderName = _npc.Gender.ToString();
        genderDropDown.OptionListNode.SelectedOption = currentGenderName;
        if (genderDropDown.LabelNode.Node != null)
            genderDropDown.LabelNode.String = currentGenderName;
        genderDropDown.OnOptionSelected = option =>
        {
            if (Enum.TryParse<Genders>(option, out var gender))
                _pendingGender = gender;
        };
        AddNode(genderDropDown);
        y += 36;

        // ── Voice dropdown ───────────────────────────────────────────
        AddNode(new TextNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 18),
            String = Loc.S("Voice"),
            FontType = FontType.Axis,
            FontSize = 13,
        });
        y += 20;

        var allVoices = _npc.Voices ?? _npcData.GetEchokrautVoices();
        _selectableVoices = allVoices
            .FindAll(v => v.IsSelectable(_npc.Name, _npc.Gender, _npc.Race, _npc.BodyType));

        // Always include the currently-assigned voice even if it's no longer "selectable" by filter,
        // so the user can see what's set without losing it on accidental save.
        if (!string.IsNullOrEmpty(_npc.voice) && !_selectableVoices.Any(v => v.BackendVoice == _npc.voice))
        {
            var current = allVoices.Find(v => v.BackendVoice == _npc.voice);
            if (current != null) _selectableVoices.Add(current);
        }

        // Sort alphabetically (A→Z, case-insensitive) so the dropdown is easy to scan.
        _selectableVoices = _selectableVoices
            .OrderBy(v => v.VoiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Build option list. Empty option = "no voice" (lets user clear assignment).
        // The same localized string is used for display AND comparison, so option matching
        // still works across languages.
        var noVoiceLabel = Loc.S("(none)");
        var voiceOptions = new List<string> { noVoiceLabel };
        voiceOptions.AddRange(_selectableVoices.Select(v => v.VoiceName));

        var voiceDropDown = new TextDropDownNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 26),
            Options = [],
        };
        voiceDropDown.OptionListNode.Options = voiceOptions;
        var currentVoiceLabel = string.IsNullOrEmpty(_npc.voice)
            ? noVoiceLabel
            : _selectableVoices.Find(v => v.BackendVoice == _npc.voice)?.VoiceName ?? noVoiceLabel;
        voiceDropDown.OptionListNode.SelectedOption = currentVoiceLabel;
        if (voiceDropDown.LabelNode.Node != null)
            voiceDropDown.LabelNode.String = currentVoiceLabel;
        voiceDropDown.OnOptionSelected = option =>
        {
            if (option == noVoiceLabel)
            {
                _pendingVoiceKey = "";
                return;
            }
            var match = _selectableVoices.Find(v => v.VoiceName == option);
            if (match != null) _pendingVoiceKey = match.BackendVoice;
        };
        AddNode(voiceDropDown);
        y += 36;

        // ── Volume slider ────────────────────────────────────────────
        AddNode(new TextNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 18),
            String = Loc.S("Volume"),
            FontType = FontType.Axis,
            FontSize = 13,
        });
        y += 20;

        var volSlider = new SliderNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 20),
            Range = 0..200,
            DecimalPlaces = 2,
            Value = (int)(_npc.Volume * 100),
        };
        volSlider.OnValueChanged = v => _pendingVolume = v / 100.0f;
        AddNode(volSlider);
        y += 28;

        // ── Enabled checkbox ─────────────────────────────────────────
        // Whole-character toggle (CharacterContext.IsEnabled). Distinct from the
        // per-instance mute in DialogExtraOptions which only silences this NPC's specific
        // ENpcBase ID (CharacterInstance.IsMuted).
        var enabledCheck = new CheckboxNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w, 24),
            String = Loc.S("Enabled"),
            IsChecked = _npc.IsEnabled,
            OnClick = v => _pendingEnabled = v,
        };
        AddNode(enabledCheck);
        y += 32;

        // ── Save / Cancel ────────────────────────────────────────────
        var saveBtn = new TextButtonNode
        {
            Position = new Vector2(pos.X, pos.Y + y),
            Size = new Vector2(w / 2 - 4, 28),
            String = Loc.S("Save"),
        };
        saveBtn.OnClick = SaveClicked;
        AddNode(saveBtn);

        var cancelBtn = new TextButtonNode
        {
            Position = new Vector2(pos.X + w / 2 + 4, pos.Y + y),
            Size = new Vector2(w / 2 - 4, 28),
            String = Loc.S("Cancel"),
        };
        cancelBtn.OnClick = Close;
        AddNode(cancelBtn);
    }

    private void SaveClicked()
    {
        try
        {
            var identityChanged = _pendingRace != _npc.Race || _pendingGender != _npc.Gender;
            var anyChange = identityChanged
                || _pendingVoiceKey != (_npc.voice ?? "")
                || Math.Abs(_pendingVolume - _npc.Volume) > 0.001f
                || _pendingEnabled != _npc.IsEnabled;

            if (!anyChange)
            {
                Close();
                return;
            }

            var oldName = _npc.Name;
            var oldGender = _npc.Gender;
            var oldRace = _npc.Race;

            _npc.Race = _pendingRace;
            _npc.RaceStr = _pendingRace.ToString();
            _npc.Gender = _pendingGender;
            _npc.voice = _pendingVoiceKey;
            _npc.Volume = _pendingVolume;
            _npc.IsEnabled = _pendingEnabled;

            if (identityChanged)
                _npcData.SaveCharacterWithOldIdentity(_npc, oldName, oldGender, oldRace);
            else
                _npcData.SaveCharacter(_npc);

            _log.Info(nameof(NativeNpcEditWindow),
                $"Updated character {oldName}: Race={oldRace}→{_pendingRace}, " +
                $"Gender={oldGender}→{_pendingGender}, Voice='{_pendingVoiceKey}', " +
                $"Volume={_pendingVolume:0.00}, Enabled={_pendingEnabled}",
                new EKEventId(0, TextSource.None));

            _onSaved();
            Close();
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(NativeNpcEditWindow),
                $"Failed to save character edit: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }
}
