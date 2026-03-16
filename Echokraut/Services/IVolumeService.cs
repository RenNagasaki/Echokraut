using Echokraut.DataClasses;

namespace Echokraut.Services;

public interface IVolumeService
{
    float GetVoiceVolume(EKEventId eventId);
}
