using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Echokraut.Windows;
using Dalamud.Game;
using Echokraut.Enums;
using Echokraut.Backend;
using System;
using R3;
using Echokraut.TextToTalk.Utils;
using Echokraut.DataClasses;
using Dalamud.Game.Text;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using GameObject = Dalamud.Game.ClientState.Objects.Types.GameObject;
using Lumina.Excel.GeneratedSheets;
using Echokraut.Helper;
using Echokraut.Extensions;
using Echokraut.Utils;
using NAudio.Wave;
using System.Reflection;

namespace Echokraut;

public partial class Echokraut : IDalamudPlugin
{

    private DalamudPluginInterface PluginInterface { get; init; }
    private IPluginLog Log { get; init; }
    private ICommandManager CommandManager { get; init; }
    private IFramework Framework { get; init; }
    private IClientState ClientState { get; init; }
    private ICondition Condition { get; init; }
    private IObjectTable ObjectTable { get; init; }
    private IGameGui GameGui { get; init; }
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
    internal readonly VolumeHelper volumeHelper;
    internal readonly UngenderedOverrideManager ungenderedOverrides;
    internal readonly SoundHelper soundHelper;
    internal readonly LipSyncHelper lipSyncHelper;
    #endregion

    public Echokraut(
        [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
        [RequiredVersion("1.0")] IPluginLog log,
        [RequiredVersion("1.0")] ICommandManager commandManager,
        [RequiredVersion("1.0")] IFramework framework,
        [RequiredVersion("1.0")] IClientState clientState,
        [RequiredVersion("1.0")] ICondition condition,
        [RequiredVersion("1.0")] IObjectTable objectTable,
        [RequiredVersion("1.0")] IDataManager dataManager,
        [RequiredVersion("1.0")] IChatGui chatGui,
        [RequiredVersion("1.0")] IGameGui gameGui,
        [RequiredVersion("1.0")] ISigScanner sigScanner,
        [RequiredVersion("1.0")] IGameInteropProvider gameInterop)
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
        this.Log = log;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        LogHelper.Setup(log, Configuration);
        BackendHelper.Setup(Configuration, this, Configuration.BackendSelection);
        this.ConfigWindow = new ConfigWindow(this, Configuration);

        this.addonTalkHelper = new AddonTalkHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonBattleTalkHelper = new AddonBattleTalkHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonSelectStringHelper = new AddonSelectStringHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.addonCutSceneSelectStringHelper = new AddonCutSceneSelectStringHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration);
        this.volumeHelper = new VolumeHelper(sigScanner, gameInterop);
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

    public void Say(GameObject? speaker, SeString speakerName, string textValue, TextSource source)
    {
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Preparing for Inference: {speakerName} - {textValue} - {source}");
        // Run a preprocessing pipeline to clean the text for the speech synthesizer
        var cleanText = FunctionalUtils.Pipe(
            textValue,
            TalkUtils.StripAngleBracketedText,
            TalkUtils.ReplaceSsmlTokens,
            TalkUtils.NormalizePunctuation,
            t => this.Configuration.RemoveStutters ? TalkUtils.RemoveStutters(t) : t,
            x => x.Trim()).Replace("/", "Schrägstrich ");

        cleanText = TalkUtils.ReplaceRomanNumbers(cleanText);

        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Cleantext: {cleanText}");
        // Ensure that the result is clean; ignore it otherwise
        if (!cleanText.Any() || !TalkUtils.IsSpeakable(cleanText))
        {
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
        npcData.race = GetSpeakerRace(speaker);
        npcData.gender = CharacterGenderUtils.GetCharacterGender(speaker, this.ungenderedOverrides, this.Log);
        npcData.name = DataHelper.cleanUpName(cleanSpeakerName);

        var resNpcData = DataHelper.getNpcMapData(Configuration.MappedNpcs, npcData);
        if (resNpcData == null)
        {
            Configuration.MappedNpcs.Add(npcData);
            Configuration.Save();
        }
        else
            npcData = resNpcData;

        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"NpcData: {npcData}");
        // Say the thing
        var voiceMessage = new VoiceMessage
        {
            Source = source.ToString(),
            Speaker = npcData,
            Text = cleanText,
            TextTemplate = textTemplate,
            Language = this.ClientState.ClientLanguage.ToString()
        };
        var volume = volumeHelper.GetVoiceVolume();
        BackendHelper.OnSay(voiceMessage, volume);
    }

    private unsafe NpcRaces GetSpeakerRace(GameObject? speaker)
    {
        var race = this.DataManager.GetExcelSheet<Race>();
        if (race is null || speaker is null || speaker.Address == nint.Zero)
        {
            return NpcRaces.Default;
        }

        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
        var speakerRace = charaStruct->DrawData.CustomizeData.Race;
        var row = race.GetRow(speakerRace);

        object raceEnum = NpcRaces.Default;
        if (!(row is null))
        {
            string raceStr = DataHelper.getRaceEng(row.Masculine.RawString, Log);
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found Race: {raceStr}");
            if (!Enum.TryParse(typeof(NpcRaces), raceStr, out raceEnum))
            {
                var modelData = charaStruct->CharacterData.ModelSkeletonId;
                var modelData2 = charaStruct->CharacterData.ModelSkeletonId_2;

                var activeData = modelData;
                if (activeData == -1)
                    activeData = modelData2;

                try
                {
                    if (!Enum.TryParse(typeof(NpcRaces), activeData.ToString(), out raceEnum))
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
        return (NpcRaces)raceEnum;
    }

    private unsafe BodyType GetSpeakerBodyType(GameObject? speaker)
    {
        if (speaker is null || speaker.Address == nint.Zero)
        {
            return BodyType.Unknown;
        }

        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
        var speakerBodyType = charaStruct->DrawData.CustomizeData.BodyType;
        var speakerModel = charaStruct->DrawData.CustomizeData;
        return (BodyType)speakerBodyType;
    }

    public void Dispose()
    {
        BackendHelper.Dispose();
        this.addonTalkHelper.Dispose();
        this.addonBattleTalkHelper.Dispose();
        this.soundHelper.Dispose();
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
