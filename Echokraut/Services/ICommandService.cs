using Echokraut.DataClasses;
using System;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface ICommandService
{
    List<string> CommandKeys { get; }

    event Action? ToggleConfigRequested;
    event Action? ToggleFirstTimeRequested;
    event Action<EKEventId>? CancelAllRequested;
    event Action? UiModeSwitchRequested;

    void ToggleConfigUi();
    void ToggleFirstTimeUi();
    void RequestUiModeSwitch();
    void PrintText(string name, string text);
    void Dispose();
}
