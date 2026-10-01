using System;
using System.Globalization;
using HarmonyLib;
using Il2Cpp;

namespace BlindIt
{
    /// <summary>
    /// Speaks the score at the end of a round.
    ///
    /// WHY A HOOK AND NOT A POLLED READ. The final score is the one number the
    /// whole game is played for, and on launch 5 it was never spoken: the kill
    /// screen has no selectable widget, so MenuReader sees nothing, and the number
    /// on screen is animated from zero upward by a tween (SoloKillScreenPanel owns
    /// an animateNumberTweenSettings and a finalScoreText). A reader that scraped
    /// that text would catch it mid-count and announce a score the player never
    /// got, for example "47" on the way to 260.
    ///
    /// FinalScorePanel.Init(PlayerStats, GameMode) receives the true, final numbers
    /// before any animation starts, and it is public, so it can be hooked directly.
    /// That makes the announcement both exact and early.
    ///
    /// WHY POSTFIX. A prefix runs before the game has set the panel up; if this
    /// code threw there it could stop the kill screen from appearing at all. A
    /// postfix cannot: the game's own work is already done by then, and the hook
    /// swallows its own exceptions besides.
    ///
    /// PlayerStats is a struct with Score and Player2Score, and GameMode is Solo,
    /// Party, PassIt or OneOnOne (both proven in the dump). Two-player modes carry
    /// a real second score, so those are announced as both scores plus who won;
    /// Solo and Party carry one.
    ///
    /// THE HIGH SCORE comes from the kill screen, not from this struct: PlayerStats
    /// holds only the two score fields (verified in the proxy metadata on
    /// 2026-10-01), while SoloKillScreenPanel carries an isHighScore the game set
    /// itself. FinalScorePanel owns that panel through its soloKillScreenPanel
    /// property, so the verdict is reachable from here without guessing at what a
    /// personal best means.
    /// </summary>
    [HarmonyPatch(typeof(FinalScorePanel), nameof(FinalScorePanel.Init))]
    internal static class ScorePatch
    {
        private static void Postfix(FinalScorePanel __instance,
            PlayerStats playerStats, GameMode gameMode)
        {
            try
            {
                int score = playerStats.Score;
                int other = playerStats.Player2Score;

                // OneOnOne is the only mode with a real second score: Solo has one
                // player, and Party and PassIt pass one device around, so their
                // Player2Score is not a second player's result.
                bool twoPlayers = gameMode == GameMode.OneOnOne;

                bool? record = HighScore(__instance);

                string spoken = Announcement.FinalScore(score, other, twoPlayers, record);

                // NO DUPLICATE GUARD HERE, DELIBERATELY. The doubled score of launch
                // 6 was this postfix being REGISTERED twice (the mod called PatchAll
                // over an assembly MelonLoader had already patched), and that was
                // fixed at the cause in Probe.cs. A dedupe here would hide a
                // regression of that bug instead of reporting it, and one "score |
                // final" line per round is the proof the fix still holds.
                Log.Line("score", "final | mode=" + gameMode.ToString()
                    + " | score=" + score.ToString(CultureInfo.InvariantCulture)
                    + " | player2=" + other.ToString(CultureInfo.InvariantCulture)
                    + " | highScore=" + (record.HasValue
                        ? (record.Value ? "yes" : "no")
                        : "unknown")
                    + " | spoke=" + spoken);

                // HANDED OVER, NOT SPOKEN. The player asked on 2026-10-01 for the
                // score to arrive together with the kill screen's menu as one
                // utterance: "I wanted it to read as soon as the menu with replay
                // appears so it's appended to it." Init runs about two and a half
                // seconds before REPLAY takes focus, so speaking here produced the
                // score, a long gap, then the menu.
                //
                // The next menu announcement joins the two. ScoreHandoff speaks the
                // score alone if no menu claims it, so the number can never be lost.
                // The hold also moves there, onto the combined line, because holding
                // here would protect a sentence that has not been spoken yet.
                ScoreHandoff.Offer(spoken);
            }
            catch (Exception ex)
            {
                // Never let this reach the game: it runs inside the game's own call
                // to Init, and a throw here would break the kill screen.
                Log.Line("score", "final score hook failed: " + ex.GetType().Name
                    + " | " + ex.Message);
            }
        }

        /// <summary>
        /// Whether the game considers this a new personal best, or null when it did
        /// not say.
        ///
        /// Read from the game's own isHighScore rather than compared against a saved
        /// value by the mod: the game owns that decision, and claiming a record the
        /// player did not set would be worse than saying nothing.
        ///
        /// Only the solo screen is asked. WithFriendsKillScreenPanel has no
        /// isHighScore at all (verified in the proxy metadata on 2026-10-01), which
        /// makes sense: a shared-device round has no single player to hold a record.
        /// </summary>
        private static bool? HighScore(FinalScorePanel panel)
        {
            try
            {
                if (panel == null)
                    return null;

                SoloKillScreenPanel solo = panel.soloKillScreenPanel;
                if (solo == null)
                    return null;

                // Only trust the flag when this is the screen actually showing: the
                // other mode's panel keeps whatever value it had last time.
                if (!Ui.Alive(solo) || !Ui.Alive(solo.gameObject)
                    || !solo.gameObject.activeInHierarchy)
                    return null;

                return solo.isHighScore;
            }
            catch (Exception ex)
            {
                Log.Line("score", "high score read failed: " + ex.GetType().Name);
                return null;
            }
        }
    }
}
