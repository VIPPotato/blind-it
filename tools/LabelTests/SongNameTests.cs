using System;

namespace BlindIt.Harness
{
    /// <summary>
    /// Tests for <see cref="LabelText.SongWords"/>, the mapping from the game's own
    /// song identity to the words the player hears.
    ///
    /// Why this is tested without the game: the song name was the one value the mod
    /// could not read, and the fix is a deliberately narrow mapping of four verified
    /// identities. The risk is not that "SHAPES" fails; it is that the mapping gets
    /// loosened later into a general asset-name cleanup and starts speaking internal
    /// names like "Asset 17" at the player. These tests pin the narrowness: anything
    /// that is not one of the four known identities must come back unknown.
    ///
    /// The same mapping supplies the achievement book's four page headings, so a
    /// regression here would be heard on two screens.
    ///
    /// Run:  bash tools/label-tests.sh
    /// </summary>
    internal static class SongNameTests
    {
        private static int _passed;
        private static int _failed;

        internal static int Run()
        {
            Console.WriteLine("== song name tests ==");

            // --- The four songs this build ships ------------------------------------
            Expect("SHAPES", "Shapes", "the SHAPES track is named");
            Expect("SPACE", "Space", "the SPACE track is named");
            Expect("CITY", "City", "the CITY track is named");
            Expect("OFFICE", "Office", "the OFFICE track is named");

            // --- Case and space, the two harmless differences -----------------------
            // The identity is read from scene and asset names, which are not
            // guaranteed to keep their capitalisation across a game update.
            Expect("Shapes", "Shapes", "a differently cased identity still matches");
            Expect("office", "Office", "a lower-case identity still matches");
            Expect("  CITY  ", "City", "surrounding space does not prevent a match");

            // --- Everything else must be unknown, not guessed -----------------------
            // This is the real point of the suite. Each of these is a shape a future
            // build or a mistaken edit could produce, and each must stay silent
            // rather than reach the player as a spoken internal name.
            ExpectNull(null, "a missing identity is unknown");
            ExpectNull("", "an empty identity is unknown");
            ExpectNull("   ", "a whitespace-only identity is unknown");
            ExpectNull("Asset 17", "an internal asset name is never spoken");
            ExpectNull("BEACH", "a song added by a future update is unknown, not invented");
            ExpectNull("SHAPES_2", "a decorated identity is not accepted as SHAPES");
            ExpectNull("SHAPES BONUS", "an identity with an extra word is not accepted");
            ExpectNull("SHAPE", "a near miss is not accepted");
            ExpectNull("SHA", "a prefix of a known identity is not accepted");
            ExpectNull("Track_SHAPES", "a prefixed identity is not accepted by substring");
            ExpectNull("MainMenu", "an unrelated scene name is unknown");

            Console.WriteLine("song names: passed " + _passed + ", failed " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Expect(string key, string expected, string why)
        {
            Check(LabelText.SongWords(key), expected, key, why);
        }

        private static void ExpectNull(string key, string why)
        {
            Check(LabelText.SongWords(key), null, key, why);
        }

        private static void Check(string actual, string expected, string key, string why)
        {
            if (actual == expected)
            {
                _passed++;
                Console.WriteLine("  PASS  " + why);
                return;
            }

            _failed++;
            Console.WriteLine("  FAIL  " + why);
            Console.WriteLine("          identity : " + (key == null ? "null" : "\"" + key + "\""));
            Console.WriteLine("          expected : " + (expected ?? "null (unknown)"));
            Console.WriteLine("          actual   : " + (actual ?? "null (unknown)"));
        }
    }
}
