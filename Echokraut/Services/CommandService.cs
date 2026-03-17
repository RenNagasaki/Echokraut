using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Services;

/// <summary>
/// Registers and handles all /ek* chat commands.
/// </summary>
public class CommandService : ICommandService
{
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly ICondition _condition;
    private readonly ILogService _log;
    private readonly ICharacterDataService _characterData;
    private readonly ILuminaService _lumina;
    private readonly Configuration _config;
    private readonly IAudioFileService _audioFiles;
    private readonly IGameObjectService _gameObjects;

    public event Action? ToggleConfigRequested;
    public event Action? ToggleFirstTimeRequested;
    public event Action<EKEventId>? CancelAllRequested;
    public event Action? UiModeSwitchRequested;

    public List<string> CommandKeys { get; private set; } = new();

    public CommandService(ICommandManager commandManager, IChatGui chatGui, ICondition condition, ILogService log, ICharacterDataService characterData, ILuminaService lumina, Configuration config, IAudioFileService audioFiles, IGameObjectService gameObjects)
    {
        _commandManager = commandManager ?? throw new ArgumentNullException(nameof(commandManager));
        _chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));
        _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _characterData = characterData ?? throw new ArgumentNullException(nameof(characterData));
        _lumina = lumina ?? throw new ArgumentNullException(nameof(lumina));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _audioFiles = audioFiles ?? throw new ArgumentNullException(nameof(audioFiles));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        RegisterCommands();
    }

    private void RegisterCommands()
    {
        _commandManager.AddHandler("/ekt", new CommandInfo(OnCommand) { HelpMessage = "Toggles Echokraut" });
        _commandManager.AddHandler("/ekttalk", new CommandInfo(OnCommand) { HelpMessage = "Toggles dialogue voicing" });
        _commandManager.AddHandler("/ektbtalk", new CommandInfo(OnCommand) { HelpMessage = "Toggles battle dialogue voicing" });
        _commandManager.AddHandler("/ektbubble", new CommandInfo(OnCommand) { HelpMessage = "Toggles bubble voicing" });
        _commandManager.AddHandler("/ektchat", new CommandInfo(OnCommand) { HelpMessage = "Toggles chat voicing" });
        _commandManager.AddHandler("/ektcutschoice", new CommandInfo(OnCommand) { HelpMessage = "Toggles cutscene choice voicing" });
        _commandManager.AddHandler("/ektchoice", new CommandInfo(OnCommand) { HelpMessage = "Toggles choice voicing" });
        _commandManager.AddHandler("/ek", new CommandInfo(OnCommand) { HelpMessage = "Opens the configuration window" });
        _commandManager.AddHandler("/ekid", new CommandInfo(OnCommand) { HelpMessage = "Echoes info about current target" });
        _commandManager.AddHandler("/ekdb", new CommandInfo(OnCommand) { HelpMessage = "Echoes current debug info" });
        _commandManager.AddHandler("/ekdel", new CommandInfo(OnCommand) { HelpMessage = "/ekdel n -> Deletes last 'n' local saved files. Default 10" });
        _commandManager.AddHandler("/ekdelmin", new CommandInfo(OnCommand) { HelpMessage = "/ekdelmin n -> Deletes last 'n' minutes generated local saved files. Default 10" });
        _commandManager.AddHandler("/ekfirst", new CommandInfo(OnCommand) { HelpMessage = "Opens the first-time setup window" });

        CommandKeys = _commandManager.Commands.Keys.ToList().FindAll(p => p.StartsWith("/ek"));
        CommandKeys.Sort();
    }

    private void OnCommand(string command, string args)
    {
        var activationText = "";
        var activationType = "";
        var errorText = "";

        switch (command)
        {
            case "/ek":
                if (!_config.FirstTime)
                    ToggleConfigUi();
                break;
            case "/ekfirst":
                ToggleFirstTimeUi();
                break;
            case "/ekid":
                PrintTargetInfo();
                break;
            case "/ekdb":
                PrintDebugInfo();
                break;
            case "/ekdel":
                try
                {
                    var n = args.Trim().Length > 0 ? Convert.ToInt32(args) : 10;
                    var deleted = _audioFiles.DeleteLastNFiles(n);
                    PrintText("", $"Deleted {deleted} generated audio files");
                }
                catch
                {
                    errorText = "Please enter a valid number or leave empty";
                }
                break;
            case "/ekdelmin":
                try
                {
                    var n = args.Trim().Length > 0 ? Convert.ToInt32(args) : 10;
                    var deleted = _audioFiles.DeleteLastNMinutesFiles(n);
                    PrintText("", $"Deleted {deleted} generated audio files");
                }
                catch
                {
                    errorText = "Please enter a valid number or leave empty";
                }
                break;
            case "/ekt":
                _config.Enabled = !_config.Enabled; 
                _config.Save();
                if (!_config.Enabled)
                    CancelAllRequested?.Invoke(new EKEventId(0, TextSource.None));
                activationText = _config.Enabled ? "Enabled" : "Disabled";
                activationType = "plugin";
                break;
            case "/ekttalk":
                _config.VoiceDialogue = !_config.VoiceDialogue;
                _config.Save();
                activationText = _config.VoiceDialogue ? "Enabled" : "Disabled";
                activationType = "dialogue";
                break;
            case "/ektbtalk":
                _config.VoiceBattleDialogue = !_config.VoiceBattleDialogue;
                _config.Save();
                activationText = _config.VoiceBattleDialogue ? "Enabled" : "Disabled";
                activationType = "battle dialogue";
                break;
            case "/ektbubble":
                _config.VoiceBubble = !_config.VoiceBubble;
                _config.Save();
                activationText = _config.VoiceBubble ? "Enabled" : "Disabled";
                activationType = "bubble";
                break;
            case "/ektchat":
                _config.VoiceChat = !_config.VoiceChat;
                _config.Save();
                activationText = _config.VoiceChat ? "Enabled" : "Disabled";
                activationType = "chat";
                break;
            case "/ektcutschoice":
                _config.VoicePlayerChoicesCutscene = !_config.VoicePlayerChoicesCutscene;
                _config.Save();
                activationText = _config.VoicePlayerChoicesCutscene ? "Enabled" : "Disabled";
                activationType = "player choice in cutscene";
                break;
            case "/ektchoice":
                _config.VoicePlayerChoices = !_config.VoicePlayerChoices;
                _config.Save();
                activationText = _config.VoicePlayerChoices ? "Enabled" : "Disabled";
                activationType = "player choice";
                break;
        }

        if (!string.IsNullOrWhiteSpace(activationType) && !string.IsNullOrWhiteSpace(activationText))
        {
            PrintText("", $"{activationText} {activationType} voicing");
            if (!string.IsNullOrWhiteSpace(errorText))
                PrintText("", errorText);
        }
    }

    public void ToggleConfigUi()
    {
        if (!_config.FirstTime)
            ToggleConfigRequested?.Invoke();
        else
            ToggleFirstTimeRequested?.Invoke();
    }

    public void ToggleFirstTimeUi() => ToggleFirstTimeRequested?.Invoke();
    public void RequestUiModeSwitch() => UiModeSwitchRequested?.Invoke();

    private unsafe void PrintTargetInfo()
    {
        var localPlayer = _gameObjects.LocalPlayer;
        if (localPlayer == null) return;

        var target = localPlayer.TargetObject;
        if (target == null) return;

        var eventId = new EKEventId(0, TextSource.None);
        var race = _characterData.GetSpeakerRace(eventId, target, out var raceStr, out var modelId);
        var gender = _characterData.GetCharacterGender(eventId, target, race, out var modelBody);
        var bodyType = _lumina.GetENpcBase(target.BaseId, eventId)?.BodyType;
        PrintText(target.Name.TextValue,
            $"Target -> Name: {target.Name}, Race: {race}, Gender: {gender}, ModelID: {modelId}, ModelBody: {modelBody}, BodyType: {bodyType}");
    }

    private void PrintDebugInfo()
    {
        PrintText("Debug", "Debug -> ---Start---");
        PrintText("Debug", $"Debug -> OccupiedInQuestEvent: {_condition[ConditionFlag.OccupiedInQuestEvent]}");
        PrintText("Debug", $"Debug -> Occupied: {_condition[ConditionFlag.Occupied]}");
        PrintText("Debug", $"Debug -> Occupied30: {_condition[ConditionFlag.Occupied30]}");
        PrintText("Debug", $"Debug -> Occupied33: {_condition[ConditionFlag.Occupied33]}");
        PrintText("Debug", $"Debug -> Occupied38: {_condition[ConditionFlag.Occupied38]}");
        PrintText("Debug", $"Debug -> Occupied39: {_condition[ConditionFlag.Occupied39]}");
        PrintText("Debug", $"Debug -> OccupiedInCutSceneEvent: {_condition[ConditionFlag.OccupiedInCutSceneEvent]}");
        PrintText("Debug", $"Debug -> OccupiedInEvent: {_condition[ConditionFlag.OccupiedInEvent]}");
        PrintText("Debug", $"Debug -> OccupiedSummoningBell: {_condition[ConditionFlag.OccupiedSummoningBell]}");
        PrintText("Debug", $"Debug -> BoundByDuty: {_condition[ConditionFlag.BoundByDuty]}");
        PrintText("Debug", "Debug -> ---End---");
    }

    public void PrintText(string name, string text)
    {
        _chatGui.Print(new Dalamud.Game.Text.XivChatEntry
        {
            Name = name,
            Message = "Echokraut: " + text,
            Timestamp = DateTime.Now.Hour * 60 + DateTime.Now.Minute,
            Type = Dalamud.Game.Text.XivChatType.Echo
        });
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler("/ek");
        _commandManager.RemoveHandler("/ekt");
        _commandManager.RemoveHandler("/ekdb");
        _commandManager.RemoveHandler("/ekid");
        _commandManager.RemoveHandler("/ekttalk");
        _commandManager.RemoveHandler("/ektbtalk");
        _commandManager.RemoveHandler("/ektbubble");
        _commandManager.RemoveHandler("/ektcutschoice");
        _commandManager.RemoveHandler("/ektchoice");
        _commandManager.RemoveHandler("/ekdel");
        _commandManager.RemoveHandler("/ekdelmin");
        _commandManager.RemoveHandler("/ektchat");
        _commandManager.RemoveHandler("/ekfirst");
    }
}
