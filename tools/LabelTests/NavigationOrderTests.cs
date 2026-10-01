using System;
using System.Collections.Generic;

namespace BlindIt.Harness
{
    /// <summary>
    /// Tests for NavigationOrder, the traversal behind "Leaderboards, 2 of 6".
    ///
    /// Launch 6 is the reason these exist. The main menu spoke no position at all,
    /// because its six buttons each sit under their own ButtonContainer parent and
    /// the sibling count therefore found a list of one. The fix walks the game's own
    /// up/down navigation links instead, and the traps in doing that (an off-by-one
    /// at the ends, a wrong count when the menu wraps, an infinite loop on a
    /// malformed link) are all provable here for free.
    ///
    /// The fake menu is a plain doubly linked list of items. The real main menu's
    /// order, taken from launch 6's own selection log, is used as the fixture: PLAY,
    /// LEADERBOARDS, ACHIEVEMENTS, SETTINGS, CREDITS, QUIT.
    /// </summary>
    internal static class NavigationOrderTests
    {
        /// <summary>One item in a fake menu.</summary>
        private sealed class Item
        {
            public string Name;
            public int Id;
            public Item Up;
            public Item Down;

            /// <summary>Whether the player could reach it: a hidden or disabled widget cannot be.</summary>
            public bool Usable = true;

            /// <summary>
            /// Stands in for the widget's height on screen, which is what decides the
            /// first item of a WRAPPING menu. Smaller sorts first, matching
            /// MenuReader.ScreenOrderKey, which negates Unity's upward y.
            /// </summary>
            public double ScreenKey;
        }

        private static int _failures;
        private static int _tests;

        internal static int Run()
        {
            Console.WriteLine("== navigation order tests ==");
            _failures = 0;
            _tests = 0;

            MainMenuShape();
            WrappingMainMenu();
            SettingsShape();
            Wrapping();
            RingWithTiedKeysRefuses();
            RingWithoutKeyRefuses();
            RingWithUnreadableKeyRefuses();
            PlainListNeedsNoKey();
            Ends();
            LoneItem();
            NoLinks();
            UnreachableItemsSkipped();
            SelectedItemAlwaysCounts();
            BrokenLinksDoNotHang();
            NullAndBadArguments();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "navigation order: all " + _tests + " tests passed"
                : "navigation order: " + _failures + " of " + _tests + " FAILED");

            return _failures;
        }

        /// <summary>
        /// The launch-6 main menu: six buttons, linked top to bottom, no wrap.
        /// Every one of them must report the right position.
        /// </summary>
        private static void MainMenuShape()
        {
            string[] names = { "PLAY", "LEADERBOARDS", "ACHIEVEMENTS", "SETTINGS", "CREDITS", "QUIT" };
            List<Item> menu = Chain(names, wrap: false);

            for (int i = 0; i < menu.Count; i++)
                ExpectPosition("main menu: " + names[i], menu[i], i, 6);
        }

        /// <summary>
        /// The settings panel: nine rows. It already worked through the sibling
        /// count, and it must keep working through the navigation walk, because the
        /// walk is tried FIRST and would otherwise change a passing screen.
        /// </summary>
        private static void SettingsShape()
        {
            string[] names = { "MUSIC", "SFX", "VOICE OVER", "LANGUAGE", "VIBRATION",
                               "RESOLUTION", "FULLSCREEN", "CONTROLS", "BACK" };
            List<Item> menu = Chain(names, wrap: false);

            ExpectPosition("settings: first row", menu[0], 0, 9);
            ExpectPosition("settings: LANGUAGE", menu[3], 3, 9);
            ExpectPosition("settings: last row", menu[8], 8, 9);
        }

        /// <summary>
        /// A wrapping menu, where the last item's Down is the first item. The count
        /// must still be the real number of items, not an endless walk, and the
        /// bound must not be what stops it.
        /// </summary>
        private static void Wrapping()
        {
            string[] names = { "SOLO", "PARTY", "PASS IT", "ONE ON ONE" };
            List<Item> menu = Chain(names, wrap: true);

            ExpectPosition("wrapping: first", menu[0], 0, 4);
            ExpectPosition("wrapping: middle", menu[2], 2, 4);
            ExpectPosition("wrapping: last", menu[3], 3, 4);
        }

        /// <summary>The ends are where an off-by-one hides.</summary>
        private static void Ends()
        {
            List<Item> menu = Chain(new[] { "A", "B", "C" }, wrap: false);

            ExpectPosition("ends: top says 1 of 3", menu[0], 0, 3);
            ExpectPosition("ends: bottom says 3 of 3", menu[2], 2, 3);
        }

