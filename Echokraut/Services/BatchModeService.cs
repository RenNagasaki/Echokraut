using Echokraut.Enums;

namespace Echokraut.Services;

internal sealed class BatchModeService : IBatchModeService
{
    private readonly IDialogHarvestService _harvest;
    private readonly IVoicePackService _voicePack;

    public BatchModeService(IDialogHarvestService harvest, IVoicePackService voicePack)
    {
        _harvest = harvest;
        _voicePack = voicePack;
    }

    public bool IsActive => CurrentOperation != BatchOperation.None;

    // Resolution order is significant: harvest takes precedence over the voice pack download
    // if both somehow end up running concurrently (shouldn't happen — the UI prevents starting
    // a second op while one is active — but the order picks a stable label for logs).
    public BatchOperation CurrentOperation =>
        _harvest.IsRunning ? BatchOperation.Harvest
        : _voicePack.IsRunning ? BatchOperation.VoicePackDownload
        : BatchOperation.None;
}
