using Anamnesis.Memory;
using Anamnesis.Services;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Character = Dalamud.Game.ClientState.Objects.Types.ICharacter;
using Anamnesis.GameData.Excel;
using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System.Threading;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Echokraut.Helper.Data;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Globalization;
using Dalamud.Game.ClientState.Conditions;

namespace Echokraut.Helper.Functional
{
    internal class LipSyncHelper
    {
        private MemoryService memoryService;
        private AnimationService animationService;
        private GameDataService gameDataService;
        private IGameObject? currentLipsync;
        private readonly Dictionary<string, CancellationTokenSource> taskCancellations = new Dictionary<string, CancellationTokenSource>();
        public List<ActionTimeline> LipSyncTypes { get; private set; }

        public LipSyncHelper(EKEventId eventId)
        {

            InitializeAsync(eventId).ContinueWith(t =>
            {
                if (t.Exception != null)
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Initialization failed: " + t.Exception, eventId);
            });
        }

        public async void TriggerLipSync(EKEventId eventId, float length, IGameObject? npc = null)
        {
            return;
            if (Plugin.Condition[ConditionFlag.BoundByDuty] && !Plugin.Condition[ConditionFlag.WatchingCutscene]) return;
            if (!Plugin.Configuration.Enabled) return;

            var npcObject = npc;
            var npcName = npcObject.Name.TextValue;

            ActorMemory? actorMemory = null;
            AnimationMemory? animationMemory = null;
            if (npcObject != null)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Starting LipSync", eventId);
                actorMemory = new ActorMemory();
                actorMemory.SetAddress(npcObject.Address);
                animationMemory = actorMemory.Animation;

                // Determine the duration based on the message size
                float duration = length;

                Dictionary<int, int> mouthMovement = new Dictionary<int, int>();

                if (duration < 0.2f)
                    return;

                int durationMs = (int)(duration * 1000);


                // Decide on the lengths
                int durationRounded = (int)Math.Floor(duration);
                int remaining = durationRounded;
                mouthMovement[6] = remaining / 4;
                remaining = remaining % 4;
                mouthMovement[5] = remaining / 2;
                remaining = remaining % 2;
                mouthMovement[4] = remaining / 1;
                remaining = remaining % 1;
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"durationMs[{durationMs}] durationRounded[{durationRounded}] fours[{mouthMovement[6]}] twos[{mouthMovement[5]}] ones[{mouthMovement[4]}]", eventId);

                // Decide on the Mode
                ActorMemory.CharacterModes intialState = actorMemory.CharacterMode;
                ActorMemory.CharacterModes mode = ActorMemory.CharacterModes.EmoteLoop;

                if (!taskCancellations.ContainsKey(npcName))
                {
                    var cts = new CancellationTokenSource();
                    taskCancellations.Add(npcName, cts);
                    currentLipsync = npcObject;
                    var token = cts.Token;

                    Task task = Task.Run(async () => {
                        try
                        {
                            await Task.Delay(100, token);

                            // 4-Second Lips Movement Animation
                            if (!token.IsCancellationRequested && mouthMovement[6] > 0 && npcObject != null && actorMemory != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = false ? 0 : (ushort)631;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), false ? 0 : (ushort)631, "Lipsync");

                                int adjustedDelay = CalculateAdjustedDelay(mouthMovement[6] * 4000, 6);
                                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[6] durationMs[{mouthMovement[6] * 4}] delay [{adjustedDelay}]", eventId);
                                await Task.Delay(adjustedDelay, token);

                                if (!token.IsCancellationRequested && npcObject != null && actorMemory != null)
                                {
                                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[6] was finished", eventId);

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }

                            }

                            // 2-Second Lips Movement Animation
                            if (!token.IsCancellationRequested && mouthMovement[5] > 0 && npcObject != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = false ? 0 : (ushort)630;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), false ? 0 : (ushort)630, "Lipsync");
                                int adjustedDelay = CalculateAdjustedDelay(mouthMovement[5] * 2000, 5);
                                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[5] durationMs[{mouthMovement[5] * 2}] delay [{adjustedDelay}]", eventId);

                                await Task.Delay(adjustedDelay, token);
                                if (!token.IsCancellationRequested && npcObject != null && actorMemory != null)
                                {
                                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[5] was finished", eventId);

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }

                            }

                            // 1-Second Lips Movement Animation
                            if (!token.IsCancellationRequested && mouthMovement[4] > 0 && npcObject != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = false ? 0 : (ushort)632;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), false ? 0 : (ushort)632, "Lipsync");
                                int adjustedDelay = CalculateAdjustedDelay(mouthMovement[4] * 1000, 4);
                                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[4] durationMs[{mouthMovement[4]}] delay [{adjustedDelay}]", eventId);


