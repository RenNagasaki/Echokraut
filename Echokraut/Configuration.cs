using Dalamud.Configuration;
using Dalamud.Plugin;
using Echokraut.DataClasses;
using System;
using System.Collections.Generic;

namespace Echokraut;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public List<NpcMapData> mappedNpcs { get; set; } = new List<NpcMapData>();

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
