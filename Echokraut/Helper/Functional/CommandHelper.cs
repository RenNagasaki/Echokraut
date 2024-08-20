using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using Echokraut.Helper.DataHelper;
using Echokraut.Windows;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Reflection;
using static Lumina.Models.Models.Model;

namespace Echokraut.Helper.Functional
{
    public static class CommandHelper
    {
        private static Configuration Configuration;
        private static IChatGui ChatGui;
        private static IClientState ClientState;
        private static IDataManager DataManager;
        private static ICommandManager CommandManager;
        private static ICondition Condition;
        private static ConfigWindow ConfigWindow;

        public static void Setup(Configuration configuration, IChatGui chatGui, IClientState clientState, IDataManager dataManager, ICommandManager commandManager, ICondition condition, ConfigWindow configWindow)
        {
            Configuration = configuration;
            ChatGui = chatGui;
            ClientState = clientState;
            DataManager = dataManager;
            CommandManager = commandManager;
            Condition = condition;
            ConfigWindow = configWindow;

            RegisterCommands();
        }

        public static void RegisterCommands()
        {
            CommandManager.AddHandler("/ekt", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles Echokraut"
            });
            CommandManager.AddHandler("/ekttalk", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles dialogue voicing"
            });
            CommandManager.AddHandler("/ektbtalk", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles battle dialogue voicing"
            });
            CommandManager.AddHandler("/ektbubble", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles bubble voicing"
            });
            CommandManager.AddHandler("/ektchat", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles chat voicing"
            });
            CommandManager.AddHandler("/ektcutschoice", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles cutscene choice voicing"
            });
            CommandManager.AddHandler("/ektchoice", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles choice voicing"
            });
            CommandManager.AddHandler("/ek", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Opens the configuration window"
            });
            CommandManager.AddHandler("/ekid", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Echoes info about current target"
            });
            CommandManager.AddHandler("/ekdb", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Echoes current debug info"
            });
        }

        public static void OnCommand(string command, string args)
        {
            // in response to the slash command, just toggle the display status of our config ui

            var activationText = "";
            var activationType = "";
            switch (command)
            {
                case "/ek":
                    ToggleConfigUI();
                    break;
                case "/ekid":
                    PrintTargetInfo();
                    break;
                case "/ekdb":
                    PrintDebugInfo();
                    break;
                case "/ekt":
                    Configuration.Enabled = !Configuration.Enabled;
                    Configuration.Save();
                    activationText = (Configuration.Enabled ? "Enabled" : "Disabled");
                    activationType = "echokraut";
                    break;
                case "/ekttalk":
                    Configuration.VoiceDialogue = !Configuration.VoiceDialogue;
                    Configuration.Save();
                    activationText = (Configuration.VoiceDialogue ? "Enabled" : "Disabled");
                    activationType = "dialogue";
                    break;
                case "/ektbtalk":
                    Configuration.VoiceBattleDialogue = !Configuration.VoiceBattleDialogue;
                    Configuration.Save();
                    activationText = (Configuration.VoiceBattleDialogue ? "Enabled" : "Disabled");
                    activationType = "battle dialogue";
                    break;
                case "/ektbubble":
                    Configuration.VoiceBubble = !Configuration.VoiceBubble;
                    Configuration.Save();
                    activationText = (Configuration.VoiceBubble ? "Enabled" : "Disabled");
                    activationType = "bubble";
                    break;
                case "/ektchat":
                    Configuration.VoiceChat = !Configuration.VoiceChat;
                    Configuration.Save();
                    activationText = (Configuration.VoiceChat ? "Enabled" : "Disabled");
                    activationType = "chat";
                    break;
                case "/ektcutschoice":
                    Configuration.VoicePlayerChoicesCutscene = !Configuration.VoicePlayerChoicesCutscene;
                    Configuration.Save();
                    activationText = (Configuration.VoicePlayerChoicesCutscene ? "Enabled" : "Disabled");
                    activationType = "player choice in cutscene";
                    break;
                case "/ektchoice":
                    Configuration.VoicePlayerChoices = !Configuration.VoicePlayerChoices;
                    Configuration.Save();
                    activationText = (Configuration.VoicePlayerChoices ? "Enabled" : "Disabled");
                    activationType = "player choice";
                    break;
            }

            if (!string.IsNullOrWhiteSpace(activationType) && !string.IsNullOrWhiteSpace(activationText))
            {
                PrintText("", $"{activationText} {activationType} voicing");

                LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"New Command triggered: {command}", new EKEventId(0, TextSource.None));
            }
        }

        public static void ToggleConfigUI() => ConfigWindow.Toggle();

        public unsafe static void PrintTargetInfo()
        {
            var localPlayer = ClientState.LocalPlayer;

            if (localPlayer != null)
            {
                var target = localPlayer.TargetObject;
                if (target != null)
                {
                    var race = CharacterDataHelper.GetSpeakerRace(DataManager, new EKEventId(0, TextSource.None), target, out var raceStr, out var modelId);
                    var gender = CharacterDataHelper.GetCharacterGender(DataManager, new EKEventId(0, TextSource.None), target, race, out var modelBody);
                    var bodyType = LuminaHelper.GetENpcBase(target.DataId)?.BodyType;
                    PrintText(target.Name.TextValue, $"Echokraut Target -> Name: {target.Name}, Race: {race}, Gender: {gender}, ModelID: {modelId}, ModelBody: {modelBody}, BodyType: {bodyType}");
                }
            }
        }
        
        public static void PrintDebugInfo()
        {
            var cond1 = Condition[ConditionFlag.OccupiedInQuestEvent];
            var cond2 = Condition[ConditionFlag.Occupied];
            var cond3 = Condition[ConditionFlag.Occupied30];
            var cond4 = Condition[ConditionFlag.Occupied33];
            var cond5 = Condition[ConditionFlag.Occupied38];
            var cond6 = Condition[ConditionFlag.Occupied39];
            var cond7 = Condition[ConditionFlag.OccupiedInCutSceneEvent];
            var cond8 = Condition[ConditionFlag.OccupiedInEvent];
            var cond9 = Condition[ConditionFlag.OccupiedSummoningBell];
            PrintText("Debug", $"Echokraut Debug -> ---Start---");
            PrintText("Debug", $"Echokraut Debug -> OccupiedInQuestEvent: {cond1}");
            PrintText("Debug", $"Echokraut Debug -> Occupied: {cond2}");
            PrintText("Debug", $"Echokraut Debug -> Occupied30: {cond3}");
            PrintText("Debug", $"Echokraut Debug -> Occupied33: {cond4}");
            PrintText("Debug", $"Echokraut Debug -> Occupied38: {cond5}");
            PrintText("Debug", $"Echokraut Debug -> Occupied39: {cond6}");
            PrintText("Debug", $"Echokraut Debug -> OccupiedInCutSceneEvent: {cond7}");
            PrintText("Debug", $"Echokraut Debug -> OccupiedInEvent: {cond8}");
            PrintText("Debug", $"Echokraut Debug -> OccupiedSummoningBell: {cond9}");
            PrintText("Debug", $"Echokraut Debug -> ---End---");
        }

        public static void PrintText(string name, string text)
        {
            ChatGui.Print(new Dalamud.Game.Text.XivChatEntry() { Name = name, Message = text, Timestamp = DateTime.Now.Hour * 60 + DateTime.Now.Minute, Type = Dalamud.Game.Text.XivChatType.Echo });
        }

        internal static void Dispose()
        {
            CommandManager.RemoveHandler("/ek");
            CommandManager.RemoveHandler("/ekt");
            CommandManager.RemoveHandler("/ekdb");
            CommandManager.RemoveHandler("/ekid");
            CommandManager.RemoveHandler("/ekttalk");
            CommandManager.RemoveHandler("/ektbtalk");
            CommandManager.RemoveHandler("/ektbubble");
            CommandManager.RemoveHandler("/ektcutschoice");
            CommandManager.RemoveHandler("/ektchoice");
        }
    }
}
