using Dalamud.Configuration;
using Dalamud.Plugin;
using Echokraut.Enums;
using Echotools.Logging.Enums;
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
    public float GlobalVolume { get; set; } = 1.0f;
    public bool VoiceDialogue { get; set; } = true;
    public bool VoiceBattleDialogue { get; set; } = true;
    public bool VoiceBattleDialogQueued { get; set; } = true;
    public bool VoicePlayerChoicesCutscene { get; set; } = true;
    public bool VoicePlayerChoices { get; set; } = true;
    public bool RemovePunctuation { get; set; } = false;
    public bool ShowExtraOptionsInDialogue { get; set; } = true;
    public bool CancelSpeechOnTextAdvance { get; set; } = true;
    public bool GenerateBySentence { get; set; } = false;
    public bool AutoAdvanceTextAfterSpeechCompleted { get; set; } = true;
    public bool RemoveStutters { get; set; } = true;
    public bool HideUiInCutscenes { get; set; } = true;
    public LogConfig logConfig { get; set; } = new LogConfig();
    public bool SaveToLocal { get; set; } = true;
    public bool LoadFromLocalFirst { get; set; } = true;
    public string LocalSaveLocation { get; set; } = @"C:\alltalk_tts\LocalSaves";
    public bool CreateMissingLocalSaveLocation { get; set; } = true;
    /// <summary>
    /// When true, every time a voice clip with player-name placeholders is generated for the
    /// local player, additional male + female alias variants are auto-generated using a
    /// localized generic noun (Adventurer / Abenteurer / Aventurier / 冒険者). These variants
    /// are shareable with other users via export/import.
    /// </summary>
    public bool AutoGenerateShareableAliases { get; set; } = false;
    /// <summary>
    /// One-shot marker set by <see cref="Echokraut.Services.DatabaseService.MigrateFromConfig"/>
    /// once the JSON-config import landed. The audio-file backfill (walks
    /// <see cref="LocalSaveLocation"/> and creates voice_clip_generations rows for legacy
    /// on-disk audio that has no matching DB record) reads this flag on the next plugin
    /// start where the player is logged in, runs the scan, and clears the flag back to
    /// false. Stays false on fresh installs (no JSON data to migrate from), so users
    /// who arrive after the rewrite never trigger a useless filesystem walk.
    /// </summary>
    public bool AudioFilesBackfillPending { get; set; } = false;
    /// <summary>
    /// Last plugin version whose changelog was shown to (and dismissed by) the user.
    /// Compared against <c>Plugin.PluginVersion</c> on startup; if any embedded changelog
    /// entries fall in (LastSeen, current], the changelog window pops up after login.
    /// Format mirrors <c>PluginVersion</c> ("v0.19.0.0") — strip the leading "v" before
    /// parsing as <c>System.Version</c> for ordered comparison; plain string comparison
    /// would mis-sort across digit-count boundaries.
    ///
    /// Default "v0.18.0.6" is the last release before the changelog system landed:
    /// existing users upgrading from there see the v0.19.0.0 changelog on next start.
    /// Brand-new installs get this value bumped to the current version when the
    /// First-Time wizard completes (so they don't see a changelog about features they
    /// already start with).
    /// </summary>
    public string LastSeenChangelogVersion { get; set; } = "v0.18.0.6";
    public bool VoiceRetainers { get; set; } = false;
    public bool VoiceBubble { get; set; } = false;
    public bool VoiceBubblesInCity { get; set; } = false;
    public bool VoiceSourceCam { get; set; } = false;
    public float Voice3DAudibleRange { get; set; } = 0.3f;
    public bool VoiceChat { get; set; } = false;
    public string VoiceChatLanguageAPIKey { get; set; } = "";
    public bool VoiceChatIn3D { get; set; } = true;
    public bool VoiceDialogueIn3D { get; set; } = false;
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
    public bool GoogleDriveDownload { get; set; } = false;
    public bool GoogleDriveUpload { get; set; } = false;
    public bool GoogleDriveDownloadPeriodically { get; set; } = false;
    public bool GoogleDriveRequestVoiceLine { get; set; } = false;
    public string GoogleDriveShareLink { get; set; } = "";

    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private IDalamudPluginInterface? PluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;

        // One-shot migrations from older config schemas. Each call must be idempotent —
        // it runs every time the plugin starts.
        Alltalk?.MigrateLegacyInstanceTypeFields();
    }

    public void Save()
    {
        PluginInterface?.SavePluginConfig(this);
    }
}
