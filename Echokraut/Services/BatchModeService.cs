using Echokraut.Enums;

namespace Echokraut.Services;

internal sealed class BatchModeService : IBatchModeService
{
    private readonly IDialogHarvestService _harvest;
    private readonly IVoiceSampleExtractorService _extract;

    public BatchModeService(IDialogHarvestService harvest, IVoiceSampleExtractorService extract)
    {
        _harvest = harvest;
        _extract = extract;
    }

    public bool IsActive => CurrentOperation != BatchOperation.None;

    // Resolution order is significant: harvest takes precedence over extract if both
    // somehow end up running concurrently (shouldn't happen — the UI prevents starting
    // a second op while one is active — but the order picks a stable label for logs).
    public BatchOperation CurrentOperation =>
        _harvest.IsRunning ? BatchOperation.Harvest
        : _extract.IsRunning ? BatchOperation.VoiceExtract
        : BatchOperation.None;
}
