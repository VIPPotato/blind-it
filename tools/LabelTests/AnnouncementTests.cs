using System;

namespace BlindIt.Harness
{
    /// <summary>
    /// Tests for <see cref="Announcement"/>, the part of the mod that composes what
    /// the player hears into ONE utterance.
    ///
    /// Why a test at all: launch 5 proved that a second Speech call cuts the first
    /// one off, so the mod now joins every part of an announcement before speaking.
    /// That joining is pure string logic, which means it can be proved here instead
    /// of costing the player a test launch. The achievement duplicate-name bug
    /// (six of eight achievements said their name twice) is in here as the case it
    /// was before the fix.
    ///
    /// Run:  bash tools/label-tests.sh
    /// </summary>
    internal static class AnnouncementTests
    {
        private static int _passed;
        private static int _failed;

        internal static int Run()
        {
            Console.WriteLine("== announcement tests ==");
            Console.WriteLine();

            Sentences();
            Details();
            Positions();
            Shouting();
            Achievements();
            Scores();
            BookPages();
            SameWords();
            OneUtterance();
            Catalogue();

            Console.WriteLine();
            Console.WriteLine("passed: " + _passed + ", failed: " + _failed);

            if (_failed == 0)
            {
                Console.WriteLine("ALL ANNOUNCEMENT TESTS PASS");
                return 0;
            }

            Console.WriteLine("THERE ARE FAILING ANNOUNCEMENT TESTS");
            return 1;
        }

        private static void Sentences()
        {
            Expect(Announcement.Sentences("Achievements", "8 items"),
                   "Achievements. 8 items.",
                   "two parts become two spoken sentences");

            Expect(Announcement.Sentences("Leaderboard", null, "Empty"),
                   "Leaderboard. Empty.",
                   "a null part is dropped, not spoken as a gap");

            Expect(Announcement.Sentences("Leaderboard", "", "   "),
                   "Leaderboard.",
                   "empty and whitespace parts are dropped");

            // The game's own achievement text ends in an exclamation mark. Adding a
            // full stop after it would read as a stumble.
            Expect(Announcement.Sentences("25 SUCCESSFUL HITS!", "unlocked"),
                   "25 SUCCESSFUL HITS! unlocked.",
                   "an existing exclamation mark is not doubled with a full stop");

            Expect(Announcement.Sentences("Ready?", "Go"),
                   "Ready? Go.",
                   "a question mark also ends a sentence");

            ExpectNull(Announcement.Sentences(), "no parts say nothing");
            ExpectNull(Announcement.Sentences(null, null), "only null parts say nothing");
        }

        private static void Details()
        {
            // The player's choice on 2026-09-30: position at the END of the name.
            Expect(Announcement.Detail("Credits", "4 of 6"),
                   "Credits, 4 of 6.",
                   "a menu item reads as name then position");

            Expect(Announcement.Detail("Music", "8"),
                   "Music, 8.",
                   "a settings row reads as name then value");

            Expect(Announcement.Detail("Music.", "8"),
                   "Music, 8.",
                   "a full stop before a comma is removed, not spoken as a stop");

            Expect(Announcement.Detail("25 SUCCESSFUL HITS!", "2 of 8"),
                   "25 SUCCESSFUL HITS!, 2 of 8.",
                   "an exclamation mark is kept: it is the game's own wording");

            Expect(Announcement.Detail("rank # 1", "elielgamer17", "739 points", "top score"),
                   "rank # 1, elielgamer17, 739 points, top score.",
                   "a leaderboard row reads as one comma-separated clause");

            Expect(Announcement.Detail("Quit", null),
                   "Quit.",
                   "an item with no details is still a full sentence");

            ExpectNull(Announcement.Detail(null), "nothing at all says nothing");
        }

        private static void Positions()
        {
            Expect(Announcement.Position(3, 6), "4 of 6", "index 3 of 6 is the fourth item");
            Expect(Announcement.Position(0, 1), "1 of 1", "a single item still has a position");

            // A wrong position is worse than none: it would send a blind player
            // looking for an item that is not there.
            ExpectNull(Announcement.Position(0, 0), "an empty list has no position");
            ExpectNull(Announcement.Position(-1, 6), "a negative index has no position");
            ExpectNull(Announcement.Position(6, 6), "an index past the end has no position");
        }

