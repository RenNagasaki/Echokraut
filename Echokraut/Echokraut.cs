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

namespace Echokraut;

public partial class Echokraut : IDalamudPlugin
{
    private const string CommandName = "/eksettings";

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
    public BackendHelper BackendHelper { get; init; }

    public readonly WindowSystem WindowSystem = new("Echokraut");
    private ConfigWindow ConfigWindow { get; init; }

    #region TextToTalk Base
    private readonly AddonTalkHelper addonTalkHandler;
    private readonly AddonBattleTalkHelper addonBattleTalkHandler;
    private readonly VolumeHelper volumeHelper;
    private readonly UngenderedOverrideManager ungenderedOverrides;
    private readonly SoundHelper soundHelper;
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

        this.BackendHelper = new BackendHelper(Configuration, this.Log);
        this.BackendHelper.SetBackendType(Configuration.BackendSelection);
        this.ConfigWindow = new ConfigWindow(this, this.Log, Configuration);

        this.addonTalkHandler = new AddonTalkHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration, this.Log);
        this.addonBattleTalkHandler = new AddonBattleTalkHelper(this, this.ClientState, this.Condition, this.GameGui, this.Framework, this.ObjectTable, this.Configuration, this.Log);
        this.volumeHelper = new VolumeHelper(sigScanner, gameInterop, log);
        this.ungenderedOverrides = new UngenderedOverrideManager();
        this.soundHelper =
            new SoundHelper(this.addonTalkHandler, this.addonBattleTalkHandler, sigScanner, gameInterop, log);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the configuration windows"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
    }

    public void Cancel()
    {
        Log.Info($"Stopping Inference");

        if (Configuration.CancelSpeechOnTextAdvance)
            this.BackendHelper.OnCancel();
    }

    public void Say(GameObject? speaker, SeString speakerName, string textValue, TextSource source)
    {
        Log.Info($"Preparing for Inference: {speakerName} - {textValue} - {source}");
        // Run a preprocessing pipeline to clean the text for the speech synthesizer
        var cleanText = FunctionalUtils.Pipe(
            textValue,
            TalkUtils.StripAngleBracketedText,
            TalkUtils.ReplaceSsmlTokens,
            TalkUtils.NormalizePunctuation,
            t => this.Configuration.RemoveStutters ? TalkUtils.RemoveStutters(t) : t,
            x => x.Trim()).Replace("/", "Schrägstrich ");

        Log.Info($"Cleantext: {cleanText}");
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

        Log.Info($"NpcData: {npcData}");
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
        this.BackendHelper.OnSay(voiceMessage, volume);

        char[] delimiters = new char[] {' '};
        var count = cleanText.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Length;
        var estimatedLength = count / 2.1;
        addonTalkHandler.TriggerLipSync(voiceMessage.Speaker.name, estimatedLength.ToString());
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
            Log.Info($"Found Race: {raceStr}");
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

        Log.Info($"Determined Race: {raceEnum}");
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
        this.addonTalkHandler.Dispose();
        this.addonBattleTalkHandler.Dispose();
        this.soundHelper.Dispose();
        this.Configuration.Save();
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our config ui
        ToggleConfigUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
}
