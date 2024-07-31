using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Echokraut.DataClasses;
using ImGuiNET;
using Echokraut.Enums;
using System.Linq;
using Dalamud.Interface;
using Echokraut.Helper;
using System.Reflection;
using System.IO;
using Dalamud.Interface.ImGuiFileDialog;
using OtterGui;
using Dalamud.Interface.Utility.Raii;
using static System.Net.Mime.MediaTypeNames;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Xml.Linq;

namespace Echokraut.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Echokraut plugin;
    private string testConnectionRes = "";
    private FileDialogManager fileDialogManager;
    private bool resetLogFilter = true;
    private string logFilter = "";
    private bool resetPlayerFilter = true;
    private string playerFilter = "";
    private List<NpcMapData> filteredPlayers;
    private bool resetNpcFilter = true;
    private string npcFilter = "";
    private List<NpcMapData> filteredNpcs;
    private string originalText = "";
    private string correctedText = "";
    private IClientState clientState;
    public static bool UpdateNpcData = false;
    public static bool UpdatePlayerData = false;

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Echokraut plugin, Configuration configuration, IClientState clientState) : base($"Echokraut configuration###EKSettings")
    {
        this.plugin = plugin;
        this.clientState = clientState;

        Flags = ImGuiWindowFlags.AlwaysVerticalScrollbar & ImGuiWindowFlags.HorizontalScrollbar & ImGuiWindowFlags.AlwaysHorizontalScrollbar;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;

        Configuration = configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (Configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        try 
        { 
            if (ImGui.BeginTabBar($"Echokraut##EKTab"))
            {
                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettings();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Voice selection"))
                {
                    DrawVoiceSelection();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Phonetic corrections"))
                {
                    DrawPhoneticCorrections();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Logs"))
                {
                    DrawLogs();
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawSettings()
    {
        try
        {
            if (ImGui.BeginTabBar($"Settings##EKSettingsTab"))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    DrawGeneralSettings();
                    ImGui.EndTabItem();
                }

                using (var disabled = ImRaii.Disabled(!Configuration.Enabled))
                {
                    if (ImGui.BeginTabItem("Dialogue"))
                    {
                        DrawDialogueSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Battle dialogue"))
                    {
                        DrawBattleDialogueSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Chat"))
                    {
                        DrawChatSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Bubbles"))
                    {
                        DrawBubbleSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Save/Load"))
                    {
                        DrawSaveSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Backend"))
                    {
                        DrawBackendSettings();
                        ImGui.EndTabItem();
                    }
                }
            }

            ImGui.EndTabBar();
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawGeneralSettings()
    {
        var enabled = this.Configuration.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            this.Configuration.Enabled = enabled;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!enabled))
        {
            var removeStutters = this.Configuration.RemoveStutters;
            if (ImGui.Checkbox("Remove stutters", ref removeStutters))
            {
                this.Configuration.RemoveStutters = removeStutters;
                this.Configuration.Save();
            } 
        }
    }

    private void DrawDialogueSettings()
    {
        var voiceDialog = this.Configuration.VoiceDialog;
        if (ImGui.Checkbox("Voice dialog", ref voiceDialog))
        {
            this.Configuration.VoiceDialog = voiceDialog;
            this.Configuration.Save();
        }

        var voicePlayerChoicesCutscene = this.Configuration.VoicePlayerChoicesCutscene;
        if (ImGui.Checkbox("Voice player choices in cutscene", ref voicePlayerChoicesCutscene))
        {
            this.Configuration.VoicePlayerChoicesCutscene = voicePlayerChoicesCutscene;
            this.Configuration.Save();
        }

        var voicePlayerChoices = this.Configuration.VoicePlayerChoices;
        if (ImGui.Checkbox("Voice player choices outside of cutscene", ref voicePlayerChoices))
        {
            this.Configuration.VoicePlayerChoices = voicePlayerChoices;
            this.Configuration.Save();
        }

        var cancelAdvance = this.Configuration.CancelSpeechOnTextAdvance;
        if (ImGui.Checkbox("Cancel voice on text advance", ref cancelAdvance))
        {
            this.Configuration.CancelSpeechOnTextAdvance = cancelAdvance;
            this.Configuration.Save();
        }

        var autoAdvanceOnSpeechCompletion = this.Configuration.AutoAdvanceTextAfterSpeechCompleted;
        if (ImGui.Checkbox("Click dialogue window after speech completion", ref autoAdvanceOnSpeechCompletion))
        {
            this.Configuration.AutoAdvanceTextAfterSpeechCompleted = autoAdvanceOnSpeechCompletion;
            this.Configuration.Save();
        }
    }

    private void DrawBattleDialogueSettings()
    {
        var voiceBattleDialog = this.Configuration.VoiceBattleDialog;
        if (ImGui.Checkbox("Voice battle dialog", ref voiceBattleDialog))
        {
            this.Configuration.VoiceBattleDialog = voiceBattleDialog;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!voiceBattleDialog))
        {
            var voiceBattleDialogQueued = this.Configuration.VoiceBattleDialogQueued;
            if (ImGui.Checkbox("Voice battle dialog in a queue", ref voiceBattleDialogQueued))
            {
                this.Configuration.VoiceBattleDialogQueued = voiceBattleDialogQueued;
                this.Configuration.Save();
            }
        }
    }

    private void DrawBackendSettings()
    {
        var backends = Enum.GetValues<TTSBackends>().ToArray();
        var backendsDisplay = backends.Select(b => b.ToString()).ToArray();
        var presetIndex = Enum.GetValues<TTSBackends>().ToList().IndexOf(this.Configuration.BackendSelection);
        if (ImGui.Combo($"Select Backend##EKCBoxBackend", ref presetIndex, backendsDisplay, backendsDisplay.Length))
        {
            var backendSelection = backends[presetIndex];
            this.Configuration.BackendSelection = backendSelection;
            this.Configuration.Save();
            BackendHelper.SetBackendType(backendSelection);

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated backendselection to: {Constants.BACKENDS[presetIndex]}", new EKEventId(0, TextSource.None));
        }

        if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
        {
            if (ImGui.InputText($"Base Url##EKBaseUrl", ref this.Configuration.Alltalk.BaseUrl, 40))
                this.Configuration.Save();
            ImGui.SameLine();
            if (ImGui.Button($"Test Connection##EKTestConnection"))
            {
                BackendCheckReady(new EKEventId(0, TextSource.None));
            }

            if (ImGui.InputText($"Model to reload##EKBaseUrl", ref this.Configuration.Alltalk.ReloadModel, 40))
                this.Configuration.Save();
            ImGui.SameLine();
            if (ImGui.Button($"Restart Service##EKRestartService"))
            {
                BackendReloadService(this.Configuration.Alltalk.ReloadModel);
            }

            if (ImGui.Button($"Reload Voices##EKLoadVoices"))
            {
                BackendGetVoices();
            }

            if (!string.IsNullOrWhiteSpace(testConnectionRes))
                ImGui.TextColored(new(1.0f, 1.0f, 1.0f, 0.6f), $"Connection test result: {testConnectionRes}");
        }
    }

    private void DrawSaveSettings()
    {
        var loadLocalFirst = this.Configuration.LoadFromLocalFirst;
        if (ImGui.Checkbox("Search audio locally first before generating", ref loadLocalFirst))
        {
            this.Configuration.LoadFromLocalFirst = loadLocalFirst;
            this.Configuration.Save();
        }
        var saveLocally = this.Configuration.SaveToLocal;
        if (ImGui.Checkbox("Save generated audio locally", ref saveLocally))
        {
            this.Configuration.SaveToLocal = saveLocally;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!saveLocally))
        {
            var createMissingLocalSave = this.Configuration.CreateMissingLocalSaveLocation;
            if (ImGui.Checkbox("Create directory if not existing", ref createMissingLocalSave))
            {
                this.Configuration.CreateMissingLocalSaveLocation = createMissingLocalSave;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!saveLocally && !loadLocalFirst))
        {
            string localSaveLocation = this.Configuration.LocalSaveLocation;
            if (ImGui.InputText($"##EKSavePath", ref localSaveLocation, 40))
            {
                this.Configuration.LocalSaveLocation = localSaveLocation;
                this.Configuration.Save();
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Folder.ToIconString()}##import", new Vector2(25, 25),
                    "Select a directory via dialog.", false, true))
            {
                var startDir = this.Configuration.LocalSaveLocation.Length > 0 && Directory.Exists(this.Configuration.LocalSaveLocation)
                ? this.Configuration.LocalSaveLocation
                    : null;

                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Connection test result: {startDir}", new EKEventId(0, TextSource.None));
                fileDialogManager = new FileDialogManager();
                fileDialogManager.OpenFolderDialog("Choose audiofiles directory", (b, s) =>
                {
                    if (!b)
                        return;

                    this.Configuration.LocalSaveLocation = s;
                    this.Configuration.Save();
                    fileDialogManager = null;
                }, startDir, false);
            }

            if (fileDialogManager != null)
            {
                fileDialogManager.Draw();
            }
        }
    }

    private void DrawBubbleSettings()
    {
        var voiceBubbles = this.Configuration.VoiceBubbles;
        if (ImGui.Checkbox("Voice NPC Bubbles", ref voiceBubbles))
        {
            this.Configuration.VoiceBubbles = voiceBubbles;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!voiceBubbles))
        {
            var voiceBubblesInCity = this.Configuration.VoiceBubblesInCity;
            if (ImGui.Checkbox("Voice NPC Bubbles in City", ref voiceBubblesInCity))
            {
                this.Configuration.VoiceBubblesInCity = voiceBubblesInCity;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceBubbles))
        {
            var voiceSourceCam = this.Configuration.VoiceSourceCam;
            if (ImGui.Checkbox("Voice Bubbles with camera as center", ref voiceSourceCam))
            {
                this.Configuration.VoiceSourceCam = voiceSourceCam;
                this.Configuration.Save();
            }
        }
    }

    private void DrawChatSettings()
    {
        var voiceChat = this.Configuration.VoiceChat;
        if (ImGui.Checkbox("Voice Chat", ref voiceChat))
        {
            this.Configuration.VoiceChat = voiceChat;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatPlayer = this.Configuration.VoiceChatPlayer;
            if (ImGui.Checkbox("Voice your own Chat", ref voiceChatPlayer))
            {
                this.Configuration.VoiceChatPlayer = voiceChatPlayer;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatSay = this.Configuration.VoiceChatSay;
            if (ImGui.Checkbox("Voice say Chat", ref voiceChatSay))
            {
                this.Configuration.VoiceChatSay = voiceChatSay;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatYell = this.Configuration.VoiceChatYell;
            if (ImGui.Checkbox("Voice yell Chat", ref voiceChatYell))
            {
                this.Configuration.VoiceChatYell = voiceChatYell;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatShout = this.Configuration.VoiceChatShout;
            if (ImGui.Checkbox("Voice shout Chat", ref voiceChatShout))
            {
                this.Configuration.VoiceChatShout = voiceChatShout;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatFreeCompany = this.Configuration.VoiceChatFreeCompany;
            if (ImGui.Checkbox("Voice free company Chat", ref voiceChatFreeCompany))
            {
                this.Configuration.VoiceChatFreeCompany = voiceChatFreeCompany;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatTell = this.Configuration.VoiceChatTell;
            if (ImGui.Checkbox("Voice tell Chat", ref voiceChatTell))
            {
                this.Configuration.VoiceChatTell = voiceChatTell;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatParty = this.Configuration.VoiceChatParty;
            if (ImGui.Checkbox("Voice party Chat", ref voiceChatParty))
            {
                this.Configuration.VoiceChatParty = voiceChatParty;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatAlliance = this.Configuration.VoiceChatAlliance;
            if (ImGui.Checkbox("Voice alliance Chat", ref voiceChatAlliance))
            {
                this.Configuration.VoiceChatAlliance = voiceChatAlliance;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatNoviceNetwork = this.Configuration.VoiceChatNoviceNetwork;
            if (ImGui.Checkbox("Voice novice network Chat", ref voiceChatNoviceNetwork))
            {
                this.Configuration.VoiceChatNoviceNetwork = voiceChatNoviceNetwork;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatLinkshell = this.Configuration.VoiceChatLinkshell;
            if (ImGui.Checkbox("Voice Linkshells", ref voiceChatLinkshell))
            {
                this.Configuration.VoiceChatLinkshell = voiceChatLinkshell;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatCrossLinkshell = this.Configuration.VoiceChatCrossLinkshell;
            if (ImGui.Checkbox("Voice Cross Linkshells", ref voiceChatCrossLinkshell))
            {
                this.Configuration.VoiceChatCrossLinkshell = voiceChatCrossLinkshell;
                this.Configuration.Save();
            }
        }
    }

    private async void BackendCheckReady(EKEventId eventId)
    {
        try
        {
            if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
                testConnectionRes = await BackendHelper.CheckReady(eventId);
            else
                testConnectionRes = "No backend selected";
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Connection test result: {testConnectionRes}", eventId);
        }
        catch (Exception ex)
        {
            testConnectionRes = ex.ToString();
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
        }
    }

    private async void BackendGetVoices()
    {
        try
        {
            if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
                BackendHelper.SetBackendType(this.Configuration.BackendSelection);
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }

    private async void BackendReloadService(string reloadModel)
    {
        try
        {
            if (BackendHelper.ReloadService(reloadModel, new EKEventId(0, TextSource.None)))
                testConnectionRes = "Successfully started service reload. Please wait for up to 30 seconds before using.";
            else
                testConnectionRes = "Error while service reload. Please check logs.";

            LogHelper.Important(MethodBase.GetCurrentMethod().Name, testConnectionRes, new EKEventId(0, TextSource.None));
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }

    private async void BackendTestVoice(BackendVoiceItem voice)
    {
        var eventId = DataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonTalk);
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Testing voice: {voice.ToString()}", eventId);
        // Say the thing
        var voiceMessage = new VoiceMessage
        {
            pActor = null,
            Source = TextSource.AddonTalk,
            Speaker = new NpcMapData(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.None)
            {
                gender = voice.gender,
                race = voice.race,
                name = voice.voiceName
            },
            Text = Constants.TESTMESSAGEDE,
            Language = this.clientState.ClientLanguage.ToString(),
            eventId = eventId
        };
        var volume = VolumeHelper.GetVoiceVolume(eventId);

        if (volume > 0)
            BackendHelper.OnSay(voiceMessage, volume);
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. Volume is 0", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
        }
    }

    private void DrawVoiceSelection()
    {
        try
        {
            if (ImGui.BeginTabBar($"Voices##EKVoicesTab"))
            {
                if (ImGui.BeginTabItem("Options"))
                {
                    var voicesAllOriginals = this.Configuration.VoicesAllOriginals;
                    if (ImGui.Checkbox("Show all original voices as option", ref voicesAllOriginals))
                    {
                        this.Configuration.VoicesAllOriginals = voicesAllOriginals;
                        this.Configuration.Save();
                    }
                    var voicesAllGenders = this.Configuration.VoicesAllGenders;
                    if (ImGui.Checkbox("Show both genders as option", ref voicesAllGenders))
                    {
                        this.Configuration.VoicesAllGenders = voicesAllGenders;
                        this.Configuration.Save();
                    }
                    var voicesAllRaces = this.Configuration.VoicesAllRaces;
                    if (ImGui.Checkbox("Show all races as option", ref voicesAllRaces))
                    {
                        this.Configuration.VoicesAllRaces = voicesAllRaces;
                        this.Configuration.Save();
                    }
                    var voicesAllBubbles = this.Configuration.VoicesAllBubbles;
                    if (ImGui.Checkbox("Show all bubble npcs as option", ref voicesAllBubbles))
                    {
                        this.Configuration.VoicesAllBubbles = voicesAllBubbles;
                        this.Configuration.Save();
                    }

                    ImGui.EndTabItem();
                }

                DrawNpcTab();

                DrawPlayerTab();

                DrawVoicesTab();

                ImGui.EndTabBar();
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawNpcTab()
    {
        if (ImGui.BeginTabItem("NPCs"))
        {
            if (filteredNpcs == null)
            {
                filteredNpcs = Configuration.MappedNpcs;
                filteredNpcs.Sort();
            }

            if (ImGui.InputText($"Filter by npc name##EKFilterNpc", ref npcFilter, 40) || (npcFilter.Length > 0 && UpdateNpcData))
            {
                filteredNpcs = Configuration.MappedNpcs.FindAll(b => b.name.ToLower().Contains(npcFilter.ToLower()));
                filteredNpcs.Sort();
                resetNpcFilter = false;
                UpdateNpcData = false;
            }
            else if ((!resetNpcFilter && npcFilter.Length == 0) || UpdateNpcData)
            {
                filteredNpcs = Configuration.MappedNpcs;
                filteredNpcs.Sort();
                resetNpcFilter = true;
                UpdateNpcData = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear mapped npcs##clearnpc"))
            {
                foreach (NpcMapData npcMapData in this.Configuration.MappedNpcs)
                {
                    FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, npcMapData.name);
                }
                this.Configuration.MappedNpcs.Clear();
                this.Configuration.Save();
            }

            if (ImGui.BeginChild("NpcsChild"))
            {
                if (ImGui.BeginTable("NPC Table##NPCTable", 6))
                {
                    ImGui.TableSetupScrollFreeze(0, 1); // Make top row always visible
                    ImGui.TableSetupColumn("Mapping", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Saves", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.None, 250);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextRow();

                    NpcMapData toBeRemoved = null;
                    foreach (NpcMapData mapData in filteredNpcs)
                    {
                        if (!this.Configuration.VoicesAllOriginals && mapData.voiceItem.voiceName.ToLower().Contains(mapData.name.ToLower()))
                            continue;

                        if (!this.Configuration.VoicesAllBubbles && mapData.name.StartsWith("Bubble"))
                            continue;

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Trash.ToIconString()}##delnpc{mapData.ToString()}", new Vector2(25, 25), "Remove NPC mapping", false, true))
                        {
                            toBeRemoved = mapData;
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.TrashAlt.ToIconString()}##delnpcsaves{mapData.ToString()}", new Vector2(25, 25), "Remove NPC saves", false, true))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, mapData.name);
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexGender = BackendVoiceHelper.GenderDisplay.IndexOf(p => p.Contains(mapData.gender.ToString()));
                        if (ImGui.Combo($"##EKCBoxNPC{mapData.ToString(Configuration.VoicesAllRaces)}1", ref presetIndexGender, BackendVoiceHelper.GenderDisplay, BackendVoiceHelper.GenderDisplay.Length))
                        {
                            var newGender = BackendVoiceHelper.GenderArr[presetIndexGender];
                            if (newGender != mapData.gender)
                            {
                                if (Configuration.MappedNpcs.Contains(new NpcMapData(mapData.objectKind) { gender = newGender, race = mapData.race, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Gender for Character: {mapData.ToString(true)} from: {mapData.gender} to: {newGender}", new EKEventId(0, TextSource.None));

                                    mapData.gender = newGender;
                                    filteredNpcs = null;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Gender for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexRace = BackendVoiceHelper.RaceDisplay.IndexOf(p => p.Contains(mapData.race.ToString()));
                        if (ImGui.Combo($"##EKCBoxNPC{mapData.ToString(Configuration.VoicesAllRaces)}2", ref presetIndexRace, BackendVoiceHelper.RaceDisplay, BackendVoiceHelper.RaceDisplay.Length))
                        {
                            var newRace = BackendVoiceHelper.RaceArr[presetIndexRace];
                            if (newRace != mapData.race)
                            {
                                if (Configuration.MappedNpcs.Contains(new NpcMapData(mapData.objectKind) { gender = mapData.gender, race = newRace, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Race for Character: {mapData.ToString(true)} from: {mapData.race} to: {newRace}", new EKEventId(0, TextSource.None));

                                    mapData.race = newRace;
                                    filteredNpcs = null;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Race for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(mapData.name);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var localVoices = (this.Configuration.VoicesAllOriginals ?
                                            (this.Configuration.VoicesAllGenders ?
                                                (this.Configuration.VoicesAllRaces ?
                                                    BackendVoiceHelper.Voices :
                                                    BackendVoiceHelper.FilteredVoicesAllGenders[mapData.race]
                                                ) :
                                                (this.Configuration.VoicesAllRaces ?
                                                    BackendVoiceHelper.FilteredVoicesAllRaces[mapData.gender] :
                                                    BackendVoiceHelper.FilteredVoices[mapData.gender][mapData.race]
                                                )
                                            ) :
                                            (this.Configuration.VoicesAllGenders ?
                                                (this.Configuration.VoicesAllRaces ?
                                                    BackendVoiceHelper.VoicesOriginal :
                                                    BackendVoiceHelper.FilteredVoicesAllGendersOriginal[mapData.race]
                                                ) :
                                                (this.Configuration.VoicesAllRaces ?
                                                    BackendVoiceHelper.FilteredVoicesAllRacesOriginal[mapData.gender] :
                                                    BackendVoiceHelper.FilteredVoicesOriginal[mapData.gender][mapData.race]
                                                )
                                            )
                                          );

                        var voicesDisplay = localVoices.Select(b => b.ToString()).ToArray();
                        var presetIndexVoice = localVoices.FindIndex(p => p.ToString().Contains(mapData.voiceItem?.ToString() ?? "Narrator"));
                        if (ImGui.Combo($"##EKCBoxNPC{mapData.ToString(Configuration.VoicesAllRaces)}3", ref presetIndexVoice, voicesDisplay, voicesDisplay.Length))
                        {
                            var newVoiceItem = localVoices[presetIndexVoice];

                            if (newVoiceItem != null)
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for Character: {mapData.ToString(true)} from: {mapData.voiceItem} to: {newVoiceItem}", new EKEventId(0, TextSource.None));

                                mapData.voiceItem = newVoiceItem;
                                filteredNpcs = null;
                                this.Configuration.Save();
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }

                        ImGui.TableNextRow();
                    }

                    if (toBeRemoved != null)
                    {
                        FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, toBeRemoved.name);
                        Configuration.MappedNpcs.Remove(toBeRemoved);
                        filteredNpcs.Sort();
                        Configuration.Save();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawPlayerTab()
    {
        if (ImGui.BeginTabItem("Players"))
        {
            if (filteredPlayers == null)
            {
                filteredPlayers = Configuration.MappedPlayers;
                filteredPlayers.Sort();
            }

            if (ImGui.InputText($"Filter by player name##EKFilterPlayer", ref playerFilter, 40) || (playerFilter.Length > 0 && UpdatePlayerData))
            {
                filteredPlayers = Configuration.MappedPlayers.FindAll(b => b.name.ToLower().Contains(playerFilter.ToLower()));
                filteredPlayers.Sort();
                resetPlayerFilter = false;
                UpdatePlayerData = false;
            }
            else if ((!resetPlayerFilter && playerFilter.Length == 0) || UpdatePlayerData)
            {
                filteredPlayers = Configuration.MappedPlayers;
                filteredPlayers.Sort();
                resetPlayerFilter = true;
                UpdatePlayerData = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear mapped players##clearplayers"))
            {
                foreach (NpcMapData playerMapData in this.Configuration.MappedPlayers)
                {
                    FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, playerMapData.name);
                }
                this.Configuration.MappedPlayers.Clear();
                this.Configuration.Save();
            }

            if (ImGui.BeginChild("PlayerssChild"))
            {
                if (ImGui.BeginTable("Player Table##PlayerTable", 6))
                {
                    ImGui.TableSetupScrollFreeze(0, 1); // Make top row always visible
                    ImGui.TableSetupColumn("Mapping", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Saves", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.None, 250);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextRow();

                    NpcMapData toBeRemoved = null;
                    foreach (NpcMapData mapData in filteredPlayers)
                    {
                        if (!this.Configuration.VoicesAllOriginals && mapData.voiceItem.voiceName.ToLower().Contains(mapData.name.ToLower()))
                            continue;

                        if (!this.Configuration.VoicesAllBubbles && mapData.name.StartsWith("Bubble"))
                            continue;

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Trash.ToIconString()}##delplayer{mapData.ToString()}", new Vector2(25, 25), "Remove Player mapping", false, true))
                        {
                            toBeRemoved = mapData;
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.TrashAlt.ToIconString()}##delplayersaves{mapData.ToString()}", new Vector2(25, 25), "Remove Player saves", false, true))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, mapData.name);
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexGender = BackendVoiceHelper.GenderDisplay.IndexOf(p => p.Contains(mapData.gender.ToString()));
                        if (ImGui.Combo($"##EKCBoxPlayer{mapData.ToString(Configuration.VoicesAllRaces)}1", ref presetIndexGender, BackendVoiceHelper.GenderDisplay, BackendVoiceHelper.GenderDisplay.Length))
                        {
                            var newGender = BackendVoiceHelper.GenderArr[presetIndexGender];
                            if (newGender != mapData.gender)
                            {
                                if (Configuration.MappedPlayers.Contains(new NpcMapData(mapData.objectKind) { gender = newGender, race = mapData.race, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Gender for Character: {mapData.ToString(true)} from: {mapData.gender} to: {newGender}", new EKEventId(0, TextSource.None));

                                    mapData.gender = newGender;
                                    filteredPlayers = null;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Gender for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexRace = BackendVoiceHelper.RaceDisplay.IndexOf(p => p.Contains(mapData.race.ToString()));
                        if (ImGui.Combo($"##EKCBoxPlayer{mapData.ToString(Configuration.VoicesAllRaces)}2", ref presetIndexRace, BackendVoiceHelper.RaceDisplay, BackendVoiceHelper.RaceDisplay.Length))
                        {
                            var newRace = BackendVoiceHelper.RaceArr[presetIndexRace];
                            if (newRace != mapData.race)
                            {
                                if (Configuration.MappedPlayers.Contains(new NpcMapData(mapData.objectKind) { gender = mapData.gender, race = newRace, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Race for Character: {mapData.ToString(true)} from: {mapData.race} to: {newRace}", new EKEventId(0, TextSource.None));

                                    mapData.race = newRace;
                                    filteredPlayers = null;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Race for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(mapData.name);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var localVoices = (this.Configuration.VoicesAllOriginals ?
                                            (this.Configuration.VoicesAllGenders ?
                                                (this.Configuration.VoicesAllRaces ?
                                                    BackendVoiceHelper.Voices :
                                                    BackendVoiceHelper.FilteredVoicesAllGenders[mapData.race]
                                                ) :
                                                (this.Configuration.VoicesAllRaces ?
                                                    BackendVoiceHelper.FilteredVoicesAllRaces[mapData.gender] :
                                                    BackendVoiceHelper.FilteredVoices[mapData.gender][mapData.race]
                                                )
                                            ) :
                                            (this.Configuration.VoicesAllGenders ?
                                                (this.Configuration.VoicesAllRaces ?
                                                    BackendVoiceHelper.VoicesOriginal :
                                                    BackendVoiceHelper.FilteredVoicesAllGendersOriginal[mapData.race]
                                                ) :
                                                (this.Configuration.VoicesAllRaces ?
                                                    BackendVoiceHelper.FilteredVoicesAllRacesOriginal[mapData.gender] :
                                                    BackendVoiceHelper.FilteredVoicesOriginal[mapData.gender][mapData.race]
                                                )
                                            )
                                          );

                        var voicesDisplay = localVoices.Select(b => b.ToString()).ToArray();
                        var presetIndexVoice = localVoices.FindIndex(p => p.ToString().Contains(mapData.voiceItem?.ToString() ?? "Narrator"));
                        if (ImGui.Combo($"##EKCBoxPlayer{mapData.ToString(Configuration.VoicesAllRaces)}3", ref presetIndexVoice, voicesDisplay, voicesDisplay.Length))
                        {
                            var newVoiceItem = localVoices[presetIndexVoice];

                            if (newVoiceItem != null)
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for Character: {mapData.ToString(true)} from: {mapData.voiceItem} to: {newVoiceItem}", new EKEventId(0, TextSource.None));

                                mapData.voiceItem = newVoiceItem;
                                filteredPlayers.Sort();
                                this.Configuration.Save();
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }

                        ImGui.TableNextRow();
                    }

                    if (toBeRemoved != null)
                    {
                        FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, toBeRemoved.name);
                        Configuration.MappedPlayers.Remove(toBeRemoved);
                        filteredPlayers.Sort();
                        Configuration.Save();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawVoicesTab()
    {
        if (ImGui.BeginTabItem("Voices"))
        {
            if (ImGui.BeginChild("PlayerssChild"))
            {
                if (ImGui.BeginTable("Player Table##PlayerTable", 4))
                {
                    ImGui.TableSetupScrollFreeze(0, 1); // Make top row always visible
                    ImGui.TableSetupColumn("Test", ImGuiTableColumnFlags.None, 70f);
                    ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextRow();

                    var voices = BackendVoiceHelper.Voices;
                    foreach (var voice in voices)
                    {
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.ThumbsUp.ToIconString()}##testvoice{voice.ToString()}", new Vector2(25, 25), "Test Voice", false, true))
                        {
                            BackendTestVoice(voice);
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.TextUnformatted(voice.gender.ToString());
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.TextUnformatted(voice.race.ToString());
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.TextUnformatted(voice.voiceName);
                        ImGui.TableNextRow();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawPhoneticCorrections()
    {
        try
        {
            if (Configuration.PhoneticCorrections.Count == 0)
            {
                Configuration.PhoneticCorrections.Add(new PhoneticCorrection("C'mi", "Kami"));
                Configuration.PhoneticCorrections.Add(new PhoneticCorrection("/", "Schrägstrich"));
                Configuration.PhoneticCorrections.Sort();
                Configuration.Save();
            }


            if (ImGui.BeginChild("NpcsChild"))
            {
                if (ImGui.BeginTable("NPC Table##NPCTable", 5))
                {
                    ImGui.TableSetupScrollFreeze(0, 1); // Make top row always visible
                    ImGui.TableSetupColumn("Delete", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Original", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableSetupColumn("Corrected", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextRow();
                    PhoneticCorrection toBeRemoved = null;
                    foreach (PhoneticCorrection phoneticCorrection in Configuration.PhoneticCorrections)
                    {
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Trash.ToIconString()}##delphoncorr{phoneticCorrection.ToString()}", new Vector2(25, 25), "Remove phonetic correction", false, true))
                        {
                            toBeRemoved = phoneticCorrection;
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.InputText($"##origText{phoneticCorrection.ToString()}", ref phoneticCorrection.OriginalText, 25);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.InputText($"##correctText{phoneticCorrection.ToString()}", ref phoneticCorrection.CorrectedText, 25);

                        ImGui.TableNextRow();
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Plus.ToIconString()}##addphoncorr", new Vector2(25, 25), "Add phonetic correction", false, true))
                    {
                        if (!string.IsNullOrWhiteSpace(originalText) && !string.IsNullOrWhiteSpace(correctedText))
                        {
                            PhoneticCorrection newCorrection = new PhoneticCorrection(originalText, correctedText);
                            if (!Configuration.PhoneticCorrections.Contains(newCorrection))
                            {
                                Configuration.PhoneticCorrections.Add(newCorrection);
                                Configuration.PhoneticCorrections.Sort();
                                Configuration.Save();
                                originalText = "";
                                correctedText = "";
                            }
                        }
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    ImGui.InputText("##origText", ref originalText, 25);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    ImGui.InputText("##correctText", ref correctedText, 25);
                    ImGui.TableNextRow();

                    if (toBeRemoved != null)
                    {
                        Configuration.PhoneticCorrections.Remove(toBeRemoved);
                        Configuration.PhoneticCorrections.Sort();
                        Configuration.Save();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawLogs()
    {
        try
        {
            if (ImGui.BeginTabBar($"Logs##EKLogsTab"))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowGeneralInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowGeneralInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.None);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowGeneralDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowGeneralDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.None);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowGeneralErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowGeneralErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.None);
                        }
                        var jumpToBottom = this.Configuration.logConfig.GeneralJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.GeneralJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.GeneralLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.None, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.None);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.GeneralJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Dialogue"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowTalkInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowTalkInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonTalk);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowTalkDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowTalkDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonTalk);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowTalkErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowTalkErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonTalk);
                        }
                        var jumpToBottom = this.Configuration.logConfig.TalkJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.TalkJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.TalkLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonTalk, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonTalk);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.TalkJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Battle dialogue"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowBattleTalkInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowBattleTalkInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowBattleTalkDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowBattleTalkDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowBattleTalkErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowBattleTalkErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                        }
                        var jumpToBottom = this.Configuration.logConfig.BattleTalkJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.BattleTalkJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.BattleTalkLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonBattleTalk, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.BattleTalkJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Chat"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowChatInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowChatInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.Chat);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowChatDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowChatDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.Chat);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowChatErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowChatErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.Chat);
                        }
                        var jumpToBottom = this.Configuration.logConfig.ChatJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.ChatJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.ChatLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.Chat, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.Chat);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.ChatJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Bubbles"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowBubbleInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowBubbleInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBubble);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowBubbleDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowBubbleDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBubble);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowBubbleErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowBubbleErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBubble);
                        }
                        var jumpToBottom = this.Configuration.logConfig.BubbleJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.BubbleJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.BubbleLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonBubble, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonBubble);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.BubbleJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Player choice in cutscenes"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowCutSceneSelectStringInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowCutSceneSelectStringInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowCutSceneSelectStringDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowCutSceneSelectStringDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowCutSceneSelectStringErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowCutSceneSelectStringErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                        }
                        var jumpToBottom = this.Configuration.logConfig.CutSceneSelectStringJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.CutSceneSelectStringJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.CutSceneSelectStringLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonCutSceneSelectString, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.CutSceneSelectStringJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Player choice"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowSelectStringInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowSelectStringInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonSelectString);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowSelectStringDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowSelectStringDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonSelectString);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowSelectStringErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowSelectStringErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonSelectString);
                        }
                        var jumpToBottom = this.Configuration.logConfig.SelectStringJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.SelectStringJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.SelectStringLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonSelectString, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonSelectString);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.SelectStringJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None), false);
        }
    }
}
