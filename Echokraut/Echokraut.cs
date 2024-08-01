using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Echokraut.Windows;
using Dalamud.Game;
using Echokraut.Enums;
using System;
using Echokraut.TextToTalk.Utils;
using Echokraut.DataClasses;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using GameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using Lumina.Excel.GeneratedSheets;
using Echokraut.Helper;
using Echokraut.Extensions;
using Echokraut.Utils;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Forms;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons;
using static Anamnesis.GUI.Views.FileBrowserView;

namespace Echokraut;

public partial class Echokraut : IDalamudPlugin
{

    private IDalamudPluginInterface PluginInterface { get; init; }
    private IPluginLog Log { get; init; }
    private ICommandManager CommandManager { get; init; }
    private IFramework Framework { get; init; }
    private IClientState ClientState { get; init; }
    private ICondition Condition { get; init; }
    private IObjectTable ObjectTable { get; init; }
    private IGameGui GameGui { get; init; }
    private IGameConfig GameConfig { get; init; }
    private IDataManager DataManager { get; init; }
    private IChatGui ChatGui { get; init; }
    private Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Echokraut");
    private ConfigWindow ConfigWindow { get; init; }

    #region TextToTalk Base
    internal readonly LipSyncHelper lipSyncHelper;
    internal readonly SoundHelper soundHelper;
    internal readonly AddonTalkHelper addonTalkHelper;
    internal readonly AddonBattleTalkHelper addonBattleTalkHelper;
    internal readonly AddonSelectStringHelper addonSelectStringHelper;
    internal readonly AddonCutSceneSelectStringHelper addonCutSceneSelectStringHelper;
    internal readonly AddonBubbleHelper addonBubbleHelper;
    internal readonly ChatTalkHelper chatTalkHelper;
    #endregion

