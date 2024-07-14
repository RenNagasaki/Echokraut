using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.Enums;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.STD.Helper;
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
using static FFXIVClientStructs.FFXIV.Common.Component.BGCollision.Resource.Delegates;
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
        private static List<Stream> playingQueue = new List<Stream>();
        private static List<VoiceMessage> playingQueueText = new List<VoiceMessage>();
        private static List<VoiceMessage> requestingQueue = new List<VoiceMessage>();
        private static List<VoiceMessage> requestedQueue = new List<VoiceMessage>();
        private static Stream currentlyPlayingStream = null;
        private static VoiceMessage currentlyPlayingStreamText = null;
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
                backend = new AlltalkBackend(Configuration.Alltalk, Configuration);
                getAndMapVoices();
            }
        }

        public static void OnSay(VoiceMessage voiceMessage, float volume)
        {
            BackendHelper.volume = volume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Starting voice inference: ");
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, voiceMessage.Text.ToString());

            if (Configuration.LoadFromLocalFirst && Directory.Exists(Configuration.LocalSaveLocation) && voiceMessage.Speaker.voiceItem != null)
            {
                var result = LoadLocalAudio(voiceMessage);

                if (result)
                    return;
            }
            else if (!Directory.Exists(Configuration.LocalSaveLocation))
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}");

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
            ClearRequestedQueue();
            ClearPlayingQueue();
            //stopGeneratingThread.Start();
            if (playing)
            {
                playing = false;
                var thread = new Thread(stopPlaying);
                thread.Start();
            }
        }

        public static bool LoadLocalAudio(VoiceMessage voiceMessage)
        {
            try
            {
                string filePath = GetLocalAudioPath(voiceMessage);

                if (File.Exists(filePath))
                {
                    WaveStream mainOutputStream = new WaveFileReader(filePath);
                    playingQueue.Add(mainOutputStream);
                    playingQueueText.Add(new VoiceMessage { Text = "", Speaker = new NpcMapData { name = Path.GetFileName(Path.GetFileName(Path.GetDirectoryName(filePath))) } });
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Local file found. Location: {filePath}");

                    return true;
                }
                else
                {
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"No local file found. Location searched: {filePath}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading local audio: {ex}");
            }

            return false;
        }

        public static string GetLocalAudioPath(VoiceMessage voiceMessage)
        {
            string filePath = Configuration.LocalSaveLocation;
            if (!filePath.EndsWith(@"\"))
                filePath += @"\";
            filePath += $"{voiceMessage.Speaker.name}\\{voiceMessage.Speaker.race.ToString()}-{voiceMessage.Speaker.voiceItem?.voiceName}\\{DataHelper.VoiceMessageToFileName(voiceMessage.Text)}.wav";

            return filePath;
        }

        public static void WriteStreamToFile(string filePath, Stream stream)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Saving audio locally: {filePath}");
            try
            {
                stream.Position = 0;
                var rawStream = new RawSourceWaveStream(stream, new WaveFormat(24000, 16, 1));

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                WaveFileWriter.CreateWaveFile(filePath, rawStream);
                //using (MemoryStream memoryStream = new MemoryStream())
                //{
                //    using (
                //        WaveFileWriter writer = new WaveFileWriter(memoryStream, rawStream.WaveFormat))
                //    {
                //        stream.CopyTo(writer);
                //        memoryStream.Position = 0;

                //        //When I try to write the memoryStream to a local file on my hard drive, the file is not readable by VLC or other audio players. 
                //        using (FileStream filetest = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                //        {
                //            memoryStream.WriteTo(filetest);
                //        }
                //    }
                //}
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while saving audio locally: {ex.ToString()}");
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

        public static void ClearRequestedQueue()
        {
            requestedQueue.Clear();
        }

        public static void AddRequestedToQueue(VoiceMessage voiceMessage)
        {
            requestedQueue.Add(voiceMessage);
        }

        static void stopPlaying()
        {
            if (activePlayer != null)
            {
                activePlayer.PlaybackStopped -= SoundOut_PlaybackStopped;
                activePlayer.Stop();
            }
        }

        static void workPlayingQueue()
        {
            while (!stopThread)
            {
                if ((activePlayer == null || activePlayer.PlaybackState != PlaybackState.Playing) && playingQueue.Count > 0)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Playing next queue item");
                    try
                    {
                        var queueItem = playingQueue[0];
                        var queueItemText = playingQueueText[0];
                        playingQueueText.RemoveAt(0);
                        playingQueue.RemoveAt(0);
                        currentlyPlayingStream = queueItem;
                        currentlyPlayingStreamText = queueItemText;
                        var stream = new RawSourceWaveStream(queueItem, new WaveFormat(24000, 16, 1));
                        var volumeSampleProvider = new VolumeSampleProvider(stream.ToSampleProvider());
                        volumeSampleProvider.Volume = volume; // double the amplitude of every sample - may go above 0dB

                        activePlayer = new WasapiOut(AudioClientShareMode.Shared, 0);
                        //activePlayer.Volume = volume;
                        activePlayer.PlaybackStopped += SoundOut_PlaybackStopped;
                        activePlayer.Init(volumeSampleProvider);
                        activePlayer.Play();
                        char[] delimiters = new char[] { ' ' };

                        var estimatedLength = .5f;
                        if (!string.IsNullOrEmpty(queueItemText.Text))
                        {
                            var count = queueItemText.Text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Length;
                            estimatedLength = count / 2.1f;
                        }
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
                    AddRequestedToQueue(queueItem);

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

                    var responseStream = await backend.GenerateAudioStreamFromVoice(textLine, voice, language);

                    if (stillTalking && requestedQueue.Contains(message))
                    {
                        playingQueue.Add(responseStream);
                        playingQueueText.Add(message);
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
        private static void SoundOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (currentlyPlayingStream != null)
            {
                if (Configuration.CreateMissingLocalSaveLocation && !Directory.Exists(Configuration.LocalSaveLocation))
                    Directory.CreateDirectory(Configuration.LocalSaveLocation);

                if (Configuration.SaveToLocal && Directory.Exists(Configuration.LocalSaveLocation))
                {
                    var playedText = currentlyPlayingStreamText;

                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Text: {playedText.Text}");
                    if (!string.IsNullOrWhiteSpace(playedText.Text))
                    {
                        var filePath = GetLocalAudioPath(playedText);
                        var stream = currentlyPlayingStream;
                        WriteStreamToFile(filePath, stream);
                    }
                }
                else
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't save file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}");
                }
            }

            var soundOut = sender as WasapiOut;
            soundOut?.Dispose();
            playing = false;
            Plugin.StopLipSync();


            if (Configuration.AutoAdvanceTextAfterSpeechCompleted)
            {
                try
                {
                    if (BackendHelper.inDialog)
                        Plugin.addonTalkHelper.Click();
                    else
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Not inDialog");
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while 'auto advance text after speech completed': {ex}");
                }
            }
            else
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"No auto advance");
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
