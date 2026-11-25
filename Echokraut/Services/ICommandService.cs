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
    /// <summary><c>/ektest</c> (false) or <c>/ektest all</c> (true = with the interactive checks).</summary>
    event Action<bool>? SelfTestRequested;

    void ToggleConfigUi();
    void ToggleVoiceClipManagerUi();
    void ToggleFirstTimeUi();
    void ToggleGameDataToolsUi();
    void PrintText(string name, string text);
    void Dispose();
}
