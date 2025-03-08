using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.API;
using Echokraut.Helper.Data;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ManagedBass;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Helper.Functional
{
    public static class PlayingHelper
    {
        public static Thread RequestingQueueThread = new Thread(WorkRequestingQueues);
        public static Thread PlayingQueueThread = new Thread(WorkPlayingQueues);
        public static float Volume = 1f;
        public static bool QueueText = false;
        public static bool InDialog = false;
        public static bool Playing = false;
        public static List<VoiceMessage> RequestedQueue = new List<VoiceMessage>();
        public static List<string> PlayingBubbleQueue = new List<string>();
        public static List<VoiceMessage> PlayingBubbleQueueText = new List<VoiceMessage>();
        public static List<Stream> PlayingQueue = new List<Stream>();
        public static List<VoiceMessage> PlayingQueueText = new List<VoiceMessage>();
        private static List<VoiceMessage> RequestingQueue = new List<VoiceMessage>();
        private static List<VoiceMessage> RequestedBubbleQueue = new List<VoiceMessage>();
        private static Stream CurrentlyPlayingStream = null;
        private static VoiceMessage CurrentlyPlayingStreamText = null;
        private static WasapiOut ActivePlayer = null;
        private static bool StopThread = false;
        private static Echokraut Echokraut;
        private static DataClasses.Configuration Configuration;
        private static IFramework Framework;

        public static void Setup(Echokraut echokraut, DataClasses.Configuration config, IFramework framework)
        {
            Echokraut = echokraut;
            Configuration = config;
            Framework = framework;

            PlayingQueueThread.Start();
            RequestingQueueThread.Start();
        }

        public static void StopPlaying()
        {
            if (ActivePlayer != null)
            {
                ActivePlayer.PlaybackStopped -= SoundOut_PlaybackStopped;
                ActivePlayer.Stop();
            }
        }

        static async void WorkRequestingQueues()
        {
            try
            {
                while (!StopThread)
                {
                    await WorkRequestingQueue();
                    await WorkRequestingBubbleQueue();
                    Thread.Sleep(100);
                }
            }
            catch { }
        }

        static void WorkPlayingQueues()
        {
            try
            {
                while (!StopThread)
                {
                    WorkPlayingQueue();
                    WorkPlayingBubbleQueue();
                    Thread.Sleep(100);
                }
            }
            catch { }
        }

        static void WorkPlayingQueue()
        {
            var eventId = new EKEventId(-1, TextSource.None);
            if ((ActivePlayer == null || ActivePlayer.PlaybackState != NAudio.Wave.PlaybackState.Playing) && PlayingQueue.Count > 0)
            {
                try
                {
                    var queueItem = PlayingQueue[0];
                    var queueItemText = PlayingQueueText[0];
                    PlayingQueueText.RemoveAt(0);
                    PlayingQueue.RemoveAt(0);
                    eventId = queueItemText.eventId;
                    PlayAudio(queueItem, queueItemText, eventId);
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while working queue: {ex}", eventId);

                    if (ActivePlayer != null)
                        ActivePlayer.Stop();
                }
            }
        }

        static void PlayAudio(Stream queueItem, VoiceMessage queueItemText, EKEventId eventId)
        {
            CurrentlyPlayingStream = queueItem;
            CurrentlyPlayingStreamText = queueItemText;
            var stream = new RawSourceWaveStream(queueItem, new NAudio.Wave.WaveFormat(24000, 16, 1));
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Playing next queue item", eventId);
            var volumeSampleProvider = new VolumeSampleProvider(stream.ToSampleProvider());
            volumeSampleProvider.Volume = Volume;

            ActivePlayer = new WasapiOut(AudioClientShareMode.Shared, 3);
            ActivePlayer.PlaybackStopped += SoundOut_PlaybackStopped;
            ActivePlayer.Init(volumeSampleProvider);
            ActivePlayer.Play();
            var delimiters = new char[] { ' ' };

            var estimatedLength = 10f;
            if (!string.IsNullOrEmpty(queueItemText.Text))
            {
                var count = queueItemText.Text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Length;
                estimatedLength = count / 2.1f;
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Lipsyncdata text: {queueItemText.Text} length: {estimatedLength}", eventId);
            }
            Echokraut.lipSyncHelper.TriggerLipSync(eventId, queueItemText.Speaker.Name, estimatedLength, queueItemText.pActor);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Lipsyncdata text: {queueItemText.Speaker.Name} length: {estimatedLength}", eventId);
            Playing = true;
        }

        static void WorkPlayingBubbleQueue()
        {
            var eventId = new EKEventId(-1, TextSource.None);
            if (PlayingBubbleQueue.Count > 0)
            {
                try
                {
                    var queueItem = PlayingBubbleQueue[0];
                    var queueItemText = PlayingBubbleQueueText[0];
                    PlayingBubbleQueueText.RemoveAt(0);
                    PlayingBubbleQueue.RemoveAt(0);
                    eventId = queueItemText.eventId;
                    PlayAudio3D(queueItem, queueItemText, eventId);
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while working queue: {ex}", eventId);
                }
            }
        }

        static void PlayAudio3D(string queueItem, VoiceMessage queueItemText, EKEventId eventId)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Playing next bubble queue item", eventId);
            var volume10k = Volume * 15000;
            Bass.GlobalSampleVolume = Convert.ToInt32(volume10k > 10000 ? 10000 : volume10k);

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Starting 3D Audio for {queueItemText}", eventId);
            var pActor = queueItemText.pActor;
            if (pActor != null)
            {
                var channel = Bass.SampleLoad(queueItem, 0, 0, 1, Flags: BassFlags.Bass3D);
                Bass.ChannelSet3DPosition(channel, new Vector3D(pActor.Position.X, pActor.Position.Y, pActor.Position.Z), null, new Vector3D());
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Setup parameters", eventId);

                var thread = new Thread(() =>
                {
                    while (Bass.ChannelIsActive(channel) == ManagedBass.PlaybackState.Playing)
                    {
                        //LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Updating 3D Position of source");
                        Bass.ChannelSet3DPosition(channel, new Vector3D(pActor.Position.X, pActor.Position.Y, pActor.Position.Z), null, new Vector3D());
                    }

                    Bass.SampleFree(channel);
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Done playing", eventId);
                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                });

                channel = Bass.SampleGetChannel(channel);

                Bass.ChannelPlay(channel);
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Started playing", eventId);
                thread.Start();
            }
        }

        static async Task<bool> WorkRequestingQueue()
        {
            if (RequestingQueue.Count > 0)
            {
                var queueItem = RequestingQueue[0];
                RequestingQueue.RemoveAt(0);

                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating next queued audio", queueItem.eventId);
                AddRequestedToQueue(queueItem);

                await BackendHelper.GenerateVoice(queueItem);

            }

            return true;
        }

        static async Task<bool> WorkRequestingBubbleQueue()
        {
            var eventId = new EKEventId(-1, TextSource.None);
            if (RequestedBubbleQueue.Count > 0)
            {
                try
                {
                    var voiceMessage = RequestedBubbleQueue[0];
                    eventId = voiceMessage.eventId;
                    if (Configuration.LoadFromLocalFirst)
                    {
                        if (Directory.Exists(Configuration.LocalSaveLocation))
                        {
                            if (voiceMessage != null && voiceMessage.Speaker != null && voiceMessage.Speaker.Voice != null)
                            {
                                var result = AudioFileHelper.LoadLocalBubbleAudio(eventId, Configuration.LocalSaveLocation, voiceMessage);

                                if (result)
                                {
                                    RequestedBubbleQueue.RemoveAt(0);
                                    return true;
                                }
                            }
                            else
                                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. No voice set.", eventId);
                        }
                        else if (!Directory.Exists(Configuration.LocalSaveLocation))
                            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}", eventId);
                    }

                    if (!InDialog && voiceMessage != null)
                    {
                        var res = await BackendHelper.GenerateVoice(voiceMessage);
                        if (res)
                            RequestedBubbleQueue.RemoveAt(0);

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while working bubble queue: {ex}", eventId);
                }
            }

            return true;
        }
        private static void SoundOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            var eventId = new EKEventId(-1, TextSource.None);
            if (CurrentlyPlayingStreamText != null)
                eventId = CurrentlyPlayingStreamText.eventId;

            if (CurrentlyPlayingStream != null)
            {
                if (Configuration.CreateMissingLocalSaveLocation && !Directory.Exists(Configuration.LocalSaveLocation))
                    Directory.CreateDirectory(Configuration.LocalSaveLocation);

                if (Configuration.SaveToLocal && Directory.Exists(Configuration.LocalSaveLocation))
                {
                    var playedText = CurrentlyPlayingStreamText;
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Text: {playedText.Text}", eventId);
                    if (!string.IsNullOrWhiteSpace(playedText.Text) && playedText.Source != TextSource.VoiceTest && !playedText.loadedLocally)
                    {
                        var filePath = AudioFileHelper.GetLocalAudioPath(Configuration.LocalSaveLocation, playedText);
                        var stream = CurrentlyPlayingStream;
                        AudioFileHelper.WriteStreamToFile(eventId, filePath, stream);
                    }
                }
                else
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't save file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}", eventId);
                }
            }

            var soundOut = sender as WasapiOut;
            soundOut?.Dispose();
            Playing = false;
            CurrentlyPlayingStream.Dispose();

            if (Configuration.AutoAdvanceTextAfterSpeechCompleted)
            {
                try
                {
                    if (InDialog)
                        Framework.RunOnFrameworkThread(() => Echokraut.addonTalkHelper.Click(eventId));
                    else
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Not inDialog", eventId);
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while 'auto advance text after speech completed': {ex}", eventId);
                }
            }
            else
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"No auto advance", eventId);

            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
        }

        public static void AddRequestToQueue(VoiceMessage voiceMessage)
        {
            if (Configuration.LoadFromLocalFirst && Directory.Exists(Configuration.LocalSaveLocation) && voiceMessage.Speaker.Voice != null && voiceMessage.Source != TextSource.VoiceTest)
            {
                var result = AudioFileHelper.LoadLocalAudio(voiceMessage.eventId, Configuration.LocalSaveLocation, voiceMessage);

                if (result)
                    return;
            }
            else if (!Directory.Exists(Configuration.LocalSaveLocation))
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}", voiceMessage.eventId);

            RequestingQueue.Add(voiceMessage);
        }

        public static void AddRequestedToQueue(VoiceMessage voiceMessage)
        {
            RequestedQueue.Add(voiceMessage);
        }

        public static void AddRequestBubbleToQueue(VoiceMessage voiceMessage)
        {
            RequestedBubbleQueue.Add(voiceMessage);
        }

        public static void ClearPlayingQueue()
        {
            PlayingQueue.Clear();
            PlayingQueueText.Clear();
        }

        public static void ClearRequestingQueue()
        {
            RequestingQueue.Clear();
        }

        public static void ClearRequestedQueue()
        {
            RequestedQueue.Clear();
        }

        public static void Dispose()
        {
            try
            {
                StopThread = true;
                PlayingQueueThread.Interrupt();
                RequestingQueueThread.Interrupt();
            }
            catch { }
        }
    }
}
