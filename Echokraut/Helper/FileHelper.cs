using Echokraut.DataClasses;
using NAudio.Wave;
using System;
using System.IO;
using System.Reflection;

namespace Echokraut.Helper
{
    public static class FileHelper
    {
        public static bool LoadLocalAudio(string localSaveLocation, VoiceMessage voiceMessage)
        {
            try
            {
                string filePath = GetLocalAudioPath(localSaveLocation, voiceMessage);

                if (File.Exists(filePath))
                {
                    WaveStream mainOutputStream = new WaveFileReader(filePath);
                    PlayingHelper.PlayingQueue.Add(mainOutputStream);
                    PlayingHelper.PlayingQueueText.Add(new VoiceMessage { Text = "", Speaker = new NpcMapData { name = Path.GetFileName(Path.GetFileName(Path.GetDirectoryName(filePath))) } });
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

        public static bool LoadLocalBubbleAudio(string localSaveLocation, VoiceMessage voiceMessage)
        {
            try
            {
                string filePath = GetLocalAudioPath(localSaveLocation, voiceMessage);

                if (File.Exists(filePath))
                {
                    PlayingHelper.PlayingBubbleQueue.Add(filePath);
                    PlayingHelper.PlayingBubbleQueueText.Add(new VoiceMessage { pActor = voiceMessage.pActor, Text = "", Speaker = new NpcMapData { name = Path.GetFileName(Path.GetFileName(Path.GetDirectoryName(filePath))) } });
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

        public static string GetLocalAudioPath(string localSaveLocation, VoiceMessage voiceMessage)
        {
            string filePath = localSaveLocation;
            if (!filePath.EndsWith(@"\"))
                filePath += @"\";
            filePath += $"{voiceMessage.Speaker.name}\\{voiceMessage.Speaker.race.ToString()}-{voiceMessage.Speaker.voiceItem?.voiceName}\\{DataHelper.VoiceMessageToFileName(voiceMessage.Text)}.wav";

            return filePath;
        }

        public static void WriteStreamToFile(string filePath, ReadSeekableStream stream)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Saving audio locally: {filePath}");
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                var rawStream = new RawSourceWaveStream(stream, new NAudio.Wave.WaveFormat(24000, 16, 1));

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                NAudio.Wave.WaveFileWriter.CreateWaveFile(filePath, rawStream);
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while saving audio locally: {ex.ToString()}");
            }
        }
    }
}