    public Echokraut(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        ICondition condition,
        IObjectTable objectTable,
        IDataManager dataManager,
        IChatGui chatGui,
        IGameGui gameGui,
        ISigScanner sigScanner,
        IGameInteropProvider gameInterop,
        IGameConfig gameConfig)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        Framework = framework;
        ClientState = clientState;
        Condition = condition;
        ObjectTable = objectTable;
        DataManager = dataManager;
        ChatGui = chatGui;
        GameGui = gameGui;
        GameConfig = gameConfig;
        Log = log;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        LogHelper.Setup(log, Configuration);
        BackendHelper.Setup(Configuration, clientState, this, Configuration.BackendSelection);
        VoiceMapHelper.Setup(this.ClientState.ClientLanguage);
        VolumeHelper.Setup(gameConfig);
        DataHelper.Setup(Configuration, this.ClientState, this.DataManager);
        ECommonsMain.Init(pluginInterface, this, ECommons.Module.All);
        this.ConfigWindow = new ConfigWindow(this, Configuration, this.ClientState);
        this.lipSyncHelper = new LipSyncHelper(this.ClientState, this.ObjectTable, this.Configuration, new EKEventId(0, Enums.TextSource.None));
        this.addonTalkHelper = new AddonTalkHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonBattleTalkHelper = new AddonBattleTalkHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.soundHelper = new SoundHelper(this.addonTalkHelper, this.addonBattleTalkHelper, sigScanner, gameInterop);
        this.addonSelectStringHelper = new AddonSelectStringHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonCutSceneSelectStringHelper = new AddonCutSceneSelectStringHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonBubbleHelper = new AddonBubbleHelper(this, this.DataManager, this.Framework, this.ObjectTable,sigScanner, gameInterop, this.ClientState, this.Configuration);
        this.chatTalkHelper = new ChatTalkHelper(this, this.Configuration, chatGui, objectTable, clientState);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler("/eksettings", new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the configuration window"
        });
        CommandManager.AddHandler("/ek", new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the configuration window"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
    }

    public void Cancel(EKEventId eventId)
    {
        if (Configuration.CancelSpeechOnTextAdvance)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Stopping Inference", eventId);
            BackendHelper.OnCancel(eventId);
        }
    }

    public void StopLipSync(EKEventId eventId)
    {
        lipSyncHelper.StopLipSync(eventId);
    }

    public unsafe void Say(EKEventId eventId, GameObject? speaker, SeString speakerName, string textValue)
    {
        try
        {
            var source = eventId.textSource;
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Preparing for Inference: {speakerName} - {textValue} - {source}", eventId);
            // Run a preprocessing pipeline to clean the text for the speech synthesizer
            var cleanText = FunctionalUtils.Pipe(
                textValue,
                TalkUtils.StripAngleBracketedText,
                TalkUtils.ReplaceSsmlTokens,
                TalkUtils.NormalizePunctuation,
                t => this.Configuration.RemoveStutters ? TalkUtils.RemoveStutters(t) : t,
                x => x.Trim());

            cleanText = TalkUtils.ReplaceRomanNumbers(eventId, cleanText);
            cleanText = TalkUtils.ReplacePhonetics(cleanText, Configuration.PhoneticCorrections);
            cleanText = DataHelper.AnalyzeAndImproveText(cleanText);

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Cleantext: {cleanText}", eventId);
            // Ensure that the result is clean; ignore it otherwise
            if (!cleanText.Any() || !TalkUtils.IsSpeakable(cleanText) || cleanText.Length == 0)
            {
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Text not speakable: {cleanText}", eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                return;
            }

            // Some characters have emdashes in their names, which should be treated
            // as hyphens for the sake of the plugin.
            var cleanSpeakerName = TalkUtils.NormalizePunctuation(speakerName.TextValue);

            var objectKind = speaker == null ? ObjectKind.None : speaker.ObjectKind;
            NpcMapData npcData = new NpcMapData(objectKind);
            // Get the speaker's race if it exists.
            var raceStr = "";
            npcData.race = GetSpeakerRace(eventId, speaker, out raceStr);
            npcData.raceStr = raceStr;
            npcData.gender = CharacterGenderUtils.GetCharacterGender(eventId, speaker);
            npcData.name = DataHelper.CleanUpName(cleanSpeakerName);

            if (npcData.objectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                npcData.name = VoiceMapHelper.GetNpcName(npcData.name);

            if (npcData.name == "PLAYER")
                npcData.name = this.ClientState.LocalPlayer?.Name.ToString() ?? "PLAYER";
            else if (string.IsNullOrWhiteSpace(npcData.name) && source == TextSource.AddonBubble)
                npcData.name = GetBubbleName(speaker, cleanText);

            var resNpcData = DataHelper.GetAddCharacterMapData(npcData, eventId);
            if (resNpcData != null && resNpcData.race == NpcRaces.Unknown && npcData.raceStr != NpcRaces.Unknown.ToString())
            {
                resNpcData.race = npcData.race;
                resNpcData.raceStr = npcData.raceStr;
                Configuration.Save();
            }

            npcData = resNpcData;

            if (npcData.objectKind != objectKind && objectKind != ObjectKind.None)
            {
                npcData.objectKind = objectKind;
                Configuration.Save();
            }

            switch (source)
            {
                case TextSource.AddonBubble:
                    if (!npcData.hasBubbles)
                        npcData.hasBubbles = true;

                    if (npcData.mutedBubble)
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Bubble is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case TextSource.AddonBattleTalk:
                case TextSource.AddonTalk:
                    if (npcData.muted)
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Npc is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case TextSource.AddonCutSceneSelectString:
                case TextSource.AddonSelectString:
                case TextSource.Chat:
                    if (npcData.mutedBubble)
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Player is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
            }
            // Say the thing
            var voiceMessage = new VoiceMessage
            {
                pActor = speaker,
                Source = source,
                Speaker = npcData,
                Text = cleanText,
                Language = this.ClientState.ClientLanguage,
                eventId = eventId
            };
            var volume = VolumeHelper.GetVoiceVolume(eventId);

            if (volume > 0)
                BackendHelper.OnSay(voiceMessage, volume);
            else
            {
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. Volume is 0", eventId);

                if (voiceMessage.Speaker.voiceItem == null)
                {
                    LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Getting voice since not set.", eventId);
                    BackendHelper.GetVoiceOrRandom(eventId, voiceMessage.Speaker);
                }
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while starting voice inference: {ex}", eventId);
        }
    }

    private unsafe NpcRaces GetSpeakerRace(EKEventId eventId, GameObject? speaker, out string raceStr)
    {
        var race = this.DataManager.GetExcelSheet<Race>();
        var raceEnum = NpcRaces.Unknown;

        try
        {
            if (race is null || speaker is null || speaker.Address == nint.Zero)
            {
                raceStr = raceEnum.ToString();
                return raceEnum;
            }

            var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
            var speakerRace = charaStruct->DrawData.CustomizeData.Race;
            var row = race.GetRow(speakerRace);

            if (!(row is null))
            {
                raceStr = DataHelper.GetRaceEng(row.Masculine.RawString);
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found Race: {raceStr}", eventId);
                if (!Enum.TryParse<NpcRaces>(raceStr.Replace(" ", ""), out raceEnum))
                {
                    var modelData = charaStruct->CharacterData.ModelSkeletonId;
                    var modelData2 = charaStruct->CharacterData.ModelSkeletonId_2;

                    var activeData = modelData;
                    if (activeData == -1)
                        activeData = modelData2;

                    var activeNpcRace = NpcRaces.Unknown;
                    try
                    {
                        if (NpcRacesHelper.ModelsToRaceMap.TryGetValue(activeData, out activeNpcRace))
                            raceEnum = activeNpcRace;
                        else
                        {
                            raceEnum = NpcRaces.Unknown;
                        }
                    }
                    catch (Exception ex)
                    {
                        raceEnum = NpcRaces.Unknown;
                    }
                    raceStr = activeData.ToString();
                }
            }

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Determined Race: {raceEnum}", eventId);
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while determining race: {ex}", eventId);
        }

        raceStr = raceEnum.ToString();
        return raceEnum;
    }

    private unsafe string GetBubbleName(GameObject? speaker, string text)
    {
        var territory = DataHelper.GetTerritory();
        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
        var modelData = charaStruct->CharacterData.ModelSkeletonId;
        var modelData2 = charaStruct->CharacterData.ModelSkeletonId_2;

        var activeData = modelData;
        if (activeData == -1)
            activeData = modelData2;

        return $"BB-{territory.PlaceName.Value.Name.ToString()}-{activeData}-{text.Substring(0, 20)}";
    }

    public void Dispose()
    {
        PlayingHelper.Dispose(); 
        ECommonsMain.Dispose();
        this.soundHelper.Dispose();
        this.addonTalkHelper.Dispose();
        this.addonBattleTalkHelper.Dispose();
        this.addonCutSceneSelectStringHelper.Dispose();
        this.addonSelectStringHelper.Dispose();
        this.addonBubbleHelper.Dispose();
        this.chatTalkHelper.Dispose();

        this.Configuration.Save();
        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();

        CommandManager.RemoveHandler("/eksettings");
        CommandManager.RemoveHandler("/ek");
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our config ui
        ToggleConfigUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
}
