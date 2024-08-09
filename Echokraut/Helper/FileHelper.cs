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
                    voiceMessage.loadedLocally = true;
                    WaveStream mainOutputStream = new WaveFileReader(filePath);
                    PlayingHelper.PlayingQueue.Add(mainOutputStream);
                    PlayingHelper.PlayingQueueText.Add(voiceMessage);
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
                    voiceMessage.loadedLocally = true;
                    PlayingHelper.PlayingBubbleQueue.Add(filePath);
                    PlayingHelper.PlayingBubbleQueueText.Add(voiceMessage);
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
            string filePath = GetSpeakerAudioPath(localSaveLocation, voiceMessage.Speaker.name) + $"{ voiceMessage.Speaker.race.ToString()}-{voiceMessage.Speaker.voiceItem?.voiceName}\\{VoiceMessageToFileName(voiceMessage.Text)}.wav";

            return filePath;
        }

        public static string GetSpeakerAudioPath(string localSaveLocation, string speaker)
        {
            string filePath = localSaveLocation;
            if (!filePath.EndsWith(@"\"))
                filePath += @"\";

            speaker = speaker != "" ? speaker : "NOPERSON";
            filePath += $"{speaker}\\";

            return filePath;
        }

        public static string VoiceMessageToFileName(string voiceMessage)
        {
            string fileName = voiceMessage;
            string[] temp = fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries);
            fileName = String.Join("", temp).ToLower().Replace(" ", "").Replace(".", "").Replace("!", "").Replace(",", "").Replace("-", "").Replace("_", "");
            if (fileName.Length > 120)
                fileName = fileName.Substring(0, 120);

            return fileName;
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

        public static bool RemoveSavedNpcFiles(string localSaveLocation, string speaker)
        {
            var speakerFolderPath = GetSpeakerAudioPath(localSaveLocation, speaker);

            if (Directory.Exists(speakerFolderPath))
            {
                try
                {
                    Directory.Delete(speakerFolderPath, true);
                    return true;
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while deleting local saves for: {speaker} - {ex.ToString()}", new EKEventId(0, Enums.TextSource.None));
                }
            }

            return false;
        }
    }
}
