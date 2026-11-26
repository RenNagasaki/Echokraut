using Echokraut.Enums;

namespace Echokraut.Services
{
    /// <summary>
    /// Keeps the two TTS engines' voice folders in sync when the user switches the active engine,
    /// and orchestrates the switch itself (flush queue → copy voices → flip selection → reconnect).
    /// </summary>
    public interface ITtsVoiceSyncService
    {
        /// <summary>
        /// Copy the <paramref name="from"/> engine's voices into the <paramref name="to"/> engine's
        /// voice folder under the shared install root: overwrite same-named files, keep the target's
        /// extras, never delete. No-op when from == to or the source folder is missing. Returns the
        /// number of files copied.
        /// </summary>
        int CopyVoicesForSwitch(TTSBackends from, TTSBackends to);

        /// <summary>
        /// Switch the active engine to <paramref name="newEngine"/>: if it actually changed, flush
        /// the generation queue, copy the previous engine's voices into the new engine's folder,
        /// <b>stop the previous engine's local process</b>, persist the new selection, and rebuild the
        /// backend. No-op when already on that engine.
        ///
        /// <para>The old engine is stopped because both engines serve on their own port and hold a
        /// model in VRAM: a process left behind keeps that memory and keeps answering health probes,
        /// so the Backend tab shows an engine as running that the plugin no longer talks to.</para>
        /// </summary>
        void SwitchEngine(TTSBackends newEngine);
    }
}