        /// <summary>A lone button is not a list: no position, rather than "1 of 1".</summary>
        private static void LoneItem()
        {
            List<Item> menu = Chain(new[] { "BACK" }, wrap: false);
            ExpectNoPosition("lone item gives no position", menu[0]);
        }

        /// <summary>
        /// The main menu's OLD failure mode, now at the navigation level: an item
        /// with no links at all. It must return false so the caller falls back to
        /// the sibling count instead of speaking a wrong number.
        /// </summary>
        private static void NoLinks()
        {
            Item alone = new Item { Name = "PLAY", Id = 1 };
            ExpectNoPosition("no links at all gives no position", alone);
        }

        /// <summary>
        /// A hidden or disabled row must not be counted: Unity's own navigation
        /// skips it, so counting it would announce "6 of 9" on a menu of six
        /// reachable items. This is the launch-4 lesson, kept.
        /// </summary>
        private static void UnreachableItemsSkipped()
        {
            List<Item> menu = Chain(new[] { "A", "B", "HIDDEN", "C" }, wrap: false);
            menu[2].Usable = false;

            // The walk stops at the unreachable item, so A and B form the list.
            ExpectPosition("unreachable row ends the list", menu[0], 0, 2);
        }

        /// <summary>
        /// The selection itself always counts, even if the usable test would reject
        /// it: it is on screen by definition, and asking whether the selection is
        /// selectable is circular. Without this rule a slider mid-drag could lose its
        /// position.
        ///
        /// The expected count here is 3, not 2: B being unusable does not remove it
        /// from the walk when B is the selection, so the whole A-B-C list is still
        /// found. (An earlier version of this test expected 2, which was the test
        /// being wrong rather than the code.)
        /// </summary>
        private static void SelectedItemAlwaysCounts()
        {
            List<Item> menu = Chain(new[] { "A", "B", "C" }, wrap: false);
            menu[1].Usable = false;

            ExpectPosition("selected item counts even when not usable", menu[1], 1, 3);
        }

        /// <summary>
        /// A malformed link set (an item whose Down points back at itself) must be
        /// stopped by the bound rather than freezing the game's update loop. The
        /// test passing at all is the proof: an unbounded walk would hang here.
        /// </summary>
        private static void BrokenLinksDoNotHang()
        {
            Item a = new Item { Name = "A", Id = 1 };
            Item b = new Item { Name = "B", Id = 2 };
            a.Down = b;
            b.Up = a;
            b.Down = b;   // points at itself: malformed

            int index, count;
            bool ok = NavigationOrder.TryFind(
                a, i => i.Up, i => i.Down, i => i.Id, i => i.Usable, 64,
                i => i.ScreenKey, out index, out count);

            // Any bounded answer is acceptable; hanging is not. A and B are real, so
            // a sane implementation reports the two of them.
            Check("broken links terminate", ok && count >= 2);
        }

        /// <summary>
        /// The launch-6 main menu as it ACTUALLY is: wrapping, because QUIT's "down"
        /// selects PLAY again (proven in the launch-6 selection log). Screen order
        /// decides who is first, so every item must still get its true position.
        ///
        /// This is the case that a links-only walk got wrong, reporting PLAY as
        /// "4 of 4". These tests are the reason that never reached the player.
        /// </summary>
        private static void WrappingMainMenu()
        {
            string[] names = { "PLAY", "LEADERBOARDS", "ACHIEVEMENTS", "SETTINGS", "CREDITS", "QUIT" };
            List<Item> menu = Chain(names, wrap: true);

            for (int i = 0; i < menu.Count; i++)
                ExpectPosition("wrapping main menu: " + names[i], menu[i], i, 6);
        }

        /// <summary>
        /// A ring whose items cannot be told apart by screen position: no position is
        /// spoken at all. Numbering it would announce an order that changes between
        /// visits, and a moving "PLAY, 4 of 6" is worse than a silent one.
        /// </summary>
        private static void RingWithTiedKeysRefuses()
        {
            List<Item> menu = Chain(new[] { "A", "B", "C" }, wrap: true);
            foreach (Item item in menu)
                item.ScreenKey = 0.0;   // all at the same height: no order to be had

            ExpectNoPosition("ring with tied screen keys gives no position", menu[0]);
        }