                                await Task.Delay(adjustedDelay, token);
                                if (!token.IsCancellationRequested && npcObject != null && actorMemory != null)
                                {
                                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[4] was finished", eventId);

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }
                            }

                            if (!token.IsCancellationRequested)
                            {
                                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Task was Completed", eventId);
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "LipSync was completed", eventId);

                                cts.Dispose();
                                if (taskCancellations.ContainsKey(npcName))
                                    taskCancellations.Remove(npcName);
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Task was canceled.", eventId);
                            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "LipSync was canceled", eventId);

                            animationMemory.LipsOverride = 0;
                            MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                            MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                            cts.Dispose();
                            if (taskCancellations.ContainsKey(npcName))
                                taskCancellations.Remove(npcName);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Unhandled exception in TriggerLipSync task: {ex}", eventId);

                            animationMemory.LipsOverride = 0;
                            MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                            MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                            cts.Dispose();
                            if (taskCancellations.ContainsKey(npcName))
                                taskCancellations.Remove(npcName);
                        }
                    }, token);
                }
            }
        }

        public async void StopLipSync(EKEventId eventId)
        {
            return;
            try
            {
                if (Plugin.Condition[ConditionFlag.BoundByDuty]) return;
                if (!Plugin.Configuration.Enabled) return;
                if (currentLipsync == null) return;

                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Stopping Lipsync for {currentLipsync.Name.TextValue}", eventId);
                if (taskCancellations.TryGetValue(currentLipsync.ToString(), out var cts))
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Cancellation " + currentLipsync.Name.TextValue, eventId);
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"CTS for {currentLipsync.Name.TextValue} was called to be disposed even though it was disposed already.", eventId);
                    }
                    return;
                }

                var actorMemory = new ActorMemory();
                actorMemory.SetAddress(currentLipsync.Address);
                var animationMemory = actorMemory.Animation;
                animationMemory.LipsOverride = 0;
                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), 0, "Animation Mode Override");
                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                taskCancellations.Remove(currentLipsync.Name.TextValue);
                currentLipsync = null;
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Stopped Lipsync", eventId);
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"{ex}", eventId);
            }
        }

        private async Task InitializeAsync(EKEventId eventId)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "InitializeAsync --> Waiting for Game Process Stability", eventId);
            await WaitForGameProcessStability();
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "InitializeAsync --> Done waiting", eventId);
            InitializeServices();
        }

        private void InitializeServices()
        {
            // Initialize all services that depend on the game process
            memoryService = new MemoryService();
            gameDataService = new GameDataService();
            animationService = new AnimationService();
            StartServices();
        }

        private async Task WaitForGameProcessStability()
        {
            // Wait until the game process is stable
            while (Process.GetCurrentProcess() == null || !Process.GetCurrentProcess().Responding)
            {
                await Task.Delay(1000); // Check every second
            }
        }

        private async void StartServices()
        {
            await memoryService.Initialize();
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "StartServices --> Waiting for Process Response", new EKEventId(0, Enums.TextSource.None));
            while (!Process.GetCurrentProcess().Responding)
                await Task.Delay(100);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "StartServices --> Done waiting", new EKEventId(0, Enums.TextSource.None));
            await memoryService.OpenProcess(Process.GetCurrentProcess());
            await gameDataService.Initialize();

            //LipSyncTypes = GenerateLipList().ToList();
            await animationService.Initialize();
            await animationService.Start();
            await memoryService.Start();
        }

        //private IEnumerable<ActionTimeline> GenerateLipList()
        //{
        //    // Grab "no animation" and all "speak/" animations, which are the only ones valid in this slot
        //    var lips = GameDataService.ActionTimelines.Where(x => x.AnimationId == 0 || (x.Key?.StartsWith("speak/") ?? false));
        //    return lips;
        //}

        public int EstimateDurationFromMessage(string message)
        {
            var words = message.Split(new char[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var wordsPerSecond = 150.0 / 60; // 150 words per minute converted to words per second

            return (int)(words / wordsPerSecond * 1000); // duration in milliseconds
        }

        int CalculateAdjustedDelay(int durationMs, int lipSyncType)
        {
            var delay = 0;
            int animationLoop;
            if (lipSyncType == 4)
                animationLoop = 1000;
            else if (lipSyncType == 5)
                animationLoop = 2000;
            else
                animationLoop = 4000;
            var halfStep = animationLoop / 2;

            if (durationMs <= 1 * animationLoop + halfStep)
            {
                return 1 * animationLoop - 50;
            }
            else
                for (var i = 2; delay < durationMs; i++)
                    if (durationMs > i * animationLoop - halfStep && durationMs <= i * animationLoop + halfStep)
                    {
                        delay = i * animationLoop - 50;
                        return delay;
                    }

            return 404;
        }

        private IGameObject? DiscoverNpc(string npcName)
        {
            if (npcName == "???")
            {
                /*
                foreach (var item in _objectTable) {

                    if (item as Character == null || item as Character == _clientState.LocalPlayer || item.Name.TextValue == "") continue;

                    if (true) {
                        Character character = item as Character;
                        if (character != null && character != _clientState.LocalPlayer) {
                            gender = Convert.ToBoolean(character.Customize[(int)CustomizeIndex.Gender]);
                            race = character.Customize[(int)CustomizeIndex.Race];
                            body = character.Customize[(int)CustomizeIndex.ModelType];
                            return character;
                        }
                        return item;
                    }
                }*/
            }
            else
            {
                foreach (var item in Plugin.ObjectTable)
                {
                    if (!(item is Character) || item as Character == Plugin.ClientState.LocalPlayer || item.Name.TextValue == "") continue;
                    if (item.Name.TextValue == npcName)
                    {
                        return item;
                    }
                }
            }

            return null;
        }
    }
}
