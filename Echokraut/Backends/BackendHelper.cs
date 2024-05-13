using Dalamud.Plugin.Services;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.Enums;
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

namespace FF14_Echokraut.Helpers
{
    public class BackendHelper
    {
        static private bool stopThread = false;
        static private List<RawSourceWaveStream> voiceQueue = new List<RawSourceWaveStream>();
        static private WasapiOut activePlayer = null;
        static private bool playing = false;
        static private IPluginLog Log;
        public List<BackendVoiceItem> mappedVoices = null;
        public Dictionary<string, NpcMapData> npcDatas = null;
        public bool queueText = false;
        public Thread queueThread = new Thread(workQueue);
        ITTSBackend backend;
        Random rand = new Random(Guid.NewGuid().GetHashCode());
        Configuration configuration;
        BackendData data;

        internal BackendHelper(Configuration configuration, IPluginLog log)
        {
            Log = log;
            this.configuration = configuration;
            queueThread.Start();

        }

        public void SetBackendType(TTSBackends backendType)
        {
            if (backendType == TTSBackends.Alltalk)
            {
                backend = new AlltalkBackend();
                data = configuration.Alltalk;
                getAndMapVoices();
            }
        }

        internal void addNPCData(NpcMapData npcData)
        {
            npcDatas.Add(npcData.name, npcData);
        }

        public void OnSay(VoiceMessage voiceMessage)
        {
            if (voiceMessage.Source == "Chat")
            {

            }
            else
                generateVoice(analyzeAndImproveText(voiceMessage.Text), getAllTalkVoice(voiceMessage.NpcId, voiceMessage.Speaker), voiceMessage.Language);
        }

        public void OnCancel()
        {
            if (playing)
            {
                if (activePlayer != null)
                {
                    activePlayer.Stop();
                }
                backend.StopGenerating(data);
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
                    Log.Debug("Playing next Queue Item");
                    activePlayer = new WasapiOut(AudioClientShareMode.Shared, 0);
                    activePlayer.PlaybackStopped += SoundOut_PlaybackStopped;
                    activePlayer.Init(queueItem);
                    activePlayer.Play();
                    playing = true;
                }

                Thread.Sleep(100);
            }
        }

        string analyzeAndImproveText(string text)
        {
            string resultText = text;

            resultText = Regex.Replace(resultText, "(?<=^|[^/.\\w])[a-zA-Z]+[\\.\\,\\!\\?](?=[a-zA-ZäöüÄÖÜ])", "$& ");

            return resultText;
        }

        void getAndMapVoices()
        {
            Log.Debug("Loading and mapping voices");
            mappedVoices = backend.GetAvailableVoices(data);
            mappedVoices.Sort((x, y) => x.ToString().CompareTo(y.ToString()));

            Log.Debug("Success");
        }

        public async void generateVoice(string text, string voice, string language)
        {
            Log.Debug("Generating Audio");

            try
            {
                var splitText = prepareAndSentenceSplit(text).ToList();
                splitText.RemoveAt(splitText.Count - 1);

                foreach (var textLine in splitText)
                {

                    var ready = "";

                    while (ready != "Ready")
                        ready = await backend.CheckReady(data);

                    var responseStream = await backend.GenerateAudioStreamFromVoice(data, textLine, voice, language);

                    var s = new RawSourceWaveStream(responseStream, new WaveFormat(24000, 16, 1));
                    voiceQueue.Add(s);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex.ToString());
            }

            Log.Debug("Done");
        }

        public async Task<string> CheckReady()
        {
            return await backend.CheckReady(data);
        }

        private string[] prepareAndSentenceSplit(string text)
        {
            text = text.Replace("...", ",,,");
            text = text.Replace("..", ",,");
            text = text.Replace(".", "D0T.");
            text = text.Replace("!", "EXC!");
            text = text.Replace("?", "QUEST?");

            var splitText = text.Split(Constants.SENTENCESEPARATORS);

            for (int i = 0; i < splitText.Length; i++)
            {
                splitText[i] = splitText[i].Replace(",,,", "...").Replace(",,", "..").Replace("D0T", ".").Replace("EXC", "!").Replace("QUEST", "?").Trim();
            }

            return splitText;
        }

        private static void SoundOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            var soundOut = sender as WasapiOut;
            soundOut.Dispose();
            playing = false;
        }

        void getVoiceOrRandom(ref NpcMapData npcData, int npcId)
        {
            var localNpcData = npcData;
            BackendVoiceItem voiceItem = npcData.voiceItem;

            var voiceItems = mappedVoices.FindAll(p => p.gender == localNpcData.gender && p.race == localNpcData.race && p.voiceName == localNpcData.name && localNpcData.patchVersion >= p.patchVersion);
            if (voiceItems.Count > 0)
            {
                voiceItems.Sort((a, b) => b.patchVersion.CompareTo(a.patchVersion));
                voiceItem = voiceItems[0];
            }

            if (voiceItem == null)
            {
                voiceItems = mappedVoices.FindAll(p => p.gender == localNpcData.gender && p.race == localNpcData.race && p.voiceName.Contains("NPC"));

                if (voiceItems.Count == 0)
                    voiceItems = mappedVoices.FindAll(p => p.gender == localNpcData.gender && p.race == "Default" && p.voiceName.Contains("NPC"));

                var randomVoice = voiceItems[rand.Next(0, voiceItems.Count)];
                voiceItem = randomVoice;
            }

            if (voiceItem == null)
                voiceItem = mappedVoices.Find(p => p.voice == Constants.NARRATORVOICE);

            npcData.voiceItem = voiceItem;
        }

        void loadNPCDataAPI(int npcId, ref NpcMapData npcData, bool newData = true)
        {
            //XIVApiHelper.loadNPCData(npcId, ref npcData, newData);

            getVoiceOrRandom(ref npcData, npcId);

            if (!npcDatas.ContainsKey(npcData.name))
                addNPCData(npcData);

            Log.Debug(string.Format("Loaded NPC Data from API -> {0} | {1} | {2}({3})", npcData.gender, npcData.race, npcData.name, npcId));
        }

        string getAllTalkVoice(int? npcId, string npcName)
        {
            if (npcId != null)
            {
                NpcMapData npcData;
                if (!npcDatas.TryGetValue(cleanUpName(npcName), out npcData))
                {
                    npcData = new NpcMapData();
                    loadNPCDataAPI(npcId.Value, ref npcData);
                }
                else
                    loadNPCDataAPI(npcId.Value, ref npcData, false);

                Log.Debug(string.Format("Loaded voice: {0} for NPC: {1}", npcData.voiceItem.voice, npcName));
                return npcData.voiceItem.voice;
            }

            return Constants.NARRATORVOICE;
        }

        static internal string cleanUpName(string name)
        {
            name = name.Replace("[a]", "");
            name = Regex.Replace(name, "[^a-zA-Z0-9-' ]+", "");
            name = name.Replace(" ", "+").Replace("'", "=");

            return name;
        }

        static internal string unCleanUpName(string name)
        {
            name = name.Replace("+", " ").Replace("=", "'");

            return name;
        }
    }
}
