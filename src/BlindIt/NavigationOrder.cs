using System;
using System.Collections.Generic;

namespace BlindIt
{
    /// <summary>
    /// Works out "this is item 4 of 6" by walking a menu's own up/down navigation
    /// links, with no Unity or IL2CPP types anywhere in it.
    ///
    /// WHY IT IS SEPARATE FROM MenuReader. This is the one piece of the position fix
    /// that has real logic in it: walking to the top of a list, numbering downwards,
    /// and refusing to answer when the links are broken or cyclic. Every mistake
    /// possible here (an off-by-one, a wrong count on the last item, a hang on a
    /// cycle) is a mistake a desktop test can catch for free, whereas testing it
    /// inside MenuReader would cost the player a game launch per attempt. So the
    /// traversal lives here as plain C# over delegates, and MenuReader supplies the
    /// Unity-flavoured callbacks.
    ///
    /// Launch 6 is why this exists at all: the main menu gives every button its own
    /// parent, so counting siblings found one item and the position was never
    /// spoken. The navigation links are the game's own answer to "what comes next",
    /// and they are what the player's arrow keys already follow.
    ///
    /// WRAPPING MENUS, AND WHY LINKS ALONE ARE NOT ENOUGH. Launch 6's log shows the
    /// main menu wrapping: after QUIT, pressing down selects PLAY again. In a ring
    /// every item has an item above it, so walking up never reaches a "first" one,
    /// and the walk would number the list from wherever it happened to stop. That is
    /// how an earlier version of this class reported PLAY as "4 of 4", caught by the
    /// desktop tests before it ever cost a launch. The links still give the right
    /// membership and the right count in a ring; what they cannot give is the origin.
    /// So a ring is detected explicitly, and the caller supplies an origin key (for
    /// this game, the item's height on screen) to decide which item is first. With no
    /// key, a ring yields no position at all, because a "PLAY, 4 of 6" that moves
    /// between visits is worse than silence.
    /// </summary>
    internal static class NavigationOrder
    {
        /// <summary>
        /// Finds the selected item's index and the list's length by walking up to
        /// the first item and then down through all of them.
        ///
        /// Returns false when no honest answer exists, and the caller must then say
        /// the label with no position rather than guess: the item was not found in
        /// its own list, the list turned out to hold fewer than two items (a lone
        /// button is not a list), there are no links to walk at all, or the list is a
        /// ring and no <paramref name="originKey"/> was supplied to say which item
        /// comes first.
        ///
        /// THE SELECTED ITEM ALWAYS COUNTS, even if <paramref name="usable"/> would
        /// reject it. It is on screen by definition, because the reader only speaks
        /// a visible selection, and asking whether the selection is selectable is
        /// circular.
        ///
        /// BOTH WALKS ARE BOUNDED by <paramref name="maxWalk"/>. A malformed link set
        /// would otherwise loop forever inside the game's update, which is a freeze
        /// rather than a glitch.
        /// </summary>
        /// <param name="start">The selected item. Null yields false.</param>
        /// <param name="up">The item above, or null at the top.</param>
        /// <param name="down">The item below, or null at the bottom.</param>
        /// <param name="id">A stable identity, used to compare items.</param>
        /// <param name="usable">Whether an item can be reached by the player.</param>
        /// <param name="maxWalk">Hard bound on steps per walk.</param>
        /// <param name="originKey">
        /// Ranks items when the list is a ring: the SMALLEST key is item one.
        /// MenuReader passes the negated height on screen, so the topmost widget
        /// comes first, which is the order a sighted player reads. Null means
        /// "refuse to number a ring".
        /// </param>
        /// <param name="index">Zero-based position of the selected item.</param>
        /// <param name="count">How many items the walk found.</param>
        internal static bool TryFind<T>(
            T start,
            Func<T, T> up,
            Func<T, T> down,
            Func<T, int> id,
            Func<T, bool> usable,
            int maxWalk,
            Func<T, double> originKey,
            out int index,
            out int count) where T : class
        {
            index = -1;
            count = 0;

            if (start == null || up == null || down == null || id == null
                || usable == null || maxWalk < 1)
                return false;

            int startId = id(start);

            // Walk up to the top. A link to something the player cannot reach ends
            // the walk, because Unity's own navigation skips those too.
            //
            // A ring is the case where walking up arrives back at the selection. It
            // is recorded, not merely stopped: in a ring the item we stop on is not
            // the first item, only the one before the selection.
            T first = start;
            bool ring = false;

            for (int steps = 0; steps < maxWalk; steps++)
            {
                T above = up(first);
                if (above == null)
                    break;

                if (!Counts(above, startId, id, usable))
                    break;

                if (id(above) == startId)
                {
                    ring = true;
                    break;
                }

                first = above;
            }

            // Collect the list in link order from wherever the up-walk reached. In a
            // plain list that is the true first item; in a ring it is arbitrary and
            // gets corrected below.
            List<T> items = new List<T>();
            int firstId = id(first);

            T cur = first;
            for (int steps = 0; steps < maxWalk; steps++)
            {
                if (!Counts(cur, startId, id, usable))
                    break;

                items.Add(cur);

                T below = down(cur);
                if (below == null)
                    break;

                // Back at the top: the list is complete, and this too is a ring.
                if (id(below) == firstId)
                {
                    ring = true;
                    break;
                }

                cur = below;
            }

            // A list of one is not a list. This is also the "no links at all" case,
            // which must fall through to the caller's sibling count.
            if (items.Count < 2)
                return false;

            // In a ring the links give membership and count but not the origin, so an
            // outside ordering decides which item is first. Without one, refuse.
            if (ring)
            {
                if (originKey == null)
                    return false;

                if (!TrySortByOrigin(items, originKey))
                    return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (id(items[i]) == startId)
                {
                    index = i;
                    count = items.Count;
                    return true;
                }
            }

            // The selection was not in its own list: the walk described a different
            // list than the player is in, so say nothing.
            index = -1;
            count = 0;
            return false;
        }