        /// <summary>
        /// The game shouts its headings. NVDA can be set to change pitch on capitals
        /// or spell them out, so a group heading is turned into ordinary words before
        /// it is spoken on every move.
        /// </summary>
        private static void Shouting()
        {
            Expect(Announcement.Sentence("CLASSIC"), "Classic",
                   "a shouted group name becomes ordinary words");

            Expect(Announcement.Sentence("2. EXTREME"), "Extreme",
                   "the heading's list number is dropped with the shouting");

            Expect(Announcement.Sentence("25 SUCCESSFUL HITS!"), "25 Successful Hits!",
                   "every word of a shouted caption is un-shouted, punctuation kept");

            // The game's own mixed-case wording is never second-guessed.
            Expect(Announcement.Sentence("Track select"), "Track select",
                   "mixed case is left exactly as the game wrote it");

            Expect(Announcement.Sentence("iPhone MODE"), "iPhone MODE",
                   "a caption with any lower case is not treated as shouting");

            Expect(Announcement.Sentence("PLAYER'S BEST"), "Player's Best",
                   "an apostrophe does not start a new word");

            Expect(Announcement.Sentence("A"), "A",
                   "a single letter is not enough to call it shouting");

            ExpectNull(Announcement.Sentence(null), "nothing stays nothing");
            ExpectNull(Announcement.Sentence("   "), "whitespace says nothing");
        }

        private static void Achievements()
        {
            // THE LAUNCH-5 BUG. The game holds the same name twice in different
            // case; the mod spoke both, so the player heard it twice.
            Expect(Announcement.Achievement("Classic", "25 Successful Hits!", "b. 25 SUCCESSFUL HITS!", "unlocked"),
                   "Classic. 25 Successful Hits! unlocked.",
                   "the same name in two cases is spoken ONCE (launch-5 defect)");

            Expect(Announcement.Achievement(null, "25 Successful Hits!", "25 SUCCESSFUL HITS!", "locked"),
                   "25 Successful Hits! locked.",
                   "the duplicate is dropped even with no group");

            // These two really are different sentences and must both be heard.
            Expect(Announcement.Achievement("Extreme", "You Played Bop It!", "a. YOU PLAYED BOP IT! EXTREME", "unlocked"),
                   "Extreme. You Played Bop It! YOU PLAYED BOP IT! EXTREME. unlocked.",
                   "a genuinely different description is still spoken");

            Expect(Announcement.Achievement("Classic", null, "b. 25 SUCCESSFUL HITS!", "locked"),
                   "Classic. 25 SUCCESSFUL HITS! locked.",
                   "with no title the drawn description is used, without its list letter");

            Expect(Announcement.Achievement("Classic", "25 Successful Hits!", null, null),
                   "Classic. 25 Successful Hits!",
                   "a missing state is not invented");
        }

        private static void SameWords()
        {
            ExpectTrue(Announcement.SameWords("25 Successful Hits!", "25 SUCCESSFUL HITS!"),
                       "case alone does not make two different names");

            ExpectTrue(Announcement.SameWords("25 Successful Hits!", "b. 25 SUCCESSFUL HITS!"),
                       "the book's list marker is not part of the name");

            ExpectTrue(Announcement.SameWords("You Played Bop It!", "YOU PLAYED BOP IT"),
                       "trailing punctuation does not make two different names");

            ExpectTrue(Announcement.SameWords("Bop  It", "BOP IT"),
                       "doubled spaces do not make two different names");

            ExpectFalse(Announcement.SameWords("You Played Bop It!", "YOU PLAYED BOP IT! EXTREME"),
                        "a longer sentence IS a different name");

            ExpectFalse(Announcement.SameWords("100 Successful Hits!", "200 SUCCESSFUL HITS!"),
                        "different numbers are different names");

            // "Extreme." as a whole caption must not be eaten as a list marker.
            ExpectFalse(Announcement.SameWords("Ex. tra", "tra"),
                        "a two-letter marker is only skipped when a name follows");
        }

