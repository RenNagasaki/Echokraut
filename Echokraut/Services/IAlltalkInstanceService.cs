using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface IAlltalkInstanceService
{
    event Action? OnInstanceReady;

    bool Installing { get; }
    bool InstanceRunning { get; }
    bool InstanceStarting { get; }
    bool InstanceStopping { get; }
    bool IsWindows { get; }
    bool IsCudaInstalled { get; }

    void Install();
    void StartInstance();
    void StopInstance(EKEventId eventId);
    Task InstallCustomData(EKEventId eventId, bool installProcess = true);
}
