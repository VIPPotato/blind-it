using System;

namespace BlindIt
{
    /// <summary>
    /// The decision behind the end-of-round score handoff, with no Unity, no
    /// MelonLoader and no clock of its own, so the desktop harness can prove it.
    ///
    /// WHAT IT DECIDES. The score is spoken joined to the kill screen's menu rather
    /// than on its own (the player's request on 2026-10-01: "I wanted it to read as
    /// soon as the menu with replay appears so it's appended to it"). That means three
    /// rules worth testing, because each of them failing is a defect the player would
    /// hear:
    ///
    ///  1. A menu announcement that arrives in time takes the score and says both.
    ///  2. The score is handed out ONCE. Said twice it would be worse than the
    ///     original defect.
    ///  3. The score is never lost. If no menu claims it, it must still be spoken
    ///     alone, because silence about the number the round was played for is the
    ///     worst outcome of the three.
    ///
    /// Time is passed in rather than read from a clock, so a test can place a claim
    /// before or after the deadline without waiting.
    /// </summary>
    internal sealed class ScoreHandoffState
    {
        private readonly double _graceSeconds;
        private string _pending;
        private double _pendingSince;

        internal ScoreHandoffState(double graceSeconds)
        {
            _graceSeconds = graceSeconds;
        }

        /// <summary>Whether a score is waiting to be spoken.</summary>
        internal bool Waiting => _pending != null;

        /// <summary>
        /// Offers the score. A new score replaces anything still waiting: two rounds
        /// cannot end at once, and the newer number is the right one.
        /// </summary>
        internal void Offer(string spoken, double now)
        {
            if (string.IsNullOrEmpty(spoken))
                return;

            _pending = spoken;
            _pendingSince = now;
        }

        /// <summary>
        /// Takes the waiting score for joining to a menu line, or null when there is
        /// none. Taking it clears it, so it cannot be spoken twice.
        /// </summary>
        internal string Claim()
        {
            string spoken = _pending;
            _pending = null;
            return spoken;
        }

        /// <summary>
        /// The score to speak alone because no menu claimed it in time, or null while
        /// it is still worth waiting. Returning it clears it, like a claim.
        /// </summary>
        internal string Expired(double now)
        {
            if (_pending == null)
                return null;

            if (now - _pendingSince < _graceSeconds)
                return null;

            return Claim();
        }
    }
}