        /// <summary>
        /// The one-utterance rule itself: whatever the mod composes, it is a single
        /// string with no line break in it, because two lines would be two calls.
        /// </summary>
        private static void OneUtterance()
        {
            string entering = Announcement.Sentences(
                "Achievements",
                "8 items",
                Announcement.Detail(
                    Announcement.Achievement("Classic", "You Played Bop It!", "a. YOU PLAYED BOP IT!", "unlocked"),
                    Announcement.Position(0, 8)));

            Expect(entering,
                   "Achievements. 8 items. Classic. You Played Bop It! unlocked, 1 of 8.",
                   "entering a screen composes name, count and first item as ONE string");

            ExpectFalse(entering.Contains("\n") || entering.Contains("\r"),
                        "a composed announcement contains no line break");
        }

        /// <summary>
        /// The end-of-round announcement, which the player has never heard: launch 5
        /// said nothing at all on the kill screen. Reaching that screen costs a whole
        /// round played, so the wording is proved here instead.
        /// </summary>
        private static void Scores()
        {
            // These cases retain the score, plural and outcome checks. Their old
            // full-stop expectations also pinned the split heard in launch 8.
            Expect(Announcement.FinalScore(260, 0, false),
                "Final score, 260 points.",
                "a solo round says the one score");

            Expect(Announcement.FinalScore(1, 0, false),
                "Final score, 1 point.",
                "a score of one is singular");

            Expect(Announcement.FinalScore(0, 0, false),
                "Final score, 0 points.",
                "a zero score is still spoken, not skipped");

            Expect(Announcement.FinalScore(310, 275, true),
                "Final score, Player 1, 310 points, Player 2, 275 points, Player 1 wins.",
                "one on one names both scores and the winner");

            Expect(Announcement.FinalScore(275, 310, true),
                "Final score, Player 1, 275 points, Player 2, 310 points, Player 2 wins.",
                "the second player can win");

            Expect(Announcement.FinalScore(200, 200, true),
                "Final score, Player 1, 200 points, Player 2, 200 points, A draw.",
                "equal scores are a draw, not a win");

            ExpectFalse(Announcement.FinalScore(310, 275, true).Contains("  "),
                "the result has no double spaces from empty parts");

            Expect(Announcement.FinalScore(260, 0, false, true),
                "Final score, 260 points, New best score.",
                "a personal best is celebrated after the number");

            Expect(Announcement.FinalScore(260, 0, false, false),
                "Final score, 260 points.",
                "an ordinary score says nothing about records");

            Expect(Announcement.FinalScore(260, 0, false, null),
                "Final score, 260 points.",
                "an unknown high-score state claims nothing either way");

            ExpectFalse(Announcement.FinalScore(260, 0, false, true).Contains("\n"),
                "the record clause stays inside the one utterance");

            Expect(Announcement.FinalScore(310, 275, true, true),
                "Final score, Player 1, 310 points, Player 2, 275 points, "
                    + "Player 1 wins, New best score.",
                "a one-on-one best comes after the outcome");

            Expect(Announcement.FinalScore(100, 0, false),
                "Final score, 100 points.",
                "launch-8 result has no sentence break after Final score");
            ExpectFalse(Announcement.FinalScore(310, 275, true, true).TrimEnd('.').Contains("."),
                "even the longest English result has no internal full stop");

            try
            {
                Strings.Load("test", "en", code => code == "test"
                    ? "{\"score.final\":\"{0}, final score\","
                        + "\"score.final.twoPlayers\":\"{2}, second {1}, first {0}\"}"
                    : null);
                Expect(Announcement.FinalScore(100, 0, false),
                    "100 points, final score.",
                    "a translator can move the solo score before the label");
                Expect(Announcement.FinalScore(310, 275, true),
                    "Player 1 wins, second 275 points, first 310 points.",
                    "a translator can reorder the complete two-player result");
            }
            finally
            {
                Strings.Load(null, "en", null);
            }
        }

