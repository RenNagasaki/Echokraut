using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;

namespace Echokraut.Services;

public interface IVolumeService
{
    float GetVoiceVolume(EKEventId eventId);
}
