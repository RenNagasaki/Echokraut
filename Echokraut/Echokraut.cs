using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Echokraut.Windows;
using Dalamud.Game;
using Echokraut.Enums;
using System;
using Echokraut.DataClasses;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using GameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.Helper.Addons;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.API;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;

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
        pluginInterface.UiBuilder.DisableCutsceneUiHide = !Configuration.HideUiInCutscenes;
        this.ConfigWindow = new ConfigWindow(this, Configuration, this.ClientState, this.PluginInterface);

        LogHelper.Setup(log, Configuration);
        JsonLoaderHelper.Setup(this.ClientState.ClientLanguage);
        DetectLanguageHelper.Setup(Configuration, clientState);
        NpcDataHelper.Setup(Configuration);
        LuminaHelper.Setup(clientState, dataManager);
        BackendHelper.Setup(this, Configuration, clientState, framework, Configuration.BackendSelection);
        VolumeHelper.Setup(gameConfig);
        CommandHelper.Setup(Configuration, chatGui, clientState, dataManager, commandManager, condition, ConfigWindow);
        this.lipSyncHelper = new LipSyncHelper(framework, condition, clientState, objectTable, Configuration, new EKEventId(0, Enums.TextSource.None));
        this.addonTalkHelper = new AddonTalkHelper(this, condition, addonLifecycle, clientState, objectTable, Configuration);
        this.addonBattleTalkHelper = new AddonBattleTalkHelper(this, addonLifecycle, clientState, objectTable, Configuration);
        this.addonSelectStringHelper = new AddonSelectStringHelper(this, addonLifecycle, clientState, objectTable, condition, Configuration);
        this.addonCutSceneSelectStringHelper = new AddonCutSceneSelectStringHelper(this, addonLifecycle, clientState, objectTable, Configuration);
        this.addonBubbleHelper = new AddonBubbleHelper(this, condition, dataManager, framework, objectTable,sigScanner, gameInterop, clientState, Configuration);
        this.chatTalkHelper = new ChatTalkHelper(this, Configuration, chatGui, objectTable, clientState);
        this.soundHelper = new SoundHelper(this.addonTalkHelper, this.addonBattleTalkHelper, this.addonBubbleHelper, sigScanner, gameInterop, dataManager);

        WindowSystem.AddWindow(ConfigWindow);

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += CommandHelper.ToggleConfigUI;
    }

    public void Cancel(EKEventId eventId)
    {
        if (Configuration.CancelSpeechOnTextAdvance)
        {
            BackendHelper.OnCancel(eventId);
            StopLipSync(eventId);
        }
    }

    public void StopLipSync(EKEventId eventId)
    {
        lipSyncHelper.StopLipSync(eventId);
    }

    public async void Say(EKEventId eventId, GameObject? speaker, SeString speakerName, string textValue)
    {
        try
        {
            var source = eventId.textSource;
            var language = this.ClientState.ClientLanguage;
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Preparing for Inference: {speakerName} - {textValue} - {source}", eventId);

            var cleanText = TalkTextHelper.StripAngleBracketedText(textValue);
            cleanText = TalkTextHelper.ReplaceSsmlTokens(cleanText);
            cleanText = TalkTextHelper.NormalizePunctuation(cleanText);
            cleanText = this.Configuration.RemoveStutters ? TalkTextHelper.RemoveStutters(cleanText) : cleanText;

            if (source == TextSource.Chat)
            {
                if (!Configuration.VoiceChatWithout3D && speaker == null)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Player is not on the same map: {speakerName.TextValue}. Can't voice", eventId);
                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                    return;
                }
                else if (Configuration.VoiceChatWithout3D)
                    speaker = ClientState.LocalPlayer;

                language = await DetectLanguageHelper.GetTextLanguage(cleanText, eventId);
            }

            cleanText = TalkTextHelper.ReplaceDate(eventId, cleanText, language);
            cleanText = TalkTextHelper.ReplaceTime(eventId, cleanText, language);
            cleanText = TalkTextHelper.ReplaceRomanNumbers(eventId, cleanText);
            cleanText = TalkTextHelper.ReplaceCurrency(eventId, cleanText);
            cleanText = TalkTextHelper.ReplaceIntWithVerbal(eventId, cleanText, language);
            cleanText = TalkTextHelper.ReplacePhonetics(cleanText, Configuration.PhoneticCorrections);
            cleanText = TalkTextHelper.AnalyzeAndImproveText(cleanText);

            if (source == TextSource.Chat)
                cleanText = TalkTextHelper.ReplaceEmoticons(eventId, cleanText);
            cleanText = cleanText.Trim();

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Cleantext: {cleanText}", eventId);
            // Ensure that the result is clean; ignore it otherwise
            if (!cleanText.Any() || !TalkTextHelper.IsSpeakable(cleanText) || cleanText.Length == 0)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Text not speakable: {cleanText}", eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                return;
            }

            // Some characters have emdashes in their names, which should be treated
            // as hyphens for the sake of the plugin.
            var cleanSpeakerName = TalkTextHelper.NormalizePunctuation(speakerName.TextValue);

            var objectKind = speaker == null ? ObjectKind.None : speaker.ObjectKind;
            NpcMapData npcData = new NpcMapData(objectKind);
            // Get the speaker's race if it exists.
            var raceStr = "";
            npcData.Race = CharacterDataHelper.GetSpeakerRace(this.DataManager, eventId, speaker, out raceStr, out var modelId);
            npcData.RaceStr = raceStr;
            npcData.Gender = CharacterDataHelper.GetCharacterGender(this.DataManager, eventId, speaker, npcData.Race, out var modelBody);
            npcData.Name = TalkTextHelper.CleanUpName(cleanSpeakerName);

            if (npcData.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                npcData.Name = JsonLoaderHelper.GetNpcName(npcData.Name);

            if (npcData.Name == "PLAYER")
                npcData.Name = this.ClientState.LocalPlayer?.Name.ToString() ?? "PLAYER";
            else if (string.IsNullOrWhiteSpace(npcData.Name) && source == TextSource.AddonBubble)
                npcData.Name = TalkTextHelper.GetBubbleName(speaker, cleanText);

            var resNpcData = NpcDataHelper.GetAddCharacterMapData(npcData, eventId);
            Configuration.Save();

            npcData = resNpcData;
            
            if (speaker != null && (source == TextSource.AddonBubble || source == TextSource.AddonTalk || source == TextSource.AddonBattleTalk))
            {
                npcData.IsChild = LuminaHelper.GetENpcBase(speaker.DataId)?.BodyType == 4;
                Configuration.Save();
            }

            if (npcData.ObjectKind != objectKind && objectKind != ObjectKind.None)
            {
                npcData.ObjectKind = objectKind;
                Configuration.Save();
            }

            var npcVolume = npcData.Volume;
            switch (source)
            {
                case TextSource.AddonBubble:
                    if (!npcData.HasBubbles)
                        npcData.HasBubbles = true;

                    npcVolume = npcData.VolumeBubble;
                    if (npcData.VolumeBubble == 0f || !npcData.IsEnabledBubble)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Bubble is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case TextSource.AddonBattleTalk:
                case TextSource.AddonTalk:
                    if (npcData.Volume == 0f || !npcData.IsEnabled)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Npc is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case TextSource.AddonCutsceneSelectString:
                case TextSource.AddonSelectString:
                case TextSource.Chat:
                    if (npcData.Volume == 0f || !npcData.IsEnabled)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Player is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
            }

            if (npcData.Voice.Volume == 0f)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Voice is muted: {npcData.ToString()}", eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                return;
            }

            var voiceMessage = new VoiceMessage
            {
                pActor = speaker,
                Source = source,
                Speaker = npcData,
                Text = cleanText,
                Language = language,
                eventId = eventId
            };
            var volume = VolumeHelper.GetVoiceVolume(eventId) * npcData.Voice.Volume * npcVolume;

            if (volume > 0)
                BackendHelper.OnSay(voiceMessage, volume);
            else
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. Volume is 0", eventId);

                if (voiceMessage.Speaker.Voice == null)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Getting voice since not set.", eventId);
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

    public void Dispose()
    {
        DetectLanguageHelper.Dispose();
        PlayingHelper.Dispose();
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
        CommandHelper.Dispose();
    }

    private void DrawUI() => WindowSystem.Draw();
}
