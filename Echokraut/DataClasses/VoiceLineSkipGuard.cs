using System;

namespace Echokraut.DataClasses
{
    /// <summary>
    /// Tracks the "the next dialog line is already voiced by the game, so skip TTS" signal that the
    /// Talk / BattleTalk / Bubble addon helpers each used to hand-roll with a <c>bool</c> +
    /// <c>DateTime</c> pair. <see cref="Notify"/> arms it; <see cref="ConsumeIsVoice"/> reads-and-clears
    /// it, treating a notification older than the given freshness window as stale. The clock is
    /// injectable for tests; production uses <see cref="DateTime.Now"/>.
    /// </summary>
    public sealed class VoiceLineSkipGuard
    {
        private readonly Func<DateTime> _now;
        private bool _pending;
        private DateTime _stamp;

        public VoiceLineSkipGuard(Func<DateTime>? now = null)
        {
            _now = now ?? (() => DateTime.Now);
            _stamp = _now();
        }

        public void Notify()
        {
            _pending = true;
            _stamp = _now();
        }

        /// <summary>
        /// Reads and clears the pending flag. Returns true only when <see cref="Notify"/> fired within
        /// the last <paramref name="freshnessMs"/> milliseconds; older (stale) notifications return false.
        /// </summary>
        public bool ConsumeIsVoice(int freshnessMs)
        {
            var pending = _pending;
            _pending = false;
            return pending && _now() <= _stamp.AddMilliseconds(freshnessMs);
        }
    }
}
