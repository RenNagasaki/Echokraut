using Echokraut.DataClasses;
using Echokraut.Helper.Data;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Echokraut.Helper.Functional
{
    public static class AlltalkInstallHelper
    {
        public const string AlltalkUrl = "https://github.com/RenNagasaki/alltalk_tts/releases/download/alltalk_tts-alltalkbeta/alltalk_tts-alltalkbeta.zip";
        public static void Install(EKEventId eventId)
        {
            try
            {

            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while installing alltalk locally: {ex}", eventId);
            }
        }
    }
}
