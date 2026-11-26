using System;
using System.Threading.Tasks;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// The game's voice volume slider (System config <c>SoundVoice</c>, 0-100) for the E2E scenario <c>volume</c>
/// (plan › B7). It is a setting of the game, not of the plugin: nothing in the plugin backup restores it, so
/// the scenario lists the original in <see cref="E2eState.CreatedIds"/> (<see cref="IdPrefix"/>) and the
/// cleanup stage puts it back when the scenario could not.
/// </summary>
public interface IE2eGameSound
{
    uint? VoiceVolume { get; }

    /// <summary>Master or voice muted in the game: every line is skipped with volume 0.</summary>
    bool Muted { get; }

    Task SetVoiceVolumeAsync(uint value);
}

public static class E2eGameSoundIds
{
    public const string IdPrefix = "gamecfg:SoundVoice=";

    public static string Id(uint original) => IdPrefix + original;

    public static uint? Parse(string id) =>
        id.StartsWith(IdPrefix, StringComparison.Ordinal) && uint.TryParse(id[IdPrefix.Length..], out var v) && v <= 100 ? v : null;
}

public sealed class GameConfigSound : IE2eGameSound
{
    private readonly IGameConfig _gameConfig;
    private readonly Func<Action, Task> _onFramework;

    public GameConfigSound(IGameConfig gameConfig, Func<Action, Task> onFramework)
    {
        _gameConfig = gameConfig;
        _onFramework = onFramework;
    }

    public uint? VoiceVolume => _gameConfig.TryGet(SystemConfigOption.SoundVoice, out uint v) ? v : null;

    public bool Muted =>
        (_gameConfig.TryGet(SystemConfigOption.IsSndMaster, out bool master) && master)
        || (_gameConfig.TryGet(SystemConfigOption.IsSndVoice, out bool voice) && voice);

    public Task SetVoiceVolumeAsync(uint value) => _onFramework(() => _gameConfig.Set(SystemConfigOption.SoundVoice, value));
}
