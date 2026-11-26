namespace Echokraut.Enums;

/// <summary>
/// What a launch of the EchokrauTTS wrapper is supposed to do. Replaces the former
/// <c>(bool download, bool update)</c> pair in <c>EchokrauTtsInstanceService.Launch</c>: the pair
/// could express four combinations but only three were meaningful, and the test install needs a
/// fourth behaviour ("no download, but still run the install bookkeeping") that the two flags
/// cannot say at all.
/// </summary>
public enum WrapperLaunchMode
{
    /// <summary>Bootstrap-if-needed and serve whatever already lies in <c>echokrautts/</c>.</summary>
    Start,

    /// <summary>Download the release zip, unpack it wholesale, seed the voice pack, mark installed.</summary>
    Install,

    /// <summary>
    /// Download the release zip but unpack it keeping <c>samples/</c> and <c>models/</c>, and record
    /// the new tag. Never seeds the voice pack — that would delete the voices the update preserved.
    /// </summary>
    Update,

    /// <summary>
    /// Developer-only: unpack a locally built wrapper zip (see <c>TestWrapperInstall</c>) instead of
    /// downloading a release, then behave exactly like <see cref="Install"/>. Lets an untagged
    /// wrapper build be tested through the real install path, which is otherwise reachable only by
    /// publishing a GitHub release — <c>/releases/latest</c> ignores drafts and pre-releases.
    /// </summary>
    TestInstall,
}
