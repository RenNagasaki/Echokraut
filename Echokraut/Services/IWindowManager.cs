using System;

namespace Echokraut.Services;

public interface IWindowManager : IDisposable
{
    bool IsFirstTimeOpen { get; }
    bool IsChangelogOpen { get; }
    void ToggleConfig();
    void ToggleFirstTime();
    void ToggleVoiceClipManager();
    void ToggleGameDataTools();
    void ToggleChangelog();
    void Draw();
}
