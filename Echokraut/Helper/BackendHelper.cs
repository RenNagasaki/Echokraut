using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.Enums;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Echokraut.Helper
{
    public static class BackendHelper
    {
        public static Thread requestingQueueThread = new Thread(workRequestingQueue);
        public static Thread playingQueueThread = new Thread(workPlayingQueue);
        public static List<BackendVoiceItem> mappedVoices = null;
        public static bool queueText = false;
        public static bool inDialog = false;
        private static List<RawSourceWaveStream> playingQueue = new List<RawSourceWaveStream>();
        private static List<VoiceMessage> playingQueueText = new List<VoiceMessage>();
        private static List<VoiceMessage> requestingQueue = new List<VoiceMessage>();
        private static WasapiOut activePlayer = null;
        private static bool stopThread = false;
        private static bool playing = false;
        static Random rand = new Random(Guid.NewGuid().GetHashCode());
        static Configuration Configuration;
        static bool stillTalking = false;
        static ITTSBackend backend;
        static float volume = 1f;
        static Echokraut Plugin;

        public static void Setup(Configuration configuration, Echokraut plugin, TTSBackends backendType)
        {
            Configuration = configuration;
            Plugin = plugin;
            playingQueueThread.Start();
            requestingQueueThread.Start();
            SetBackendType(backendType);
        }

        public static void SetBackendType(TTSBackends backendType)
        {
            if (backendType == TTSBackends.Alltalk)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Creating backend instance: {backendType}");
                backend = new AlltalkBackend(Configuration.Alltalk);
                getAndMapVoices();
            }
        }

        public static void OnSay(VoiceMessage voiceMessage, float volume)
        {
            BackendHelper.volume = volume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Starting voice inference: ");
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, voiceMessage.Text.ToString());
            if (voiceMessage.Source == "Chat")
            {

            }
            else
                AddRequestToQueue(voiceMessage);
        }

        public static void OnCancel()
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Stopping voice inference");
            stillTalking = false;
            ClearRequestingQueue();
            ClearPlayingQueue();
            //stopGeneratingThread.Start();
            if (playing)
            {
                playing = false;
                var thread = new Thread(stopPlaying);
                thread.Start();
            }
        }

        public static void ClearPlayingQueue()
        {
            playingQueue.Clear();
            playingQueueText.Clear();
        }

        public static void ClearRequestingQueue()
        {
            requestingQueue.Clear();
        }

        public static void AddRequestToQueue(VoiceMessage voiceMessage)
        {
            requestingQueue.Add(voiceMessage);
        }

        static void stopPlaying()
        {
            if (activePlayer != null)
            {
                activePlayer.PlaybackStopped -= SoundOut_PlaybackStopped;
                activePlayer.PlaybackStopped += SoundOut_PlaybackManuallyStopped;
                activePlayer.Stop();
            }
        }

        static void workPlayingQueue()
        {
            while (!stopThread)
            {
                if (!playing && playingQueue.Count > 0)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Playing next queue item");
                    var queueItem = playingQueue[0];
                    var queueItemText = playingQueueText[0];
                    playingQueue.RemoveAt(0);
                    playingQueueText.RemoveAt(0);
                    try
                    {
                        var volumeSampleProvider = new VolumeSampleProvider(queueItem.ToSampleProvider());
                        volumeSampleProvider.Volume = volume; // double the amplitude of every sample - may go above 0dB

                        activePlayer = new WasapiOut(AudioClientShareMode.Shared, 0);
                        //activePlayer.Volume = volume;
                        activePlayer.PlaybackStopped += SoundOut_PlaybackStopped;
                        activePlayer.Init(volumeSampleProvider);
                        activePlayer.Play();
                        char[] delimiters = new char[] { ' ' };
                        var count = queueItemText.Text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Length;
                        var estimatedLength = count / 2.1f;
                        Plugin.lipSyncHelper.TriggerLipSync(queueItemText.Speaker.name, estimatedLength);
                        playing = true;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while working queue: {ex}");

                        if (activePlayer != null)
                            activePlayer.Stop();
                    }
                }
            }
        }

        static void workRequestingQueue()
        {
            while (!stopThread)
            {
                if (requestingQueue.Count > 0)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating next queued audio");
                    var queueItem = requestingQueue[0];
                    requestingQueue.RemoveAt(0);

                    generateVoice(queueItem);
                }
            }
        }

        static void getAndMapVoices()
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Loading and mapping voices");
            mappedVoices = backend.GetAvailableVoices();
            mappedVoices.Sort((x, y) => x.ToString().CompareTo(y.ToString()));
            mappedVoices.Insert(0, new BackendVoiceItem() { voiceName = "Remove", race = NpcRaces.Default, gender = Gender.None });

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Success");
        }

        public static async void generateVoice(VoiceMessage message)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating...");
            try
            {
                var text = DataHelper.analyzeAndImproveText(message.Text);
                var voice = getVoice(message.Speaker);
                var language = message.Language;
                var splitText = new List<string>() { text };
                
                //prepareAndSentenceSplit(text).ToList();
                //splitText.RemoveAt(splitText.Count - 1);

                foreach (var textLine in splitText)
                {
                    stillTalking = true;
                    var ready = await CheckReady();

                    var responseStream = await backend.GenerateAudioStreamFromVoice(textLine, voice, language);

                    if (stillTalking)
                    {
                        var s = new RawSourceWaveStream(responseStream, new WaveFormat(24000, 16, 1));
                        playingQueue.Add(s);
                        playingQueueText.Add(message);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, ex.ToString());
            }
        }

        public static async Task<string> CheckReady()
        {
            return await backend.CheckReady();
        }

        //private string[] prepareAndSentenceSplit(string text)
        //{
        //    text = text.Replace("...", ",,,");
        //    text = text.Replace("..", ",,");
        //    text = text.Replace(".", "D0T.");
        //    text = text.Replace("!", "EXC!");
        //    text = text.Replace("?", "QUEST?");

        //    var splitText = text.Split(Constants.SENTENCESEPARATORS);

        //    for (var i = 0; i < splitText.Length; i++)
        //    {
        //        splitText[i] = splitText[i].Replace(",,,", "...").Replace(",,", "..").Replace("D0T", ".").Replace("EXC", "!").Replace("QUEST", "?").Trim();
        //    }

        //    return splitText;
        //}

        private static void SoundOut_PlaybackManuallyStopped(object? sender, StoppedEventArgs e)
        {
            var soundOut = sender as WasapiOut;
            soundOut?.Dispose();
            playing = false;
            Plugin.StopLipSync();
        }
        private static void SoundOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            var soundOut = sender as WasapiOut;
            soundOut?.Dispose();
            playing = false;
            Plugin.StopLipSync();

            if (Configuration.AutoAdvanceTextAfterSpeechCompleted)
            {
                try
                {
                    if (BackendHelper.inDialog)
                        ClickHelper.Click();
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while 'auto advance text after speech completed': {ex}");
                }
            }
        }

        static void getVoiceOrRandom(NpcMapData npcData)
        {
            var voiceItem = npcData.voiceItem;

            if (voiceItem == null)
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

                if (voiceItem != npcData.voiceItem)
                {
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chose voice: {voiceItem.voiceName} for NPC: {npcData.name}");
                    Configuration.MappedNpcs.Remove(npcData);
                    npcData.voiceItem = voiceItem;
                    Configuration.MappedNpcs.Add(npcData);
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

        public static void Dispose()
        {
            stopThread = true;
        }
    }
}
