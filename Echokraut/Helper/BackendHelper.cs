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

namespace Echokraut.Helper
{
    public class BackendHelper
    {
        static private bool stopThread = false;
        static private List<RawSourceWaveStream> voiceQueue = new List<RawSourceWaveStream>();
        static private WasapiOut activePlayer = null;
        static private bool playing = false;
        static Configuration Configuration;
        static Echokraut Plugin;
        static float volume = 1f;
        public List<BackendVoiceItem> mappedVoices = null;
        public bool queueText = false;
        public Thread queueThread = new Thread(workQueue);
        ITTSBackend backend;
        Random rand = new Random(Guid.NewGuid().GetHashCode());
        bool stillTalking = false;

        internal BackendHelper(Configuration configuration, Echokraut plugin)
        {
            Configuration = configuration;
            Plugin = plugin;
            queueThread.Start();

        }

        public void SetBackendType(TTSBackends backendType)
        {
            if (backendType == TTSBackends.Alltalk)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Creating backend instance: {backendType}");
                backend = new AlltalkBackend(Configuration.Alltalk);
                getAndMapVoices();
            }
        }

        public void OnSay(VoiceMessage voiceMessage, float volume)
        {
            BackendHelper.volume = volume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Starting voice inference: ");
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, voiceMessage.ToString());
            if (voiceMessage.Source == "Chat")
            {

            }
            else
                generateVoice(DataHelper.analyzeAndImproveText(voiceMessage.Text), getVoice(voiceMessage.Speaker), voiceMessage.Language);
        }

        public void OnCancel()
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Stopping voice inference");
            stillTalking = false;
            if (playing)
            {
                playing = false;
                if (activePlayer != null)
                {
                    activePlayer.Stop();
                }
                backend.StopGenerating();
                voiceQueue.Clear();
            }
        }

        static void workQueue()
        {
            while (!stopThread)
            {
                if (!playing && voiceQueue.Count > 0)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Playing next Queue Item");
                    var queueItem = voiceQueue[0];
                    voiceQueue.RemoveAt(0);
                    try
                    {
                        var volumeSampleProvider = new VolumeSampleProvider(queueItem.ToSampleProvider());
                        volumeSampleProvider.Volume = volume; // double the amplitude of every sample - may go above 0dB

                        activePlayer = new WasapiOut(AudioClientShareMode.Shared, 0);
                        //activePlayer.Volume = volume;
                        activePlayer.PlaybackStopped += SoundOut_PlaybackStopped;
                        activePlayer.Init(volumeSampleProvider);
                        activePlayer.Play();
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

        void getAndMapVoices()
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Loading and mapping voices");
            mappedVoices = backend.GetAvailableVoices();
            mappedVoices.Sort((x, y) => x.ToString().CompareTo(y.ToString()));
            mappedVoices.Insert(0, new BackendVoiceItem() { voiceName = "Remove", race = NpcRaces.Default, gender = Gender.None });

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Success");
        }

        public async void generateVoice(string text, string voice, string language)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating Audio");

            try
            {
                var splitText = new List<string>() { text };//prepareAndSentenceSplit(text).ToList();
                //splitText.RemoveAt(splitText.Count - 1);

                foreach (var textLine in splitText)
                {
                    stillTalking = true;

                    var ready = "";

                    int i = 0;
                    while (ready != "Ready" && i < 50)
                    {
                        i++;
                        ready = await CheckReady();
                    }

                    var responseStream = await backend.GenerateAudioStreamFromVoice(textLine, voice, language);

                    if (stillTalking)
                    {
                        var s = new RawSourceWaveStream(responseStream, new WaveFormat(24000, 16, 1));
                        voiceQueue.Add(s);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, ex.ToString());
            }

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Done");
        }

        public async Task<string> CheckReady()
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
            var soundOut = sender as WasapiOut;
            soundOut?.Dispose();
            playing = false;
            Plugin.StopLipSync();


            if (Configuration.AutoAdvanceTextAfterSpeechCompleted)
            {
                try
                {
                    ClickHelper.Click();
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while 'auto advance text after speech completed': {ex}");
                }
            }
        }

        void getVoiceOrRandom(NpcMapData npcData)
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

                    mappedVoices.ForEach((voiceItem) => { LogHelper.Info(MethodBase.GetCurrentMethod().Name, voiceItem.ToString()); });
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
                    Configuration.MappedNpcs.Remove(npcData);
                    npcData.voiceItem = voiceItem;
                    Configuration.MappedNpcs.Add(npcData);
                    Configuration.Save();
                }
            }
        }

        string getVoice(NpcMapData npcData)
        {
            getVoiceOrRandom(npcData);

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, string.Format("Loaded voice: {0} for NPC: {1}", npcData.voiceItem.voice, npcData.name));
            return npcData.voiceItem.voice;
        }
    }
}
