using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using OtterGui;
using OtterGui.Raii;

namespace Echokraut.Windows;

public class VoiceClipManagerWindow : Window, IDisposable
{
    private readonly IDatabaseService _db;
    private readonly IVoiceClipManagerService _voiceClipManager;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly INpcDataService _npcData;

    private int? _playingVoiceClipId;

    // NPC + Player lists (top level)
    private List<NpcMapData> _npcList = new();
    private List<NpcMapData> _playerList = new();
    private List<NpcMapData> _filteredNpcList = new();
    private List<NpcMapData> _filteredPlayerList = new();
    private bool _updateNpcData = true;

    // NPC filters
    private string _filterGender = "";
    private string _filterRace = "";
    private string _filterName = "";
    private string _filterVoice = "";

    // Expanded NPCs — lazy-loaded encounters grouped by instance
    private readonly HashSet<string> _expandedNpcs = new();
    private readonly HashSet<long> _expandedInstances = new();
    private readonly Dictionary<string, List<(long npcBaseId, List<VoiceClipEntity> encounters)>> _npcInstanceCache = new();


    // Bulk operation
    private bool _bulkRunning;
    private int _bulkProgress;
    private int _bulkTotal;
    private CancellationTokenSource? _bulkCts;
    private bool _confirmDeleteAll;
    private bool _confirmClearHistory;
    private DateTime _lastConfirmClick;

    // Audio file existence cache
    private readonly Dictionary<int, bool> _audioExistsCache = new();

    // Track NPC list sizes to detect external changes (e.g. voice selection tab deletes)
    private int _lastNpcCount;
    private int _lastPlayerCount;

    // Encounter count cache per NPC key — loaded once, not per frame
    private readonly Dictionary<string, int> _encCountCache = new();
    private readonly Dictionary<string, int> _charIdCache = new();
    // Instance location cache: npcBaseId → (zoneName, mapX, mapY)
    private readonly Dictionary<long, (string zone, float x, float y)> _instanceLocationCache = new();

