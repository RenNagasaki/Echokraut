using Echokraut.DataClasses;

namespace Echokraut.Services;

public interface IAddonCancelService
{
    void Cancel(VoiceMessage? message, bool dialogClosed = false);
}
