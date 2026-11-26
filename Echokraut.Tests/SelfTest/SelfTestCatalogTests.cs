using System.Linq;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Xunit;

namespace Echokraut.Tests.SelfTest;

/// <summary>SelfTestCatalog + its InteractiveSkip rules, and the /ektest argument.</summary>
public class SelfTestCatalogTests
{
    [Fact]
    public void Talk_NeedsPluginAndDialogueVoicing()
    {
        Assert.Null(InteractiveSkip.Talk(new Configuration()));
        Assert.NotNull(InteractiveSkip.Talk(new Configuration { Enabled = false }));
        Assert.NotNull(InteractiveSkip.Talk(new Configuration { VoiceDialogue = false }));
    }

    [Fact]
    public void Bubble_NeedsBubbleVoicing()
    {
        Assert.NotNull(InteractiveSkip.Bubble(new Configuration()));
        Assert.Null(InteractiveSkip.Bubble(new Configuration { VoiceBubble = true }));
    }

    [Fact]
    public void Chat_NeedsChatOwnMessagesAndSay()
    {
        Assert.NotNull(InteractiveSkip.Chat(new Configuration { VoiceChat = true, VoiceChatPlayer = true }));
        Assert.Null(InteractiveSkip.Chat(new Configuration { VoiceChat = true, VoiceChatPlayer = true, VoiceChatSay = true }));
    }

    [Theory]
    [InlineData("all", true)]
    [InlineData(" ALL ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("quick", false)]
    public void SelfTestCommand_AllArgument(string? args, bool expected)
    {
        Assert.Equal(expected, CommandService.IsAllArgument(args));
    }
}
