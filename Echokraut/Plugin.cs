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
using Dalamud.IoC;
using Echokraut.Helper.Addons;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.API;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Echokraut;

public partial class Plugin : IDalamudPlugin
{

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    internal static Configuration Configuration { get; private set; } = null!;
    internal static ConfigWindow ConfigWindow { get; private set; } = null!;
    internal static AlltalkInstanceWindow AlltalkInstanceWindow { get; private set; } = null!;
    internal static FirstTimeWindow FirstTimeWindow { get; private set; } = null!;
    internal static DialogExtraOptionsWindow DialogExtraOptionsWindow { get; private set; } = null!;

    internal static LipSyncHelper LipSyncHelper{ get; private set; } = null!;
    internal static SoundHelper SoundHelper{ get; private set; } = null!;
    internal static AddonTalkHelper AddonTalkHelper{ get; private set; } = null!;
    internal static AddonBattleTalkHelper AddonBattleTalkHelper{ get; private set; } = null!;
    internal static AddonSelectStringHelper AddonSelectStringHelper{ get; private set; } = null!;
    internal static AddonCutSceneSelectStringHelper AddonCutSceneSelectStringHelper{ get; private set; } = null!;
    internal static AddonBubbleHelper AddonBubbleHelper{ get; private set; } = null!;
    internal static ChatTalkHelper ChatTalkHelper{ get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("Echokraut");

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IPlayerState playerState,
        ICondition condition,
        IObjectTable objectTable,
        IDataManager dataManager,
        IChatGui chatGui,
        IGameGui gameGui,
        ISigScanner sigScanner,
        IGameInteropProvider gameInteropProvider,
        IGameConfig gameConfig,
        IAddonLifecycle addonLifecycle)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        Framework = framework;
        ClientState = clientState;
        PlayerState = playerState;
        Condition = condition;
        ObjectTable = objectTable;
        DataManager = dataManager;
        ChatGui = chatGui;
        GameGui = gameGui;
        GameConfig = gameConfig;
        SigScanner = sigScanner;
        GameInteropProvider = gameInteropProvider;
        AddonLifecycle = addonLifecycle;
        Log = log;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        pluginInterface.UiBuilder.DisableCutsceneUiHide = !Configuration.HideUiInCutscenes;
        ConfigWindow = new ConfigWindow();
        AlltalkInstanceWindow = new AlltalkInstanceWindow();
        FirstTimeWindow = new FirstTimeWindow();
        DialogExtraOptionsWindow = new DialogExtraOptionsWindow();

        LogHelper.Initialize(log);
        JsonLoaderHelper.Initialize(ClientState.ClientLanguage);
        DetectLanguageHelper.Initialize();
        BackendHelper.Initialize(Configuration.BackendSelection);
        CommandHelper.Initialize();
        AlltalkInstanceHelper.Initialize();
        LipSyncHelper = new LipSyncHelper();
        AddonTalkHelper = new AddonTalkHelper();
        AddonBattleTalkHelper = new AddonBattleTalkHelper();
        AddonSelectStringHelper = new AddonSelectStringHelper();
        AddonCutSceneSelectStringHelper = new AddonCutSceneSelectStringHelper();
        AddonBubbleHelper = new AddonBubbleHelper();
        ChatTalkHelper = new ChatTalkHelper();
        SoundHelper = new SoundHelper();

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(AlltalkInstanceWindow);
        WindowSystem.AddWindow(FirstTimeWindow);
        WindowSystem.AddWindow(DialogExtraOptionsWindow);

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += CommandHelper.ToggleConfigUi;
        ClientState.Login += OnLogin;

        if (Configuration.FirstTime && !FirstTimeWindow.IsOpen && ClientState.IsLoggedIn)
            CommandHelper.ToggleFirstTimeUi();

        if (!Configuration.FirstTime && ClientState.IsLoggedIn && Configuration.Alltalk.LocalInstall && Configuration.Alltalk.LocalInstance && Configuration.Alltalk.AutoStartLocalInstance)
            AlltalkInstanceHelper.StartInstance();
            
