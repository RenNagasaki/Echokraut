using Dalamud.Plugin.Services;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using Echokraut.Windows;
using ManagedBass;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Config = Echokraut.DataClasses.Configuration;

namespace Echokraut.Helper.API
{
    public static class BackendHelper
    {
        static Random Rand { get; set; }
        static Config Configuration { get; set; }
        static IClientState ClientState { get; set; }
        static ITTSBackend Backend { get; set; }
        static Echokraut Echokraut { get; set; }

        public static void Setup(Echokraut echokraut, Config configuration, IClientState clientState, IFramework framework, TTSBackends backendType)
        {
            Configuration = configuration;
            ClientState = clientState;
            Echokraut = echokraut;
            Rand = new Random(Guid.NewGuid().GetHashCode());
            SetBackendType(backendType);
            PlayingHelper.Setup(echokraut, configuration, framework);
        }

        public static void SetBackendType(TTSBackends backendType)
        {
            if (backendType == TTSBackends.Alltalk)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Creating backend instance: {backendType}", new EKEventId(0, TextSource.None));
                Backend = new AlltalkBackend(Configuration.Alltalk, Configuration);
                GetAndMapVoices(new EKEventId(0, TextSource.None));
            }
        }

        public static bool ReloadService(string reloadModel, EKEventId eventId)
        {
            return Backend.ReloadService(reloadModel, eventId).Result;
        }

        public static void OnSay(VoiceMessage voiceMessage, float volume)
        {
            var eventId = voiceMessage.eventId;
            PlayingHelper.Volume = volume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Starting voice inference: {voiceMessage.Language}", eventId);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, voiceMessage.Text.ToString(), eventId);

            switch (voiceMessage.Source)
            {
                case TextSource.Chat:
                case TextSource.AddonBubble:
                    PlayingHelper.AddRequestBubbleToQueue(voiceMessage);
                    break;
                case TextSource.AddonTalk:
                case TextSource.AddonBattleTalk:
                case TextSource.AddonCutsceneSelectString:
                case TextSource.AddonSelectString:
                case TextSource.VoiceTest:
                    PlayingHelper.AddRequestToQueue(voiceMessage);
                    break;
            }
        }

