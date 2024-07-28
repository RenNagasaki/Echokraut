using Dalamud.Plugin.Services;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.Enums;
using ECommons.Configuration;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ManagedBass;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Config = Echokraut.DataClasses.Configuration;

namespace Echokraut.Helper
{
    public static class BackendHelper
    {
        static Random rand = new Random(Guid.NewGuid().GetHashCode());
        static Config Configuration;
        static IClientState ClientState;
        static ITTSBackend backend;
        static Echokraut Echokraut;

        public static void Setup(Config configuration, IClientState clientState, Echokraut echokraut, TTSBackends backendType)
        {
            Configuration = configuration;
            ClientState = clientState;
            Echokraut = echokraut;
            SetBackendType(backendType);
            PlayingHelper.Setup(echokraut, configuration);
        }

        public static void SetBackendType(TTSBackends backendType)
        {
            if (backendType == TTSBackends.Alltalk)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Creating backend instance: {backendType}", new EKEventId(0, Enums.TextSource.None));
                backend = new AlltalkBackend(Configuration.Alltalk, Configuration);
                getAndMapVoices(new EKEventId(0, TextSource.None));
            }
        }

        public static void OnSay(VoiceMessage voiceMessage, float volume)
        {
            var eventId = voiceMessage.eventId;
            PlayingHelper.Volume = volume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Starting voice inference: ", eventId);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, voiceMessage.Text.ToString(), eventId);

            switch (voiceMessage.Source)
            {
                case TextSource.Chat:
                case TextSource.AddonBubble:
                    PlayingHelper.AddRequestBubbleToQueue(voiceMessage);
                    break;
                case TextSource.AddonTalk:
                case TextSource.AddonBattleTalk:
                case TextSource.AddonCutSceneSelectString:
                case TextSource.AddonSelectString:
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

        static void getAndMapVoices(EKEventId eventId)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Loading and mapping voices", eventId);
            BackendVoiceHelper.Setup(backend.GetAvailableVoices(eventId));

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Success", eventId);
        }

        public static async Task<bool> GenerateVoice(VoiceMessage message)
        {
            var eventId = message.eventId;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating...", eventId);
            try
            {
                var text = message.Text;
                var voice = getVoice(eventId, message.Speaker);
                var language = message.Language;

                var ready = "";
                int i = 0;
                while (ready != "Ready" && i < 5)
                {
                    try
                    {
                        ready = await CheckReady(eventId);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
                    }

                    i++;
                }

                if (ready != "Ready")
                    return false;

                var responseStream = await backend.GenerateAudioStreamFromVoice(eventId, text, voice, language);
                if (message.Source == TextSource.AddonBubble || message.Source == TextSource.Chat)
                {
                    if (Configuration.SaveToLocal && Directory.Exists(Configuration.LocalSaveLocation))
                    {
                        var playedText = message;

                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Text: {playedText.Text}", eventId);
                        if (!string.IsNullOrWhiteSpace(playedText.Text))
                        {
                            var filePath = FileHelper.GetLocalAudioPath(Configuration.LocalSaveLocation, playedText);
                            if (FileHelper.WriteStreamToFile(eventId, filePath, responseStream as ReadSeekableStream))
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
            }

            return false;
        }

        public static async Task<string> CheckReady(EKEventId eventId)
        {
            return await backend.CheckReady(eventId);
        }

        static void getVoiceOrRandom(EKEventId eventId, NpcMapData npcData)
        {
            if (BackendVoiceHelper.Voices.Count == 0)
            {
                SetBackendType(TTSBackends.Alltalk);
            }

            var voiceItem = npcData.voiceItem;
            var mappedList = npcData.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ? Configuration.MappedPlayers : Configuration.MappedNpcs;

            if (voiceItem == null || mappedList.Find(p => p.voiceItem == voiceItem) == null)
            {
                var voiceItems = BackendVoiceHelper.Voices.FindAll(p => p.voiceName.Equals(npcData.name, StringComparison.OrdinalIgnoreCase));
                if (voiceItems.Count > 0)
                {
                    voiceItem = voiceItems[0];
                }

                if (voiceItem == null)
                {
                    voiceItems = BackendVoiceHelper.Voices.FindAll(p => p.gender == npcData.gender && p.race == npcData.race && p.voiceName.Contains("npc", StringComparison.OrdinalIgnoreCase));

                    if (voiceItems.Count == 0)
                        voiceItems = BackendVoiceHelper.Voices.FindAll(p => p.gender == npcData.gender && p.race == NpcRaces.Default && p.voiceName.Contains("npc", StringComparison.OrdinalIgnoreCase));

                    if (voiceItems.Count > 0)
                    {
                        var randomVoice = voiceItems[rand.Next(0, voiceItems.Count)];
                        voiceItem = randomVoice;
                    }
                }

                if (voiceItem == null)
                    voiceItem = BackendVoiceHelper.Voices.Find(p => p.voice == Constants.NARRATORVOICE);

                if (voiceItem == null)
                    voiceItem = BackendVoiceHelper.Voices[0];

                if (voiceItem != npcData.voiceItem)
                {
                    if (npcData.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chose voice: {voiceItem.voiceName} for Player: {npcData.name}", eventId);
                        Configuration.MappedPlayers = Configuration.MappedPlayers.OrderBy(p => p.ToString(true)).ToList();
                    }
                    else
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chose voice: {voiceItem.voiceName} for NPC: {npcData.name}", eventId);
                        Configuration.MappedNpcs = Configuration.MappedNpcs.OrderBy(p => p.ToString(true)).ToList();
                    }
                    npcData.voiceItem = voiceItem;
                    Configuration.Save();
                }
            }
        }

        static string getVoice(EKEventId eventId, NpcMapData npcData)
        {
            getVoiceOrRandom(eventId, npcData);

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, string.Format("Loaded voice: {0} for NPC: {1}", npcData.voiceItem.voice, npcData.name), eventId);
            return npcData.voiceItem.voice;
        }
    }
}