        /// <summary>
        /// A ring with no origin key supplied at all must refuse rather than number
        /// from an arbitrary point. This is the guarantee the caller relies on.
        /// </summary>
        private static void RingWithoutKeyRefuses()
        {
            List<Item> menu = Chain(new[] { "A", "B", "C" }, wrap: true);

            int index, count;
            bool ok = NavigationOrder.TryFind(
                menu[0], i => i.Up, i => i.Down, i => i.Id, i => i.Usable, 64,
                null, out index, out count);

            Check("ring with no origin key gives no position", !ok);
        }

        /// <summary>
        /// A PLAIN list needs no origin key: its first item is found by walking up.
        /// Only rings depend on the key, so a screen whose widgets cannot be measured
        /// still gets positions as long as it does not wrap.
        /// </summary>
        private static void PlainListNeedsNoKey()
        {
            List<Item> menu = Chain(new[] { "A", "B", "C" }, wrap: false);

            int index, count;
            bool ok = NavigationOrder.TryFind(
                menu[1], i => i.Up, i => i.Down, i => i.Id, i => i.Usable, 64,
                null, out index, out count);

            Check("plain list works with no origin key (want 2 of 3, got "
                  + (ok ? (index + 1) + " of " + count : "none") + ")",
                  ok && index == 1 && count == 3);
        }

        /// <summary>
        /// A ring whose screen keys are unreadable (NaN, as MenuReader returns for a
        /// widget it cannot measure) must refuse, not sort NaN into some position.
        /// </summary>
        private static void RingWithUnreadableKeyRefuses()
        {
            List<Item> menu = Chain(new[] { "A", "B", "C" }, wrap: true);
            menu[1].ScreenKey = double.NaN;

            ExpectNoPosition("ring with an unreadable screen key gives no position", menu[0]);
        }

        /// <summary>Bad arguments must fail quietly, never throw into the game's loop.</summary>
        private static void NullAndBadArguments()
        {
            int index, count;

            bool nullStart = NavigationOrder.TryFind<Item>(
                null, i => i.Up, i => i.Down, i => i.Id, i => i.Usable, 64,
                i => i.ScreenKey, out index, out count);
            Check("null start returns false", !nullStart);

            Item a = new Item { Name = "A", Id = 1 };
            bool nullCallback = NavigationOrder.TryFind(
                a, null, i => i.Down, i => i.Id, i => i.Usable, 64,
                i => i.ScreenKey, out index, out count);
            Check("null callback returns false", !nullCallback);

            bool zeroWalk = NavigationOrder.TryFind(
                a, i => i.Up, i => i.Down, i => i.Id, i => i.Usable, 0,
                i => i.ScreenKey, out index, out count);
            Check("zero bound returns false", !zeroWalk);
        }

        /// <summary>Builds a doubly linked chain of items, optionally wrapping.</summary>
        private static List<Item> Chain(string[] names, bool wrap)
        {
            List<Item> items = new List<Item>();
            for (int i = 0; i < names.Length; i++)
            {
                // ScreenKey ascends with the on-screen order, the way
                // MenuReader.ScreenOrderKey does for real widgets.
                items.Add(new Item { Name = names[i], Id = i + 1, ScreenKey = i * 10.0 });
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    items[i].Up = items[i - 1];
                if (i < items.Count - 1)
                    items[i].Down = items[i + 1];
            }

            if (wrap && items.Count > 1)
            {
                items[0].Up = items[items.Count - 1];
                items[items.Count - 1].Down = items[0];
            }

            return items;
        }

        private static void ExpectPosition(string what, Item start, int wantIndex, int wantCount)
        {
            int index, count;
            bool ok = NavigationOrder.TryFind(
                start, i => i.Up, i => i.Down, i => i.Id, i => i.Usable, 64,
                i => i.ScreenKey, out index, out count);

            bool pass = ok && index == wantIndex && count == wantCount;
            Check(what + " (want " + (wantIndex + 1) + " of " + wantCount
                  + ", got " + (ok ? (index + 1) + " of " + count : "none") + ")", pass);
        }

        private static void ExpectNoPosition(string what, Item start)
        {
            int index, count;
            bool ok = NavigationOrder.TryFind(
                start, i => i.Up, i => i.Down, i => i.Id, i => i.Usable, 64,
                i => i.ScreenKey, out index, out count);

            Check(what, !ok);
        }

        private static void Check(string what, bool pass)
        {
            _tests++;
            if (pass)
            {
                Console.WriteLine("  PASS  " + what);
            }
            else
            {
                _failures++;
                Console.WriteLine("  FAIL  " + what);
            }
        }
    }
}
