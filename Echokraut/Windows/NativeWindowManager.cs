using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echokraut.Windows.Native;

namespace Echokraut.Windows;

public class NativeWindowManager : IWindowManager
{
    private readonly DialogTalkController _dialogController;

    public bool IsFirstTimeOpen => false;

    public NativeWindowManager(ServiceContainer services, Configuration config, IAddonLifecycle addonLifecycle)
    {
        var addonTalk = services.GetService<IAddonTalkHelper>();

        _dialogController = new DialogTalkController(
            config,
            services.GetService<IAudioPlaybackService>(),
            services.GetService<ILipSyncHelper>(),
            () => addonTalk.RecreateInference(),
            addonLifecycle);
    }

    // Config/FirstTime windows are stubs — Phase 3 will add native addons for these.
    public void ToggleConfig() { }
    public void ToggleFirstTime() { }
    public void Draw() { }

    public void Dispose()
    {
        _dialogController.Dispose();
        KamiToolKit.KamiToolKitLibrary.Cleanup();
    }
}
