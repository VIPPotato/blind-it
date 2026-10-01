using System;
using System.Text;

namespace BlindIt.Harness
{
    internal static class Program
    {
        private static int Main()
        {
            // The test output contains accented characters. Without this the
            // console writes them as '?' on a non-UTF-8 code page and a passing
            // test would look like a failing one.
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Some consoles refuse; the tests still run and still compare
                // correctly, only the display suffers.
            }

            // Both suites always run, and the exit code fails if EITHER fails.
            // Running only up to the first failure would hide a second defect and
            // cost an extra round trip to find it.
            int labels = LabelTests.Run();
            Console.WriteLine();
            int announcements = AnnouncementTests.Run();
            Console.WriteLine();
            int navigation = NavigationOrderTests.Run();
            Console.WriteLine();
            int leaderboard = LeaderboardWindowTests.Run();
            Console.WriteLine();
            int trackChoices = TrackChoiceStateTests.Run();
            Console.WriteLine();
            int songNames = SongNameTests.Run();
            Console.WriteLine();
            int scoreHandoff = ScoreHandoffTests.Run();

            return labels != 0 || announcements != 0 || navigation != 0 || leaderboard != 0
                || trackChoices != 0 || songNames != 0 || scoreHandoff != 0 ? 1 : 0;
        }
    }
}
