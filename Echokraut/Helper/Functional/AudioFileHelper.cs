using Echokraut.DataClasses;
using Echokraut.Helper.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Echokraut.Helper.Functional
{
    public static class AudioFileHelper
    {
        public static Dictionary<DateTime, string> SavedFiles = new Dictionary<DateTime, string>();
        public static bool LoadLocalAudio(EKEventId eventId, string localSaveLocation, VoiceMessage voiceMessage)
        {
            try
            {
                var filePath = GetLocalAudioPath(localSaveLocation, voiceMessage);

                if (File.Exists(filePath))
                {
                    voiceMessage.LoadedLocally = true;
                    using var mainOutputStream = new WavFileReader(filePath);
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
                var filePath = GetLocalAudioPath(localSaveLocation, voiceMessage);

                if (File.Exists(filePath))
                {
                    voiceMessage.LoadedLocally = true;
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
            var filePath = GetSpeakerAudioPath(localSaveLocation, voiceMessage.Speaker.Name) + $"{voiceMessage.Speaker.Race.ToString()}-{voiceMessage.Speaker.Voice?.VoiceName}\\{VoiceMessageToFileName(voiceMessage.Text)}.wav";

            return filePath;
        }

        public static string GetSpeakerAudioPath(string localSaveLocation, string speaker)
        {
            var filePath = localSaveLocation;
            if (!filePath.EndsWith(@"\"))
                filePath += @"\";

            speaker = speaker != "" ? speaker : "NOPERSON";
            filePath += $"{speaker}\\";

            return filePath;
        }

        public static string VoiceMessageToFileName(string voiceMessage)
        {
            var fileName = voiceMessage;
            var temp = fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries);
            fileName = string.Join("", temp).ToLower().Replace(" ", "").Replace(".", "").Replace("!", "").Replace(",", "").Replace("-", "").Replace("_", "");
            if (fileName.Length > 120)
                fileName = fileName.Substring(0, 120);

            return fileName;
        }

        public static bool WriteStreamToFile(EKEventId eventId, string filePath, Stream stream)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Saving audio locally: {filePath}", eventId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                stream.Seek(0, SeekOrigin.Begin);
                RawPcmToWav.CreateWaveFileAsync(filePath, stream, sampleRate: 24000, bitsPerSample: 16, channels: 1);
                SavedFiles.Add(DateTime.Now, filePath);

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while saving audio locally: {ex.ToString()}", eventId);
            }

            return false;
        }

        public static int DeleteLastNFiles(int nFilesToDelete = 10)
        {
            var timeStamps = SavedFiles.Keys.ToList();
            timeStamps.Sort((a, b) => DateTime.Compare(b, a));
            var file = "";
            var deletedFiles = 0;

            for (int i = 0; i < nFilesToDelete; i++)
            {
                if (SavedFiles.Count > 0)
                {
                    try
                    {
                        file = SavedFiles[timeStamps[0]];
                        File.Delete(file);
                        deletedFiles++;
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Deleted local saved file: {file}", new EKEventId(0, Enums.TextSource.None));
                    }
                    catch (FileNotFoundException ex)
                    { }
                    catch (Exception ex)
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while deleting local saved file: {file} - {ex.ToString()}", new EKEventId(0, Enums.TextSource.None));
                    }
                    SavedFiles.Remove(timeStamps[0]);
                    timeStamps.RemoveAt(0);
                }
                else
                    break;
            }

            return deletedFiles;
        }

        public static int DeleteLastNMinutesFiles(int nMinutesFilesToDelete = 10)
        {
            var timeStamps = SavedFiles.Keys.ToList().FindAll(p => p >= DateTime.Now.AddMinutes(-nMinutesFilesToDelete));
            var file = "";
            var deletedFiles = 0;

            foreach (var timeStamp in timeStamps)
            {
                if (SavedFiles.Count > 0)
                {
                    try
                    {
                        file = SavedFiles[timeStamp];
                        File.Delete(file);
                        deletedFiles++;
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Deleted local saved file: {file}", new EKEventId(0, Enums.TextSource.None));

                    }
                    catch (FileNotFoundException ex)
                    { }
                    catch (Exception ex)
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while deleting local saved file: {file} - {ex.ToString()}", new EKEventId(0, Enums.TextSource.None));
                    }
                    SavedFiles.Remove(timeStamp);
                }
                else
                    break;
            }

            return deletedFiles;
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
