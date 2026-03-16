using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Echokraut.DataClasses;
using System.Threading;
using System.Text.RegularExpressions;

namespace Echokraut.Services
{
    public class LipSyncHelper : ILipSyncHelper
    {
        private readonly ILogService _log;
        private readonly IFramework _framework;
        private readonly IObjectTable _objectTable;

        public LipSyncHelper(ILogService log, IFramework framework, IObjectTable objectTable)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _framework = framework ?? throw new ArgumentNullException(nameof(framework));
            _objectTable = objectTable ?? throw new ArgumentNullException(nameof(objectTable));
        }

        public sealed record Options(
            double SyllablesPerSecond = 5.0,
            double CommaPause = 0.12,
            double ColonPause = 0.18,
            double DashPause = 0.15,
            double EndPause = 0.30,
            double NewlinePause = 0.20,
            bool TreatHyphenAsDash = false
        );

        // ActionTimeline exd sheet
        private const ushort SpeakNone = 0;
        private const ushort SpeakNormalLong = 631;
        private const ushort SpeakNormalMiddle = 630;
        private const ushort SpeakNormalShort = 629;

        private ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = [];

        private string GetTaskId(VoiceMessage message)
        {
            if (!string.IsNullOrEmpty(message.Speaker?.Name)) return message.Speaker.Name;
            if (message.SpeakerObj != null) return message.SpeakerObj.BaseId.ToString();
            return message.EventId.Id.ToString();
        }

        public async Task TryLipSync(VoiceMessage message)
        {
            var durationSeconds = EstimateSeconds(message.Text);
            if (durationSeconds < 0.2f) return;

            TryStopLipSync(message);

            IntPtr character =
                await _framework.RunOnFrameworkThread(() => TryFindCharacter(message.Speaker.Name,
                                                                message.SpeakerObj?.BaseId ?? 0));
            if (character == IntPtr.Zero)
            {
                _log.Debug(nameof(TryLipSync),
                                $"No lipsync target found for speaker {message.Speaker.Name} ({message.SpeakerObj?.BaseId})",
                                message.EventId);
                return;
            }

            CancellationTokenSource cts = new();
            string taskId = GetTaskId(message);
            if (!_runningTasks.TryAdd(taskId, cts))
            {
                cts.Dispose();
                _log.Debug(nameof(TryLipSync),
                                $"Could not add CTS for {taskId}, task already running.", message.EventId);
                return;
            }

            CancellationToken token = cts.Token;
            CharacterModes initialCharacterMode = TryGetCharacterMode(character);
            CharacterModes characterMode = CharacterModes.EmoteLoop;

            int durationMs = (int)(durationSeconds * 1000);
            int durationRounded = (int)Math.Floor(durationSeconds);
            Dictionary<int, int> mouthMovement = new()
            {
                [6] = durationRounded / 4,
                [5] = durationRounded % 4 / 2,
                [4] = durationRounded % 2
            };

            _log.Debug(nameof(TryLipSync),
                            $"durationMs[{durationMs}] durationRounded[{durationRounded}] fours[{mouthMovement[6]}] twos[{mouthMovement[5]}] ones[{mouthMovement[4]}]",
                            message.EventId);

            await Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100, token);

                    if (mouthMovement[6] > 0)
                    {
                        int delay = CalculateAdjustedDelay(message, mouthMovement[6] * 4000, 6);
                        _log.Debug(nameof(TryLipSync), $"Starting 4s lip movement. Delay: {delay}",
                                        message.EventId);
                        await AnimateLipSync(message, initialCharacterMode, characterMode, SpeakNormalLong, delay,
                                             token);
                    }

                    if (mouthMovement[5] > 0)
                    {
                        int delay = CalculateAdjustedDelay(message, mouthMovement[5] * 2000, 5);
                        _log.Debug(nameof(TryLipSync), $"Starting 2s lip movement. Delay: {delay}",
                                        message.EventId);
                        await AnimateLipSync(message, initialCharacterMode, characterMode, SpeakNormalMiddle, delay,
                                             token);
                    }

                    if (mouthMovement[4] > 0)
                    {
                        int delay = CalculateAdjustedDelay(message, mouthMovement[4] * 1000, 4);
                        _log.Debug(nameof(TryLipSync), $"Starting 1s lip movement. Delay: {delay}",
                                        message.EventId);
                        await AnimateLipSync(message, initialCharacterMode, characterMode, SpeakNormalShort, delay,
                                             token);
                    }

