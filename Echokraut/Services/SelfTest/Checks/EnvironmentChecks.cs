using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Echokraut.Localization;

namespace Echokraut.Services.SelfTest.Checks;

/// <summary>Logged in, and the cached player identity (name, content id) is filled.</summary>
public sealed class PlayerCheck : SelfTestCheck
{
    private readonly IClientState _clientState;
    private readonly IFramework _framework;
    private readonly IGameObjectService _gameObjects;

    public PlayerCheck(IClientState clientState, IFramework framework, IGameObjectService gameObjects)
    {
        _clientState = clientState;
        _framework = framework;
        _gameObjects = gameObjects;
    }

    public override string Id => "player";
    public override string Name => Loc.S("Logged-in character");

    public override async Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        // The object table may only be read on the framework thread.
        var (loggedIn, name, contentId) = await _framework.RunOnFrameworkThread(() =>
        {
            _gameObjects.RefreshLocalPlayer();
            return (_clientState.IsLoggedIn, _gameObjects.LocalPlayerName ?? "", _gameObjects.LocalPlayerContentId);
        }).ConfigureAwait(false);

        if (!loggedIn) return SelfTestResult.Fail("Not logged in.");
        if (string.IsNullOrEmpty(name)) return SelfTestResult.Fail("Logged in, but the player name is unknown (player placeholders would not resolve).");
        if (contentId == 0) return SelfTestResult.Fail("Logged in, but the content id is 0.");
        return SelfTestResult.Pass($"Client language {_clientState.ClientLanguage}, player name known.");
    }
}

/// <summary>The game sheets the plugin reads (dialogue sheets, NPC names) load in the client language.</summary>
public sealed class GameDataCheck : SelfTestCheck
{
    private readonly IClientState _clientState;
    private readonly IDialogSheetSource _sheets;
    private readonly IGameVoiceDataSource _gameData;

    public GameDataCheck(IClientState clientState, IDialogSheetSource sheets, IGameVoiceDataSource gameData)
    {
        _clientState = clientState;
        _sheets = sheets;
        _gameData = gameData;
    }

    public override string Id => "game-data";
    public override string Name => Loc.S("Game data");

    public override Task<SelfTestResult> RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        var language = _clientState.ClientLanguage;
        // A handful of sheets is enough to prove Lumina reads them; all of them take seconds.
        var sheets = _sheets.ReadDialogueSheets(language).Take(20).ToList();
        var lines = sheets.Sum(s => s.Count);
        ct.ThrowIfCancellationRequested();
        var names = _gameData.GetNpcNames(language).Count;

        if (sheets.Count == 0 || lines == 0) return SelfTestResult.Fail("No dialogue sheet could be read.");
        if (names == 0) return SelfTestResult.Fail("ENpcResident has no names in this language.");
        return SelfTestResult.Pass($"{sheets.Count} dialogue sheets ({lines} lines) read, {names} NPC names ({language}).");
    }, ct);
}

/// <summary>The SQLite database answers queries.</summary>
public sealed class DatabaseCheck : SelfTestCheck
{
    private readonly IDatabaseService _db;

    public DatabaseCheck(IDatabaseService db) => _db = db;

    public override string Id => "database";
    public override string Name => Loc.S("Database access");

    public override Task<SelfTestResult> RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        var clips = _db.GetVoiceClipCount();
        var muted = _db.GetMutedBaseIds().Count;
        return SelfTestResult.Pass($"Readable: {clips} voice clip(s), {muted} muted dialogue(s).");
    }, ct);
}

/// <summary>The story patch of the character is known (drives the voice-actor epoch of game voices).</summary>
public sealed class StoryProgressCheck : SelfTestCheck
{
    private readonly IStoryProgressService _progress;

    public StoryProgressCheck(IStoryProgressService progress) => _progress = progress;

    public override string Id => "story-progress";
    public override string Name => Loc.S("Story progress");
    public override TimeSpan Timeout => TimeSpan.FromSeconds(15);

    public override async Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        // The snapshot is taken on the framework thread a moment after login.
        var deadline = Environment.TickCount64 + 10_000;
        while (_progress.ReachedPatchKey == null && Environment.TickCount64 < deadline)
            await Task.Delay(250, ct).ConfigureAwait(false);

        return _progress.ReachedPatchKey switch
        {
            null => SelfTestResult.Fail("Story progress unknown after 10 s (sheet missing or not logged in)."),
            "" => SelfTestResult.Pass("Before the first voiced story patch."),
            var key => SelfTestResult.Pass($"Reached story patch {key}."),
        };
    }
}
