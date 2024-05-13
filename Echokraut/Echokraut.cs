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
using Echokraut.TextToTalk.TextProviders;
using Echokraut.TextToTalk.Talk;
using System;
using Echokraut.TextToTalk;
using R3;
using Echokraut.TextToTalk.Utils;
using Echokraut.DataClasses;
using FF14_Echokraut.Helpers;
using Dalamud.Game.Text;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using GameObject = Dalamud.Game.ClientState.Objects.Types.GameObject;
using Echokraut.TextToTalk.Enums;
using Echokraut.TextToTalk.Extensions;
using Lumina.Excel.GeneratedSheets;
using Echokraut.TextToTalk.Events;

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
    private Configuration Configuration { get; init; }
    public BackendHelper BackendHelper { get; init; }

    private readonly IDisposable unsubscribeAll;
    public readonly WindowSystem WindowSystem = new("Echokraut");
    private ConfigWindow ConfigWindow { get; init; }
    private readonly IAddonTalkHandler addonTalkHandler;
    private readonly IAddonBattleTalkHandler addonBattleTalkHandler;
    private readonly AddonTalkManager addonTalkManager;
    private readonly AddonBattleTalkManager addonBattleTalkManager;

    public Echokraut(
        [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
        [RequiredVersion("1.0")] IPluginLog log,
        [RequiredVersion("1.0")] ICommandManager commandManager,
        [RequiredVersion("1.0")] IFramework framework,
        [RequiredVersion("1.0")] IClientState clientState,
        [RequiredVersion("1.0")] ICondition condition,
        [RequiredVersion("1.0")] IObjectTable objectTable,
        [RequiredVersion("1.0")] IDataManager dataManager,
        [RequiredVersion("1.0")] IGameGui gameGui)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        Framework = framework;
        ClientState = clientState;
        Condition = condition;
        ObjectTable = objectTable;
        DataManager = dataManager;
        GameGui = gameGui;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        this.BackendHelper = new BackendHelper(Configuration, log);

        ConfigWindow = new ConfigWindow(this, log, Configuration);

        var sharedState = new SharedState();
        this.addonTalkManager = new AddonTalkManager(framework, clientState, condition, gameGui);
        this.addonBattleTalkManager = new AddonBattleTalkManager(framework, clientState, condition, gameGui);
        this.addonTalkHandler = new AddonTalkHandler(this.addonTalkManager, framework, objectTable, this.Configuration, this.Log);
        this.addonBattleTalkHandler = new AddonBattleTalkHandler(this.addonBattleTalkManager, framework, objectTable, this.Configuration, this.Log);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

        var handleTextCancel = HandleTextCancel();
        var handleTextEmit = HandleTextEmit();

        this.unsubscribeAll = Disposable.Combine(handleTextCancel, handleTextEmit);
    }

    private IDisposable HandleTextCancel()
    {
        return OnTextSourceCancel()
            .Where(this, static (_, p) => p.Configuration is { Enabled: true, CancelSpeechOnTextAdvance: true })
            .Do(LogTextEvent)
            .SubscribeOnThreadPool()
            .Subscribe(
                ev => FunctionalUtils.RunSafely(
                    () => this.BackendHelper.OnCancel(),
                    ex => Log.Error(ex, "Failed to handle text cancel event")),
                ex => Log.Error(ex, "Text cancel event sequence has faulted"),
                _ => { });
    }

    private IDisposable HandleTextEmit()
    {
        return OnTextEmit()
            .Where(this, static (_, p) => p.Configuration.Enabled)
            .Do(LogTextEvent)
            .SubscribeOnThreadPool()
            .Subscribe(
                ev => FunctionalUtils.RunSafely(
                    () => Say(ev.Speaker, ev.SpeakerName, ev.GetChatType(), ev.Text.TextValue, ev.Source),
                    ex => Log.Error(ex, "Failed to handle text emit event")),
                ex => Log.Error(ex, "Text emit event sequence has faulted"),
                _ => { });
    }

    private void LogTextEvent(TextEvent ev)
    {
        FunctionalUtils.RunSafely(
            () => this.Log.Debug(ev.ToLogEntry().ToString()),
            ex => Log.Error(ex, "Failed to log text emit event"));
    }

    private void Say(GameObject? speaker, SeString speakerName, XivChatType? chatType, string textValue, TextSource source)
    {
        // Run a preprocessing pipeline to clean the text for the speech synthesizer
        var cleanText = FunctionalUtils.Pipe(
            textValue,
            TalkUtils.StripAngleBracketedText,
            TalkUtils.ReplaceSsmlTokens,
            TalkUtils.NormalizePunctuation,
            t => this.Configuration.RemoveStutters ? TalkUtils.RemoveStutters(t) : t,
            x => x.Trim());

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

        // Attempt to get the speaker's ID, if they're an NPC
        var npcId = speaker?.GetNpcId();

        // Get the speaker's race if it exists.
        var race = GetSpeakerRace(speaker);

        // Get the speaker's age if it exists.
        var bodyType = GetSpeakerBodyType(speaker);

        // Say the thing
        BackendSay(new VoiceMessage
        {
            Source = source.ToString(),
            Speaker = cleanSpeakerName,
            Text = cleanText,
            TextTemplate = textTemplate,
            ChatType = Convert.ToInt32(chatType),
            Language = this.ClientState.ClientLanguage.ToString(),
            NpcId = Convert.ToInt32(npcId),
            Race = race
        });
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

        if (row is null)
        {
            return NpcRaces.Default;
        }

        Log.Debug($"Found Race: {row.Masculine.RawString}");

        return (NpcRaces)Enum.Parse(typeof(NpcRaces), row.Masculine.RawString);
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

    private void BackendSay(VoiceMessage request)
    {
        this.BackendHelper.OnSay(request);
    }

    public void Dispose()
    {
        this.addonTalkHandler.Dispose();
        this.addonBattleTalkManager.Dispose();
        this.unsubscribeAll.Dispose();
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