    public VoiceClipManagerWindow(
        IDatabaseService db,
        IVoiceClipManagerService voiceClipManager,
        IAudioPlaybackService audioPlayback,
        INpcDataService npcData)
        : base($"Echokraut {Plugin.PluginVersion} {Loc.S("Voice Clip Manager")}###EKEncounterHistory")
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _voiceClipManager = voiceClipManager ?? throw new ArgumentNullException(nameof(voiceClipManager));
        _audioPlayback = audioPlayback ?? throw new ArgumentNullException(nameof(audioPlayback));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));

        _voiceClipManager.VoiceClipUpdated += () =>
        {
            _updateNpcData = true;
            _npcInstanceCache.Clear();
            _audioExistsCache.Clear();
        };
        _db.VoiceClipLogged += () => _updateNpcData = true;
        _audioPlayback.CurrentMessageChanged += msg =>
        {
            if (msg == null)
                _playingVoiceClipId = null;
        };

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(900, 400),
            MaximumSize = new Vector2(2000, 1200)
        };
    }

    public override void OnOpen()
    {
        _updateNpcData = true;
        _npcInstanceCache.Clear();
        _audioExistsCache.Clear();
    }

    public override void Draw()
    {
        if ((DateTime.Now - _lastConfirmClick).TotalSeconds > 3)
        {
            _confirmDeleteAll = false;
            _confirmClearHistory = false;
        }

        DrawBulkActions();

        // Detect external changes (e.g. mapping deleted in voice selection tab)
        if (_npcData.MappedNpcs.Count != _lastNpcCount || _npcData.MappedPlayers.Count != _lastPlayerCount)
            _updateNpcData = true;

        if (_updateNpcData)
            LoadNpcData();

        using var tabBar = ImRaii.TabBar("##ekEncTabs");
        if (!tabBar) return;

        using (var npcTab = ImRaii.TabItem($"{Loc.S("NPCs")}##ekEncNpcTab"))
        {
            if (npcTab)
                DrawNpcTable(_filteredNpcList, false);
        }

        using (var playerTab = ImRaii.TabItem($"{Loc.S("Players")}##ekEncPlayerTab"))
        {
            if (playerTab)
                DrawNpcTable(_filteredPlayerList, true);
        }
    }

    private void DrawBulkActions()
    {
        using (ImRaii.Disabled(_bulkRunning))
        {
            if (ImGui.Button($"{Loc.S("Generate All Unsaved")}##ekEncGenAll"))
            {
                _bulkCts = new CancellationTokenSource();
                _bulkRunning = true;
                _bulkProgress = 0;
                var encounters = _db.GetVoiceClips(100000);
                _bulkTotal = encounters.Count(e => !e.SavedToDisk && !string.IsNullOrEmpty(e.VoiceKey));
                Task.Run(async () =>
                {
                    try
                    {
                        await _voiceClipManager.GenerateAllUnsaved(encounters,
                            (done, total) => { _bulkProgress = done; _bulkTotal = total; },
                            _bulkCts.Token);
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        _bulkRunning = false;
                        _updateNpcData = true;
                        _npcInstanceCache.Clear();
                        _audioExistsCache.Clear();
                    }
                });
            }

            ImGui.SameLine();
            if (_confirmDeleteAll)
            {
                if (ImGui.Button($"{Loc.S("Click again to confirm!")}##ekEncDelAllConfirm"))
                {
                    _confirmDeleteAll = false;
                    var encounters = _db.GetVoiceClips(100000);
                    _voiceClipManager.DeleteAllSaved(encounters);
                    _updateNpcData = true;
                    _npcInstanceCache.Clear();
                    _audioExistsCache.Clear();
                }
            }
            else if (ImGui.Button($"{Loc.S("Delete All Saved")}##ekEncDelAll"))
            {
                _confirmDeleteAll = true;
                _lastConfirmClick = DateTime.Now;
            }

            ImGui.SameLine();
            if (_confirmClearHistory)
            {
                if (ImGui.Button($"{Loc.S("Click again to confirm!")}##ekEncClearConfirm"))
                {
                    _confirmClearHistory = false;
                    _db.ClearVoiceClips();
                    _updateNpcData = true;
                    _npcInstanceCache.Clear();
                    _audioExistsCache.Clear();
                }
            }
            else if (ImGui.Button($"{Loc.S("Clear History")}##ekEncClear"))
            {
                _confirmClearHistory = true;
                _lastConfirmClick = DateTime.Now;
            }
        }

        if (_bulkRunning)
        {
            ImGui.SameLine();
            if (ImGui.Button($"{Loc.S("Cancel")}##ekEncBulkCancel"))
                _bulkCts?.Cancel();

            ImGui.ProgressBar(_bulkTotal > 0 ? (float)_bulkProgress / _bulkTotal : 0,
                new Vector2(-1, 0), $"{_bulkProgress} / {_bulkTotal}");
        }
    }

    private void LoadNpcData()
    {
        _updateNpcData = false;

        _npcList = new List<NpcMapData>(_npcData.MappedNpcs);
        _playerList = new List<NpcMapData>(_npcData.MappedPlayers);
        _lastNpcCount = _npcData.MappedNpcs.Count;
        _lastPlayerCount = _npcData.MappedPlayers.Count;

        // Pre-cache encounter counts, character IDs, and instance locations
        _encCountCache.Clear();
        _charIdCache.Clear();
        _instanceLocationCache.Clear();
        foreach (var mapData in _npcList.Concat(_playerList))
        {
            var key = mapData.ToString();
            if (_encCountCache.ContainsKey(key)) continue;
            var character = _db.FindCharacter(mapData.Name, mapData.Gender, mapData.Race);
            if (character != null)
            {
                _charIdCache[key] = character.Id;
                _encCountCache[key] = _db.GetVoiceClipCountForCharacter(character.Id);
                foreach (var inst in _db.GetInstancesForCharacter(character.Id))
                    _instanceLocationCache[inst.NpcBaseId] = (inst.ZoneName, inst.MapX, inst.MapY);
            }
            else
            {
                _encCountCache[key] = 0;
            }
        }

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        _filteredNpcList = ApplyListFilters(_npcList);
        _filteredPlayerList = ApplyListFilters(_playerList);
    }

    private List<NpcMapData> ApplyListFilters(List<NpcMapData> source)
    {
        var result = new List<NpcMapData>(source);

        if (_filterGender.Length > 0)
            result = result.FindAll(p =>
                p.Gender.ToString().Contains(_filterGender, StringComparison.OrdinalIgnoreCase));
        if (_filterRace.Length > 0)
            result = result.FindAll(p =>
                p.Race.ToString().Contains(_filterRace, StringComparison.OrdinalIgnoreCase));
        if (_filterName.Length > 0)
            result = result.FindAll(p =>
                p.Name.Contains(_filterName, StringComparison.OrdinalIgnoreCase));
        if (_filterVoice.Length > 0)
            result = result.FindAll(p =>
                p.Voice != null && p.Voice.ToString().Contains(_filterVoice, StringComparison.OrdinalIgnoreCase));

        return result;
    }

    private void DrawNpcTable(List<NpcMapData> dataList, bool isPlayer)
    {
        using var table = ImRaii.Table("Encounter NPC Table##EncNpcTable", 8,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable |
            ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY);
        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 2);
        ImGui.TableSetupColumn("##expand", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25f);
        ImGui.TableSetupColumn(Loc.S("Gender"), ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn(Loc.S("Race"), ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn(Loc.S("Name"), ImGuiTableColumnFlags.WidthFixed, 180);
        ImGui.TableSetupColumn(Loc.S("Voice"), ImGuiTableColumnFlags.WidthStretch, 200);
        ImGui.TableSetupColumn(Loc.S("Volume"), ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn(Loc.S("Voice Clips"), ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("##npcactions", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 85f);
        ImGui.TableHeadersRow();

        // Filter row
        ImGui.TableNextColumn(); // expand - no filter
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##ekEncFltGender", ref _filterGender, 40))
            ApplyFilters();
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##ekEncFltRace", ref _filterRace, 40))
            ApplyFilters();
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##ekEncFltName", ref _filterName, 40))
            ApplyFilters();
        ImGui.TableNextColumn(); // voice - no filter
        ImGui.TableNextColumn(); // volume - no filter
        ImGui.TableNextColumn(); // encounters - no filter
        ImGui.TableNextColumn(); // actions - no filter

        // Sorting
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
            SortNpcData(dataList, sortSpecs.Specs.ColumnIndex, sortSpecs.Specs.SortDirection);
            sortSpecs.SpecsDirty = false;
        }

        // NPC rows
        foreach (var mapData in dataList)
        {
            var npcKey = mapData.ToString();
            var isExpanded = _expandedNpcs.Contains(npcKey);
            _encCountCache.TryGetValue(npcKey, out var encCount);

            ImGui.TableNextRow();

            // Expand button
            ImGui.TableNextColumn();
            var icon = isExpanded ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
            using (ImRaii.Disabled(encCount == 0))
            {
                if (ImGuiUtil.DrawDisabledButton($"{icon.ToIconString()}##exp{npcKey}",
                    new Vector2(25, 25), "", encCount == 0, true))
                {
                    if (isExpanded)
                        _expandedNpcs.Remove(npcKey);
                    else
                        _expandedNpcs.Add(npcKey);
                }
            }

            // Gender (editable combo)
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            var genderIdx = Constants.GENDERLIST.FindIndex(p => p == mapData.Gender);
            if (ImGui.Combo($"##ekEncG{npcKey}", ref genderIdx, Constants.GENDERNAMESLIST, Constants.GENDERNAMESLIST.Length))
            {
                var newGender = Constants.GENDERLIST[genderIdx];
                if (newGender != mapData.Gender)
                {
                    var oldGender = mapData.Gender;
                    mapData.Gender = newGender;
                    mapData.DoNotDelete = true;
                    _npcData.SaveCharacterWithOldIdentity(mapData, mapData.Name, oldGender, mapData.Race);
                    _updateNpcData = true;
                }
            }

            // Race (editable combo)
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            var raceIdx = Constants.RACELIST.FindIndex(p => p == mapData.Race);
            if (ImGui.Combo($"##ekEncR{npcKey}", ref raceIdx, Constants.RACENAMESLIST, Constants.RACENAMESLIST.Length))
            {
                var newRace = Constants.RACELIST[raceIdx];
                if (newRace != mapData.Race)
                {
                    var oldRace = mapData.Race;
                    mapData.Race = newRace;
                    mapData.DoNotDelete = true;
                    _npcData.SaveCharacterWithOldIdentity(mapData, mapData.Name, mapData.Gender, oldRace);
                    _updateNpcData = true;
                }
            }

            // Name
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(mapData.Name);

            // Voice (editable selector)
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (mapData.VoicesSelectable != null)
            {
                if (mapData.VoicesSelectable.Draw(mapData.Voice?.VoiceName ?? "", out var selectedIdx))
                {
                    var voices = _npcData.GetEchokrautVoices()
                        .FindAll(f => f.IsSelectable(mapData.Name, mapData.Gender, mapData.Race, mapData.IsChild));
                    if (selectedIdx >= 0 && selectedIdx < voices.Count)
                    {
                        mapData.Voice = voices[selectedIdx];
                        mapData.DoNotDelete = true;
                        mapData.RefreshSelectable();
                        _npcData.SaveCharacter(mapData);
                    }
                }
            }
            else
            {
                ImGui.TextUnformatted(mapData.voice ?? "");
            }

            // Volume
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            var volume = mapData.Volume;
            if (ImGui.SliderFloat($"##ekEncVol{npcKey}", ref volume, 0f, 2f))
            {
                mapData.Volume = volume;
                mapData.DoNotDelete = true;
                _npcData.SaveCharacter(mapData);
            }

            // Encounter count
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(encCount.ToString());

            // Actions: Generate all / Delete audio / Delete mapping
            ImGui.TableNextColumn();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Download.ToIconString()}##genNpc{npcKey}",
                new Vector2(25, 25), Loc.S("Generate All Unsaved"), encCount == 0 || string.IsNullOrEmpty(mapData.voice), true))
            {
                if (_charIdCache.TryGetValue(npcKey, out var genCharId))
                {
                    Task.Run(async () =>
                    {
                        var encounters = _db.GetVoiceClipsForCharacter(genCharId, 10000);
                        await _voiceClipManager.GenerateAllUnsaved(encounters);
                        _updateNpcData = true;
                        _npcInstanceCache.Clear();
                        _audioExistsCache.Clear();
                    });
                }
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Trash.ToIconString()}##delAudioNpc{npcKey}",
                new Vector2(25, 25), Loc.S("Delete All Saved"), encCount == 0, true))
            {
                _voiceClipManager.StopPlayback();
                _playingVoiceClipId = null;
                if (_charIdCache.TryGetValue(npcKey, out var delCharId))
                {
                    var encounters = _db.GetVoiceClipsForCharacter(delCharId, 10000);
                    _voiceClipManager.DeleteAllSaved(encounters);
                    _npcInstanceCache.Clear();
                    _audioExistsCache.Clear();
                    _updateNpcData = true;
                }
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.UserTimes.ToIconString()}##delMap{npcKey}",
                new Vector2(25, 25), Loc.S("Delete mapping"), mapData.DoNotDelete, true))
            {
                _voiceClipManager.StopPlayback();
                _playingVoiceClipId = null;
                _npcData.RemoveCharacter(mapData);
                _npcData.MappedNpcs.Remove(mapData);
                _npcData.MappedPlayers.Remove(mapData);
                _npcInstanceCache.Clear();
                _audioExistsCache.Clear();
                _updateNpcData = true;
            }

            // Expanded: show encounter rows
            if (isExpanded)
                DrawExpandedEncounters(npcKey, isPlayer);
        }
    }

    private void DrawExpandedEncounters(string npcKey, bool isPlayer)
    {
        // Lazy load and group encounters by NpcBaseId
        if (!_npcInstanceCache.TryGetValue(npcKey, out var instanceGroups))
        {
            instanceGroups = new List<(long npcBaseId, List<VoiceClipEntity> encounters)>();
            if (_charIdCache.TryGetValue(npcKey, out var charId))
            {
                var allEncounters = _db.GetVoiceClipsForCharacter(charId, 500);
                var grouped = allEncounters
                    .GroupBy(e => e.NpcBaseId)
                    .OrderByDescending(g => g.Max(e => e.Timestamp));
                foreach (var group in grouped)
                    instanceGroups.Add((group.Key, group.OrderByDescending(e => e.Timestamp).ToList()));
            }
            _npcInstanceCache[npcKey] = instanceGroups;
        }

        foreach (var (npcBaseId, encounters) in instanceGroups)
        {
            var isInstanceExpanded = _expandedInstances.Contains(npcBaseId);
            var isMuted = _npcData.IsDialogueMuted((uint)npcBaseId);

            // Instance row
            ImGui.TableNextRow();

            // Expand button
            ImGui.TableNextColumn();
            var instIcon = isInstanceExpanded ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
            if (ImGuiUtil.DrawDisabledButton($"{instIcon.ToIconString()}##iexp{npcBaseId}",
                new Vector2(25, 25), "", false, true))
            {
                if (isInstanceExpanded)
                    _expandedInstances.Remove(npcBaseId);
                else
                    _expandedInstances.Add(npcBaseId);
            }

            // Gender column → Instance location label (from cache)
            ImGui.TableNextColumn();
            string instanceLabel;
            if (isPlayer)
                instanceLabel = Loc.S("Chat");
            else if (_instanceLocationCache.TryGetValue(npcBaseId, out var loc) && !string.IsNullOrEmpty(loc.zone))
                instanceLabel = $"{loc.zone} ({loc.x:F1}, {loc.y:F1})";
            else if (npcBaseId > 0)
                instanceLabel = $"ID: {npcBaseId}";
            else
                instanceLabel = Loc.S("Unknown");
            ImGui.TextWrapped(instanceLabel);

            // Race column → Mute toggle
            ImGui.TableNextColumn();
            var muted = isMuted;
            if (ImGui.Checkbox($"##mute{npcBaseId}", ref muted))
            {
                if (muted)
                    _npcData.MuteDialogue((uint)npcBaseId);
                else
                    _npcData.UnmuteDialogue((uint)npcBaseId);
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.S("Muted"));

            // Name column → encounter count
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{encounters.Count} {Loc.S("Voice Clips").ToLower()}");

            // Voice column
            ImGui.TableNextColumn();

            // Volume column
            ImGui.TableNextColumn();

            // Encounters column
            ImGui.TableNextColumn();

            // Actions: Generate all / Delete audio for this instance
            ImGui.TableNextColumn();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Download.ToIconString()}##genInst{npcBaseId}",
                new Vector2(25, 25), Loc.S("Generate All Unsaved"), false, true))
            {
                var instEncounters = new List<VoiceClipEntity>(encounters);
                Task.Run(async () =>
                {
                    await _voiceClipManager.GenerateAllUnsaved(instEncounters);
                    _npcInstanceCache.Remove(npcKey);
                    _audioExistsCache.Clear();
                    _updateNpcData = true;
                });
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Trash.ToIconString()}##delInst{npcBaseId}",
                new Vector2(25, 25), Loc.S("Delete All Saved"), false, true))
            {
                _voiceClipManager.StopPlayback();
                _playingVoiceClipId = null;
                _voiceClipManager.DeleteAllSaved(encounters);
                _npcInstanceCache.Remove(npcKey);
                _audioExistsCache.Clear();
                _updateNpcData = true;
            }

            // Expanded instance: show encounter rows
            if (isInstanceExpanded)
            {
                foreach (var enc in encounters)
                    DrawEncounterRow(enc, npcKey);
            }
        }
    }

    private void DrawEncounterRow(VoiceClipEntity enc, string npcKey)
    {
        var hasSaved = GetAudioExists(enc);

        ImGui.TableNextRow();

        // Play/Stop
        ImGui.TableNextColumn();
        var isPlayingThis = _playingVoiceClipId == enc.Id;
        if (isPlayingThis)
        {
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Stop.ToIconString()}##stp{enc.Id}",
                new Vector2(25, 25), Loc.S("Stop"), false, true))
            {
                _voiceClipManager.StopPlayback();
                _playingVoiceClipId = null;
            }
        }
        else
        {
            var canPlay = !string.IsNullOrEmpty(enc.VoiceKey);
            var tooltip = hasSaved ? Loc.S("Play") : Loc.S("Generate and play");
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Play.ToIconString()}##ply{enc.Id}",
                new Vector2(25, 25), tooltip, !canPlay, true))
            {
                _playingVoiceClipId = enc.Id;
                if (hasSaved)
                {
                    _voiceClipManager.PlayEncounter(enc);
                }
                else
                {
                    Task.Run(async () =>
                    {
                        var success = await _voiceClipManager.GenerateForEncounter(enc);
                        if (success)
                            _voiceClipManager.PlayEncounter(enc);
                        else
                            _playingVoiceClipId = null;
                        _npcInstanceCache.Remove(npcKey);
                        _audioExistsCache.Clear();
                    });
                }
            }
        }

        // Gender column → Source
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(((TextSource)enc.TextSource).ToString());

        // Race column
        ImGui.TableNextColumn();

        // Name column → Timestamp
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(enc.Timestamp.ToLocalTime().ToString("MM/dd HH:mm"));

        // Voice column → Text (word wrapped)
        ImGui.TableNextColumn();
        ImGui.TextWrapped(enc.OriginalText);

        // Volume column
        ImGui.TableNextColumn();

        // Encounters column → Saved status
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(hasSaved ? "Y" : "N");

        // Actions column
        ImGui.TableNextColumn();
        if (hasSaved)
        {
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Redo.ToIconString()}##erg{enc.Id}",
                new Vector2(25, 25), Loc.S("Regenerate"), string.IsNullOrEmpty(enc.VoiceKey), true))
            {
                _voiceClipManager.StopPlayback();
                _playingVoiceClipId = null;
                Task.Run(async () =>
                {
                    await _voiceClipManager.GenerateForEncounter(enc);
                    _npcInstanceCache.Remove(npcKey);
                    _audioExistsCache.Clear();
                });
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Trash.ToIconString()}##edl{enc.Id}",
                new Vector2(25, 25), Loc.S("Delete audio"), false, true))
            {
                _voiceClipManager.StopPlayback();
                _playingVoiceClipId = null;
                _voiceClipManager.DeleteAudioForEncounter(enc);
                _npcInstanceCache.Remove(npcKey);
                _audioExistsCache.Clear();
            }
        }
        else if (!string.IsNullOrEmpty(enc.VoiceKey))
        {
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Download.ToIconString()}##egn{enc.Id}",
                new Vector2(25, 25), Loc.S("Generate"), false, true))
            {
                Task.Run(async () =>
                {
                    await _voiceClipManager.GenerateForEncounter(enc);
                    _npcInstanceCache.Remove(npcKey);
                    _audioExistsCache.Clear();
                });
            }
        }
    }

    private void SortNpcData(List<NpcMapData> dataList, int columnIndex, ImGuiSortDirection direction)
    {
        Comparison<NpcMapData> cmp = columnIndex switch
        {
            1 => (a, b) => a.Gender.ToString().CompareTo(b.Gender.ToString()),
            2 => (a, b) => a.Race.ToString().CompareTo(b.Race.ToString()),
            3 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            4 => (a, b) => string.Compare(a.Voice?.ToString() ?? "", b.Voice?.ToString() ?? ""),
            5 => (a, b) => a.Volume.CompareTo(b.Volume),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
        };

        if (direction == ImGuiSortDirection.Descending)
        {
            var inner = cmp;
            cmp = (a, b) => inner(b, a);
        }

        dataList.Sort(cmp);
    }

    private bool GetAudioExists(VoiceClipEntity enc)
    {
        if (_audioExistsCache.TryGetValue(enc.Id, out var exists))
            return exists;

        exists = _voiceClipManager.HasLocalAudio(enc);
        _audioExistsCache[enc.Id] = exists;
        return exists;
    }

    public void Dispose()
    {
        _bulkCts?.Cancel();
        _bulkCts?.Dispose();
    }
}
