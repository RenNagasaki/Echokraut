using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using Echokraut.Helper.DataHelper;
using Echokraut.Windows;
using System;
using System.Reflection;

namespace Echokraut.Helper.Functional
{
    public static class CommandHelper
    {
        private static Configuration Configuration;
        private static IChatGui ChatGui;
        private static IClientState ClientState;
        private static IDataManager DataManager;
        private static ICommandManager CommandManager;
        private static ConfigWindow ConfigWindow;

        public static void Setup(Configuration configuration, IChatGui chatGui, IClientState clientState, IDataManager dataManager, ICommandManager commandManager, ConfigWindow configWindow)
        {
            Configuration = configuration;
            ChatGui = chatGui;
            ClientState = clientState;
            DataManager = dataManager;
            CommandManager = commandManager;
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
                HelpMessage = "Opens the configuration window"
            });
        }

        public static void OnCommand(string command, string args)
        {
            // in response to the slash command, just toggle the display status of our config ui

            switch (command)
            {
                case "/ek":
                    ToggleConfigUI();
                    break;
                case "/ekid":
                    PrintTargetInfo(ChatGui, ClientState, DataManager);
                    break;
                case "/ekt":
                    Configuration.Enabled = !Configuration.Enabled;
                    Configuration.Save();
                    break;
                case "/ekttalk":
                    Configuration.VoiceDialogue = !Configuration.VoiceDialogue;
                    Configuration.Save();
                    break;
                case "/ektbtalk":
                    Configuration.VoiceBattleDialogue = !Configuration.VoiceBattleDialogue;
                    Configuration.Save();
                    break;
                case "/ektbubble":
                    Configuration.VoiceBubble = !Configuration.VoiceBubble;
                    Configuration.Save();
                    break;
                case "/ektchat":
                    Configuration.VoiceChat = !Configuration.VoiceChat;
                    Configuration.Save();
                    break;
                case "/ektcutschoice":
                    Configuration.VoicePlayerChoicesCutscene = !Configuration.VoicePlayerChoicesCutscene;
                    Configuration.Save();
                    break;
                case "/ektchoice":
                    Configuration.VoicePlayerChoices = !Configuration.VoicePlayerChoices;
                    Configuration.Save();
                    break;
            }

            LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"New Command triggered: {command}", new EKEventId(0, TextSource.None));
        }

        public static void ToggleConfigUI() => ConfigWindow.Toggle();

        public unsafe static void PrintTargetInfo(IChatGui chatGui, IClientState clientState, IDataManager dataManager)
        {
            var localPlayer = clientState.LocalPlayer;

            if (localPlayer != null)
            {
                var target = localPlayer.TargetObject;
                if (target != null)
                {
                    var race = CharacterDataHelper.GetSpeakerRace(dataManager, new EKEventId(0, TextSource.None), target, out var raceStr, out var modelId);
                    var gender = CharacterDataHelper.GetCharacterGender(dataManager, new EKEventId(0, TextSource.None), target, race, out var modelBody);
                    var bodyType = LuminaHelper.GetENpcBase(target.DataId)?.BodyType;
                    chatGui.Print(new Dalamud.Game.Text.XivChatEntry() { Name = target.Name, Message = $"Echokraut Target -> Name: {target.Name}, Race: {race}, Gender: {gender}, ModelID: {modelId}, ModelBody: {modelBody}, BodyType: {bodyType}", Timestamp = 22 * 60 + 12, Type = Dalamud.Game.Text.XivChatType.Echo });
                }
            }
        }

        internal static void Dispose()
        {
            CommandManager.RemoveHandler("/ek");
            CommandManager.RemoveHandler("/ekt");
            CommandManager.RemoveHandler("/ekid");
            CommandManager.RemoveHandler("/ekttalk");
            CommandManager.RemoveHandler("/ektbtalk");
            CommandManager.RemoveHandler("/ektbubble");
            CommandManager.RemoveHandler("/ektcutschoice");
            CommandManager.RemoveHandler("/ektchoice");
        }
    }
}