        /// <summary>
        /// Sorts a ring's items by the caller's origin key, smallest first.
        ///
        /// Returns false when the key cannot separate the items (two or more share a
        /// key, or a key is not a real number). A tie leaves the order undecided, and
        /// numbering anyway would announce a position that changes between visits,
        /// which is the exact lie this class exists to avoid.
        /// </summary>
        private static bool TrySortByOrigin<T>(List<T> items, Func<T, double> originKey)
            where T : class
        {
            double[] keys = new double[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                double k;
                try
                {
                    k = originKey(items[i]);
                }
                catch
                {
                    return false;
                }

                if (double.IsNaN(k) || double.IsInfinity(k))
                    return false;

                keys[i] = k;
            }

            for (int i = 0; i < keys.Length; i++)
                for (int j = i + 1; j < keys.Length; j++)
                    if (keys[i] == keys[j])
                        return false;

            // Insertion sort: these lists are menu-sized (nine at the most), and it
            // keeps this class free of comparer plumbing.
            for (int i = 1; i < items.Count; i++)
            {
                double key = keys[i];
                T item = items[i];
                int j = i - 1;

                while (j >= 0 && keys[j] > key)
                {
                    keys[j + 1] = keys[j];
                    items[j + 1] = items[j];
                    j--;
                }

                keys[j + 1] = key;
                items[j + 1] = item;
            }

            return true;
        }

        /// <summary>
        /// Whether an item takes part in the ordering: anything the player can reach,
        /// plus the selection itself, which always counts.
        /// </summary>
        private static bool Counts<T>(T item, int startId, Func<T, int> id, Func<T, bool> usable)
            where T : class
        {
            if (item == null)
                return false;

            if (id(item) == startId)
                return true;

            return usable(item);
        }
    }
}
