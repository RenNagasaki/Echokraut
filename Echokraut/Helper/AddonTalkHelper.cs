using System;
using Dalamud.Plugin.Services;
using R3;
using Echokraut.TextToTalk.Utils;
using Dalamud.Configuration;
using Echokraut.DataClasses;
using static Dalamud.Plugin.Services.IFramework;
using System.IO;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using Echokraut.Utils;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Anamnesis.Memory;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using Dalamud.Game.ClientState.Objects.Types;
using Anamnesis.Services;
using Anamnesis.GameData.Excel;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using static Dalamud.Interface.Utility.Raii.ImRaii;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
using System.Reflection;

namespace Echokraut.Helper;

public class AddonTalkHelper
{
    private record struct AddonTalkState(string? Speaker, string? Text, AddonPollSource PollSource);

    private readonly IObjectTable objects;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IGameGui gui;
    private readonly IFramework framework;
    private readonly Configuration config;
    private readonly Echokraut plugin;
    private OnUpdateDelegate updateHandler;
    private Character currentLipsync;

    private readonly string name;

    private MemoryService _memoryService;
    private AnimationService _animationService;
    private GameDataService _gameDataService;

    public static nint Address { get; set; }

    // Most recent speaker/text specific to this addon
    private string? lastAddonSpeaker;
    private string? lastAddonText;
    private AddonTalkState lastValue;
    Dictionary<Character, CancellationTokenSource> taskCancellations = new Dictionary<Character, CancellationTokenSource>();
    public List<ActionTimeline> LipSyncTypes { get; private set; }

    public AddonTalkHelper(Echokraut plugin, IClientState clientState, ICondition condition, IGameGui gui, IFramework framework, IObjectTable objects, Configuration config)
    {
        this.plugin = plugin;
        this.clientState = clientState;
        this.condition = condition;
        this.gui = gui;
        this.framework = framework;
        this.config = config;
        this.objects = objects;

        HookIntoFrameworkUpdate();
        InitializeAsync().ContinueWith(t => {
            if (t.Exception != null)
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Initialization failed: " + t.Exception);
        });
    }

    private async Task InitializeAsync()
    {
        LogHelper.Info(MethodBase.GetCurrentMethod().Name, "InitializeAsync --> Waiting for Game Process Stability");
        await WaitForGameProcessStability();
        LogHelper.Info(MethodBase.GetCurrentMethod().Name, "InitializeAsync --> Done waiting");
        InitializeServices();
    }

    private async Task WaitForGameProcessStability()
    {
        // Wait until the game process is stable
        while (Process.GetCurrentProcess() == null || !Process.GetCurrentProcess().Responding)
        {
            await Task.Delay(1000); // Check every second
        }
    }

    private void InitializeServices()
    {
        // Initialize all services that depend on the game process
        _memoryService = new MemoryService();
        _gameDataService = new GameDataService();
        _animationService = new AnimationService();
        StartServices();
    }

    private async void StartServices()
    {
        await _memoryService.Initialize();
        LogHelper.Info(MethodBase.GetCurrentMethod().Name, "StartServices --> Waiting for Process Response");
        while (!Process.GetCurrentProcess().Responding)
            await Task.Delay(100);
        LogHelper.Info(MethodBase.GetCurrentMethod().Name, "StartServices --> Done waiting");
        await _memoryService.OpenProcess(Process.GetCurrentProcess());
        await _gameDataService.Initialize();

        LipSyncTypes = GenerateLipList().ToList();
        await _animationService.Initialize();
        await _animationService.Start();
        await _memoryService.Start();
    }

    private void HookIntoFrameworkUpdate()
    {
        updateHandler = new OnUpdateDelegate(Handle);
        framework.Update += updateHandler;

    }
    void Handle(IFramework f)
    {
        UpdateAddonAddress();
        if (!config.Enabled) return;
        if (!config.VoiceDialog) return;
        PollAddon(AddonPollSource.FrameworkUpdate);
    }

    private void Mutate(AddonTalkState nextValue)
    {
        if (lastValue.Equals(nextValue))
        {
            return;
        }

        lastValue = nextValue;
        HandleChange(nextValue);
    }

