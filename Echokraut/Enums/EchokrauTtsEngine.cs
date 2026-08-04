namespace Echokraut.Enums
{
    /// <summary>
    /// Sub-engine the local EchokrauTTS wrapper loads at startup (passed through to the wrapper as
    /// <c>--tts-backend</c>). The bootstrap installs BOTH engines and all their models, so switching
    /// is a restart of the local instance — never a reinstall.
    /// <list type="bullet">
    ///   <item><see cref="XTTS"/> — Coqui XTTS-v2: clones from audio only (no ref-text), multilingual.
    ///   Default: better quality.</item>
    ///   <item><see cref="F5"/> — F5-TTS: per-language finetunes, needs a ref-text per sample.</item>
    /// </list>
    /// The enum name lower-cased is the exact wrapper arg value (<c>xtts</c> / <c>f5</c>).
    /// </summary>
    public enum EchokrauTtsEngine
    {
        XTTS,
        F5
    }
}
