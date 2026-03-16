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

    void ToggleConfigUi();
    void ToggleFirstTimeUi();
    void PrintText(string name, string text);
    void Dispose();
}
