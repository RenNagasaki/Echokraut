using Dalamud.Plugin.Services;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.Enums;
using ECommons.Configuration;
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
        public static List<BackendVoiceItem> mappedVoices = null;
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
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Creating backend instance: {backendType}");
                backend = new AlltalkBackend(Configuration.Alltalk, Configuration);
                getAndMapVoices();
            }
        }

        public static void OnSay(VoiceMessage voiceMessage, float volume)
        {
            PlayingHelper.Volume = volume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Starting voice inference: ");
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, voiceMessage.Text.ToString());

            switch (voiceMessage.Source)
            {
                case TextSource.Chat:
                    break;
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

        public static void OnCancel()
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Stopping voice inference");
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

        static void getAndMapVoices()
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Loading and mapping voices");
            mappedVoices = backend.GetAvailableVoices();
            mappedVoices.Sort((x, y) => x.ToString().CompareTo(y.ToString()));

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Success");
        }

        public static async void GenerateVoice(VoiceMessage message)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating...");
            try
            {
                var text = message.Text;
                var voice = getVoice(message.Speaker);
                var language = message.Language;

                var ready = "";
                int i = 0;
                while (ready != "Ready" && i < 5)
                {
                    try
                    {
                        ready = await CheckReady();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString());
                    }

                    i++;
                }

                if (ready != "Ready")
                    return;

                var responseStream = await backend.GenerateAudioStreamFromVoice(text, voice, language);

                if (message.Source == TextSource.AddonBubble)
                {
                    if (Configuration.SaveToLocal && Directory.Exists(Configuration.LocalSaveLocation))
                    {
                        var playedText = message;

                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Text: {playedText.Text}");
                        if (!string.IsNullOrWhiteSpace(playedText.Text))
                        {
                            var filePath = FileHelper.GetLocalAudioPath(Configuration.LocalSaveLocation, playedText);
                            var stream = responseStream;
                            FileHelper.WriteStreamToFile(filePath, stream as ReadSeekableStream);
                            PlayingHelper.PlayingBubbleQueue.Add(filePath);
                            PlayingHelper.PlayingBubbleQueueText.Add(message);
                        }
                    }
                    else
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't save file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}");
                    }
                }
                else
                {
                    if (PlayingHelper.RequestedQueue.Contains(message))
                    {
                        PlayingHelper.PlayingQueue.Add(responseStream);
                        PlayingHelper.PlayingQueueText.Add(message);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString());
            }
        }

        public static async Task<string> CheckReady()
        {
            return await backend.CheckReady();
        }

        static void getVoiceOrRandom(NpcMapData npcData)
        {
            var voiceItem = npcData.voiceItem;

            if (voiceItem == null || Configuration.MappedNpcs.Find(p => p.voiceItem == voiceItem) == null)
            {
                var voiceItems = mappedVoices.FindAll(p => p.voiceName.Equals(npcData.name, StringComparison.OrdinalIgnoreCase));
                if (voiceItems.Count > 0)
                {
                    voiceItem = voiceItems[0];
                }

                if (voiceItem == null)
                {
                    voiceItems = mappedVoices.FindAll(p => p.gender == npcData.gender && p.race == npcData.race && p.voiceName.Contains("npc", StringComparison.OrdinalIgnoreCase));

                    if (voiceItems.Count == 0)
                        voiceItems = mappedVoices.FindAll(p => p.gender == npcData.gender && p.race == NpcRaces.Default && p.voiceName.Contains("npc", StringComparison.OrdinalIgnoreCase));

                    if (voiceItems.Count > 0)
                    {
                        var randomVoice = voiceItems[rand.Next(0, voiceItems.Count)];
                        voiceItem = randomVoice;
                    }
                }

                if (voiceItem == null)
                    voiceItem = mappedVoices.Find(p => p.voice == Constants.NARRATORVOICE);

                if (voiceItem == null)
                    voiceItem = mappedVoices[0];

                if (voiceItem != npcData.voiceItem)
                {
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chose voice: {voiceItem.voiceName} for NPC: {npcData.name}");
                    Configuration.MappedNpcs.Remove(npcData);
                    npcData.voiceItem = voiceItem;
                    Configuration.MappedNpcs.Add(npcData);
                    Configuration.MappedNpcs = Configuration.MappedNpcs.OrderBy(p => p.ToString(true)).ToList();
                    Configuration.Save();
                }
            }
        }

        static string getVoice(NpcMapData npcData)
        {
            getVoiceOrRandom(npcData);

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, string.Format("Loaded voice: {0} for NPC: {1}", npcData.voiceItem.voice, npcData.name));
            return npcData.voiceItem.voice;
        }
    }
}
