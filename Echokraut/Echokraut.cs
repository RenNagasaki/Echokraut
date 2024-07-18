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
    internal readonly AddonTalkHelper addonTalkHelper;
    internal readonly AddonBattleTalkHelper addonBattleTalkHelper;
    internal readonly AddonSelectStringHelper addonSelectStringHelper;
    internal readonly AddonCutSceneSelectStringHelper addonCutSceneSelectStringHelper;
    internal readonly AddonBubbleHelper addonBubbleHelper;
    internal readonly UngenderedOverrideManager ungenderedOverrides;
    internal readonly SoundHelper soundHelper;
    internal readonly LipSyncHelper lipSyncHelper;
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
        LogHelper.Info("", $"{Assembly.GetCallingAssembly().Location}");
        BackendHelper.Setup(Configuration, clientState, this, Configuration.BackendSelection);
        this.ConfigWindow = new ConfigWindow(this, Configuration);

        this.addonTalkHelper = new AddonTalkHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonBattleTalkHelper = new AddonBattleTalkHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonSelectStringHelper = new AddonSelectStringHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonCutSceneSelectStringHelper = new AddonCutSceneSelectStringHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonBubbleHelper = new AddonBubbleHelper(this, this.DataManager, this.Framework, this.ObjectTable,sigScanner, gameInterop, this.ClientState, this.Configuration);
        this.ungenderedOverrides = new UngenderedOverrideManager();
        this.soundHelper = new SoundHelper(this.addonTalkHelper, this.addonBattleTalkHelper, sigScanner, gameInterop);
        this.lipSyncHelper = new LipSyncHelper(this.ClientState, this.ObjectTable, this.Configuration);

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

    public void Cancel()
    {
        if (Configuration.CancelSpeechOnTextAdvance)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Stopping Inference");
            BackendHelper.OnCancel();
        }
    }

    public void StopLipSync()
    {
        lipSyncHelper.StopLipSync();
    }

    public unsafe void Say(GameObject? speaker, SeString speakerName, string textValue, TextSource source)
    {
        try
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Preparing for Inference: {speakerName} - {textValue} - {source}");
            // Run a preprocessing pipeline to clean the text for the speech synthesizer
            var cleanText = FunctionalUtils.Pipe(
                textValue,
                TalkUtils.StripAngleBracketedText,
                TalkUtils.ReplaceSsmlTokens,
                TalkUtils.NormalizePunctuation,
                t => this.Configuration.RemoveStutters ? TalkUtils.RemoveStutters(t) : t,
                x => x.Trim()).Replace("/", "Schrägstrich ").Replace("C'mi", "Kami");

            cleanText = TalkUtils.ReplaceRomanNumbers(cleanText); 
            cleanText = DataHelper.analyzeAndImproveText(cleanText);

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Cleantext: {cleanText}");
            // Ensure that the result is clean; ignore it otherwise
            if (!cleanText.Any() || !TalkUtils.IsSpeakable(cleanText))
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Text not speakable: {cleanText}");
                return;
            }

            // Build a template for the text payload
            var textTemplate = TalkUtils.ExtractTokens(cleanText, new Dictionary<string, string?>
            {
                { "{{FULL_NAME}}", this.ClientState.LocalPlayer?.GetFullName() },
                { "{{FIRST_NAME}}", this.ClientState.LocalPlayer?.GetFirstName() },
                { "{{LAST_NAME}}", this.ClientState.LocalPlayer?.GetLastName() },
            });

            // Some characters have emdashes in their names, which should be treated
            // as hyphens for the sake of the plugin.
            var cleanSpeakerName = TalkUtils.NormalizePunctuation(speakerName.TextValue);

            NpcMapData npcData = new NpcMapData();
            // Get the speaker's race if it exists.
            var raceStr = "";
            npcData.race = GetSpeakerRace(speaker, out raceStr);
            npcData.raceStr = raceStr;
            npcData.gender = CharacterGenderUtils.GetCharacterGender(speaker, this.ungenderedOverrides, this.Log);
            npcData.name = DataHelper.cleanUpName(cleanSpeakerName);

            if (npcData.name == "PLAYER")
                npcData.name = this.ClientState.LocalPlayer?.Name.ToString() ?? "PLAYER";
            else if (string.IsNullOrWhiteSpace(npcData.name) && source == TextSource.AddonBubble)
                npcData.name = GetBubbleName(speaker);

            var resNpcData = DataHelper.getNpcMapData(Configuration.MappedNpcs, npcData);
            if (resNpcData != null && resNpcData.race == NpcRaces.Default && npcData.raceStr != NpcRaces.Default.ToString())
            {
                resNpcData.race = npcData.race;
                Configuration.Save();
            }

            if (resNpcData == null)
            {
                Configuration.MappedNpcs.Add(npcData);
                Configuration.MappedNpcs = Configuration.MappedNpcs.OrderBy(p => p.ToString(true)).ToList();
                Configuration.Save();
            }
            else
                npcData = resNpcData;

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"NpcData: {npcData.ToString(true)}");
            // Say the thing
            var voiceMessage = new VoiceMessage
            {
                pActor = speaker,
                Source = source,
                Speaker = npcData,
                Text = cleanText,
                TextTemplate = textTemplate,
                Language = this.ClientState.ClientLanguage.ToString()
            };
            var volume = VolumeHelper.GetVoiceVolume(GameConfig);

            if (volume > 0)
                BackendHelper.OnSay(voiceMessage, volume);
            else
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. Volume is 0");
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while starting voice inference: {ex}");
        }
    }

    private unsafe NpcRaces GetSpeakerRace(GameObject? speaker, out string raceStr)
    {
        var race = this.DataManager.GetExcelSheet<Race>();
        var raceEnum = NpcRaces.Default;

        try
        {
            if (race is null || speaker is null || speaker.Address == nint.Zero)
            {
                raceStr = raceEnum.ToString();
                return NpcRaces.Default;
            }

            var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
            var speakerRace = charaStruct->DrawData.CustomizeData.Race;
            var row = race.GetRow(speakerRace);

            if (!(row is null))
            {
                raceStr = DataHelper.getRaceEng(row.Masculine.RawString, Log);
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found Race: {raceStr}");
                if (!Enum.TryParse<NpcRaces>(raceStr.Replace(" ", ""), out raceEnum))
                {
                    var modelData = charaStruct->CharacterData.ModelSkeletonId;
                    var modelData2 = charaStruct->CharacterData.ModelSkeletonId_2;

                    var activeData = modelData;
                    if (activeData == -1)
                        activeData = modelData2;

                    var activeNpcRace = NpcRaces.Default;
                    try
                    {
                        if (NpcRacesHelper.ModelsToRaceMap.TryGetValue(activeData, out activeNpcRace))
                            raceEnum = activeNpcRace;
                        else
                        {
                            raceEnum = NpcRaces.Default;
                        }
                    }
                    catch (Exception ex)
                    {
                        raceEnum = NpcRaces.Default;
                    }
                }
            }

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Determined Race: {raceEnum}");
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while determining race: {ex}");
        }

        raceStr = raceEnum.ToString();
        return raceEnum;
    }

    private unsafe string GetBubbleName(GameObject? speaker)
    {
        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
        var modelData = charaStruct->CharacterData.ModelSkeletonId;
        var modelData2 = charaStruct->CharacterData.ModelSkeletonId_2;

        var activeData = modelData;
        if (activeData == -1)
            activeData = modelData2;

        return "Bubble-" + activeData;
    }

    public void Dispose()
    {
        BackendHelper.Dispose();
        this.addonTalkHelper.Dispose();
        this.addonBattleTalkHelper.Dispose();
        this.soundHelper.Dispose();
        this.Configuration.Save();
        this.addonBubbleHelper.Dispose();
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
