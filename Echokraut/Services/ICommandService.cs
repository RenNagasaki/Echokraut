using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface ICommandService
{
    List<string> CommandKeys { get; }

    event Action? ToggleConfigRequested;
    event Action? ToggleFirstTimeRequested;
    event Action? ToggleVoiceClipManagerRequested;
    event Action? ToggleGameDataToolsRequested;
    event Action<EKEventId>? CancelAllRequested;

    void ToggleConfigUi();
    void ToggleVoiceClipManagerUi();
    void ToggleFirstTimeUi();
    void ToggleGameDataToolsUi();
    void PrintText(string name, string text);
    void Dispose();
}
