using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using NAudio.Wave;
using System;
using System.IO;
using System.Reflection;

namespace Echokraut.Helper
{
    public static class FileHelper
    {
        public static bool LoadLocalAudio(EKEventId eventId, string localSaveLocation, VoiceMessage voiceMessage)
        {
            try
            {
                string filePath = GetLocalAudioPath(localSaveLocation, voiceMessage);

                if (File.Exists(filePath))
                {
                    WaveStream mainOutputStream = new WaveFileReader(filePath);
                    PlayingHelper.PlayingQueue.Add(mainOutputStream);
                    PlayingHelper.PlayingQueueText.Add(new VoiceMessage { eventId = voiceMessage.eventId, Text = "", Speaker = new NpcMapData(voiceMessage.Speaker.objectKind) { name = Path.GetFileName(Path.GetFileName(Path.GetDirectoryName(filePath))) } });
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Local file found. Location: {filePath}", eventId);

                    return true;
                }
                else
                {
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"No local file found. Location searched: {filePath}", eventId);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading local audio: {ex}", eventId);
            }

            return false;
        }

        public static bool LoadLocalBubbleAudio(EKEventId eventId, string localSaveLocation, VoiceMessage voiceMessage)
        {
            try
            {
                string filePath = GetLocalAudioPath(localSaveLocation, voiceMessage);

                if (File.Exists(filePath))
                {
                    PlayingHelper.PlayingBubbleQueue.Add(filePath);
                    PlayingHelper.PlayingBubbleQueueText.Add(new VoiceMessage { eventId = voiceMessage.eventId, pActor = voiceMessage.pActor, Text = "", Speaker = new NpcMapData(voiceMessage.Speaker.objectKind) { name = Path.GetFileName(Path.GetFileName(Path.GetDirectoryName(filePath))) } });
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Local file found. Location: {filePath}", eventId);

                    return true;
                }
                else
                {
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"No local file found. Location searched: {filePath}", eventId);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading local audio: {ex}", eventId);
            }

            return false;
        }

        public static string GetLocalAudioPath(string localSaveLocation, VoiceMessage voiceMessage)
        {
            string filePath = localSaveLocation;
            if (!filePath.EndsWith(@"\"))
                filePath += @"\";
            filePath += $"{voiceMessage.Speaker.name}\\{voiceMessage.Speaker.race.ToString()}-{voiceMessage.Speaker.voiceItem?.voiceName}\\{DataHelper.VoiceMessageToFileName(voiceMessage.Text)}.wav";

            return filePath;
        }

        public static bool WriteStreamToFile(EKEventId eventId, string filePath, ReadSeekableStream stream)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Saving audio locally: {filePath}", eventId);
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                var rawStream = new RawSourceWaveStream(stream, new NAudio.Wave.WaveFormat(24000, 16, 1));

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                NAudio.Wave.WaveFileWriter.CreateWaveFile(filePath, rawStream);

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while saving audio locally: {ex.ToString()}", eventId);
            }

            return false;
        }
    }
}