    private IEnumerable<ActionTimeline> GenerateLipList()
    {
        // Grab "no animation" and all "speak/" animations, which are the only ones valid in this slot
        IEnumerable<ActionTimeline> lips = GameDataService.ActionTimelines.Where(x => x.AnimationId == 0 || (x.Key?.StartsWith("speak/") ?? false));
        return lips;
    }

    private void UpdateAddonAddress()
    {
        if (!clientState.IsLoggedIn || condition[ConditionFlag.CreatingCharacter])
        {
            Address = nint.Zero;
            return;
        }

        if (Address == nint.Zero)
        {
            Address = gui.GetAddonByName("Talk");
        }
    }

    private AddonTalkState GetTalkAddonState(AddonPollSource pollSource)
    {
        if (!IsVisible())
        {
            return default;
        }

        var addonTalkText = ReadText();
        return addonTalkText != null
            ? new AddonTalkState(addonTalkText.Speaker, addonTalkText.Text, pollSource)
            : default;
    }

    public void PollAddon(AddonPollSource pollSource)
    {
        var state = GetTalkAddonState(pollSource);
        Mutate(state);
    }

    private void HandleChange(AddonTalkState state)
    {
        var (speaker, text, pollSource) = state;

        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonTalk ({state})");
        if (state == default)
        {
            // The addon was closed
            plugin.Cancel();
            lastAddonSpeaker = "";
            lastAddonText = "";
            return;
        }

        // Notify observers that the addon state was advanced
        plugin.Cancel();

        text = TalkUtils.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonTalk ({pollSource}): \"{text}\"");

        {
            // This entire callback executes twice in a row - once for the voice line, and then again immediately
            // afterwards for the framework update itself. This prevents the second invocation from being spoken.
            if (lastAddonSpeaker == speaker && lastAddonText == text)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping duplicate line: {text}");
                return;
            }

