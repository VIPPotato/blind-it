using System;

namespace BlindIt.Harness
{
    internal static class TrackChoiceStateTests
    {
        private static int _passed;
        private static int _failed;

        internal static int Run()
        {
            Console.WriteLine("== track choice state tests ==");
            var state = new TrackChoiceState();
            Expect(state.Observe(10, 1, "Shapes", 0, "Classic"),
                "Bop it type, Classic, song, Shapes.", "entry combines device and song");
            Expect(state.Observe(10, 1, "Shapes", 0, "Classic"), null,
                "unchanged choices stay silent");
            Expect(state.Observe(10, 2, "Space", 0, "Classic"), "Song, Space.",
                "a song change speaks only the song");
            Expect(state.Observe(10, 2, "Space", 1, "Extreme"), "Bop it type, Extreme.",
                "a device change speaks only the type");
            Expect(state.Observe(10, 3, "City", 0, "Classic"),
                "Bop it type, Classic, song, City.", "simultaneous changes form one announcement");
            Expect(state.Observe(10, 4, "City", 0, "Classic"), "Song, City.",
                "different song identities with the same title still announce");
            Expect(state.Repeat(), "Bop it type, Classic, song, City.",
                "manual repeat includes both choices");
            Expect(state.Repeat(), "Bop it type, Classic, song, City.",
                "identical manual repeats are not suppressed");
            state.Reset();
            Expect(state.Repeat(), null, "leaving discards the previous screen's repeat text");
            Expect(state.Observe(10, 4, "City", 0, "Classic"),
                "Bop it type, Classic, song, City.", "reentry on the same choices announces again");
            Expect(state.Observe(20, 4, "City", 0, "Classic"),
                "Bop it type, Classic, song, City.", "a new scene instance is an entry even with identical choices");
            state.Reset();
            Expect(state.Observe(20, 4, null, 0, "Classic"), null,
                "entry waits for both readable values");
            Expect(state.Observe(20, 4, "City", null, null), null,
                "an unfinished device swap is not announced as a known choice");
            Expect(state.Observe(20, 4, "City", 0, "Classic"),
                "Bop it type, Classic, song, City.", "late values still produce the full entry");
            Expect(state.Observe(20, 4, null, 0, "Classic"), null,
                "temporary unreadability does not invent a change");
            Expect(state.Observe(20, 4, "City", 0, "Classic"), null,
                "recovered identical values do not repeat the entry");
            Console.WriteLine("track choice state: passed " + _passed + ", failed " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Expect(string actual, string expected, string why)
        {
            if (actual == expected)
            {
                _passed++;
                Console.WriteLine("  PASS  " + why);
                return;
            }
            _failed++;
            Console.WriteLine("  FAIL  " + why + " | expected=" + (expected ?? "<null>")
                + " | actual=" + (actual ?? "<null>"));
        }
    }
}
