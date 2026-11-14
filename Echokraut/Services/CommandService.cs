using Echotools.Logging.Services;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
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
    private readonly ILogService _log;
    private readonly Configuration _config;

    public event Action? ToggleConfigRequested;
    public event Action? ToggleFirstTimeRequested;
    public event Action? ToggleVoiceClipManagerRequested;
    public event Action? ToggleGameDataToolsRequested;
    public event Action<EKEventId>? CancelAllRequested;
    public List<string> CommandKeys { get; private set; } = new();

    public CommandService(ICommandManager commandManager, IChatGui chatGui, ILogService log, Configuration config)
    {
        _commandManager = commandManager ?? throw new ArgumentNullException(nameof(commandManager));
        _chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
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
        _commandManager.AddHandler("/ek", new CommandInfo(OnCommand) { HelpMessage = "Opens the Voice Clip Manager" });
        _commandManager.AddHandler("/ekconfig", new CommandInfo(OnCommand) { HelpMessage = "Opens the configuration window" });
        _commandManager.AddHandler("/ekfirst", new CommandInfo(OnCommand) { HelpMessage = "Opens the first-time setup window" });
        _commandManager.AddHandler("/ekdata", new CommandInfo(OnCommand) { HelpMessage = "Opens the Game Data Tools window" });

        CommandKeys = _commandManager.Commands.Keys.ToList().FindAll(p => p.StartsWith("/ek"));
        CommandKeys.Sort();
    }

    private void OnCommand(string command, string args)
    {
        var activationText = "";
        var activationType = "";

        switch (command)
        {
            case "/ek":
                ToggleVoiceClipManagerUi();
                break;
            case "/ekconfig":
                ToggleConfigUi();
                break;
            case "/ekfirst":
                ToggleFirstTimeUi();
                break;
            case "/ekdata":
                ToggleGameDataToolsUi();
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
            PrintText("", $"{activationText} {activationType} voicing");
    }

    public void ToggleConfigUi()
    {
        if (!_config.FirstTime)
            ToggleConfigRequested?.Invoke();
        else
            ToggleFirstTimeRequested?.Invoke();
    }

    public void ToggleVoiceClipManagerUi()
    {
        // Block until first-time setup is done — opening the manager before voices exist is useless.
        if (_config.FirstTime)
            ToggleFirstTimeRequested?.Invoke();
        else
            ToggleVoiceClipManagerRequested?.Invoke();
    }

    public void ToggleFirstTimeUi() => ToggleFirstTimeRequested?.Invoke();

    public void ToggleGameDataToolsUi()
    {
        // FirstTime gate: until the user finishes setup, every window-opening command
        // funnels into the wizard so they don't start poking at backend tooling that
        // depends on a configured backend. Once FirstTime is false the request goes
        // through normally; NativeWindowManager.ToggleGameDataTools then applies its
        // own None-mode guard (refuses to open if there's no live backend).
        if (_config.FirstTime)
            ToggleFirstTimeRequested?.Invoke();
        else
            ToggleGameDataToolsRequested?.Invoke();
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
        _commandManager.RemoveHandler("/ekconfig");
        _commandManager.RemoveHandler("/ekt");
        _commandManager.RemoveHandler("/ekttalk");
        _commandManager.RemoveHandler("/ektbtalk");
        _commandManager.RemoveHandler("/ektbubble");
        _commandManager.RemoveHandler("/ektcutschoice");
        _commandManager.RemoveHandler("/ektchoice");
        _commandManager.RemoveHandler("/ektchat");
        _commandManager.RemoveHandler("/ekfirst");
        _commandManager.RemoveHandler("/ekdata");
    }
}
