namespace Echokraut.Enums;

/// <summary>
/// Long-running, exclusive operations the plugin can be busy with. While one of these is
/// active, <see cref="Echokraut.Services.IBatchModeService.IsActive"/> returns true and UI
/// surfaces that mutate backend / mode state must dim themselves so the run isn't disturbed
/// (only the operation's own Stop button stays interactive).
/// </summary>
public enum BatchOperation
{
    None,
    Harvest,
    VoiceExtract,
}