        /// <summary>
        /// The achievements book's page count. Launch 5 flipped a page in silence,
        /// and the numbers come from a book that reports zeroes while it is turning.
        /// </summary>
        private static void BookPages()
        {
            Expect(Announcement.BookPages(2, 3, 8),
                "Pages 2 and 3 of 8",
                "an open book names both pages");

            Expect(Announcement.BookPages(1, 0, 8),
                "Page 1 of 8",
                "a single open page is not plural");

            Expect(Announcement.BookPages(0, 8, 8),
                "Page 8 of 8",
                "the last page reads on its own");

            Expect(Announcement.BookPages(4, 4, 8),
                "Page 4 of 8",
                "the same page twice is one page, not two");

            ExpectNull(Announcement.BookPages(0, 0, 8),
                "a closed book says nothing");

            ExpectNull(Announcement.BookPages(2, 3, 0),
                "a book with no page count says nothing");

            ExpectNull(Announcement.BookPages(9, 10, 8),
                "pages beyond the count are a mid-turn read, so nothing is said");
        }

        // ---- plumbing ---------------------------------------------------------
        private static void Expect(string actual, string expected, string why)
        {
            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                Pass(why);
                return;
            }

            Fail(why);
            Console.WriteLine("          expected : " + Show(expected));
            Console.WriteLine("          actual   : " + Show(actual));
        }

        private static void ExpectNull(string actual, string why)
        {
            if (actual == null)
            {
                Pass(why);
                return;
            }

            Fail(why);
            Console.WriteLine("          expected : null");
            Console.WriteLine("          actual   : " + Show(actual));
        }

        private static void ExpectTrue(bool actual, string why)
        {
            if (actual)
            {
                Pass(why);
                return;
            }

            Fail(why);
            Console.WriteLine("          expected true, got false");
        }

        private static void ExpectFalse(bool actual, string why)
        {
            if (!actual)
            {
                Pass(why);
                return;
            }

            Fail(why);
            Console.WriteLine("          expected false, got true");
        }

        private static void Pass(string why)
        {
            _passed++;
            Console.WriteLine("  PASS  " + why);
        }

        private static void Fail(string why)
        {
            _failed++;
            Console.WriteLine("  FAIL  " + why);
        }

        private static string Show(string s)
        {
            return s == null ? "null" : "\"" + s + "\"";
        }

        /// <summary>
        /// Checks the word catalogue itself, the file every spoken line now comes
        /// from.
        ///
        /// WHY THIS EXISTS. While routing the last hardcoded English through the
        /// catalogue, two keys (screen.trackSelect and screen.calibrate) were added a
        /// second time. A C# collection initializer with a duplicate key throws at
        /// class-load time, which in the game means the mod dies on startup and the
        /// player hears nothing at all, with no hint why. The compiler does not catch
        /// it. This test does, on the desktop, in a second.
        ///
        /// It also proves a placeholder is filled rather than read aloud: a key whose
        /// English contains {0} must not reach the player with the braces still in it.
        /// </summary>
        private static void Catalogue()
        {
            // Touching any key forces the static table to build. A duplicate key
            // throws here, and an unhandled throw fails the run loudly.
            string name = Strings.Get("mod.name");
            ExpectTrue(!string.IsNullOrEmpty(name),
                "the catalogue loads without a duplicate key");

            Expect(Strings.Get("mod.name"), "Blind it!",
                "the mod's own name comes from the catalogue");

            // An argument replaces the placeholder.
            Expect(Strings.Get("list.items", "6"), "6 items",
                "a placeholder is filled with the argument");

            Expect(Strings.Get("list.position", "4", "6"), "4 of 6",
                "two placeholders are filled in order");

            // An unknown key must not crash and must not speak the key itself: a
            // player hearing "leaderboard.rank" learns nothing. The key is returned
            // so the gap is visible in the log, which is the documented behaviour;
            // this test fixes it so a future change is a deliberate one.
            ExpectTrue(Strings.Get("no.such.key.exists") != null,
                "an unknown key returns something rather than null");

            // Every phrase the final score is built from must be present, because a
            // missing one would leave the player's result half-spoken. The composed
            // sentence is the real check: it passes only if all four keys resolved.
            ExpectFalse(Announcement.FinalScore(260, 0, false, true).Contains("{"),
                "no unfilled placeholder survives into the final score");

            ExpectFalse(Announcement.FinalScore(310, 275, true, true).Contains("{"),
                "no unfilled placeholder survives into a one-on-one result");
        }
    }
}
