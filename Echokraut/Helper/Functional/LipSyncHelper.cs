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

namespace Echokraut.Helper.Functional
{
    internal class LipSyncHelper
    {
        private readonly IObjectTable objects;
        private readonly IClientState clientState;
        private readonly Configuration config;
        private MemoryService _memoryService;
        private AnimationService _animationService;
        private GameDataService _gameDataService;
        private Character currentLipsync;
        private ActorMemory.CharacterModes currentIntialState;
        Dictionary<Character, CancellationTokenSource> taskCancellations = new Dictionary<Character, CancellationTokenSource>();
        public List<ActionTimeline> LipSyncTypes { get; private set; }

        public LipSyncHelper(IClientState clientState, IObjectTable objects, Configuration config, EKEventId eventId)
        {
            this.clientState = clientState;
            this.config = config;
            this.objects = objects;

            InitializeAsync(eventId).ContinueWith(t =>
            {
                if (t.Exception != null)
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Initialization failed: " + t.Exception, eventId);
            });
        }

        public async void TriggerLipSync(EKEventId eventId, string npcName, float length, IGameObject npc = null)
        {
            if (Conditions.IsBoundByDuty) return;
            if (!config.Enabled) return;


            var npcObject = npc ?? DiscoverNpc(npcName);
            ActorMemory actorMemory = null;
            AnimationMemory animationMemory = null;
            if (npcObject != null)
            {
                var character = (Character)npcObject;
                currentLipsync = character;
                actorMemory = new ActorMemory();
                actorMemory.SetAddress(character.Address);
                animationMemory = actorMemory.Animation;

                // Determine the duration based on the message size
                var duration = length;

                var mouthMovement = new Dictionary<int, int>();

                if (duration < 0.2f)
                    return;

                var durationMs = (int)(duration * 1000);


                // Decide on the lengths
                var durationRounded = (int)Math.Floor(duration);
                var remaining = durationRounded;
                mouthMovement[6] = remaining / 4;
                remaining = remaining % 4;
                mouthMovement[5] = remaining / 2;
                remaining = remaining % 2;
                mouthMovement[4] = remaining / 1;
                remaining = remaining % 1;
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"durationMs[{durationMs}] durationRounded[{durationRounded}] fours[{mouthMovement[6]}] twos[{mouthMovement[5]}] ones[{mouthMovement[4]}]", eventId);

                // Decide on the Mode
                var intialState = actorMemory.CharacterMode;
                var mode = ActorMemory.CharacterModes.EmoteLoop;
                currentIntialState = intialState;


                if (!taskCancellations.ContainsKey(character))
                {
                    var cts = new CancellationTokenSource();
                    taskCancellations.Add(character, cts);
                    var token = cts.Token;
                    token.ThrowIfCancellationRequested();

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(100, token);

                            if (!token.IsCancellationRequested && mouthMovement[6] > 0 && character != null && actorMemory != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = LipSyncTypes[6].Timeline.AnimationId;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), LipSyncTypes[6].Timeline.AnimationId, "Lipsync");

                                var adjustedDelay = CalculateAdjustedDelay(mouthMovement[6] * 4000, 6);

                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[6] durationMs[{mouthMovement[6] * 4}] delay [{adjustedDelay}]", eventId);

                                await Task.Delay(adjustedDelay, token);
                                if (!token.IsCancellationRequested && character != null && actorMemory != null)
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[6] was finished", eventId);

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }

                            }

                            if (!token.IsCancellationRequested && mouthMovement[5] > 0 && character != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = LipSyncTypes[5].Timeline.AnimationId;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), LipSyncTypes[5].Timeline.AnimationId, "Lipsync");
                                var adjustedDelay = CalculateAdjustedDelay(mouthMovement[5] * 2000, 5);

                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[5] durationMs[{mouthMovement[5] * 2}] delay [{adjustedDelay}]", eventId);

                                await Task.Delay(adjustedDelay, token);
                                if (!token.IsCancellationRequested && character != null && actorMemory != null)
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[5] was finished", eventId);

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }

                            }

                            if (!token.IsCancellationRequested && mouthMovement[4] > 0 && character != null && actorMemory != null)
                            {
                                animationMemory.LipsOverride = LipSyncTypes[4].Timeline.AnimationId;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), LipSyncTypes[4].Timeline.AnimationId, "Lipsync");
                                var adjustedDelay = CalculateAdjustedDelay(mouthMovement[4] * 1000, 4);

                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[4] durationMs[{mouthMovement[4]}] delay [{adjustedDelay}]", eventId);

                                await Task.Delay(adjustedDelay, token);
                                if (!token.IsCancellationRequested && character != null && actorMemory != null)
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[4] was finished", eventId);

                                    animationMemory.LipsOverride = 0;
                                    MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                    MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                }
                            }

                            if (!token.IsCancellationRequested)
                            {

                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was Completed", eventId);

                                cts.Dispose();
                                taskCancellations.Remove(character);
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            try
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task canceling.", eventId);
                                animationMemory.LipsOverride = 0;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                                cts.Dispose();
                                taskCancellations.Remove(character);
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was canceled.", eventId);
                            }
                            catch (Exception ex)
                            {
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while cancelling task. Exception: {ex}", eventId);
                            }
                        }
                    }, token);
                }
            }
        }

        public async void StopLipSync(EKEventId eventId)
        {
            try
            {
                if (Conditions.IsBoundByDuty) return;
                if (!config.Enabled) return;
                if (currentLipsync == null) return;

                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Stopping Lipsync for {currentLipsync.Name.TextValue}", eventId);
                if (taskCancellations.TryGetValue(currentLipsync, out var cts))
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
                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), currentIntialState, "Animation Mode Override");
                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                taskCancellations.Remove(currentLipsync);
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
            _memoryService = new MemoryService();
            _gameDataService = new GameDataService();
            _animationService = new AnimationService();
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
            await _memoryService.Initialize();
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "StartServices --> Waiting for Process Response", new EKEventId(0, Enums.TextSource.None));
            while (!Process.GetCurrentProcess().Responding)
                await Task.Delay(100);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "StartServices --> Done waiting", new EKEventId(0, Enums.TextSource.None));
            await _memoryService.OpenProcess(Process.GetCurrentProcess());
            await _gameDataService.Initialize();

            LipSyncTypes = GenerateLipList().ToList();
            await _animationService.Initialize();
            await _animationService.Start();
            await _memoryService.Start();
        }

        private IEnumerable<ActionTimeline> GenerateLipList()
        {
            // Grab "no animation" and all "speak/" animations, which are the only ones valid in this slot
            var lips = GameDataService.ActionTimelines.Where(x => x.AnimationId == 0 || (x.Key?.StartsWith("speak/") ?? false));
            return lips;
        }

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

        private IGameObject DiscoverNpc(string npcName)
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
                foreach (var item in objects)
                {
                    if (item as Character == null || item as Character == clientState.LocalPlayer || item.Name.TextValue == "") continue;
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