        if (Configuration.GoogleDriveDownloadPeriodically)
            GoogleDriveHelper.StartSync();
    }

    private void OnLogin()
    {
        try
        {
            if (Configuration.GoogleDriveDownload)
                GoogleDriveHelper.DownloadFolder(Configuration.LocalSaveLocation, Configuration.GoogleDriveShareLink);
            
            if (Configuration.FirstTime && !FirstTimeWindow.IsOpen)
                CommandHelper.ToggleFirstTimeUi();

            if (!Configuration.FirstTime && Configuration.Alltalk.LocalInstall && Configuration.Alltalk.LocalInstance && !AlltalkInstanceHelper.InstanceRunning && !AlltalkInstanceHelper.InstanceStarting)
                AlltalkInstanceHelper.StartInstance();
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while starting voice inference: {e}", new EKEventId(0, TextSource.None));
        }
    }

    public static void CancelAll(EKEventId ekEventId)
    {
        BackendHelper.OnCancelAll();
    }

    public static void Cancel(VoiceMessage? message)
    {
        if (message != null)
        {
            StopLipSync(message);
            BackendHelper.OnCancel(message);
        }
    }

    public static void Pause(VoiceMessage message)
    {
        BackendHelper.OnPause(message);
    }

    public static void Resume(VoiceMessage message)
    {
        BackendHelper.OnResume(message);
    }

    public static void StopLipSync(VoiceMessage message)
    {
        LipSyncHelper.TryStopLipSync(message);
    }

    public static async void Say(EKEventId eventId, GameObject? speaker, SeString speakerName, string textValue)
    {
        try
        {
            var onlyRequest = false;
            if (!BackendHelper.IsBackendAvailable())
            {
                if (Configuration.GoogleDriveRequestVoiceLine)
                    onlyRequest = true;
                else 
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"No backend available yet, skipping!", eventId);
                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                    PlayingHelper.RecreationStarted = false;
                    return;
                }
            }

            var source = eventId.textSource;
            var language = ClientState.ClientLanguage;
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Preparing for Inference: {speakerName} - {textValue} - {source}", eventId);

            var originalText = textValue;
            var cleanText = TalkTextHelper.StripAngleBracketedText(textValue);
            cleanText = TalkTextHelper.ReplaceSsmlTokens(cleanText);
            cleanText = TalkTextHelper.NormalizePunctuation(cleanText);
            cleanText = Configuration.RemoveStutters ? TalkTextHelper.RemoveStutters(cleanText) : cleanText;

            if (source == TextSource.Chat)
            {
                if ((Configuration.VoiceChatIn3D && speaker == null))
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Player is not on the same map: {speakerName.TextValue}. Can't voice", eventId);
                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                    PlayingHelper.RecreationStarted = false;
                    return;
                }

                if (!Configuration.VoiceChatIn3D)
                    speaker = DalamudHelper.LocalPlayer;

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
                PlayingHelper.RecreationStarted = false;
                return;
            }

            // Some characters have emdashes in their names, which should be treated
            // as hyphens for the sake of the plugin.
            var cleanSpeakerName = TalkTextHelper.NormalizePunctuation(speakerName.TextValue);

            var objectKind = speaker == null ? ObjectKind.None : speaker.ObjectKind;
            NpcMapData npcData = new NpcMapData(objectKind);
            // Get the speaker's race if it exists.
            var raceStr = "";
            npcData.Race = CharacterDataHelper.GetSpeakerRace(eventId, speaker, out raceStr, out var modelId);
            npcData.RaceStr = raceStr;
            npcData.Gender = CharacterDataHelper.GetCharacterGender(eventId, speaker, npcData.Race, out var modelBody);
            npcData.Name = TalkTextHelper.CleanUpName(cleanSpeakerName);

            if (npcData.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                npcData.Name = JsonLoaderHelper.GetNpcName(npcData.Name);

            if (npcData.Name == "PLAYER")
                npcData.Name = DalamudHelper.LocalPlayer?.Name.ToString() ?? "PLAYER";
            else if (string.IsNullOrWhiteSpace(npcData.Name) && source == TextSource.AddonBubble)
                npcData.Name = TalkTextHelper.GetBubbleName(speaker, cleanText);

            var resNpcData = NpcDataHelper.GetAddCharacterMapData(npcData, eventId);
            Configuration.Save();

            npcData = resNpcData;
            
            if (speaker != null && (source == TextSource.AddonBubble || source == TextSource.AddonTalk))
            {
                npcData.IsChild = LuminaHelper.GetENpcBase(speaker.DataId, eventId)?.BodyType == 4;
                Configuration.Save();
            }

            if (npcData.ObjectKind != objectKind && objectKind != ObjectKind.None)
            {
                npcData.ObjectKind = objectKind;
                Configuration.Save();
            }

            var npcVolume = npcData.Volume;
            var is3d = false;
            switch (source)
            {
                case TextSource.AddonBubble:
                    is3d = true;
                    if (!npcData.HasBubbles)
                        npcData.HasBubbles = true;

                    npcVolume = npcData.VolumeBubble;
                    if (npcData.VolumeBubble == 0f || !npcData.IsEnabledBubble)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Bubble is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        PlayingHelper.RecreationStarted = false;
                        return;
                    }
                    break;
                case TextSource.AddonBattleTalk:
                case TextSource.AddonTalk:
                    if (source == TextSource.AddonTalk)
                        is3d = Configuration.VoiceDialogueIn3D;
                    if (npcData.Volume == 0f || !npcData.IsEnabled)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Npc is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        PlayingHelper.RecreationStarted = false;
                        return;
                    }
                    break;
                case TextSource.AddonCutsceneSelectString:
                case TextSource.AddonSelectString:
                case TextSource.Chat:
                    if (onlyRequest)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Can't request Chat and Player Selections: {npcData.ToString()}",
                                       eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }

                    if (source == TextSource.Chat)
                        is3d = Configuration.VoiceChatIn3D;
                    if (npcData.Volume == 0f || !npcData.IsEnabled)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Player is muted: {npcData.ToString()}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        PlayingHelper.RecreationStarted = false;
                        return;
                    }
                    break;
            }


            if (npcData.Voice == null && !onlyRequest && !Configuration.Alltalk.NoInstance)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Getting voice since not set.", eventId);
                BackendHelper.GetVoiceOrRandom(eventId, npcData);
            }

            if (npcData.Voice == null && !onlyRequest && !Configuration.Alltalk.NoInstance)
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. No Voice set.", eventId);


            if (npcData.Voice != null && npcData.Voice.Volume == 0f && !onlyRequest && !Configuration.Alltalk.NoInstance)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Voice is muted: {npcData.ToString()}", eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                PlayingHelper.RecreationStarted = false;
                return;
            }
            
            var volume = VolumeHelper.GetVoiceVolume(eventId) * (npcData.Voice?.Volume ?? 1) * npcVolume;
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Voice volume: {volume}", eventId);

            var voiceMessage = new VoiceMessage
            {
                SpeakerObj = speaker,
                SpeakerFollowObj = is3d && speaker != null ? speaker : DalamudHelper.LocalPlayer,
                Source = source,
                Speaker = npcData,
                Text = cleanText,
                OriginalText = originalText,
                Language = language,
                EventId = eventId,
                OnlyRequest = onlyRequest,
                Volume = volume
            };
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, voiceMessage.GetDebugInfo(), eventId);
            
            if (speaker != null && Configuration.MutedNpcDialogues.Contains(speaker.DataId))
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping muted dialogue: {cleanText}", eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                PlayingHelper.RecreationStarted = false;
                return;
            }
            
            if (volume > 0)
                BackendHelper.OnSay(voiceMessage);
            else
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. Volume is 0.", eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                PlayingHelper.RecreationStarted = false;
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while starting voice inference: {ex}", eventId);
        }
    }

    public void Dispose()
    {
        GoogleDriveHelper.StopSync();
        DetectLanguageHelper.Dispose();
        PlayingHelper.Dispose();
        SoundHelper.Dispose();
        AddonTalkHelper.Dispose();
        AddonBattleTalkHelper.Dispose();
        AddonCutSceneSelectStringHelper.Dispose();
        AddonSelectStringHelper.Dispose();
        AddonBubbleHelper.Dispose();
        ChatTalkHelper.Dispose();

        Configuration.Save();
        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        CommandHelper.Dispose();
        AlltalkInstanceHelper.Dispose();
    }

    private void DrawUI() => WindowSystem.Draw();
}
