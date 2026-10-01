using System;

namespace BlindIt.Harness
{
    /// <summary>
    /// Tests for <see cref="ScoreHandoffState"/>, the rule that decides when the
    /// end-of-round score is spoken.
    ///
    /// Why this is tested without the game: the player asked for the score to arrive
    /// joined to the kill screen's menu as one utterance, which means the score is no
    /// longer spoken where it is produced. That buys three ways to fail, and each one
    /// would be heard:
    ///
    ///  - said twice, which is worse than the original complaint;
    ///  - lost entirely, the worst outcome, because the score is the point of the
    ///    round;
    ///  - held so long the player hears a silence before the menu.
    ///
    /// Time is injected, so the deadline is tested exactly rather than by sleeping.
    ///
    /// Run:  bash tools/label-tests.sh
    /// </summary>
    internal static class ScoreHandoffTests
    {
        private const double Grace = 4.0;

        private static int _passed;
        private static int _failed;

        internal static int Run()
        {
            Console.WriteLine("== score handoff tests ==");

            NothingWaitingByDefault();
            MenuTakesTheScore();
            ScoreIsHandedOutOnlyOnce();
            MenuInTimeStopsTheFallback();
            UnclaimedScoreIsSpokenAlone();
            NotSpokenAloneTooEarly();
            SpokenAloneExactlyAtTheDeadline();
            ExpiredScoreIsNotRepeated();
            NewerScoreReplacesAnOlderOne();
            EmptyOfferIsIgnored();
            ClaimAfterExpiryFindsNothing();

            Console.WriteLine("score handoff: passed " + _passed + ", failed " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        // A fresh state must not invent a score to speak.
        private static void NothingWaitingByDefault()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);

            Check(state.Waiting, false, "nothing is waiting before a round ends");
            Check(state.Claim(), null, "a menu announcement finds no score to carry");
            Check(state.Expired(1000.0), null, "no score is invented after a long wait");
        }

        // The whole point: the menu picks the score up and says both together.
        private static void MenuTakesTheScore()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 150 points, New best score.", 10.0);

            Check(state.Waiting, true, "the score waits for the menu");
            Check(state.Claim(), "Final score, 150 points, New best score.",
                "the menu announcement carries the score");
        }

        // Said twice would be a worse defect than the one being fixed.
        private static void ScoreIsHandedOutOnlyOnce()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 150 points.", 10.0);

            state.Claim();

            Check(state.Claim(), null, "a second menu item does not repeat the score");
            Check(state.Waiting, false, "nothing is left waiting after a claim");
        }

        // Once the menu has it, the lone-speech fallback must not fire as well.
        private static void MenuInTimeStopsTheFallback()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 150 points.", 10.0);
            state.Claim();

            Check(state.Expired(10.0 + Grace + 5.0), null,
                "the score is not spoken again after the menu took it");
        }

        // The score must never be lost, even if no menu ever appears.
        private static void UnclaimedScoreIsSpokenAlone()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 150 points.", 10.0);

            Check(state.Expired(10.0 + Grace + 0.1), "Final score, 150 points.",
                "an unclaimed score is still spoken, alone");
        }

        // Giving up early would reintroduce the gap the player complained about.
        private static void NotSpokenAloneTooEarly()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 150 points.", 10.0);

            Check(state.Expired(10.0), null, "not spoken alone on the same frame");
            Check(state.Expired(12.5), null,
                "not spoken alone while the real menu is still coming (2.5s)");
            Check(state.Expired(10.0 + Grace - 0.01), null,
                "not spoken alone a hair before the deadline");
            Check(state.Waiting, true, "it is still waiting for the menu");
        }

        private static void SpokenAloneExactlyAtTheDeadline()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 7 points.", 0.0);

            Check(state.Expired(Grace), "Final score, 7 points.",
                "spoken alone once the deadline is reached");
        }

        // Tick runs every frame, so a repeat here would stutter the score.
        private static void ExpiredScoreIsNotRepeated()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 150 points.", 10.0);
            state.Expired(20.0);

            Check(state.Expired(20.1), null, "the next frame does not say it again");
            Check(state.Expired(60.0), null, "nor does any later frame");
            Check(state.Waiting, false, "nothing is left waiting");
        }

        private static void NewerScoreReplacesAnOlderOne()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 10 points.", 10.0);
            state.Offer("Final score, 260 points.", 11.0);

            Check(state.Claim(), "Final score, 260 points.",
                "the newer score is the one spoken");
        }

        private static void EmptyOfferIsIgnored()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer(null, 10.0);
            state.Offer("", 10.0);

            Check(state.Waiting, false, "an empty score is not held");
            Check(state.Expired(100.0), null, "an empty score is never spoken");
        }

        // A claim arriving on the same frame the fallback fired must find nothing,
        // or the player hears the score twice in a row.
        private static void ClaimAfterExpiryFindsNothing()
        {
            ScoreHandoffState state = new ScoreHandoffState(Grace);
            state.Offer("Final score, 150 points.", 10.0);
            state.Expired(20.0);

            Check(state.Claim(), null,
                "a menu arriving right after the fallback does not repeat it");
        }

        private static void Check(string actual, string expected, string why)
        {
            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                Pass(why);
                return;
            }

            Fail(why, expected ?? "null", actual ?? "null");
        }

        private static void Check(bool actual, bool expected, string why)
        {
            if (actual == expected)
            {
                Pass(why);
                return;
            }

            Fail(why, expected.ToString(), actual.ToString());
        }

        private static void Pass(string why)
        {
            _passed++;
            Console.WriteLine("  PASS  " + why);
        }

        private static void Fail(string why, string expected, string actual)
        {
            _failed++;
            Console.WriteLine("  FAIL  " + why);
            Console.WriteLine("          expected : " + expected);
            Console.WriteLine("          actual   : " + actual);
        }
    }
}
