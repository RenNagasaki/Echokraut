using Dalamud.Configuration;
using Dalamud.Plugin;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using System;
using System.Collections.Generic;

namespace Echokraut.DataClasses;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    /// <summary>
    /// Active TTS engine. Defaults to <see cref="TTSBackends.EchokrauTTS"/> so brand-new installs
    /// land on the recommended (zero-local-install, remote-ready) engine. Existing installs are
    /// pinned back to <see cref="TTSBackends.Alltalk"/> exactly once by
    /// <see cref="MigrateBackendSelectionForExistingInstalls"/> — since this field never existed in
    /// their saved JSON, the deserialized value would otherwise silently be this new default and flip
    /// a working AllTalk setup to EchokrauTTS.
    /// </summary>
    public TTSBackends BackendSelection { get; set; } = TTSBackends.EchokrauTTS;
    public AlltalkData Alltalk { get; set; } = new AlltalkData();
    public EchokrauTtsData EchokrauTts { get; set; } = new EchokrauTtsData();

    /// <summary>
    /// Shared, engine-agnostic install root. Both engines live under it (AllTalk in
    /// <c>alltalk_tts\</c>, EchokrauTTS in <c>echokrautts\</c>; see <c>TtsPaths</c>). Migrated once
    /// from the legacy <see cref="AlltalkData.LocalInstallPath"/> in <see cref="Initialize"/>.
    /// </summary>
    public string TtsInstallRoot { get; set; } = DefaultTtsInstallRoot;

    internal const string DefaultTtsInstallRoot = "C:\\alltalk_tts";

    /// <summary>The EchokrautLocalInstaller release tag currently extracted on disk. Compared
    /// against <c>RemoteUrlsData.InstallerVersion</c> to force a re-download when a newer installer
    /// (with new arg modes) is expected, even if the exe already exists (BLK-5).</summary>
    public string InstalledInstallerVersion { get; set; } = string.Empty;

    /// <summary>
    /// The instance type (Local / Remote / None) of the currently-selected engine. Single accessor
    /// so runtime gates and UI read the active engine instead of hard-coding <c>Alltalk</c>. Follows
    /// <see cref="BackendSelection"/> (EchokrauTTS by default on fresh installs; existing installs are
    /// migrated to AllTalk once — see <see cref="MigrateBackendSelectionForExistingInstalls"/>).
    /// </summary>
    public AlltalkInstanceType ActiveInstanceType =>
        BackendSelection == TTSBackends.EchokrauTTS ? EchokrauTts.InstanceType : Alltalk.InstanceType;

    /// <summary>
    /// Engine-aware "can we generate audio" gate (Local or Remote on the active engine). Mirrors
    /// <see cref="AlltalkData.HasLiveGeneration"/> but follows <see cref="BackendSelection"/>.
    /// Runtime / None-mode call sites should gate on this rather than <c>Alltalk.HasLiveGeneration</c>
    /// (re-routing of existing raw sites happens in phase 2).
    /// </summary>
    public bool HasLiveGeneration => ActiveInstanceType != AlltalkInstanceType.None;

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
    // Legacy per-source log-filter blob. Kept only so older saved configs still deserialize;
    // MigrateLegacyLogConfig() folds it into LogSources on startup and nulls it out.
    public LogConfig? logConfig { get; set; }

    // Per-source log-filter prefs (Echotools LogSourceConfig), keyed by TextSource.
    public Dictionary<TextSource, LogSourceConfig> LogSources { get; set; } = new();
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
        MigrateTtsInstallRoot();
        MigrateBackendSelectionForExistingInstalls();
        MigrateLegacyLogConfig();
    }

    /// <summary>
    /// One-shot guard for the <see cref="BackendSelection"/> default flip to
    /// <see cref="TTSBackends.EchokrauTTS"/>. The field is new — an existing user's saved JSON has no
    /// value for it, so after deserialization it would carry the new default and silently switch a
    /// working AllTalk install to EchokrauTTS on the next start. Runs exactly once (gated on
    /// <see cref="Version"/>): for any config that predates this field, an existing install
    /// (<see cref="FirstTime"/> already completed) is pinned back to AllTalk. Fresh installs
    /// (<see cref="FirstTime"/> still true) keep the EchokrauTTS default. After the version bump the
    /// user is free to switch engines and have it persist.
    /// </summary>
    public void MigrateBackendSelectionForExistingInstalls()
    {
        if (Version >= 1)
            return; // already migrated — respect the user's saved choice.

        if (!FirstTime)
            BackendSelection = TTSBackends.Alltalk;

        Version = 1;
    }

    /// <summary>
    /// One-shot migration of the legacy AllTalk-specific <see cref="AlltalkData.LocalInstallPath"/>
    /// into the engine-agnostic <see cref="TtsInstallRoot"/>. Runs only while <see cref="TtsInstallRoot"/>
    /// is still at its default and the legacy path holds a meaningful custom value. Idempotent — once
    /// a user has a non-default root it is never overwritten, and an empty/blank legacy path is
    /// ignored (the default root stands, which also fixes old configs that saved an empty path).
    /// </summary>
    public void MigrateTtsInstallRoot()
    {
        if (!string.IsNullOrWhiteSpace(TtsInstallRoot) && TtsInstallRoot != DefaultTtsInstallRoot)
            return; // user already on a custom root — leave it.

        var legacy = Alltalk?.LocalInstallPath;
        if (!string.IsNullOrWhiteSpace(legacy) && legacy != DefaultTtsInstallRoot)
            TtsInstallRoot = legacy;
        else if (string.IsNullOrWhiteSpace(TtsInstallRoot))
            TtsInstallRoot = DefaultTtsInstallRoot;
    }

    public void Save()
    {
        PluginInterface?.SavePluginConfig(this);
    }

    /// <summary>Per-source log-filter prefs for <paramref name="source"/>, creating defaults on first use.</summary>
    public LogSourceConfig GetLogSource(TextSource source)
    {
        if (!LogSources.TryGetValue(source, out var cfg))
        {
            cfg = new LogSourceConfig();
            LogSources[source] = cfg;
        }
        return cfg;
    }

    /// <summary>
    /// One-shot migration of the legacy <see cref="logConfig"/> blob into <see cref="LogSources"/>.
    /// Idempotent — a no-op once <see cref="logConfig"/> is null. Persisted on the next Save().
    /// </summary>
    public void MigrateLegacyLogConfig()
    {
        if (logConfig is not { } c) return;

        LogSources[TextSource.None]                      = new LogSourceConfig { ShowDebugLog = c.ShowGeneralDebugLog,              ShowErrorLog = c.ShowGeneralErrorLog,              ShowId0 = c.ShowGeneralId0,              JumpToBottom = c.GeneralJumpToBottom };
        LogSources[TextSource.Chat]                      = new LogSourceConfig { ShowDebugLog = c.ShowChatDebugLog,                 ShowErrorLog = c.ShowChatErrorLog,                 ShowId0 = c.ShowChatId0,                 JumpToBottom = c.ChatJumpToBottom };
        LogSources[TextSource.AddonTalk]                 = new LogSourceConfig { ShowDebugLog = c.ShowTalkDebugLog,                 ShowErrorLog = c.ShowTalkErrorLog,                 ShowId0 = c.ShowTalkId0,                 JumpToBottom = c.TalkJumpToBottom };
        LogSources[TextSource.AddonBattleTalk]           = new LogSourceConfig { ShowDebugLog = c.ShowBattleTalkDebugLog,           ShowErrorLog = c.ShowBattleTalkErrorLog,           ShowId0 = c.ShowBattleTalkId0,           JumpToBottom = c.BattleTalkJumpToBottom };
        LogSources[TextSource.AddonBubble]               = new LogSourceConfig { ShowDebugLog = c.ShowBubbleDebugLog,               ShowErrorLog = c.ShowBubbleErrorLog,               ShowId0 = c.ShowBubbleId0,               JumpToBottom = c.BubbleJumpToBottom };
        LogSources[TextSource.AddonCutsceneSelectString] = new LogSourceConfig { ShowDebugLog = c.ShowCutsceneSelectStringDebugLog, ShowErrorLog = c.ShowCutsceneSelectStringErrorLog, ShowId0 = c.ShowCutsceneSelectStringId0, JumpToBottom = c.CutsceneSelectStringJumpToBottom };
        LogSources[TextSource.AddonSelectString]         = new LogSourceConfig { ShowDebugLog = c.ShowSelectStringDebugLog,         ShowErrorLog = c.ShowSelectStringErrorLog,         ShowId0 = c.ShowSelectStringId0,         JumpToBottom = c.SelectStringJumpToBottom };
        LogSources[TextSource.Backend]                   = new LogSourceConfig { ShowDebugLog = c.ShowBackendDebugLog,              ShowErrorLog = c.ShowBackendErrorLog,              ShowId0 = c.ShowBackendId0,              JumpToBottom = c.BackendJumpToBottom };

        logConfig = null;
    }
}
