using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlindIt
{
    /// <summary>Rank metadata captured alongside the review cursor's text snapshot.</summary>
    internal sealed class LeaderboardWindow
    {
        private readonly List<int> _ranks = new List<int>();
        private bool _hasOwnScore;

        internal void Add(string displayedRank, int fallbackRank, bool ownScore)
        {
            _ranks.Add(RankValue(displayedRank, fallbackRank));
            _hasOwnScore |= ownScore;
        }

        internal void AddContextLine()
        {
            _ranks.Add(0);
        }

        internal string Description
        {
            get
            {
                if (_hasOwnScore && _ranks.Contains(1))
                {
                    for (int i = 1; i < _ranks.Count; i++)
                    {
                        if (Gap(i - 1, i) != null)
                            return Strings.Get("leaderboard.windowAroundYou");
                    }
                }
                return Strings.Get("leaderboard.window");
            }
        }

        internal string Gap(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || toIndex < 0 || fromIndex >= _ranks.Count
                || toIndex >= _ranks.Count || fromIndex == toIndex)
                return null;
            int previous = _ranks[fromIndex];
            int current = _ranks[toIndex];
            if (previous <= 0 || current <= 0 || Math.Abs((long)current - previous) <= 1)
                return null;
            int firstMissing = Math.Min(previous, current) + 1;
            int lastMissing = Math.Max(previous, current) - 1;
            return firstMissing == lastMissing
                ? Strings.Get("leaderboard.gap.one", firstMissing)
                : Strings.Get("leaderboard.gap.many", firstMissing, lastMissing);
        }

        internal static string RankWords(string displayedRank, int fallbackRank)
        {
            string rank = CleanRank(displayedRank);
            if (string.IsNullOrEmpty(rank) && fallbackRank > 0)
                rank = fallbackRank.ToString(CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(rank) ? null : Strings.Get("leaderboard.rank", rank);
        }

        private static int RankValue(string displayedRank, int fallbackRank)
        {
            string rank = CleanRank(displayedRank);
            if (rank == null)
                return fallbackRank > 0 ? fallbackRank : 0;
            return int.TryParse(rank, NumberStyles.Integer | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out int value) && value > 0 ? value : 0;
        }

        private static string CleanRank(string displayedRank)
        {
            string rank = LabelText.Clean(displayedRank);
            if (rank == null)
                return null;
            int start = 0;
            while (start < rank.Length && (rank[start] == '#' || char.IsWhiteSpace(rank[start])))
                start++;
            string clean = rank.Substring(start).Trim();
            return clean.Length == 0 ? null : clean;
        }
    }
}
