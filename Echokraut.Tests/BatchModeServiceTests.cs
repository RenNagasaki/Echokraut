using Echokraut.Enums;
using Echokraut.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

public class BatchModeServiceTests
{
    private readonly Mock<IDialogHarvestService> _harvest = new();
    private readonly Mock<IVoicePackService> _voicePack = new();
    private readonly BatchModeService _sut;

    public BatchModeServiceTests()
    {
        _sut = new BatchModeService(_harvest.Object, _voicePack.Object);
    }

    [Fact]
    public void Idle_ReportsNoneAndInactive()
    {
        _harvest.SetupGet(h => h.IsRunning).Returns(false);
        _voicePack.SetupGet(v => v.IsRunning).Returns(false);

        Assert.Equal(BatchOperation.None, _sut.CurrentOperation);
        Assert.False(_sut.IsActive);
    }

    [Fact]
    public void HarvestRunning_ReportsHarvest()
    {
        _harvest.SetupGet(h => h.IsRunning).Returns(true);
        _voicePack.SetupGet(v => v.IsRunning).Returns(false);

        Assert.Equal(BatchOperation.Harvest, _sut.CurrentOperation);
        Assert.True(_sut.IsActive);
    }

    [Fact]
    public void VoicePackRunning_ReportsVoicePackDownload()
    {
        _harvest.SetupGet(h => h.IsRunning).Returns(false);
        _voicePack.SetupGet(v => v.IsRunning).Returns(true);

        Assert.Equal(BatchOperation.VoicePackDownload, _sut.CurrentOperation);
        Assert.True(_sut.IsActive);
    }

    [Fact]
    public void BothRunning_HarvestWinsForLabel()
    {
        // Shouldn't happen in practice but the resolver must pick a stable answer rather
        // than oscillate between two truthy flags.
        _harvest.SetupGet(h => h.IsRunning).Returns(true);
        _voicePack.SetupGet(v => v.IsRunning).Returns(true);

        Assert.Equal(BatchOperation.Harvest, _sut.CurrentOperation);
        Assert.True(_sut.IsActive);
    }
}
