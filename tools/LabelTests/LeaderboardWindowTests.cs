using System;

namespace BlindIt.Harness
{
    internal static class LeaderboardWindowTests
    {
        private static int _passed;
        private static int _failed;

        internal static int Run()
        {
            Console.WriteLine("== leaderboard window tests ==");
            Expect(LeaderboardWindow.RankWords("# 1", 0), "rank 1",
                "launch-8 drawn rank has no doubled hash");
            Expect(LeaderboardWindow.RankWords("1", 0), "rank 1",
                "a plain drawn rank has the same wording");
            Expect(LeaderboardWindow.RankWords(null, 84), "rank 84",
                "missing rank text uses the game's positive rank");
            Expect(LeaderboardWindow.RankWords("# 1,001", 0), "rank 1,001",
                "rank wording preserves displayed number formatting");
            Expect(LeaderboardWindow.RankWords(null, 0), null,
                "a missing rank is not invented");

            // Actual ranks and own-row position from logs/launch8-BlindIt.log.
            var logged = new LeaderboardWindow();
            logged.Add("# 1", 1, false);
            logged.Add("# 81", 81, false);
            logged.Add("# 82", 82, false);
            logged.Add("# 83", 83, false);
            logged.Add("# 84", 84, true);
            logged.Add("# 85", 85, false);
            Expect(logged.Description, "Showing the top score and scores around your position",
                "the entry explanation describes the observed board window");
            Expect(logged.Gap(0, 1), "Ranks 2 through 80 are not shown",
                "moving from rank 1 to rank 81 explains the missing ranks");
            Expect(logged.Gap(1, 0), "Ranks 2 through 80 are not shown",
                "the same boundary is explained when moving backwards");
            Expect(logged.Gap(1, 2), null, "adjacent ranks have no gap announcement");
            Expect(logged.Gap(1, 1), null, "repeating a row does not repeat a gap");
            Expect(logged.Gap(-1, 1), null, "an unknown previous row has no invented gap");
            Expect(logged.Gap(5, 6), null, "the bottom edge has no invented gap");

            var other = new LeaderboardWindow();
            other.Add("# 1", 1, false);
            other.Add("# 3", 3, false);
            Expect(other.Gap(0, 1), "Rank 2 is not shown", "one missing rank uses singular wording");
            Expect(other.Description, "Only the scores shown by the game are listed",
                "without an own row the description does not claim one");
            other.Add("unranked", 100, false);
            Expect(other.Gap(1, 2), null,
                "an unreadable displayed rank does not use a contradictory fallback");

            var contiguous = new LeaderboardWindow();
            contiguous.Add("# 1", 1, false);
            contiguous.Add("# 2", 2, true);
            Expect(contiguous.Description, "Only the scores shown by the game are listed",
                "a contiguous board does not claim a separated top-score window");

            var extreme = new LeaderboardWindow();
            extreme.Add("# 1", 1, false);
            extreme.Add(int.MaxValue.ToString(), int.MaxValue, false);
            Expect(extreme.Gap(0, 1), "Ranks 2 through 2147483646 are not shown",
                "a large rank gap does not overflow");
            var withHeading = new LeaderboardWindow();
            withHeading.AddContextLine();
            withHeading.Add("# 81", 81, false);
            Expect(withHeading.Gap(0, 1), null, "a song heading is not rank zero");

            Console.WriteLine("leaderboard window: passed " + _passed + ", failed " + _failed);
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
