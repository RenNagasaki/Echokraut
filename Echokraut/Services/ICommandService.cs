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
    event Action<EKEventId>? CancelAllRequested;
    event Action? DumpSheetsRequested;
    event Action<uint>? SearchSheetIdRequested;

    void ToggleConfigUi();
    void ToggleVoiceClipManagerUi();
    void ToggleFirstTimeUi();
    void PrintText(string name, string text);
    void Dispose();
}
