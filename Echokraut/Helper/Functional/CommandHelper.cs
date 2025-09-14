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
        public static List<string> CommandKeys;

        public static void Initialize()
        {

            RegisterCommands();
        }

        public static void RegisterCommands()
        {
            Plugin.CommandManager.AddHandler("/ekt", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles Echokraut"
            });
            Plugin.CommandManager.AddHandler("/ekttalk", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles dialogue voicing"
            });
            Plugin.CommandManager.AddHandler("/ektbtalk", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles battle dialogue voicing"
            });
            Plugin.CommandManager.AddHandler("/ektbubble", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles bubble voicing"
            });
            Plugin.CommandManager.AddHandler("/ektchat", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles chat voicing"
            });
            Plugin.CommandManager.AddHandler("/ektcutschoice", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles cutscene choice voicing"
            });
            Plugin.CommandManager.AddHandler("/ektchoice", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Toggles choice voicing"
            });
            Plugin.CommandManager.AddHandler("/ek", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Opens the Plugin.Configuration window"
            });
            Plugin.CommandManager.AddHandler("/ekid", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Echoes info about current target"
            });
            Plugin.CommandManager.AddHandler("/ekdb", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "Echoes current debug info"
            });
            Plugin.CommandManager.AddHandler("/ekdel", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "/ekdel n -> Deletes last 'n' local saved files. Default 10"
            });
            Plugin.CommandManager.AddHandler("/ekdelmin", new CommandInfo(CommandHelper.OnCommand)
            {
                HelpMessage = "/ekdelmin n -> Deletes last 'n' minutes generated local saved files. Default 10"
            });

            CommandKeys = Plugin.CommandManager.Commands.Keys.ToList().FindAll(p => p.StartsWith("/ek"));
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
                    if (!Plugin.Configuration.FirstTime)
                        ToggleConfigUi();
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
                    Plugin.Configuration.Enabled = !Plugin.Configuration.Enabled;
                    Plugin.Configuration.Save();
                    
                    if (!Plugin.Configuration.Enabled)
                        Plugin.Cancel(new EKEventId(0, TextSource.None));
                    activationText = (Plugin.Configuration.Enabled ? "Enabled" : "Disabled");
                    activationType = "plugin";
                    break;
                case "/ekttalk":
                    Plugin.Configuration.VoiceDialogue = !Plugin.Configuration.VoiceDialogue;
                    Plugin.Configuration.Save();
                    activationText = (Plugin.Configuration.VoiceDialogue ? "Enabled" : "Disabled");
                    activationType = "dialogue";
                    break;
                case "/ektbtalk":
                    Plugin.Configuration.VoiceBattleDialogue = !Plugin.Configuration.VoiceBattleDialogue;
                    Plugin.Configuration.Save();
                    activationText = (Plugin.Configuration.VoiceBattleDialogue ? "Enabled" : "Disabled");
                    activationType = "battle dialogue";
                    break;
                case "/ektbubble":
                    Plugin.Configuration.VoiceBubble = !Plugin.Configuration.VoiceBubble;
                    Plugin.Configuration.Save();
                    activationText = (Plugin.Configuration.VoiceBubble ? "Enabled" : "Disabled");
                    activationType = "bubble";
                    break;
                case "/ektchat":
                    Plugin.Configuration.VoiceChat = !Plugin.Configuration.VoiceChat;
                    Plugin.Configuration.Save();
                    activationText = (Plugin.Configuration.VoiceChat ? "Enabled" : "Disabled");
                    activationType = "chat";
                    break;
                case "/ektcutschoice":
                    Plugin.Configuration.VoicePlayerChoicesCutscene = !Plugin.Configuration.VoicePlayerChoicesCutscene;
                    Plugin.Configuration.Save();
                    activationText = (Plugin.Configuration.VoicePlayerChoicesCutscene ? "Enabled" : "Disabled");
                    activationType = "player choice in cutscene";
                    break;
                case "/ektchoice":
                    Plugin.Configuration.VoicePlayerChoices = !Plugin.Configuration.VoicePlayerChoices;
                    Plugin.Configuration.Save();
                    activationText = (Plugin.Configuration.VoicePlayerChoices ? "Enabled" : "Disabled");
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

        public static void ToggleConfigUi()
        {
            if (!Plugin.Configuration.FirstTime)
                Plugin.ConfigWindow.Toggle();
            else
                Plugin.FirstTimeWindow.Toggle();
        }

        public static void ToggleDialogUi() => Plugin.DialogExtraOptionsWindow.Toggle();

        public static void ToggleFirstTimeUi() => Plugin.FirstTimeWindow.Toggle();

        public unsafe static void PrintTargetInfo()
        {
            var localPlayer = Plugin.ClientState.LocalPlayer;

            if (localPlayer != null)
            {
                var target = localPlayer.TargetObject;
                if (target != null)
                {
                    var race = CharacterDataHelper.GetSpeakerRace(new EKEventId(0, TextSource.None), target, out var raceStr, out var modelId);
                    var gender = CharacterDataHelper.GetCharacterGender(new EKEventId(0, TextSource.None), target, race, out var modelBody);
                    var bodyType = LuminaHelper.GetENpcBase(target.DataId, new EKEventId(0, TextSource.None))?.BodyType;
                    PrintText(target.Name.TextValue, $"Target -> Name: {target.Name}, Race: {race}, Gender: {gender}, ModelID: {modelId}, ModelBody: {modelBody}, BodyType: {bodyType}");
                }
            }
        }
        
        public static void PrintDebugInfo()
        {
            var cond1 = Plugin.Condition[ConditionFlag.OccupiedInQuestEvent];
            var cond2 = Plugin.Condition[ConditionFlag.Occupied];
            var cond3 = Plugin.Condition[ConditionFlag.Occupied30];
            var cond4 = Plugin.Condition[ConditionFlag.Occupied33];
            var cond5 = Plugin.Condition[ConditionFlag.Occupied38];
            var cond6 = Plugin.Condition[ConditionFlag.Occupied39];
            var cond7 = Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent];
            var cond8 = Plugin.Condition[ConditionFlag.OccupiedInEvent];
            var cond9 = Plugin.Condition[ConditionFlag.OccupiedSummoningBell];
            var cond10 = Plugin.Condition[ConditionFlag.BoundByDuty];
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
            Plugin.ChatGui.Print(new Dalamud.Game.Text.XivChatEntry() { Name = name, Message = "Echokraut: " + text, Timestamp = DateTime.Now.Hour * 60 + DateTime.Now.Minute, Type = Dalamud.Game.Text.XivChatType.Echo });
        }

        internal static void Dispose()
        {
            Plugin.CommandManager.RemoveHandler("/ek");
            Plugin.CommandManager.RemoveHandler("/ekt");
            Plugin.CommandManager.RemoveHandler("/ekdb");
            Plugin.CommandManager.RemoveHandler("/ekid");
            Plugin.CommandManager.RemoveHandler("/ekttalk");
            Plugin.CommandManager.RemoveHandler("/ektbtalk");
            Plugin.CommandManager.RemoveHandler("/ektbubble");
            Plugin.CommandManager.RemoveHandler("/ektcutschoice");
            Plugin.CommandManager.RemoveHandler("/ektchoice");
            Plugin.CommandManager.RemoveHandler("/ekdel");
            Plugin.CommandManager.RemoveHandler("/ekdelmin");
        }
    }
}