        public static void OnCancel(EKEventId eventId)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Stopping voice inference", eventId);
            PlayingHelper.ClearRequestingQueue();
            PlayingHelper.ClearRequestedQueue();
            PlayingHelper.ClearPlayingQueue();
            //stopGeneratingThread.Start();
            if (PlayingHelper.Playing)
            {
                PlayingHelper.Playing = false;
                var thread = new Thread(PlayingHelper.StopPlaying);
                thread.Start();
            }
        }

        static void GetAndMapVoices(EKEventId eventId)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Loading and mapping voices", eventId);
            var backendVoices = Backend.GetAvailableVoices(eventId);

            var newVoices = backendVoices.FindAll(p => Configuration.EchokrautVoices.Find(f => f.BackendVoice == p) == null);

            if (newVoices.Count > 0)
            {
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Adding {newVoices.Count} new Voices", eventId);
                foreach (var newVoice in newVoices)
                {
                    var voiceName = Path.GetFileNameWithoutExtension(newVoice);
                    var newEkVoice = new EchokrautVoice()
                    {
                        BackendVoice = newVoice,
                        VoiceName = voiceName,
                        Volume = 1,
                        AllowedGenders = new List<Genders>(),
                        AllowedRaces = new List<NpcRaces>(),
                        IsDefault = newVoice.Equals(Constants.NARRATORVOICE, StringComparison.OrdinalIgnoreCase),
                        UseAsRandom = voiceName.Contains("NPC")
                    };

                    NpcDataHelper.ReSetVoiceGenders(newEkVoice, eventId);
                    NpcDataHelper.ReSetVoiceRaces(newEkVoice, eventId);

                    Configuration.EchokrautVoices.Add(newEkVoice);
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Added {newEkVoice}", eventId);
                }

                Configuration.Save();
            }

            var oldVoices =
                Configuration.EchokrautVoices.FindAll(p => backendVoices.Find(f => f == p.BackendVoice) == null);
            
            if (oldVoices.Count > 0)
            {
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Replacing {oldVoices.Count} old Voices", eventId);
                foreach (var oldVoice in oldVoices)
                {
                    EchokrautVoice? newEkVoice = null;

                    if (oldVoice.BackendVoice.Contains("NPC"))
                    {
                        if (oldVoice.AllowedRaces.Count > 0 && NpcDataHelper.IsGenderedRace(oldVoice.AllowedRaces[0]))
                            newEkVoice = Configuration.EchokrautVoices.Find(
                                f => !oldVoices.Contains(f) &&
                                     f.VoiceName.Contains("NPC") &&
                                     f.IsChildVoice == oldVoice.IsChildVoice &&
                                     !oldVoice.AllowedGenders.Except(f.AllowedGenders).Any() &&
                                     !oldVoice.AllowedRaces.Except(f.AllowedRaces).Any()
                            );
                        else
                            newEkVoice = Configuration.EchokrautVoices.Find(
                                f => !oldVoices.Contains(f) &&
                                     f.VoiceName.Contains("NPC") &&
                                     f.IsChildVoice == oldVoice.IsChildVoice &&
                                     !oldVoice.AllowedRaces.Except(f.AllowedRaces).Any()
                            );
                    }
                    else
                    {
                        newEkVoice = Configuration.EchokrautVoices.Find(
                            f => !oldVoices.Contains(f) &&
                                 f.VoiceName == oldVoice.VoiceName);
                    }

                    if (newEkVoice != null)
                    {
                        NpcDataHelper.MigrateOldData(oldVoice, newEkVoice);
                        Configuration.EchokrautVoices.Remove(oldVoice);
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name,
                                        $"Replaced {oldVoice} with {newEkVoice}", eventId);
                        continue;
                    }

                    Configuration.EchokrautVoices.Remove(oldVoice);
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Failed to replace {oldVoice}", eventId);
                }

                Configuration.Save();
            }

            NpcDataHelper.MigrateOldData();

            NpcDataHelper.RefreshSelectables();
            ConfigWindow.UpdateDataVoices = true;

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Success", eventId);
        }

        public static async Task<bool> GenerateVoice(VoiceMessage message)
        {
            var eventId = message.eventId;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating...", eventId);
            try
            {
                var text = message.Text;
                var voice = GetVoice(eventId, message.Speaker);
                var language = message.Language;

                Stream responseStream = null;
                var i = 0;
                while (i < 10 && responseStream == null)
                {
                    try
                    {
                        responseStream = await Backend.GenerateAudioStreamFromVoice(eventId, text, voice, language);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
                    }

                    i++;
                }

                if (message.Source == TextSource.AddonBubble || message.Source == TextSource.Chat)
                {
                    if (Configuration.SaveToLocal && Directory.Exists(Configuration.LocalSaveLocation))
                    {
                        var playedText = message;

                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Text: {playedText.Text}", eventId);
                        if (!string.IsNullOrWhiteSpace(playedText.Text))
                        {
                            var filePath = FileHelper.GetLocalAudioPath(Configuration.LocalSaveLocation, playedText);
                            if (FileHelper.WriteStreamToFile(eventId, filePath, responseStream))
                            {
                                PlayingHelper.PlayingBubbleQueue.Add(filePath);
                                PlayingHelper.PlayingBubbleQueueText.Add(message);
                            }
                            return true;
                        }
                    }
                    else
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't save file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}", eventId);
                    }
                }
                else
                {
                    if (PlayingHelper.RequestedQueue.Contains(message))
                    {
                        PlayingHelper.PlayingQueue.Add(responseStream);
                        PlayingHelper.PlayingQueueText.Add(message);

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            }

            return false;
        }

        public static async Task<string> CheckReady(EKEventId eventId)
        {
            return await Backend.CheckReady(eventId);
        }

        public static void GetVoiceOrRandom(EKEventId eventId, NpcMapData npcData)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Searching voice: {npcData.Voice?.VoiceName ?? ""} for NPC: {npcData.Name}", eventId);
            var voiceItem = npcData.Voice;
            var isChild = npcData.IsChild;
            var mappedList = npcData.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ? Configuration.MappedPlayers : Configuration.MappedNpcs;

            if (voiceItem == null || voiceItem == Configuration.EchokrautVoices.Find(p => p.IsDefault))
            {
                var npcName = npcData.Name;

                var voiceItems = Configuration.EchokrautVoices.FindAll(p => p.VoiceName.Contains(npcName, StringComparison.OrdinalIgnoreCase));
                if (voiceItems.Count > 0)
                {
                    voiceItem = voiceItems[0];
                }

                if (voiceItem == null)
                {
                    var isGenderedRace = NpcDataHelper.IsGenderedRace(npcData.Race);
                        voiceItems = Configuration.EchokrautVoices.FindAll(p => p.FitsNpcData(npcData.Gender, npcData.Race, isChild, isGenderedRace));
                        
                    if (voiceItems.Count > 0)
                    {
                        var randomVoice = voiceItems[Rand.Next(0, voiceItems.Count)];
                        voiceItem = randomVoice;
                    }
                }

                if (voiceItem == null)
                    voiceItem = Configuration.EchokrautVoices.Find(p => p.IsDefault);

                if (voiceItem != npcData.Voice)
                {
                    if (npcData.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chose voice: {voiceItem} for Player: {npcName}", eventId);
                    }
                    else
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chose voice: {voiceItem} for NPC: {npcName}", eventId);
                    }
                    npcData.Voice = voiceItem;
                    Configuration.Save();
                }
            }
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found voice: {voiceItem} for NPC: {npcData.Name}", eventId);
        }

        private static string GetVoice(EKEventId eventId, NpcMapData npcData)
        {
            GetVoiceOrRandom(eventId, npcData);

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, string.Format("Loaded voice: {0} for NPC: {1}", npcData.Voice.BackendVoice, npcData.Name), eventId);
            return npcData.Voice.BackendVoice;
        }
    }
}
