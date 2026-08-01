using Echokraut.DataClasses;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface ILipSyncHelper
{
    Task TryLipSync(VoiceMessage message);
    void TryStopLipSync(VoiceMessage message);
}
