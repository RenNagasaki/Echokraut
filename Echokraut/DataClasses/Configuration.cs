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
    public List<NpcMapData> MappedPlayers { get; set; } = new List<NpcMapData>();
    public List<uint> MutedNpcDialogues { get; set; } = new List<uint>();
    public List<EchokrautVoice> EchokrautVoices { get; set; } = new List<EchokrautVoice>();
    public bool FirstTime { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public bool VoiceDialogue { get; set; } = true;
    public bool VoiceBattleDialogue { get; set; } = true;
    public bool VoiceBattleDialogQueued { get; set; } = true;
    public bool VoicePlayerChoicesCutscene { get; set; } = true;
    public bool VoicePlayerChoices { get; set; } = true;
    public bool RemovePunctuation { get; set; } = false;
    public bool ShowExtraOptionsInDialogue { get; set; } = true;
    public bool ShowExtraExtraOptionsInDialogue { get; set; } = true;
    public bool CancelSpeechOnTextAdvance { get; set; } = true;
    public bool GenerateBySentence { get; set; } = false;
    public bool AutoAdvanceTextAfterSpeechCompleted { get; set; } = true;
    public bool RemoveStutters { get; set; } = true;
    public bool HideUiInCutscenes { get; set; } = true;
    public LogConfig logConfig { get; set; } = new LogConfig();
    public bool SaveToLocal { get; set; } = false;
    public bool LoadFromLocalFirst { get; set; } = false;
    public string LocalSaveLocation { get; set; } = @"C:\alltalk_tts\LocalSaves";
    public bool CreateMissingLocalSaveLocation { get; set; } = false;
    public bool VoiceRetainers { get; set; } = false;
    public bool VoiceBubble { get; set; } = false;
    public bool VoiceBubblesInCity { get; set; } = false;
    public bool VoiceSourceCam { get; set; } = false;
    public float VoiceBubbleAudibleRange { get; set; } = 1f;
    public bool VoiceChat { get; set; } = false;
    public string VoiceChatLanguageAPIKey { get; set; } = "";
    public bool VoiceChatWithout3D { get; set; } = false;
    public bool VoiceChatPlayer { get; set; } = false;
    public bool VoiceChatSay { get; set; } = false;
    public bool VoiceChatNoviceNetwork { get; set; } = false;
    public bool VoiceChatTell { get; set; } = false;
    public bool VoiceChatAlliance { get; set; } = false;
    public bool VoiceChatCrossLinkshell { get; set; } = false;
    public bool VoiceChatLinkshell { get; set; } = false;
    public bool VoiceChatParty { get; set; } = false;
    public bool VoiceChatYell { get; set; } = false;
    public bool VoiceChatShout { get; set; } = false;
    public bool VoiceChatFreeCompany { get; set; } = false;
    public List<PhoneticCorrection> PhoneticCorrections { get; set; } = new List<PhoneticCorrection>();

    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private IDalamudPluginInterface? PluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public void Save()
    {
        PluginInterface!.SavePluginConfig(this);
    }
}