            lastAddonSpeaker = speaker;
            lastAddonText = text;
        }

        if (pollSource == AddonPollSource.VoiceLinePlayback)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice-acted line: {text}");
            return;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? ObjectTableUtils.GetGameObjectByName(objects, speaker) : null;

        if (speakerObj != null)
        {
            plugin.Say(speakerObj, speakerObj.Name, text, TextSource.AddonTalk);
        }
        else
        {
            plugin.Say(null, state.Speaker ?? "", text, TextSource.AddonTalk);
        }
    }

    public unsafe AddonTalkText? ReadText()
    {
        var addonTalk = GetAddonTalk();
        return addonTalk == null ? null : TalkUtils.ReadTalkAddon(addonTalk);
    }

    public unsafe bool IsVisible()
    {
        var addonTalk = GetAddonTalk();
        return addonTalk != null && addonTalk->AtkUnitBase.IsVisible;
    }

    private unsafe AddonTalk* GetAddonTalk()
    {
        return (AddonTalk*)Address.ToPointer();
    }

    public async void TriggerLipSync(string npcName, float length)
    {
        if (Conditions.IsBoundByDuty && !Conditions.IsWatchingCutscene) return;
        if (!config.Enabled) return;

        GameObject npcObject = DiscoverNpc(npcName);
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
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"durationMs[{durationMs}] durationRounded[{durationRounded}] fours[{mouthMovement[6]}] twos[{mouthMovement[5]}] ones[{mouthMovement[4]}]");

            // Decide on the Mode
            ActorMemory.CharacterModes intialState = actorMemory.CharacterMode;
            ActorMemory.CharacterModes mode = ActorMemory.CharacterModes.EmoteLoop;


            if (!taskCancellations.ContainsKey(character))
            {
                var cts = new CancellationTokenSource();
                taskCancellations.Add(character, cts);
                var token = cts.Token;

                Task task = Task.Run(async () => {
                    try
                    {
                        await Task.Delay(100, token);

                        if (!token.IsCancellationRequested && mouthMovement[6] > 0 && character != null && actorMemory != null && actorMemory != null)
                        {
                            animationMemory.LipsOverride = LipSyncTypes[6].Timeline.AnimationId;
                            MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), mode, "Animation Mode Override");
                            MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), LipSyncTypes[6].Timeline.AnimationId, "Lipsync");

                            int adjustedDelay = CalculateAdjustedDelay(mouthMovement[6] * 4000, 6);

                            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[6] durationMs[{mouthMovement[6] * 4}] delay [{adjustedDelay}]");

                            await Task.Delay(adjustedDelay, token);

                            if (!token.IsCancellationRequested && character != null && actorMemory != null)
                            {

                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[6] was finished");

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
                            int adjustedDelay = CalculateAdjustedDelay(mouthMovement[5] * 2000, 5);

                            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[5] durationMs[{mouthMovement[5] * 2}] delay [{adjustedDelay}]");

                            await Task.Delay(adjustedDelay, token);
                            if (!token.IsCancellationRequested && character != null && actorMemory != null)
                            {

                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[5] was finished");

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
                            int adjustedDelay = CalculateAdjustedDelay(mouthMovement[4] * 1000, 4);

                            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was started mouthMovement[4] durationMs[{mouthMovement[4]}] delay [{adjustedDelay}]");

                            await Task.Delay(adjustedDelay, token);
                            if (!token.IsCancellationRequested && character != null && actorMemory != null)
                            {

                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task mouthMovement[4] was finished");

                                animationMemory.LipsOverride = 0;
                                MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                                MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                            }
                        }

                        if (!token.IsCancellationRequested)
                        {

                            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was Completed");

                            cts.Dispose();
                            taskCancellations.Remove(character);
                        }
                    }
                    catch (TaskCanceledException)
                    {


                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Task was canceled.");

                        animationMemory.LipsOverride = 0;
                        MemoryService.Write(actorMemory.GetAddressOfProperty(nameof(ActorMemory.CharacterModeRaw)), intialState, "Animation Mode Override");
                        MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
                        cts.Dispose();
                        taskCancellations.Remove(character);
                    }
                }, token);
            }
        }
    }

    int CalculateAdjustedDelay(int durationMs, int lipSyncType)
    {
        int delay = 0;
        int animationLoop;
        if (lipSyncType == 4)
            animationLoop = 1000;
        else if (lipSyncType == 5)
            animationLoop = 2000;
        else
            animationLoop = 4000;
        int halfStep = animationLoop / 2;

        if (durationMs <= (1 * animationLoop) + halfStep)
        {
            return (1 * animationLoop) - 50;
        }
        else
            for (int i = 2; delay < durationMs; i++)
                if (durationMs > (i * animationLoop) - halfStep && durationMs <= (i * animationLoop) + halfStep)
                {
                    delay = (i * animationLoop) - 50;
                    return delay;
                }

        return 404;
    }

    private GameObject DiscoverNpc(string npcName)
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

    public async void StopLipSync()
    {
        if (Conditions.IsBoundByDuty && !Conditions.IsWatchingCutscene) return;
        if (!config.Enabled) return;
        if (currentLipsync == null) return;

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Stopping Lipsync for {currentLipsync.Name}");
        if (taskCancellations.TryGetValue(currentLipsync, out var cts))
        {
            //LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Cancellation " + character.Name);
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"CTS for {currentLipsync.Name} was called to be disposed even though it was disposed already.");
            }
            return;
        }

        try
        {
            //LogHelper.Info(MethodBase.GetCurrentMethod().Name, "StopLipSync " + character.Name);
            var actorMemory = new ActorMemory();
            actorMemory.SetAddress(currentLipsync.Address);
            var animationMemory = actorMemory.Animation;
            animationMemory.LipsOverride = LipSyncTypes[5].Timeline.AnimationId;
            MemoryService.Write(animationMemory.GetAddressOfProperty(nameof(AnimationMemory.LipsOverride)), 0, "Lipsync");
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"{ex}");
        }
    }

    public int EstimateDurationFromMessage(string message)
    {
        int words = message.Split(new char[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        double wordsPerSecond = 150.0 / 60; // 150 words per minute converted to words per second

        return (int)(words / wordsPerSecond * 1000); // duration in milliseconds
    }

    public void Dispose()
    {
        framework.Update -= updateHandler;
    }
}
