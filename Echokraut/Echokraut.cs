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
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using GameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using Echokraut.Helper;
using Echokraut.Utils;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.UI;

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
        IGameConfig gameConfig,
        IAddonLifecycle addonLifecycle)
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
        DataHelper.Setup(Configuration, this.ClientState, this.DataManager);
        BackendHelper.Setup(Configuration, clientState, this, Configuration.BackendSelection);
        VoiceMapHelper.Setup(this.ClientState.ClientLanguage);
        NpcGenderRacesHelper.Setup();
        VolumeHelper.Setup(gameConfig);
        ECommonsMain.Init(pluginInterface, this, ECommons.Module.All);
        this.ConfigWindow = new ConfigWindow(this, Configuration, this.ClientState);
        this.lipSyncHelper = new LipSyncHelper(this.ClientState, this.ObjectTable, this.Configuration, new EKEventId(0, Enums.TextSource.None));
        this.addonTalkHelper = new AddonTalkHelper(this, addonLifecycle, this.ClientState, this.ObjectTable, this.Configuration);
        this.addonBattleTalkHelper = new AddonBattleTalkHelper(this, addonLifecycle, this.ClientState, this.ObjectTable, this.Configuration);
        this.addonSelectStringHelper = new AddonSelectStringHelper(this, addonLifecycle, this.ClientState, this.ObjectTable, condition, this.Configuration);
        this.addonCutSceneSelectStringHelper = new AddonCutSceneSelectStringHelper(this, addonLifecycle, this.ClientState, this.ObjectTable, this.Configuration);
        this.addonBubbleHelper = new AddonBubbleHelper(this, this.DataManager, this.Framework, this.ObjectTable,sigScanner, gameInterop, this.ClientState, this.Configuration);
        this.chatTalkHelper = new ChatTalkHelper(this, this.Configuration, chatGui, objectTable, clientState);
        this.soundHelper = new SoundHelper(this.addonTalkHelper, this.addonBattleTalkHelper, this.addonBubbleHelper, sigScanner, gameInterop);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler("/ekt", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles Echokraut"
        });
        CommandManager.AddHandler("/ekttalk", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles dialogue voicing"
        });
        CommandManager.AddHandler("/ektbtalk", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles battle dialogue voicing"
        });
        CommandManager.AddHandler("/ektbubble", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles bubble voicing"
        });
        CommandManager.AddHandler("/ektchat", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles chat voicing"
        });
        CommandManager.AddHandler("/ektcutschoice", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles cutscene choice voicing"
        });
        CommandManager.AddHandler("/ektchoice", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles choice voicing"
        });
        CommandManager.AddHandler("/ek", new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the configuration window"
        });
        CommandManager.AddHandler("/ekid", new CommandInfo(OnCommand)
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
            StopLipSync(eventId);
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
            npcData.race = CharacterGenderRaceUtils.GetSpeakerRace(this.DataManager, eventId, speaker, out raceStr, out var modelId);
            npcData.raceStr = raceStr;
            npcData.gender = CharacterGenderRaceUtils.GetCharacterGender(this.DataManager, eventId, speaker, npcData.race, out var modelBody);
            npcData.name = DataHelper.CleanUpName(cleanSpeakerName);

            if (npcData.objectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                npcData.name = VoiceMapHelper.GetNpcName(npcData.name);

            if (npcData.name == "PLAYER")
                npcData.name = this.ClientState.LocalPlayer?.Name.ToString() ?? "PLAYER";
            else if (string.IsNullOrWhiteSpace(npcData.name) && source == TextSource.AddonBubble)
                npcData.name = GetBubbleName(speaker, cleanText);

            var resNpcData = DataHelper.GetAddCharacterMapData(npcData, eventId);
            Configuration.Save();

            npcData = resNpcData;

            if (npcData.objectKind != objectKind && objectKind != ObjectKind.None)
            {
                npcData.objectKind = objectKind;
                Configuration.Save();
            }

            var language = this.ClientState.ClientLanguage;
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
                    if (source == TextSource.Chat)
                        language = DataHelper.GetTextLanguage(cleanText, eventId);
                    break;
            }
            // Say the thing
            var voiceMessage = new VoiceMessage
            {
                pActor = speaker,
                Source = source,
                Speaker = npcData,
                Text = cleanText,
                Language = language,
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

    private unsafe string GetBubbleName(GameObject? speaker, string text)
    {
        var territory = DataHelper.GetTerritory();
        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
        var modelData = charaStruct->CharacterData.ModelSkeletonId;
        var modelData2 = charaStruct->CharacterData.ModelSkeletonId_2;

        var activeData = modelData;
        if (activeData == -1)
            activeData = modelData2;

        text = FileHelper.VoiceMessageToFileName(text);
        var textSubstring = text.Length > 20 ? text.Substring(0, 20) : text;
        return $"BB-{territory.PlaceName.Value.Name.ToString()}-{activeData}-{textSubstring}";
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
        CommandManager.RemoveHandler("/ek");
        CommandManager.RemoveHandler("/ekt");
        CommandManager.RemoveHandler("/ekid");
        CommandManager.RemoveHandler("/ekttalk");
        CommandManager.RemoveHandler("/ektbtalk");
        CommandManager.RemoveHandler("/ektbubble");
        CommandManager.RemoveHandler("/ektcutschoice");
        CommandManager.RemoveHandler("/ektchoice");
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our config ui

        switch (command)
        {
            case "/ek":
                ToggleConfigUI();
                break;
            case "/ekid":
                DataHelper.PrintTargetInfo(ChatGui, ClientState, DataManager);
                break;
            case "/ekt":
                Configuration.Enabled = !Configuration.Enabled;
                Configuration.Save();
                break;
            case "/ekttalk":
                Configuration.VoiceDialogue = !Configuration.VoiceDialogue;
                Configuration.Save();
                break;
            case "/ektbtalk":
                Configuration.VoiceBattleDialogue = !Configuration.VoiceBattleDialogue;
                Configuration.Save();
                break;
            case "/ektbubble":
                Configuration.VoiceBubble = !Configuration.VoiceBubble;
                Configuration.Save();
                break;
            case "/ektchat":
                Configuration.VoiceChat = !Configuration.VoiceChat;
                Configuration.Save();
                break;
            case "/ektcutschoice":
                Configuration.VoicePlayerChoicesCutscene = !Configuration.VoicePlayerChoicesCutscene;
                Configuration.Save();
                break;
            case "/ektchoice":
                Configuration.VoicePlayerChoices = !Configuration.VoicePlayerChoices;
                Configuration.Save();
                break;
        }

        LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"New Command triggered: {command}", new EKEventId(0, TextSource.None));
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
}
