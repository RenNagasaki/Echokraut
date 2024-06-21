using Dalamud.Configuration;
using Dalamud.Plugin;
using Echokraut.Enums;
using System;
using System.Collections.Generic;

namespace Echokraut.DataClasses;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public TTSBackends BackendSelection { get; set; } = TTSBackends.Alltalk;
    public AlltalkData Alltalk { get; set; } = new AlltalkData();
    public List<NpcMapData> MappedNpcs { get; set; } = new List<NpcMapData>();
    public bool Enabled { get; set; } = false;
    public bool VoiceBattleDialog { get; set; } = false;
    public bool VoiceDialog { get; set; } = false;
    public bool CancelSpeechOnTextAdvance { get; set; } = true;
    public bool AutoAdvanceTextAfterSpeechCompleted { get; set; } = false;
    public bool RemoveStutters { get; set; } = true;
    public bool ShowInfoLog { get; set; } = true;
    public bool ShowDebugLog { get; set; } = true;
    public bool ShowErrorLog { get; set; } = true;
    public bool JumpToBottom { get; set; } = true;

    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private DalamudPluginInterface? PluginInterface;

    public void Initialize(DalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public void Save()
    {
        PluginInterface!.SavePluginConfig(this);
    }
}
