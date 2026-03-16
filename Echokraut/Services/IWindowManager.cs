using System;

namespace Echokraut.Services;

public interface IWindowManager : IDisposable
{
    bool IsFirstTimeOpen { get; }
    void ToggleConfig();
    void ToggleFirstTime();
    void Draw();
}
