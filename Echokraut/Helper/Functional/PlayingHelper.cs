using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.API;
using Echokraut.Helper.Data;
using ManagedBass;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.Windows;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace Echokraut.Helper.Functional
{
    public static class PlayingHelper
    {
        public static Thread RequestingQueueThread = new Thread(WorkRequestingQueues);
        public static Thread PlayingQueueThread = new Thread(WorkPlayingQueues);
        public static bool InDialog = false;
        public static bool Playing = false;
        public static bool RecreationStarted = false;
        public static Live3DAudioEngine AudioEngine = new Live3DAudioEngine();
        public static List<VoiceMessage> RequestedQueue = new List<VoiceMessage>();
        public static List<VoiceMessage> PlayingQueue = new List<VoiceMessage>();
        private static List<VoiceMessage> RequestingQueue = new List<VoiceMessage>();
        private static List<VoiceMessage> RequestingBubbleQueue = new List<VoiceMessage>();
        public static Dictionary<Guid, VoiceMessage> CurrentlyPlayingDictionary = new Dictionary<Guid, VoiceMessage>();
        private static bool StopThread = false;
        private static unsafe Camera* Camera;

        public static void Setup()
        {
            AudioEngine.ConfigureListener(new Vector3D(0,0,0), new Vector3D(0,0,1), new Vector3D(0,1,0));
            AudioEngine.SourceEnded += SoundOut_PlaybackStopped;
            PlayingQueueThread.Start();
            RequestingQueueThread.Start();
        }

        public static void Update3DFactors(float audibleRange)
        {
            Bass.Set3DFactors(1, audibleRange, 1);
            Bass.Apply3D();
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated 3D factors to: {audibleRange}", new EKEventId(0, TextSource.AddonBubble));
        }

        public static void StopPlaying(VoiceMessage message)
        {
            RecreationStarted = false;
            Playing = false;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Stopping voice inference", message.EventId);
            if (AudioEngine.GetState(message.StreamId) != PlaybackState.Stopped)
                AudioEngine.Stop(message.StreamId);
        }

        public static void PausePlaying(VoiceMessage message)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Pausing voice inference", message.EventId);
            if (AudioEngine.GetState(message.StreamId) == PlaybackState.Playing)
                AudioEngine.Pause(message.StreamId);
        }

        public static void ResumePlaying(VoiceMessage message)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Resuming voice inference", message.EventId);
            if (AudioEngine.GetState(message.StreamId) == PlaybackState.Paused)
                AudioEngine.Resume(message.StreamId);
        }

        static async void WorkRequestingQueues()
        {
            try
            {
                while (!StopThread)
                {
                    if (RequestingBubbleQueue.Count > 0 || RequestingQueue.Count > 0)
                    {
                            if (RequestingQueue.Count == 0)
                                await WorkRequestingBubbleQueue();
                            else
                                await WorkRequestingQueue();
                    }

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
                    Thread.Sleep(100);
                }
            }
            catch { }
        }

        static void WorkPlayingQueue()
        {
            if (PlayingQueue.Count > 0)
            {
                var queueItem = PlayingQueue[0];
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Working queue", queueItem.EventId);
                try
                {
                    PlayingQueue.RemoveAt(0);
                    PlayAudio(queueItem);
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while working queue: {ex}", queueItem.EventId);
                    AudioEngine.Stop(queueItem.StreamId);
                }
            }
        }

        static void PlayAudio(VoiceMessage queueItem)
        {

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Playing next queue item", queueItem.EventId);

            queueItem.StreamId = AudioEngine.PlayStream(queueItem.Stream, channels: 1, initialPosition: new Vector3D(5,0,2));
            CurrentlyPlayingDictionary.Add(queueItem.StreamId, queueItem);
            
            if (queueItem.Source == TextSource.AddonTalk || queueItem.Source == TextSource.VoiceTest)
                DialogExtraOptionsWindow.CurrentVoiceMessage = queueItem;

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Audio volume: {queueItem.Volume}", queueItem.EventId);
            AudioEngine.SetVolume(queueItem.StreamId, queueItem.Volume);
            
            AudioEngine.SetSourcePoller(queueItem.StreamId, () => new Vector3D(queueItem.SpeakerFollowObj?.Position.X ?? 0, queueItem.SpeakerFollowObj?.Position.Y ?? 0, queueItem.SpeakerFollowObj?.Position.Z ?? 0));
            Plugin.LipSyncHelper.TryLipSync(queueItem);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Lipsyncdata text: {queueItem.Speaker.Name}",
                           queueItem.EventId);
            Playing = true;
            RecreationStarted = false;
        }

        static async Task<bool> WorkRequestingQueue()
        {
            if (RequestingQueue.Count > 0)
            {
                var queueItem = RequestingQueue[0];
                var response = await BackendHelper.CheckReady(queueItem.EventId);
                var ready = response == "Ready";
                if (ready)
                {
                    RequestingQueue.RemoveAt(0);

                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating next queued audio",
                                   queueItem.EventId);
                    AddRequestedToQueue(queueItem);

                    await BackendHelper.GenerateVoice(queueItem);
                }
            }

            return true;
        }

        static async Task<bool> WorkRequestingBubbleQueue()
        {
            if (RequestingBubbleQueue.Count > 0)
            {
                var queueItem = RequestingBubbleQueue[0];
                var response = await BackendHelper.CheckReady(queueItem.EventId);
                var ready = response == "Ready";
                if (ready)
                {
                    RequestingBubbleQueue.RemoveAt(0);

                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating next queued audio",
                                   queueItem.EventId);
                    AddRequestedToQueue(queueItem);

                    await BackendHelper.GenerateVoice(queueItem);
                }
            }

            return true;
        }
        private static void SoundOut_PlaybackStopped(Guid guid)
        {
            var eventId = new EKEventId(-1, TextSource.None);
            if (CurrentlyPlayingDictionary.ContainsKey(guid))
            {
                var currentlyPlayingMessage = CurrentlyPlayingDictionary[guid];
                eventId = currentlyPlayingMessage.EventId;

                if (currentlyPlayingMessage.Stream != null)
                {
                    if (Plugin.Configuration.CreateMissingLocalSaveLocation &&
                        !Directory.Exists(Plugin.Configuration.LocalSaveLocation))
                        Directory.CreateDirectory(Plugin.Configuration.LocalSaveLocation);

                    if (Plugin.Configuration.SaveToLocal && !currentlyPlayingMessage.LoadedLocally)
                    {
                        if (Directory.Exists(Plugin.Configuration.LocalSaveLocation))
                        {
                            var playedText = currentlyPlayingMessage;
                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Text: {playedText.Text}", eventId);
                            if (!string.IsNullOrWhiteSpace(playedText.Text) &&
                                !playedText.LoadedLocally)
                            {
                                var stream = currentlyPlayingMessage.Stream;
                                AudioFileHelper.WriteStreamToFile(eventId, playedText, stream);
                            }
                        }
                        else
                        {
                            LogHelper.Error(MethodBase.GetCurrentMethod().Name,
                                            $"Couldn't save file locally. Save location doesn't exist: {Plugin.Configuration.LocalSaveLocation}",
                                            eventId);
                        }
                    }

                    currentlyPlayingMessage.Stream.Dispose();
                }

                Playing = false;

                if (currentlyPlayingMessage.IsLastInDialogue)
                {
                    if (Plugin.Configuration.AutoAdvanceTextAfterSpeechCompleted)
                    {
                        try
                        {
                            if (InDialog)
                                Plugin.Framework.RunOnFrameworkThread(() => Plugin.AddonTalkHelper.Click(eventId));
                            else
                                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Not inDialog", eventId);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Error(MethodBase.GetCurrentMethod().Name,
                                            $"Error while 'auto advance text after speech completed': {ex}", eventId);
                        }
                    }
                    else
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"No auto advance", eventId);
                }
                else
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Not last sentence", eventId);
            }

            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
        }

        public static void AddRequestToQueue(VoiceMessage voiceMessage)
        {
            if (Plugin.Configuration.GoogleDriveRequestVoiceLine)
            {
                var voiceLine = new VoiceLine()
                {
                    Gender = voiceMessage.Speaker.Gender,
                    Race = voiceMessage.Speaker.Race,
                    Name = voiceMessage.Speaker.Name,
                    Text = voiceMessage.Text,
                    Language = voiceMessage.Language
                };
                
                GoogleDriveHelper.UploadVoiceLine(Constants.GOOGLEDRIVEVOICELINESHARE, voiceLine, voiceMessage.EventId);
            }
            
            if (Plugin.Configuration.LoadFromLocalFirst && Directory.Exists(Plugin.Configuration.LocalSaveLocation) && voiceMessage.Speaker.Voice != null && voiceMessage.Source != TextSource.VoiceTest)
            {
                var result = AudioFileHelper.LoadLocalAudio(voiceMessage.EventId, Plugin.Configuration.LocalSaveLocation, voiceMessage);

                if (result)
                    return;
            }
            else if (!Directory.Exists(Plugin.Configuration.LocalSaveLocation))
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. Save location doesn't exists: {Plugin.Configuration.LocalSaveLocation}", voiceMessage.EventId);

            RequestingQueue.Add(voiceMessage);
        }

        public static void AddRequestedToQueue(VoiceMessage voiceMessage)
        {
            RequestedQueue.Add(voiceMessage);
        }

        public static void AddRequestBubbleToQueue(VoiceMessage voiceMessage)
        {
            if (Plugin.Configuration.GoogleDriveRequestVoiceLine && voiceMessage.Source == TextSource.AddonBubble)
            {
                var voiceLine = new VoiceLine()
                {
                    Gender = voiceMessage.Speaker.Gender,
                    Race = voiceMessage.Speaker.Race,
                    Name = voiceMessage.Speaker.Name,
                    Text = voiceMessage.Text,
                    Language = voiceMessage.Language
                };
                
                GoogleDriveHelper.UploadVoiceLine(Constants.GOOGLEDRIVEVOICELINESHARE, voiceLine, voiceMessage.EventId);
            }
                
            if (Plugin.Configuration.LoadFromLocalFirst && Directory.Exists(Plugin.Configuration.LocalSaveLocation) && voiceMessage.Speaker.Voice != null && voiceMessage.Source != TextSource.VoiceTest)
            {
                var result = AudioFileHelper.LoadLocalAudio(voiceMessage.EventId, Plugin.Configuration.LocalSaveLocation, voiceMessage);

                if (result)
                    return;
            }
            else if (!Directory.Exists(Plugin.Configuration.LocalSaveLocation))
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldn't load file locally. Save location doesn't exists: {Plugin.Configuration.LocalSaveLocation}", voiceMessage.EventId);

            RequestingBubbleQueue.Add(voiceMessage);
        }

        public static void ClearPlayingQueue()
        {
            PlayingQueue.Clear();
            PlayingQueue.Clear();
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