                    _log.Debug(nameof(TryLipSync), "LipSync completed successfully",
                                    message.EventId);
                }
                catch (TaskCanceledException)
                {
                    _log.Debug(nameof(TryLipSync), "LipSync was cancelled", message.EventId);
                }
                catch (Exception ex)
                {
                    _log.Error(nameof(TryLipSync), $"Error: {ex.Message}\r\nStacktrace: {ex.StackTrace}", message.EventId);
                } finally
                {
                    await _framework.RunOnFrameworkThread(() =>
                    {
                        IntPtr character = TryFindCharacter(message.Speaker.Name, message.SpeakerObj?.BaseId ?? 0);
                        TrySetCharacterMode(character, initialCharacterMode);
                        TrySetLipsOverride(character, SpeakNone);
                    });

                    if (_runningTasks.TryRemove(taskId, out CancellationTokenSource? oldCts))
                        oldCts.Dispose();
                }
            }, token);
        }

        public void TryStopLipSync(VoiceMessage message)
        {
            string taskId = GetTaskId(message);
            if (_runningTasks.TryRemove(taskId, out CancellationTokenSource? cts))
            {
                try
                {
                    _log.Debug(nameof(TryStopLipSync), $"StopLipSync cancelling CTS for {taskId}",
                                    message.EventId);
                    cts.Cancel();
                    cts.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Error(nameof(TryStopLipSync), $"Error: {ex.Message}\r\nStacktrace: {ex.StackTrace}", message.EventId);
                }
            }
        }

        private async Task AnimateLipSync(
            VoiceMessage message, CharacterModes initialMode, CharacterModes targetMode, ushort speakValue, int delayMs,
            CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            await _framework.RunOnFrameworkThread(() =>
            {
                IntPtr character = TryFindCharacter(message.Speaker.Name, message.SpeakerObj?.BaseId ?? 0);
                TrySetCharacterMode(character, targetMode);
                TrySetLipsOverride(character, speakValue);
            });

            await Task.Delay(delayMs, token);

            if (!token.IsCancellationRequested)
            {
                await _framework.RunOnFrameworkThread(() =>
                {
                    IntPtr character = TryFindCharacter(message.Speaker.Name, message.SpeakerObj?.BaseId ?? 0);
                    TrySetCharacterMode(character, initialMode);
                    TrySetLipsOverride(character, SpeakNone);
                });
                _log.Debug(nameof(AnimateLipSync),
                                $"LipSync {speakValue} block finished after {delayMs}ms", message.EventId);
            }
        }

        private int CalculateAdjustedDelay(VoiceMessage message, int durationMs, int lipSyncType)
        {
            int animationLoop = lipSyncType switch
            {
                4 => 1000,
                5 => 2000,
                6 => 4000,
                _ => 4000
            };

            int halfStep = animationLoop / 2;

            for (int i = 1; i <= 10; i++)
            {
                int ideal = i * animationLoop;
                if (durationMs <= ideal + halfStep)
                    return ideal - 50;
            }

            _log.Debug(nameof(CalculateAdjustedDelay),
                            $"CalculateAdjustedDelay fell through: {durationMs}, {lipSyncType}", message.EventId);
            return 404;
        }

        private unsafe void TrySetLipsOverride(IntPtr _character, ushort lipsOverride)
        {
            Character* character = (Character*)_character;
            if (character == null) return;
            character->Timeline.SetLipsOverrideTimeline(lipsOverride);
        }

        private unsafe CharacterModes TryGetCharacterMode(IntPtr _character)
        {
            Character* character = (Character*)_character;
            if (character == null) return CharacterModes.None;
            return character->Mode;
        }

        private unsafe void TrySetCharacterMode(IntPtr _character, CharacterModes mode)
        {
            Character* character = (Character*)_character;
            if (character == null) return;
            character->SetMode(mode, 0);
        }

        public IntPtr TryFindCharacter(string name, uint? baseId)
        {
            IntPtr baseIdCharacter = IntPtr.Zero;

            foreach (IGameObject gameObject in _objectTable)
            {
                if ((gameObject as ICharacter) == null) continue;

                if (gameObject.BaseId == baseId && baseId != 0)
                    baseIdCharacter = gameObject.Address;

                if (!string.IsNullOrEmpty(name) && gameObject.Name.TextValue == name)
                    return gameObject.Address;
            }

            return baseIdCharacter;
        }

        public static double EstimateSeconds(string text, Options? opt = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0.0;
            var o = opt ?? new Options();

            var cleaned = text.Replace("\r", "");
            var words = Regex.Split(cleaned, @"\s+")
                             .Where(w => w.Length > 0)
                             .ToArray();

            long syllables = 0;
            foreach (var w0 in words)
            {
                var w = w0.ToLowerInvariant();
                w = Regex.Replace(w, "qu", "q");
                var core = Regex.Replace(w, @"^[^\p{L}]+|[^\p{L}]+$", "");
                var m = Regex.Matches(core, @"[aeiouyäöüy]+", RegexOptions.IgnoreCase);
                var count = m.Count;
                if (count == 0 && core.Length > 0) count = 1;
                syllables += count;
            }

            double pauses = 0.0;
            var endMatches = Regex.Matches(cleaned, @"(\.\.\.|…|[.!?])");
            pauses += endMatches.Count * o.EndPause;
            pauses += cleaned.Count(c => c == ',') * o.CommaPause;
            pauses += cleaned.Count(c => c == ';' || c == ':') * o.ColonPause;
            var dashCount = Regex.Matches(cleaned, "[–—]").Count
                            + (o.TreatHyphenAsDash ? cleaned.Count(c => c == '-') : 0);
            pauses += dashCount * o.DashPause;
            pauses += cleaned.Count(c => c == '\n') * o.NewlinePause;

            double speechSeconds = syllables / Math.Max(1e-6, o.SyllablesPerSecond);
            return speechSeconds + pauses;
        }
    }
}
