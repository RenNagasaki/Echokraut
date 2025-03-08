using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using Echokraut.Helper.DataHelper;
using Echokraut.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Echokraut.Helper.Functional
{
    public static class CommandHelper
    {
        private static Configuration Configuration;
        private static IChatGui ChatGui;
        private static IClientState ClientState;
        private static IDataManager DataManager;
        public static ICommandManager CommandManager;
        private static ICondition Condition;
        private static ConfigWindow ConfigWindow;
        public static List<string> CommandKeys;

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
            CommandManager.AddHandler("/ekdel", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "/ekdel n -> Deletes last 'n' local saved files. Default 10"
            });
            CommandManager.AddHandler("/ekdelmin", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "/ekdelmin n -> Deletes last 'n' minutes generated local saved files. Default 10"
            });

            CommandKeys = CommandHelper.CommandManager.Commands.Keys.ToList().FindAll(p => p.StartsWith("/ek"));
            CommandKeys.Sort();
        }

        public static void OnCommand(string command, string args)
        {
            // in response to the slash command, just toggle the display status of our config ui

            var activationText = "";
            var activationType = "";
            var errorText = "";
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
                case "/ekdel":
                    try
                    {
                        var deleteNFiles = 10;
                        if (args.Trim().Length > 0)
                            deleteNFiles = Convert.ToInt32(args);

                        var deletedFiles = AudioFileHelper.DeleteLastNFiles(deleteNFiles);
                        PrintText("", $"Deleted {deletedFiles} generated audio files");
                    }
                    catch (Exception ex)
                    {
                        errorText = $"Please enter a valid number or leave empty";
                    }
                    break;
                case "/ekdelmin":
                    try
                    {
                        var deleteNMinutesFiles = 10;
                        if (args.Trim().Length > 0)
                            deleteNMinutesFiles = Convert.ToInt32(args);

                        var deletedFiles = AudioFileHelper.DeleteLastNMinutesFiles(deleteNMinutesFiles);
                        PrintText("", $"Deleted {deletedFiles} generated audio files");
                    }
                    catch (Exception ex)
                    {
                        errorText = $"Please enter a valid number or leave empty";
                    }
                    break;
                case "/ekt":
                    Configuration.Enabled = !Configuration.Enabled;
                    Configuration.Save();
                    activationText = (Configuration.Enabled ? "Enabled" : "Disabled");
                    activationType = "plugin";
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

                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"New Command triggered: {command}", new EKEventId(0, TextSource.None));

                if (!string.IsNullOrWhiteSpace(errorText))
                    PrintText("", errorText);
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
                    PrintText(target.Name.TextValue, $"Target -> Name: {target.Name}, Race: {race}, Gender: {gender}, ModelID: {modelId}, ModelBody: {modelBody}, BodyType: {bodyType}");
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
            var cond10 = Condition[ConditionFlag.BoundByDuty];
            PrintText("Debug", $"Debug -> ---Start---");
            PrintText("Debug", $"Debug -> OccupiedInQuestEvent: {cond1}");
            PrintText("Debug", $"Debug -> Occupied: {cond2}");
            PrintText("Debug", $"Debug -> Occupied30: {cond3}");
            PrintText("Debug", $"Debug -> Occupied33: {cond4}");
            PrintText("Debug", $"Debug -> Occupied38: {cond5}");
            PrintText("Debug", $"Debug -> Occupied39: {cond6}");
            PrintText("Debug", $"Debug -> OccupiedInCutSceneEvent: {cond7}");
            PrintText("Debug", $"Debug -> OccupiedInEvent: {cond8}");
            PrintText("Debug", $"Debug -> OccupiedSummoningBell: {cond9}");
            PrintText("Debug", $"Debug -> BoundByDuty: {cond10}");
            PrintText("Debug", $"Debug -> ---End---");
        }

        public static void PrintText(string name, string text)
        {
            ChatGui.Print(new Dalamud.Game.Text.XivChatEntry() { Name = name, Message = "Echokraut: " + text, Timestamp = DateTime.Now.Hour * 60 + DateTime.Now.Minute, Type = Dalamud.Game.Text.XivChatType.Echo });
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
            CommandManager.RemoveHandler("/ekdel");
            CommandManager.RemoveHandler("/ekdelmin");
        }
    }
}
