using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Interface.Utility.Table;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using FFXIVClientStructs.FFXIV.Client.Sound;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Data.Parsing;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Dalamud.Plugin.Services.IFramework;
using static Dalamud.Plugin.Services.IFramework;
using static Echokraut.Helper.ClickHelper;

namespace Echokraut.Helper
{
    internal static class VolumeHelper
    {

        public static unsafe float GetVoiceVolume()
        {
            var voiceVolume = .5f;
            var masterVolume = .5f;
            var instance = Framework.Instance();
            if (instance != null && instance->SoundManager != null)
            {
                var soundManager = instance->SoundManager;
                masterVolume = soundManager->MasterVolume;
                voiceVolume = soundManager->GetEffectiveVolume(SoundManager.SoundChannel.Voice);
                var channelMuted = soundManager->ChannelMuted;
                //LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Channelmuted: {channelMuted.Length}");
                //var masterMuted = channelMuted[0];
                //var voiceMuted = channelMuted[(int)SoundManager.SoundChannel.Voice];

                //var i = 0;
                //foreach (var channel in channelMuted)
                //{
                //    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"{i} muted: {channel}");
                //    i++;
                //}

                //if (masterMuted || voiceMuted)
                //    return 0f;

                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Master volume: {masterVolume}");
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Voice volume: {voiceVolume}");
            }

            var volumeFloat = masterVolume * voiceVolume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Real voice volume: {volumeFloat}");
            return volumeFloat;
        }
    }
}
