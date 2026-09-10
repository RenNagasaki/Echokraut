using Echokraut.DataClasses;
using Echotools.Logging.Enums;
using Xunit;

namespace Echokraut.Tests;

public class LogConfigMigrationTests
{
    [Fact]
    public void MigrateLegacyLogConfig_CopiesPerSourceValues_AndClearsLegacyBlob()
    {
        var cfg = new Configuration
        {
            logConfig = new LogConfig
            {
                ShowBackendDebugLog = false,
                ShowChatErrorLog = false,
                ShowGeneralId0 = false,
                TalkJumpToBottom = false,
            }
        };

        cfg.MigrateLegacyLogConfig();

        Assert.Null(cfg.logConfig); // legacy blob dropped from future saves

        // Customized values carried over to the right TextSource.
        Assert.False(cfg.GetLogSource(TextSource.Backend).ShowDebugLog);
        Assert.False(cfg.GetLogSource(TextSource.Chat).ShowErrorLog);
        Assert.False(cfg.GetLogSource(TextSource.None).ShowId0);
        Assert.False(cfg.GetLogSource(TextSource.AddonTalk).JumpToBottom);

        // Untouched legacy fields (default true) preserved as true.
        Assert.True(cfg.GetLogSource(TextSource.Backend).ShowErrorLog);
        Assert.True(cfg.GetLogSource(TextSource.AddonBubble).ShowDebugLog);
    }

    [Fact]
    public void MigrateLegacyLogConfig_IsIdempotent_DoesNotClobberAfterMigration()
    {
        var cfg = new Configuration { logConfig = null };
        cfg.GetLogSource(TextSource.Backend).ShowDebugLog = false; // a pref set post-migration

        cfg.MigrateLegacyLogConfig(); // no legacy blob → must be a no-op

        Assert.False(cfg.GetLogSource(TextSource.Backend).ShowDebugLog);
    }

    [Fact]
    public void GetLogSource_CreatesDefaultTrue_OnFirstUse()
    {
        var cfg = new Configuration { logConfig = null };

        var src = cfg.GetLogSource(TextSource.Chat);

        Assert.True(src.ShowDebugLog);
        Assert.True(src.ShowErrorLog);
        Assert.True(src.ShowId0);
        Assert.True(src.JumpToBottom);
    }
}
