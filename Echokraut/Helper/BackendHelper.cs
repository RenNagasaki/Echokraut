using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.Enums;
using ECommons.Configuration;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        static private IPluginLog Log;
        public List<BackendVoiceItem> mappedVoices = null;
        public bool queueText = false;
        public Thread queueThread = new Thread(workQueue);
        ITTSBackend backend;
        Random rand = new Random(Guid.NewGuid().GetHashCode());
        static Configuration Configuration;
        bool stillTalking = false;

        internal BackendHelper(Configuration configuration, IPluginLog log)
        {
            Log = log;
            Configuration = configuration;
            queueThread.Start();

        }

        public void SetBackendType(TTSBackends backendType)
        {
            if (backendType == TTSBackends.Alltalk)
            {
                Log.Info($"Creating backend instance: {backendType}");
                backend = new AlltalkBackend(Configuration.Alltalk);
                getAndMapVoices();
            }
        }

        public void OnSay(VoiceMessage voiceMessage)
        {
            Log.Info("Starting voice inference: ");
            Log.Info(voiceMessage.ToString());
            if (voiceMessage.Source == "Chat")
            {

            }
            else
                generateVoice(DataHelper.analyzeAndImproveText(voiceMessage.Text), getVoice(voiceMessage.Speaker), voiceMessage.Language);
        }

        public void OnCancel()
        {
            Log.Info("Stopping voice inference");
            if (playing)
            {
                if (activePlayer != null)
                {
                    activePlayer.Stop();
                }
                backend.StopGenerating(Log);
                voiceQueue.Clear();
                stillTalking = false;
                playing = false;
            }
        }

        static void workQueue()
        {
            while (!stopThread)
            {
                if (!playing && voiceQueue.Count > 0)
                {
                    var queueItem = voiceQueue[0];
                    voiceQueue.RemoveAt(0);
                    Log.Info("Playing next Queue Item");
                    activePlayer = new WasapiOut(AudioClientShareMode.Shared, 0);
                    activePlayer.PlaybackStopped += SoundOut_PlaybackStopped;
                    activePlayer.Init(queueItem);
                    activePlayer.Play();
                    playing = true;
                }

                Thread.Sleep(100);
            }
        }

        void getAndMapVoices()
        {
            Log.Info("Loading and mapping voices");
            mappedVoices = backend.GetAvailableVoices(Log);
            mappedVoices.Sort((x, y) => x.ToString().CompareTo(y.ToString()));
            mappedVoices.Insert(0, new BackendVoiceItem() { voiceName = "Remove", race = NpcRaces.Default, gender = Gender.None });

            Log.Info("Success");
        }

        public async void generateVoice(string text, string voice, string language)
        {
            Log.Info("Generating Audio");

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
                        Thread.Sleep(100);
                        ready = await CheckReady();
                    }

                    if (ready == "Ready")
                    {
                        var responseStream = await backend.GenerateAudioStreamFromVoice(Log, textLine, voice, language);

                        if (stillTalking)
                        {
                            var s = new RawSourceWaveStream(responseStream, new WaveFormat(24000, 16, 1));
                            voiceQueue.Add(s);
                        }
                    }
                    else
                        Log.Error("Backend did not respond in time, not ready");
                }
            }
            catch (Exception ex)
            {
                Log.Info(ex.ToString());
            }

            Log.Info("Done");
        }

        public async Task<string> CheckReady()
        {
            return await backend.CheckReady(Log);
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
            soundOut.Dispose();
            playing = false;


            if (Configuration.AutoAdvanceTextAfterSpeechCompleted)
            {
                ClickHelper.Click();
            }
        }

        void getVoiceOrRandom(NpcMapData npcData)
        {
            var voiceItem = npcData.voiceItem;

            if (voiceItem == null)
            {
                var voiceItems = mappedVoices.FindAll(p => p.voiceName == npcData.name && npcData.patchVersion >= p.patchVersion);
                if (voiceItems.Count > 0)
                {
                    voiceItems.Sort((a, b) => b.patchVersion.CompareTo(a.patchVersion));
                    voiceItem = voiceItems[0];
                }

                if (voiceItem == null)
                {
                    voiceItems = mappedVoices.FindAll(p => p.gender == npcData.gender && p.race == npcData.race && p.voiceName.Contains("NPC"));

                    if (voiceItems.Count == 0)
                        voiceItems = mappedVoices.FindAll(p => p.gender == npcData.gender && p.race == NpcRaces.Default && p.voiceName.Contains("NPC"));

                    mappedVoices.ForEach((voiceItem) => { Log.Info(voiceItem.ToString()); });
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

            Log.Info(string.Format("Loaded voice: {0} for NPC: {1}", npcData.voiceItem.voice, npcData.name));
            return npcData.voiceItem.voice;
        }
    }
}
