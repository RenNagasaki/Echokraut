using Echokraut.DataClasses;
using ManagedBass;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Echokraut.Helper
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
        private static DataClasses.Configuration Configuration;
        private static bool StopThread = false;
        private static Echokraut Echokraut;

        public static void Setup(Echokraut echokraut, DataClasses.Configuration config)
        {
            Configuration = config;
            Echokraut = echokraut;

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

        static void WorkRequestingQueues()
        {
            while (!StopThread)
            {
                WorkRequestingQueue();
                WorkRequestingBubbleQueue();
                Thread.Sleep(100);
            }
        }

        static void WorkPlayingQueues()
        {
            while (!StopThread)
            {
                WorkPlayingQueue();
                WorkPlayingBubbleQueue();
                Thread.Sleep(100);
            }
        }

        static void WorkPlayingQueue()
        {
            while ((ActivePlayer == null || ActivePlayer.PlaybackState != NAudio.Wave.PlaybackState.Playing) && PlayingQueue.Count > 0)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Playing next queue item");
                try
                {
                    var queueItem = PlayingQueue[0];
                    var queueItemText = PlayingQueueText[0];
                    PlayingQueueText.RemoveAt(0);
                    PlayingQueue.RemoveAt(0);
                    CurrentlyPlayingStream = queueItem;
                    CurrentlyPlayingStreamText = queueItemText;
                    var stream = new RawSourceWaveStream(queueItem, new NAudio.Wave.WaveFormat(24000, 16, 1));
                    var volumeSampleProvider = new VolumeSampleProvider(stream.ToSampleProvider());
                    volumeSampleProvider.Volume = Volume; // double the amplitude of every sample - may go above 0dB

                    ActivePlayer = new WasapiOut(AudioClientShareMode.Shared, 0);
                    //activePlayer.Volume = volume;
                    ActivePlayer.PlaybackStopped += SoundOut_PlaybackStopped;
                    ActivePlayer.Init(volumeSampleProvider);
                    ActivePlayer.Play();
                    char[] delimiters = new char[] { ' ' };

                    var estimatedLength = .5f;
                    if (!string.IsNullOrEmpty(queueItemText.Text))
                    {
                        var count = queueItemText.Text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).Length;
                        estimatedLength = count / 2.1f;
                    }
                    Echokraut.lipSyncHelper.TriggerLipSync(queueItemText.Speaker.name, estimatedLength);
                    Playing = true;
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while working queue: {ex}");

                    if (ActivePlayer != null)
                        ActivePlayer.Stop();
                }
            }
        }

        static void WorkPlayingBubbleQueue()
        {
            while (PlayingBubbleQueue.Count > 0)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Playing next bubble queue item");
                try
                {
                    var queueItem = PlayingBubbleQueue[0];
                    var queueItemText = PlayingBubbleQueueText[0];
                    PlayingBubbleQueueText.RemoveAt(0);
                    PlayingBubbleQueue.RemoveAt(0);
                    var volume10k = Volume * 15000;
                    Bass.GlobalSampleVolume = Convert.ToInt32(volume10k > 10000 ? 10000 : volume10k);

                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Starting 3D Audio");
                    var pActor = queueItemText.pActor;
                    if (pActor != null)
                    {
                        var channel = Bass.SampleLoad(queueItem, 0, 0, 1, Flags: BassFlags.Bass3D);
                        Bass.ChannelSet3DPosition(channel, new Vector3D(pActor.Position.X, pActor.Position.Z, -pActor.Position.Y), null, new Vector3D());
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Setup parameters");

                        var thread = new Thread(() =>
                        {
                            while (Bass.ChannelIsActive(channel) == ManagedBass.PlaybackState.Playing)
                            {
                                //LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Updating 3D Position of source");
                                Bass.ChannelSet3DPosition(channel, new Vector3D(pActor.Position.X, pActor.Position.Z, -pActor.Position.Y), null, new Vector3D());
                            }

                            Bass.SampleFree(channel);
                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Done playing");
                        });

                        channel = Bass.SampleGetChannel(channel);

                        Bass.ChannelPlay(channel);
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Started playing");
                        thread.Start();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while working queue: {ex}");
                }
            }
        }

        static void WorkRequestingQueue()
        {
            while (RequestingQueue.Count > 0)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating next queued audio");
                var queueItem = RequestingQueue[0];
                RequestingQueue.RemoveAt(0);
                AddRequestedToQueue(queueItem);

                BackendHelper.GenerateVoice(queueItem);
            }
        }

        static void WorkRequestingBubbleQueue()
        {
            while (RequestedBubbleQueue.Count > 0)
            {
                try
                {
                    var voiceMessage = RequestedBubbleQueue[0];
                    if (Configuration.LoadFromLocalFirst)
                    {
                        if (Directory.Exists(Configuration.LocalSaveLocation))
                        {
                            if (voiceMessage != null && voiceMessage.Speaker != null && voiceMessage.Speaker.voiceItem != null)
                            {
                                var result = FileHelper.LoadLocalBubbleAudio(Configuration.LocalSaveLocation, voiceMessage);

                                if (result)
                                {
                                    RequestedBubbleQueue.RemoveAt(0);
                                    continue;
                                }
                            }
                            else
                                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. No voice set.");
                        }
                        else if (!Directory.Exists(Configuration.LocalSaveLocation))
                            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}");
                    }

                    if (!InDialog && voiceMessage != null)
                    {
                        BackendHelper.GenerateVoice(voiceMessage);
                        RequestedBubbleQueue.RemoveAt(0);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while working bubble queue: {ex}");
                }

                Thread.Sleep(100);
            }
        }
        private static void SoundOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (CurrentlyPlayingStream != null)
            {
                if (Configuration.CreateMissingLocalSaveLocation && !Directory.Exists(Configuration.LocalSaveLocation))
                    Directory.CreateDirectory(Configuration.LocalSaveLocation);

                if (Configuration.SaveToLocal && Directory.Exists(Configuration.LocalSaveLocation))
                {
                    var playedText = CurrentlyPlayingStreamText;

                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Text: {playedText.Text}");
                    if (!string.IsNullOrWhiteSpace(playedText.Text))
                    {
                        var filePath = FileHelper.GetLocalAudioPath(Configuration.LocalSaveLocation, playedText);
                        var stream = CurrentlyPlayingStream;
                        FileHelper.WriteStreamToFile(filePath, stream as ReadSeekableStream);
                    }
                }
                else
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't save file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}");
                }
            }

            var soundOut = sender as WasapiOut;
            soundOut?.Dispose();
            Playing = false;
            Echokraut.StopLipSync();

            if (Configuration.AutoAdvanceTextAfterSpeechCompleted)
            {
                try
                {
                    if (InDialog)
                        Echokraut.addonTalkHelper.Click();
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

        public static void AddRequestToQueue(VoiceMessage voiceMessage)
        {
            if (Configuration.LoadFromLocalFirst && Directory.Exists(Configuration.LocalSaveLocation) && voiceMessage.Speaker.voiceItem != null)
            {
                var result = FileHelper.LoadLocalAudio(Configuration.LocalSaveLocation, voiceMessage);

                if (result)
                    return;
            }
            else if (!Directory.Exists(Configuration.LocalSaveLocation))
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. Save location doesn't exists: {Configuration.LocalSaveLocation}");

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
            StopThread = true;
        }
    }
}
