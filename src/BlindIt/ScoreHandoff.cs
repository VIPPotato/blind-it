using System;
using System.Diagnostics;

namespace BlindIt
{
    /// <summary>
    /// Holds the end-of-round score until the kill screen's menu appears, so the
    /// player hears both as ONE utterance.
    ///
    /// WHY THIS EXISTS. The player asked for this on 2026-10-01, after hearing the
    /// launch-9 build: "It reads right after I lose, but I wanted it to read as soon
    /// as the menu with replay appears so it's appended to it", giving the exact
    /// wording they want as one line:
    ///
    ///     "Final score, 150 points, New best score. REPLAY, 1 of 2."
    ///
    /// WHAT THE GAME DOES. FinalScorePanel.Init carries the true score and runs about
    /// two and a half seconds before the kill screen gives focus to REPLAY (launch 9:
    /// score at 18:29:04.308, focus at 18:29:06.776). Speaking at Init therefore put
    /// the score in its own utterance with a long gap before the menu, which is what
    /// the player is objecting to.
    ///
    /// WHAT THIS DOES INSTEAD. Init hands the sentence here instead of speaking it.
    /// The next menu announcement takes it and says the two together. Nothing is
    /// delayed by a timer: the score is spoken at the moment the menu would have
    /// spoken anyway.
    ///
    /// THE SCORE CAN NEVER BE LOST. If no menu announcement claims it within
    /// <see cref="GraceSeconds"/>, Tick speaks it alone. Silence about the number the
    /// whole round was played for would be a far worse defect than reading it early,
    /// so the fallback is deliberately short.
    ///
    /// The decision itself lives in <see cref="ScoreHandoffState"/>, which has no
    /// Unity in it and is covered by the desktop harness. This class is only the part
    /// that cannot be tested off the game: the clock, the log and the voice.
    ///
    /// All members run on Unity's main thread (a Harmony postfix inside the game's
    /// own Init, MelonLoader's OnUpdate, and the readers it calls), so no locking is
    /// needed here.
    /// </summary>
    internal static class ScoreHandoff
    {
        /// <summary>
        /// How long the score waits for a menu to join it before being spoken alone.
        ///
        /// The real wait is about 2.5 seconds, so this leaves margin without risking
        /// a long silence if a future build never focuses anything.
        /// </summary>
        private const double GraceSeconds = 4.0;

        /// <summary>
        /// How long the combined line is protected from being interrupted. Matches
        /// the hold the score used to take on its own: long enough for the longest
        /// form (two scores, the outcome, a record and a menu item) at a slow reading
        /// rate.
        /// </summary>
        internal const double HoldSeconds = 6.0;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly ScoreHandoffState State = new ScoreHandoffState(GraceSeconds);

        /// <summary>
        /// Offers the score sentence, for the next menu line to carry. A newer score
        /// replaces one still waiting: two rounds cannot end at once.
        /// </summary>
        internal static void Offer(string spoken)
        {
            State.Offer(spoken, Clock.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// Takes the waiting score, or null when there is none. The caller must join
        /// it to the line it is about to speak; once taken it is gone, so it can
        /// never be said twice.
        /// </summary>
        internal static string Claim()
        {
            return State.Claim();
        }

        /// <summary>
        /// Speaks the score alone if no menu announcement claimed it in time. Call
        /// once per frame, before the readers, so a claim that is about to happen
        /// this frame still wins.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                string spoken = State.Expired(Clock.Elapsed.TotalSeconds);
                if (spoken == null)
                    return;

                Log.Line("score", "no menu claimed the score in "
                    + GraceSeconds.ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture)
                    + "s, speaking it alone | text=" + spoken);

                Speech.Speak(spoken, true);
                Speech.Hold(HoldSeconds);
            }
            catch (Exception ex)
            {
                // A failure here must never cost the player the score silently, and
                // must never repeat every frame, so the score is dropped on purpose.
                Log.Line("score", "score flush failed: " + ex.GetType().Name
                    + " | " + ex.Message);
                State.Claim();
            }
        }

        /// <summary>For the F8 dump: whether a score is waiting.</summary>
        internal static string DebugState()
        {
            return State.Waiting ? "a score is waiting for the menu" : "none waiting";
        }
    }
}
