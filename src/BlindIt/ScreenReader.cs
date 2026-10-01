using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;

namespace BlindIt
{
    /// <summary>
    /// Reads the screens that have no keyboard focus for the mod to follow.
    ///
    /// WHY THIS EXISTS. MenuReader works by following the EventSystem's selected
    /// object, which covers every ordinary menu in this game. Three screens have
    /// no such selection at all, proven from the launch-4 log: entering any of
    /// them logs "selection cleared" and then "selected=NULL" for as long as the
    /// player stays. Those screens are:
    ///
    ///  - Achievements. AchievementsPanel drives an echo17 EndlessBook: the
    ///    entries are AchievementListItem objects placed onto book page views,
    ///    not selectable widgets.
    ///  - The leaderboard, and the track/device screen. BOTH are
    ///    CombinedLeaderboardPanel, whose Mode property is either MainMenu or
    ///    Game. Its rows are LeaderboardLineItem clones of a prefab, and the same
    ///    class owns ChangeMusicTrack and ChangeDevice, which is the "change
    ///    songs and switch to Bop It Extreme" screen the player described.
    ///
    /// So this is one cursor over three screens, not three separate readers.
    ///
    /// THE MENUS THAT ALREADY WORK COME FIRST. This reader can only speak by
    /// taking the frame away from MenuReader, so a wrong "yes, this is my screen"
    /// would silence the main menu and the settings, which launch 4 proved
    /// working. Panel.IsVisible is not a safe enough basis for that decision on
    /// its own: this build keeps more than one CombinedLeaderboardPanel alive
    /// (MainMenuLeaderboardPanel holds one, GameUIManager holds another), and the
    /// getter's body is not in the dump, so what it reports while the main menu is
    /// up is NOT proven. The claim is therefore also gated on the one thing the
    /// launch-4 log DID prove: these three screens have no visible selection.
    /// While the game holds a visible selection, this reader stands down, whatever
    /// the panels claim.
    ///
    /// THE CURSOR NEVER HOLDS A UI OBJECT. Items are re-found from the live scene
    /// on every read, and the cursor is a plain integer index. A cached Unity
    /// object would go stale the moment the game repopulates a row, and pooled
    /// rows are reused for different data, so a remembered reference would
    /// confidently speak the wrong score.
    ///
    /// TEXT IS READ AT SPEAK TIME, NEVER AT BUILD TIME. The row text is pulled
    /// from the live component in the same call that speaks it. Reading it when
    /// the list was discovered is the classic frozen-capture bug: the game
    /// refreshes a leaderboard row in place, and a value captured earlier is then
    /// announced for data that has since changed.
    /// </summary>
    internal static class ScreenReader
    {
        /// <summary>Which screen the cursor is currently on, or null for none.</summary>
        private static string _screen;

        /// <summary>The cursor's position in the current screen's item list.</summary>
        private static int _cursor;

        /// <summary>
        /// The items as they were when the cursor last moved, so navigation walks a
        /// list that holds still.
        ///
        /// WHY A SNAPSHOT AND NOT A FRESH READ. This used to re-read the scene on
        /// every key press, on the reasoning that the game can repopulate a list
        /// between presses. It can, and that turned out to be the defect rather than
        /// the safeguard: launch 7 recorded the player pressing into the leaderboard
        /// and hearing "rank # 1", then "rank # 493" on the next press. The game
        /// fetches scores over the network and shows a WINDOW around the player's own
        /// rank, so the rows under a fixed index are different scores from one second
        /// to the next (the aliases changed between two reads in that log:
        /// elielgamer17, then j00de, then shelvacu). An index into a list that is
        /// being rebuilt underneath it is meaningless, which is exactly what the
        /// player heard.
        ///
        /// So the list is captured once and the cursor walks the capture.
        /// <see cref="Resnapshot"/> says when a rebuild is taken on board, and the
        /// player is told when that happens instead of being moved silently.
        /// </summary>
        private static List<string> _items = new List<string>();
        private static LeaderboardWindow _leaderboardWindow = new LeaderboardWindow();

        /// <summary>
        /// How many items the last read found, so a screen that fills in late can
        /// be noticed rather than staying "Empty" for the rest of the visit.
        /// </summary>
        private static int _lastCount;

        /// <summary>Frames since the scene was last searched for a panel.</summary>
        private static int _sinceDetect;

        /// <summary>Frames since an empty screen was last re-checked for content.</summary>
        private static int _sinceEmptyPoll;

        /// <summary>
        /// The screen's context (group and date filter, or track and device) as it
        /// was last spoken, so a change the player makes with the GAME's own keys
        /// can be noticed. Null means "not seen yet on this screen", which must not
        /// be announced: entering the screen already said it.
        /// </summary>
        private static string _lastContext;

        /// <summary>
        /// How often the scene may be searched for one of these panels.
        ///
        /// Detection costs two FindObjectsOfType calls, which walk every object in
        /// the scene. Doing that every frame is the documented way to make a game
        /// stutter, and this game is played to a beat: a hitch is not cosmetic
        /// here, it costs the player the round. Ten frames is about a sixth of a
        /// second, far too short for the player to notice a delay in the
        /// announcement, and a tenth of the work.
        /// </summary>
        private const int DetectEveryFrames = 10;

        /// <summary>
        /// How often a screen that read as empty is re-checked.
        ///
        /// The leaderboard arrives over the network: CombinedLeaderboardPanel owns
        /// a WaitingSpinner, an OnLoadingChanged callback and a RefreshData method,
        /// and its rows are only built once SetScores lands. So on entry the honest
        /// answer really is "no rows yet", and without this poll the player would
        /// be told "Empty" once and never hear the scores that arrived half a
        /// second later.
        /// </summary>
        private const int EmptyPollFrames = 30;

        /// <summary>
        /// Called every frame, before MenuReader. Returns true when this reader
        /// owns the screen, so the caller can leave the focus-following reader
        /// alone and the two can never both speak.
        /// </summary>
        internal static bool Tick()
        {
            try
            {
                // GUARD, EVERY FRAME. If the game is holding a selection the player
                // can see, that selection is the truth about where they are, and
                // MenuReader is the reader that handles it. This runs before any
                // scene search: it is cheap, and it is what stops a mistake in
                // panel detection from silencing a menu that works.
                GameObject selected = Ui.Selected();
                if (selected != null && Ui.Visible(selected))
                {
                    // ONE EXCEPTION: the credits. That screen is a wall of text with
                    // a Back button on it, so the game legitimately holds a visible
                    // selection while the player's real problem is the text they
                    // cannot read. Only a deliberate press of the mod's own browse
                    // keys is answered here, and only while credits are showing, so
                    // the focused button still speaks normally through MenuReader
                    // and no other screen is affected.
                    if (BrowseCreditsBesideSelection())
                        return true;

                    Release("game holds a visible selection");
                    return false;
                }

                _sinceDetect++;

                if (_sinceDetect >= DetectEveryFrames)
                {
                    _sinceDetect = 0;

                    Panel panel;
                    string screen = DetectScreen(out panel);

                    if (screen == null)
                    {
                        Release("no self-navigated panel showing");
                        return false;
                    }

                    if (screen != _screen)
                    {
                        Enter(screen, panel);
                        return true;
                    }
                }
                else if (_screen == null)
                {
                    // Nothing claimed, and not this frame's turn to look. Let the
                    // focus-following reader have the frame.
                    return false;
                }

                // A claimed screen: only the mod's own keys do anything here.
                //
                // The arrows count as review keys HERE because this reader only ever
                // gets past the guard above when the game holds no visible selection,
                // which is the condition that makes UI/Navigate inert. See
                // Hotkeys.ReviewNextPressed for the binding evidence. The credits are
                // handled separately in BrowseCreditsBesideSelection, where the game
                // DOES hold a selection and the arrows must stay the game's.
                //
                // The latency window is the one claimed screen that keeps the
                // brackets only: CalibratePanel binds the player's own input actions
                // (OnSubmit, OnInput) while it is open, so a press there is the
                // player's answer to the calibration and must not be read as review.
                bool arrowsFree = _screen != ScreenCalibrate;

                if (arrowsFree ? Hotkeys.ReviewNextPressed() : Hotkeys.NextPressed())
                    Move(1);
                else if (arrowsFree ? Hotkeys.ReviewPreviousPressed() : Hotkeys.PreviousPressed())
                    Move(-1);
                else if (Hotkeys.RepeatPressed())
                    Repeat();
                else if (!SpeakContextChange())
                    PollForLateItems();

                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    Log.Line("screen", "tick failed: " + ex.GetType().Name + ": " + ex.Message);
                }
                catch
                {
                    // Nothing left to do.
                }

                // Never claim the screen after a failure: let the focus-following
                // reader keep working rather than leaving the player with nothing.
                return false;
            }
        }

        /// <summary>
        /// Announces a newly entered screen and its first item.
        /// </summary>
        private static void Enter(string screen, Panel panel)
        {
            _screen = screen;
            _cursor = 0;
            _sinceEmptyPoll = 0;

            // Forgotten, not read, on entry: the first tick on the new screen
            // records the context silently, so entering never speaks it twice.
            _lastContext = null;

            Log.Line("screen", "entered | " + screen);

            _items = CaptureItems(panel);
            List<string> items = _items;
            _lastCount = items.Count;

            if (items.Count == 0)
            {
                // An empty surface must say so once. Silence here is
                // indistinguishable from the mod being broken. The poll below
                // speaks again if this was only the network being slow.
                //
                // The key name comes from Hotkeys, never spelled out here: launch 5
                // said "Press F9", and after the remap that sentence would have
                // sent the player to a key that does nothing.
                Speech.Speak(Announcement.Sentences(
                    Describe(screen),
                    Strings.Get("list.empty"),
                    // Arrows are only offered where they are inert, which is every
                    // screen claimed this way EXCEPT the credits (the game keeps its
                    // Back button focused there) and the latency window (the game is
                    // waiting for the player's own presses).
                    Hotkeys.ReviewHelp(screen != ScreenCredits && screen != ScreenCalibrate)), true);
            }
            else
            {
                Speech.Speak(Announcement.Sentences(
                    Describe(screen),
                    CountItems(screen, items.Count),
                    Announce(items, 0)), true);
            }
        }

        /// <summary>
        /// "6 items", or nothing on a screen where a count is noise.
        ///
        /// The credits are prose, not choices: the player asked on 2026-10-01 for
        /// positions not to be read there, and a count of lines is the same kind of
        /// clutter in front of the same text.
        /// </summary>
        private static string CountItems(string screen, int count)
        {
            if (screen == ScreenCredits)
                return null;

            // The latency window is a status display, not a list of choices: "3
            // items" in front of "press the Bop button on every beat" is clutter in
            // front of an instruction.
            if (screen == ScreenCalibrate)
                return null;

            if (screen == ScreenLeaderboard)
                return Announcement.Sentences(_leaderboardWindow.Description,
                    Strings.Get("leaderboard.items", count.ToString(CultureInfo.InvariantCulture)));

            return Strings.Get("list.items", count.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Gives up the screen, so MenuReader takes over from this frame on.
        /// </summary>
        internal static void ReleaseForOtherReader()
        {
            Release("pre-game choices own this screen");
        }

        private static void Release(string why)
        {
            if (_screen == null)
                return;

            Log.Line("screen", "left | was=" + _screen + " | " + why);

            _screen = null;
            _cursor = 0;
            _items = new List<string>();
            _leaderboardWindow = new LeaderboardWindow();
            _lastCount = 0;
            _sinceEmptyPoll = 0;
            _lastContext = null;
        }

        /// <summary>
        /// Speaks the scores once they arrive on a screen that read as empty.
        ///
        /// Only the empty-to-filled step is announced. A leaderboard that merely
        /// refreshes its numbers is left alone, because speaking on every refresh
        /// would talk over a player trying to read the list.
        /// </summary>
        private static void PollForLateItems()
        {
            // Calibration already announces its stage through SpeakContextChange.
            // Its review list contains that same stage, not late network data.
            if (_screen == ScreenCalibrate)
                return;

            if (_lastCount > 0)
                return;

            _sinceEmptyPoll++;
            if (_sinceEmptyPoll < EmptyPollFrames)
                return;

            _sinceEmptyPoll = 0;

            List<string> items = CaptureItems(null);
            if (items.Count == 0)
                return;

            _items = items;
            _lastCount = items.Count;
            _cursor = 0;

            Log.Line("screen", "filled late | " + _screen + " | items="
                + items.Count.ToString(CultureInfo.InvariantCulture));

            Speech.Speak(Announcement.Sentences(
                CountItems(_screen, items.Count),
                Announce(items, 0)), false);
        }

        /// <summary>
        /// Lets the player step through the credits with the mod's keys even while
        /// the game keeps a button focused on that screen. Returns true when it
        /// handled a press.
        ///
        /// WHY THIS IS SEPARATE FROM THE NORMAL CLAIM. Every other screen this
        /// reader owns has NO selection at all, which is exactly how it knows the
        /// screen is its own. The credits break that rule: the text is unreachable
        /// like the other three, but the panel still focuses its Back button. So
        /// rather than weaken the guard that protects the working menus, this is a
        /// deliberate, narrow exception: it acts only when the credits are on screen
        /// AND the player pressed a browse key, and it never claims the frame
        /// otherwise.
        ///
        /// The reader stays released the rest of the time, so MenuReader keeps
        /// announcing the Back button and the player can still leave the screen.
        /// </summary>
        private static bool BrowseCreditsBesideSelection()
        {
            bool next = Hotkeys.NextPressed();
            bool previous = !next && Hotkeys.PreviousPressed();
            bool repeat = !next && !previous && Hotkeys.RepeatPressed();

            if (!next && !previous && !repeat)
                return false;

            // The cheap key check came first on purpose: this runs on a frame where
            // a menu may be perfectly healthy, so the scene search only happens once
            // the player has actually asked for something.
            if (FindShowingCredits() == null)
                return false;

            if (_screen != ScreenCredits)
            {
                // First press on this screen: announce it like an entry, so the
                // player hears what they are in and how many lines there are.
                Enter(ScreenCredits, null);
                return true;
            }

            if (repeat)
                Repeat();
            else
                Move(next ? 1 : -1);

            return true;
        }

        /// <summary>
        /// Speaks the leaderboard's or track screen's context when the player
        /// changes it: the group tab (Local, Friends, Global), the date range
        /// (Today, This month, All time), the track, or the device.
        ///
        /// WHY A WATCHER AND NOT A KEY HANDLER. These are the game's OWN keys (o
        /// and p change the group, k and l the date range, the right arrow the
        /// device, per the launch-5 binding dump), and the game may refuse or
        /// debounce a change: CombinedLeaderboardPanel puts its filter changes
        /// through an ActionDebouncer. Reading the resulting state is therefore the
        /// only honest way to report it. Watching also covers a change the player
        /// did not make, such as the game falling back to Local when it finds
        /// itself offline.
        ///
        /// Returns true when it spoke, so the caller skips the late-items poll and
        /// the frame produces exactly ONE utterance.
        ///
        /// Nothing is spoken the first time a context is seen: entering the screen
        /// has already said it, and this must not repeat it.
        /// </summary>
        private static bool SpeakContextChange()
        {
            try
            {
                string context = ReadContext();

                // A transiently missing value is not a new filter or calibration stage.
                if (string.IsNullOrEmpty(context))
                    return false;
                if (string.Equals(context, _lastContext, StringComparison.Ordinal))
                    return false;

                bool first = _lastContext == null;
                _lastContext = context;

                if (first || string.IsNullOrEmpty(context))
                    return false;

                // THE LIST UNDERNEATH JUST CHANGED. A new filter means different
                // items, so a cursor left where it was would point into a list that
                // no longer exists and speak the wrong score. Back to the top, which
                // is also where a sighted player's eye goes.
                _cursor = 0;

                // The snapshot is dropped rather than refilled here: the rows still
                // belong to the OLD filter for a moment (the panel refetches), so
                // capturing now would freeze stale scores in place. Emptying it makes
                // the late-items poll treat this as a screen waiting to fill, which
                // is exactly what it is, and the next press reads the new list.
                _items = new List<string>();
                _leaderboardWindow = new LeaderboardWindow();
                _lastCount = 0;
                _sinceEmptyPoll = 0;

                Log.Line("screen", "context changed | " + _screen + " | " + context);

                Speech.Speak(Announcement.Detail(context), true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Line("screen", "context read failed: " + ex.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// The current context of the claimed screen as one short clause, or null
        /// when this screen has none.
        /// </summary>
        private static string ReadContext()
        {
            switch (_screen)
            {
                case ScreenLeaderboard:
                {
                    CombinedLeaderboardPanel board = FindShowing<CombinedLeaderboardPanel>();
                    if (board == null)
                        return null;

                    string device = CurrentDeviceName(board.deviceContainer);
                    if (Ui.Alive(board.deviceContainer) && string.IsNullOrEmpty(device))
                        return null;
                    return Announcement.Detail(ActiveFilters(board),
                        string.IsNullOrEmpty(device) ? null : Strings.Get("track.device.spoken", device));
                }

                case ScreenTrackSelect:
                {
                    CombinedLeaderboardPanel board = FindShowing<CombinedLeaderboardPanel>();
                    if (board == null)
                        return null;

                    string track = CurrentTrackName(board);
                    string device = CurrentDeviceName();

                    return Announcement.Detail(
                        string.IsNullOrEmpty(track) ? null : Strings.Get("track.song.spoken", track),
                        string.IsNullOrEmpty(device) ? null : Strings.Get("track.device.spoken", device));
                }

                case ScreenCalibrate:
                    // The calibration's stage IS its context: the panel says Start,
                    // then Warmup, Calibrate, Finished, Result, and each one means
                    // something different for what the player should be doing. This
                    // is what makes the window audible instead of nineteen silent
                    // seconds.
                    return CalibrateStatus(CalibrateOf(null));

                case ScreenAchievements:
                    // The song whose pages are open, NOT the page number.
                    //
                    // This changed in launch 9. The reader used to list every
                    // achievement in the book at once, so a page flip altered
                    // nothing the player could walk and announcing it described a
                    // purely visual event ("Achievement pages don't matter to the
                    // mod, no matter which one I choose it moves through all of
                    // them", the player on 2026-10-01). The reader is now scoped to
                    // the open spread, so a flip really does replace the list, and
                    // the player must hear which song they have landed on.
                    //
                    // The song is the right context rather than the page number for
                    // the same reason as before: the number is a visual detail, and
                    // the player settled that it means nothing to them. Reporting
                    // the song instead both names the new list and causes the cursor
                    // to reset, because a flip now invalidates it.
                    return SpreadSong();

                default:
                    // Nothing else has a changing context: the ordinary menus are
                    // MenuReader's, and it reports their values itself.
                    return null;
            }
        }

        /// <summary>
        /// Which pages of the achievements book are open, as "pages 2 and 3 of 8",
        /// or null when the book cannot be read.
        ///
        /// WHY THIS IS NEEDED. The book shows four achievements per opening, and the
        /// game's page-flip keys replace every entry on screen. Without this the
        /// player flips the page and hears nothing at all, then finds the list has
        /// silently become a different list (launch-5 defect).
        ///
        /// Read from the echo17 EndlessBook that BookController owns. Both page
        /// numbers are given because the book really does show two at once, and
        /// LastPageNumber is the book's own count, not a guess.
        /// </summary>
        private static string BookPage()
        {
            try
            {
                BookController controller = FindBookController();
                if (controller == null)
                    return null;

                Il2Cppecho17.EndlessBook.EndlessBook book = controller.Book;
                if (book == null || !Ui.Alive(book))
                    return null;

                int left = book.CurrentLeftPageNumber;
                int right = book.CurrentRightPageNumber;
                int last = book.LastPageNumber;

                // The wording and every "is this readable" rule live in Announcement,
                // where a desktop test proves them without a game launch.
                return Announcement.BookPages(left, right, last);
            }
            catch (Exception ex)
            {
                Log.Line("screen", "book page read failed: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// The live BookController, or null.
        ///
        /// Found across the scene, for the same reason the achievements themselves
        /// are: AchievementsPanel locates the book by tag rather than owning it, so
        /// the controller is not reliably a child of the panel.
        /// </summary>
        private static BookController FindBookController()
        {
            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<BookController>());

                if (found == null)
                    return null;

                for (int i = 0; i < found.Length; i++)
                {
                    BookController c = found[i]?.TryCast<BookController>();
                    if (c == null || !Ui.Alive(c) || !Ui.Alive(c.gameObject))
                        continue;

                    if (!c.gameObject.activeInHierarchy)
                        continue;

                    return c;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The leaderboard's active group and date tabs as words.
        ///
        /// Read from the tab components the panel owns, because that is the state
        /// the game itself displays. CombinedLeaderboardPanel holds six of them as
        /// private serialised fields (local, friends, global, today, allTime,
        /// month); private fields are not reachable through the generated proxy, so
        /// the tabs are found under the panel by type instead, and each one's public
        /// IsActive flag says whether it is the chosen one.
        ///
        /// GroupFilterTab carries a Text label, so the group reads with the game's
        /// own localised wording. DateFilterTab has no text at all, only an
        /// ActiveBackground, so its name is taken from its GameObject, which the
        /// launch-5 dump shows as Today, Month and AllTime.
        /// </summary>
        private static string ActiveFilters(CombinedLeaderboardPanel board)
        {
            string group = null;
            string date = null;

            try
            {
                Il2CppReferenceArray<Component> tabs = board.GetComponentsInChildren(
                    Il2CppType.Of<GroupFilterTab>(), true);

                if (tabs != null)
                {
                    for (int i = 0; i < tabs.Length; i++)
                    {
                        GroupFilterTab tab = tabs[i]?.TryCast<GroupFilterTab>();
                        if (tab == null || !Ui.Alive(tab) || !tab.IsActive)
                            continue;

                        group = Tmp(tab.Text);
                        if (string.IsNullOrEmpty(group))
                            group = Ui.SafeName(tab.gameObject);

                        group = Announcement.Sentence(group);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Line("screen", "group tab read failed: " + ex.GetType().Name);
            }

            try
            {
                Il2CppReferenceArray<Component> tabs = board.GetComponentsInChildren(
                    Il2CppType.Of<DateFilterTab>(), true);

                if (tabs != null)
                {
                    for (int i = 0; i < tabs.Length; i++)
                    {
                        DateFilterTab tab = tabs[i]?.TryCast<DateFilterTab>();
                        if (tab == null || !Ui.Alive(tab) || !tab.IsActive)
                            continue;

                        date = DateRangeWords(Ui.SafeName(tab.gameObject));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Line("screen", "date tab read failed: " + ex.GetType().Name);
            }

            return Announcement.Detail(group, date);
        }

        /// <summary>
        /// A date filter's object name as words the player expects: the objects are
        /// named Today, Month and AllTime in this build's hierarchy.
        /// </summary>
        private static string DateRangeWords(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return null;

            string lower = objectName.ToLowerInvariant();

            if (lower.Contains("alltime") || lower.Contains("all time"))
                return Strings.Get("filter.allTime");
            if (lower.Contains("month"))
                return Strings.Get("filter.thisMonth");
            if (lower.Contains("today") || lower.Contains("day"))
                return Strings.Get("filter.today");

            return Announcement.Sentence(objectName);
        }

        /// <summary>
        /// Moves the cursor and speaks the item it lands on.
        ///
        /// The list walked here is the SNAPSHOT, not a fresh read of the scene. See
        /// <see cref="_items"/> for why: on the leaderboard a fresh read per press
        /// made the cursor jump between unrelated scores.
        /// </summary>
        private static void Move(int delta)
        {
            // A list the player has not been given yet (entered while still loading)
            // is worth one read, so the first press after the scores land works.
            if (_items.Count == 0)
                Resnapshot(false);

            List<string> items = _items;

            if (items.Count == 0)
            {
                Speech.Speak(Strings.Get("list.empty"), true);
                return;
            }

            int target = _cursor + delta;

            // Clamped, not wrapped. On a list the player cannot see, wrapping from
            // the last item to the first is indistinguishable from not moving, so
            // the edge is announced instead.
            if (target < 0)
            {
                _cursor = 0;
                Speech.Speak(Announcement.Sentences(
                    Strings.Get("list.first"), Announce(items, 0)), true);
                return;
            }

            if (target >= items.Count)
            {
                _cursor = items.Count - 1;
                Speech.Speak(Announcement.Sentences(
                    Strings.Get("list.last"), Announce(items, _cursor)), true);
                return;
            }

            string gap = _screen == ScreenLeaderboard || _screen == ScreenTrackSelect
                ? _leaderboardWindow.Gap(_cursor, target) : null;
            _cursor = target;
            Speech.Speak(Announcement.Sentences(gap, Announce(items, _cursor)), true);

            Log.Line("screen", "moved | " + _screen + " | index="
                + _cursor.ToString(CultureInfo.InvariantCulture)
                + " of " + items.Count.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Captures the screen's items, so the cursor has a list that holds still.
        ///
        /// Returns true when the captured list is DIFFERENT from the one the player
        /// was last navigating. Callers use that to tell them the list changed under
        /// them rather than moving them somewhere unexpected in silence.
        ///
        /// <paramref name="keepPosition"/> tries to leave the cursor on the same item
        /// it was on, found by its text, after a rebuild. On the leaderboard that
        /// means the player keeps their place when the window of scores refills
        /// instead of being thrown back to the top.
        /// </summary>
        private static bool Resnapshot(bool keepPosition)
        {
            List<string> fresh = CaptureItems(null);

            string was = keepPosition && _cursor >= 0 && _cursor < _items.Count
                ? _items[_cursor]
                : null;

            bool changed = !SameItems(_items, fresh);

            _items = fresh;
            _lastCount = fresh.Count;

            if (fresh.Count == 0)
            {
                _cursor = 0;
                return changed;
            }

            if (was != null)
            {
                // The same row by its words, which is what the player is holding in
                // their head. Index would be the wrong thing to preserve: the whole
                // problem is that the index now points at another score.
                int again = fresh.IndexOf(was);

                if (again >= 0)
                {
                    _cursor = again;
                    return changed;
                }
            }

            if (_cursor >= fresh.Count)
                _cursor = fresh.Count - 1;
            if (_cursor < 0)
                _cursor = 0;

            return changed;
        }

        /// <summary>Whether two item lists hold the same words in the same order.</summary>
        private static bool SameItems(List<string> a, List<string> b)
        {
            if (a == null || b == null)
                return a == b;

            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        /// <summary>Re-reads and re-speaks the current item, with the screen name.</summary>
        private static void Repeat()
        {
            // The repeat key is the player deliberately asking "what is here now", so
            // this is the one place a fresh read is right. A rebuild found here is
            // reported, because the answer may differ from what they last heard.
            bool changed = Resnapshot(true);
            List<string> items = _items;

            if (items.Count == 0)
            {
                Speech.Speak(Announcement.Sentences(
                    Describe(_screen), Strings.Get("list.empty")), true);
                return;
            }

            Speech.Speak(Announcement.Sentences(
                Describe(_screen),
                changed ? Strings.Get("leaderboard.refreshed") : null,
                Announce(items, _cursor)), true);
        }

        /// <summary>
        /// The spoken form of one item: its text plus where it sits in the list.
        /// Position matters most on exactly these screens, because the player has
        /// no visual sense of how far down a long list of scores they are.
        ///
        /// Composed through <see cref="Announcement.Detail"/>, so the position is a
        /// comma clause at the end ("rank # 1, elielgamer17, 739 points, 1 of 6")
        /// in the same shape the player chose for ordinary menus on 2026-09-30,
        /// rather than the separate sentence launch 5 spoke.
        ///
        /// EXCEPT ON THE CREDITS, where the player asked for no positions at all
        /// (2026-10-01). A position is a navigation aid for a list of choices; the
        /// credits are a passage of prose, and "1 of 60" in front of every line is
        /// noise between the player and the words.
        /// </summary>
        private static string Announce(List<string> items, int index)
        {
            if (_screen == ScreenCredits || _screen == ScreenCalibrate)
                return items[index];

            return Announcement.Detail(
                items[index],
                Announcement.Position(index, items.Count));
        }

        /// <summary>
        /// Read-only views of this reader's state, for the dump key only.
        ///
        /// These exist so a silence diagnoses itself from one dump instead of another
        /// launch: they answer "did the reader claim this screen", "what would it say
        /// for the filters right now", and "how many items can it see". Nothing in the
        /// mod's speaking path uses them, and they never change state.
        /// </summary>
        internal static string DebugScreen => _screen;

        /// <summary>Where the browse cursor sits. See <see cref="DebugScreen"/>.</summary>
        internal static int DebugCursor => _cursor;

        /// <summary>What the context watcher would say now. See <see cref="DebugScreen"/>.</summary>
        internal static string DebugContext()
        {
            try
            {
                return ReadContext();
            }
            catch (Exception ex)
            {
                return "<failed: " + ex.GetType().Name + ">";
            }
        }

        /// <summary>What the book page would say now. See <see cref="DebugScreen"/>.</summary>
        internal static string DebugBookPage()
        {
            try
            {
                return BookPage();
            }
            catch (Exception ex)
            {
                return "<failed: " + ex.GetType().Name + ">";
            }
        }

        /// <summary>How many items this screen offers now. See <see cref="DebugScreen"/>.</summary>
        internal static int DebugItemCount()
        {
            try
            {
                if (_screen == null)
                    return 0;

                return ReadItems(_screen, null).Count;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>A player-facing name for a screen.</summary>
        private static string Describe(string screen)
        {
            switch (screen)
            {
                case ScreenAchievements: return Strings.Get("screen.achievements");
                case ScreenCalibrate: return Strings.Get("calibrate.title");
                case ScreenLeaderboard: return Strings.Get("screen.leaderboard");
                case ScreenTrackSelect: return Strings.Get("screen.trackSelect");
                case ScreenCredits: return Strings.Get("screen.credits");
                default: return screen;
            }
        }

        private const string ScreenAchievements = "achievements";
        private const string ScreenLeaderboard = "leaderboard";
        private const string ScreenTrackSelect = "trackselect";
        private const string ScreenCredits = "credits";
        private const string ScreenCalibrate = "calibrate";

        /// <summary>
        /// Which self-navigated screen is showing, or null when none is, together
        /// with the panel instance that is actually on screen.
        ///
        /// The instance is handed back rather than looked up again by each reader:
        /// this build keeps several CombinedLeaderboardPanel objects alive at once
        /// (proven in the dump: MainMenuLeaderboardPanel and GameUIManager each
        /// hold one), so "find one and hope" can decide the screen from the visible
        /// panel and then read the rows of a hidden one.
        /// </summary>
        private static string DetectScreen(out Panel panel)
        {
            panel = null;

            // THE LATENCY WINDOW GOES FIRST. It opens on top of the audio settings
            // menu, and while it is up the game takes every press for its own
            // calibration (CalibratePanel subscribes OnSubmit and OnInput to the
            // player's input actions), so no other screen can be the right answer.
            //
            // It is also the screen the player reported as completely silent on
            // 2026-10-01: launch 7 shows MenuReader sitting at selected=NULL from
            // 11:51:32.713 to 11:51:54.964, nineteen seconds with nothing spoken,
            // because the game clears the UI selection when the panel takes over and
            // nothing here was claiming the screen.
            CalibratePanel calibrate = FindShowing<CalibratePanel>();
            if (calibrate != null)
            {
                panel = calibrate;
                return ScreenCalibrate;
            }

            // The achievements book takes priority: it can be open above a menu.
            AchievementsPanel book = FindShowing<AchievementsPanel>();
            if (book != null)
            {
                panel = book;
                return ScreenAchievements;
            }

            CombinedLeaderboardPanel board = FindShowing<CombinedLeaderboardPanel>();
            if (board != null)
            {
                panel = board;

                // One class, two screens. Its own Mode property says which: the
                // MainMenu form is the leaderboard the player opens from the menu,
                // and the Game form is the pre-game screen where the track and the
                // device are chosen.
                try
                {
                    return board.Mode == CombinedLeaderboardMode.Game
                        ? ScreenTrackSelect
                        : ScreenLeaderboard;
                }
                catch
                {
                    return ScreenLeaderboard;
                }
            }

            // Credits come last of the four. The panel that owns CreditsContent is
            // a plain Panel (CreditsContent holds a parentPanel field), so it has no
            // class of its own to search for; the content component is found instead.
            // No panel is handed back: the credits reader finds its own content, and
            // returning the wrong Panel type here would send BoardOf hunting for a
            // leaderboard that is not on screen.
            if (FindShowingCredits() != null)
                return ScreenCredits;

            return null;
        }

        /// <summary>
        /// The CreditsContent that is on screen, or null.
        ///
        /// HYPOTHESIS, to be confirmed by the next in-game test: the credits were
        /// never opened during launch 5, so this build's credits hierarchy is not in
        /// any dump. What IS proven from the decompiled source is the class itself:
        /// CreditsContent builds its entries at runtime from a headingPrefab, a
        /// namePrefab and a list of CreditsSection (each a localised Title plus a
        /// list of names), and every one of those fields is private. Private fields
        /// are not reachable through the generated proxy, so the text has to be read
        /// from the live objects it created rather than from the component's data.
        /// </summary>
        private static CreditsContent FindShowingCredits()
        {
            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<CreditsContent>());

                if (found == null)
                    return null;

                for (int i = 0; i < found.Length; i++)
                {
                    CreditsContent c = found[i]?.TryCast<CreditsContent>();
                    if (c == null || !Ui.Alive(c) || !Ui.Alive(c.gameObject))
                        continue;

                    if (!c.gameObject.activeInHierarchy)
                        continue;

                    if (!Ui.Visible(c.gameObject))
                        continue;

                    return c;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Whether a panel is genuinely on screen and settled.
        /// </summary>
        private static bool PanelShowing(Panel panel)
        {
            try
            {
                if (!Ui.Alive(panel) || !Ui.Alive(panel.gameObject))
                    return false;

                if (!panel.gameObject.activeInHierarchy)
                    return false;

                // A panel on its way out is not a panel the player is using.
                if (panel.IsTransitioningHide)
                    return false;

                // Faded to nothing by a CanvasGroup is also not showing. This is
                // the check that tells an open panel from a closed one when
                // IsVisible disagrees, and the game's own Panel fades with a
                // CanvasGroup, so it is the right question to ask.
                if (!Ui.Visible(panel.gameObject))
                    return false;

                return panel.IsVisible;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds the panel of this type that is actually showing, out of however
        /// many instances the scene holds.
        ///
        /// Non-generic FindObjectsOfType with Il2CppType: the generic form can
        /// fail silently on IL2CPP because the instantiation was never compiled
        /// into the game.
        /// </summary>
        private static T FindShowing<T>() where T : Panel
        {
            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<T>());

                if (found == null)
                    return null;

                for (int i = 0; i < found.Length; i++)
                {
                    T candidate = found[i]?.TryCast<T>();
                    if (candidate == null || !Ui.Alive(candidate))
                        continue;

                    if (PanelShowing(candidate))
                        return candidate;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The current items on a screen, as plain strings, read fresh.
        ///
        /// A null panel means "find the showing one again", which is what the key
        /// handlers pass: they run on later frames than the detection did, so the
        /// panel must be re-found rather than remembered across them.
        /// </summary>
        private static List<string> CaptureItems(Panel panel)
        {
            _leaderboardWindow = new LeaderboardWindow();
            return ReadItems(_screen, panel, _leaderboardWindow);
        }

        private static List<string> ReadItems(string screen, Panel panel,
            LeaderboardWindow window = null)
        {
            switch (screen)
            {
                case ScreenAchievements:
                    return ReadAchievements();

                case ScreenCalibrate:
                    return ReadCalibrate(CalibrateOf(panel));

                case ScreenLeaderboard:
                    return ReadLeaderboard(BoardOf(panel), window);

                case ScreenTrackSelect:
                    return ReadTrackSelect(BoardOf(panel), window);

                case ScreenCredits:
                    return ReadCredits();

                default:
                    return new List<string>();
            }
        }

        /// <summary>
        /// The credits, as the lines the game actually drew, in the order they
        /// appear on screen.
        ///
        /// WHY READ THE OBJECTS AND NOT THE DATA. CreditsContent keeps its sections
        /// in a private field the proxy cannot reach, and its titles are
        /// LocalizedString values that are only resolved once the entry is built.
        /// The objects it spawned carry the finished, localised text, so they are
        /// both reachable and correct.
        ///
        /// WHY THE CREDITS ARE A LIST AND NOT ONE SENTENCE. The credits auto-scroll
        /// (enableAutoScroll, scrollSpeed), which a player who cannot see them
        /// cannot follow, and reading the whole roll as a single utterance would
        /// give them no way to pause or go back. As a list, the same keys that work
        /// everywhere else in the mod step through it at the player's own pace, and
        /// the scroll no longer matters.
        ///
        /// Blank entries are dropped: the roll is padded with spacerPrefab objects
        /// that carry no text, and a cursor that stopped on those would feel broken.
        /// </summary>
        private static List<string> ReadCredits()
        {
            List<string> result = new List<string>();

            try
            {
                CreditsContent credits = FindShowingCredits();
                if (credits == null)
                    return result;

                Il2CppReferenceArray<Component> texts = credits.GetComponentsInChildren(
                    Il2CppType.Of<TMP_Text>(), true);

                if (texts == null)
                    return result;

                List<TMP_Text> live = new List<TMP_Text>();

                for (int i = 0; i < texts.Length; i++)
                {
                    TMP_Text t = texts[i]?.TryCast<TMP_Text>();
                    if (t == null || !Ui.Alive(t) || !Ui.Alive(t.gameObject))
                        continue;

                    if (!t.gameObject.activeInHierarchy)
                        continue;

                    live.Add(t);
                }

                // Reading order, not engine order: the roll is one column, so the
                // line drawn highest on screen is the first one the player wants.
                // GetComponentsInChildren does follow the hierarchy, but the entries
                // are instantiated at runtime into a container, and nothing promises
                // that creation order matches the laid-out order.
                SortTopToBottom(live);

                for (int i = 0; i < live.Count; i++)
                {
                    string line = LabelText.Clean(Tmp(live[i]));
                    if (string.IsNullOrEmpty(line))
                        continue;

                    result.Add(line);
                }
            }
            catch (Exception ex)
            {
                Log.Line("screen", "credits read failed: " + ex.GetType().Name
                    + " | " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Puts text objects into the order they are drawn down the screen.
        ///
        /// Screen position is used rather than hierarchy order because these objects
        /// are spawned from prefabs into a scrolling container; position is what the
        /// player is actually being told about. Higher on screen means a LARGER y in
        /// Unity's world space, hence the reversed comparison.
        /// </summary>
        private static void SortTopToBottom(List<TMP_Text> texts)
        {
            try
            {
                texts.Sort((a, b) =>
                {
                    float ay = a.transform.position.y;
                    float by = b.transform.position.y;

                    // Descending y: top of the screen first.
                    return by.CompareTo(ay);
                });
            }
            catch
            {
                // An unsortable list is still readable, just not necessarily in
                // reading order. Better than no credits at all.
            }
        }

        /// <summary>
        /// The leaderboard panel to read: the one detection found, or the showing
        /// one re-found now.
        ///
        /// TryCast, never a C# cast or "as": on a proxy object those go through the
        /// managed type of the reference, not the real IL2CPP type.
        /// </summary>
        private static CombinedLeaderboardPanel BoardOf(Panel panel)
        {
            try
            {
                if (Ui.Alive(panel))
                {
                    CombinedLeaderboardPanel board = panel.TryCast<CombinedLeaderboardPanel>();
                    if (board != null && Ui.Alive(board))
                        return board;
                }
            }
            catch
            {
                // Fall through to a fresh search.
            }

            return FindShowing<CombinedLeaderboardPanel>();
        }

        /// <summary>
        /// <summary>
        /// The achievements on the two pages the book currently has open.
        ///
        /// WHY THE OPEN SPREAD AND NOT THE WHOLE BOOK. The book holds four spreads,
        /// one per song, and every page keeps its entries loaded whether or not it is
        /// the page on show. A search of the whole scene therefore finds all four
        /// songs' entries at once and the player walks a list that does not match the
        /// book in front of them. Scoping to the open spread also gives the entries a
        /// heading the player can trust: the song whose pages these are.
        ///
        /// HOW THE SPREAD IS FOUND. BookController.GetPageView resolves a page number
        /// to the page view registered for it, which is the game's own lookup rather
        /// than our guess, and the book reports which two page numbers are open. Both
        /// views are StickerPageView on this build, including the text-bearing left
        /// page, and each carries the song Group it belongs to.
        ///
        /// WHY IT WAITS FOR THE BOOK TO SETTLE. During a page turn the game activates
        /// the incoming pages before it deactivates the departing ones, so for a few
        /// frames both the old and new entries are live and a list built then is a
        /// mixture of two songs. Entries are collected only when the book is open at
        /// a spread and is not flipping, turning, dragging or changing state.
        ///
        /// Sources: evidence/launch9/source-review/findings.md, section
        /// "Proven page, group and container mapping" and "Recommended settled-spread
        /// reader".
        /// </summary>
        private static List<string> ReadAchievements()
        {
            List<string> result = new List<string>();

            try
            {
                BookController controller = FindBookController();
                if (controller == null)
                    return result;

                Il2Cppecho17.EndlessBook.EndlessBook book = controller.Book;
                if (book == null || !Ui.Alive(book))
                    return result;

                if (!SpreadSettled(controller, book))
                    return result;

                // Both open page views, resolved through the game's own lookup. A
                // closed or out-of-range side simply yields nothing.
                List<AchievementListItem> items = new List<AchievementListItem>();
                string song = null;
                string viewNames = "";

                int[] pages = { book.CurrentLeftPageNumber, book.CurrentRightPageNumber };
                for (int p = 0; p < pages.Length; p++)
                {
                    PageView view = PageViewAt(controller, book, pages[p]);
                    if (view == null)
                        continue;

                    if (song == null)
                        song = SpreadGroup(view);

                    viewNames += " " + Ui.SafeName(view.gameObject);
                    CollectItemsUnder(view.gameObject, items);
                }

                // The one line that makes this testable in a single launch: which
                // spread was read, which views it resolved to, which song, and how
                // many entries came back. Without it a wrong list is
                // indistinguishable from a right one in the log, and the player
                // would pay for another launch to find out.
                Log.Line("screen", "spread | left=" + book.CurrentLeftPageNumber
                    + " right=" + book.CurrentRightPageNumber
                    + " last=" + book.LastPageNumber
                    + " | views=" + (viewNames.Length == 0 ? " <none>" : viewNames)
                    + " | song=" + (song ?? "<unnamed>")
                    + " | entries=" + items.Count);

                if (items.Count == 0)
                    return result;

                // Order is the page hierarchy's own order, so "3 of 8" means the same
                // entry on every visit. FindObjectsOfType and child walks promise
                // nothing on their own.
                SortByHierarchy(items);

                // Only the device split (Classic, Extreme) is spoken per entry, and
                // only when it changes, so a walk down a page says "Classic" once
                // before its four entries instead of prefixing all eight lines.
                //
                // The SONG is deliberately NOT repeated here. It is the spread's
                // context: ReadContext reports it, which means it is spoken once when
                // the player opens the book and once more each time a flip lands on a
                // different song, and the repeat key says it too. Adding it to the
                // first entry as well would say it twice in the same breath.
                string spokenGroup = null;

                for (int i = 0; i < items.Count; i++)
                {
                    string sub = GroupOf(items[i]);

                    string heading = null;
                    if (!string.IsNullOrEmpty(sub)
                        && !Announcement.SameWords(sub, spokenGroup))
                    {
                        heading = sub;
                        spokenGroup = sub;
                    }

                    string line = AchievementLine(items[i], heading);
                    if (!string.IsNullOrEmpty(line))
                        result.Add(line);
                }
            }
            catch (Exception ex)
            {
                Log.Line("screen", "achievements read failed: " + ex.GetType().Name);
            }

            return result;
        }

        /// <summary>
        /// The song whose pages the book currently has open, as words, or null while
        /// the book is shut, mid-turn or showing a song we cannot name.
        ///
        /// Returning null during a turn is deliberate and is what keeps the mod quiet
        /// through the animation: the context watcher treats an unreadable value as
        /// "no change", so the player hears the new song once, when the pages settle,
        /// instead of hearing each intermediate state.
        /// </summary>
        private static string SpreadSong()
        {
            try
            {
                BookController controller = FindBookController();
                if (controller == null)
                    return null;

                Il2Cppecho17.EndlessBook.EndlessBook book = controller.Book;
                if (book == null || !Ui.Alive(book))
                    return null;

                if (!SpreadSettled(controller, book))
                    return null;

                int[] pages = { book.CurrentLeftPageNumber, book.CurrentRightPageNumber };
                for (int p = 0; p < pages.Length; p++)
                {
                    PageView view = PageViewAt(controller, book, pages[p]);
                    if (view == null)
                        continue;

                    string song = SpreadGroup(view);
                    if (!string.IsNullOrEmpty(song))
                        return song;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Line("screen", "spread song read failed: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// Whether the book is resting on an open spread, rather than mid-turn.
        /// Reading during a turn mixes the departing page's entries with the
        /// arriving one's.
        /// </summary>
        private static bool SpreadSettled(BookController controller,
            Il2Cppecho17.EndlessBook.EndlessBook book)
        {
            try
            {
                if (book.CurrentState != Il2Cppecho17.EndlessBook.EndlessBook.StateEnum.OpenMiddle)
                    return false;

                if (book.IsTurningPages || book.IsDraggingPage)
                    return false;

                return !controller.IsFlipping;
            }
            catch (Exception ex)
            {
                Log.Line("screen", "book settle check failed: " + ex.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// The page view the game has registered for one page number, or null.
        /// Uses BookController.GetPageView, which matches on the view's name rather
        /// than indexing the array, so it is the game's own mapping. Page numbers
        /// outside the book, and the zero a closed side reports, yield null.
        /// </summary>
        private static PageView PageViewAt(BookController controller,
            Il2Cppecho17.EndlessBook.EndlessBook book, int page)
        {
            try
            {
                if (page < 1 || page > book.LastPageNumber)
                    return null;

                PageView view = controller.GetPageView(page);
                if (view == null || !Ui.Alive(view) || !Ui.Alive(view.gameObject))
                    return null;

                return view;
            }
            catch (Exception ex)
            {
                Log.Line("screen", "page view lookup failed: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// The song this spread belongs to, as words: "Shapes", "Space", "City" or
        /// "Office".
        ///
        /// Read from StickerPageView.Group, the song identity the game itself stores
        /// on the page. The heading the sighted player sees is picture artwork with no
        /// text, so the words come from the same checked mapping the start screen
        /// uses; an unrecognised group is left unspoken rather than read out as an
        /// internal name.
        /// </summary>
        private static string SpreadGroup(PageView view)
        {
            try
            {
                StickerPageView sticker = view.TryCast<StickerPageView>();
                if (sticker == null || !Ui.Alive(sticker))
                    return null;

                return TrackChoiceReader.SongWords(sticker.Group);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Every achievement entry below one page view, in hierarchy order.
        ///
        /// Scoped to the page's own subtree: the entries are reparented into a
        /// container below the page view when the book is built, so this finds the
        /// ones belonging to this page and no others. Inactive entries are skipped,
        /// which drops the template rows the game keeps alongside the real ones.
        /// </summary>
        private static void CollectItemsUnder(GameObject root, List<AchievementListItem> items)
        {
            try
            {
                if (!Ui.Alive(root))
                    return;

                Il2CppReferenceArray<Component> found =
                    root.GetComponentsInChildren(Il2CppType.Of<AchievementListItem>(), true);

                if (found == null)
                    return;

                for (int i = 0; i < found.Length; i++)
                {
                    AchievementListItem item = found[i]?.TryCast<AchievementListItem>();
                    if (item == null || !Ui.Alive(item) || !Ui.Alive(item.gameObject))
                        continue;

                    if (!item.gameObject.activeInHierarchy)
                        continue;

                    items.Add(item);
                }
            }
            catch (Exception ex)
            {
                Log.Line("screen", "page items read failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// Which device an achievement belongs to: "Classic" or "Extreme".
        ///
        /// Read from BopItAchievement.SubGroup, which is the Classic/Extreme split.
        /// The separate Group field is the song (SHAPES, SPACE, CITY, OFFICE) and is
        /// handled by the spread heading instead, so it is not repeated per entry;
        /// it stays as the fallback here only in case SubGroup is empty.
        /// </summary>
        private static string GroupOf(AchievementListItem item)
        {
            try
            {
                BopItAchievement data = item.Achievement;
                if (data == null)
                    return null;

                string sub = LabelText.Clean(data.SubGroup);
                if (string.IsNullOrEmpty(sub))
                    return null;

                return Announcement.Sentence(sub);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// One achievement as words: an optional group heading, its name, and its
        /// locked state.
        ///
        /// The composition, including dropping the duplicated name, lives in
        /// <see cref="Announcement.Achievement"/>, which the desktop tests cover.
        /// </summary>
        private static string AchievementLine(AchievementListItem item, string heading)
        {
            try
            {
                string title = null;
                string state = null;

                BopItAchievement data = item.Achievement;
                if (data != null)
                {
                    title = LabelText.Clean(data.Title);

                    // Read the flag now, not when the list was found.
                    state = data.IsUnlocked
                        ? Strings.Get("achievement.unlocked")
                        : Strings.Get("achievement.locked");
                }

                // The item's own Description is the text actually drawn on the
                // page, already localised by the game.
                string description = LabelText.Clean(item.Description);

                return Announcement.Achievement(heading, title, description, state);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The leaderboard rows of one panel, from its live LeaderboardLineItem
        /// components, in the order they are drawn.
        ///
        /// Scoped to the panel that is showing, because a second, hidden
        /// CombinedLeaderboardPanel elsewhere in the scene holds rows of its own,
        /// and a scene-wide search would interleave two leaderboards into one list
        /// of scores.
        ///
        /// Each row exposes Rank plus its RankText, AliasText and ScoreText
        /// components. The TMP components are read rather than the numeric Rank
        /// alone, because the game writes the display form (and the sanitised
        /// player alias) into them.
        /// </summary>
        private static List<string> ReadLeaderboard(CombinedLeaderboardPanel panel,
            LeaderboardWindow window = null)
        {
            List<string> result = new List<string>();

            try
            {
                List<LeaderboardLineItem> rows = RowsOf(panel);

                for (int i = 0; i < rows.Count; i++)
                {
                    string line = LeaderboardLine(rows[i]);
                    if (!string.IsNullOrEmpty(line))
                    {
                        result.Add(line);
                        window?.Add(Tmp(rows[i].RankText), rows[i].Rank,
                            rows[i].ItemType == LeaderboardItemType.Me);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Line("screen", "leaderboard read failed: " + ex.GetType().Name);
            }

            return result;
        }

        /// <summary>
        /// The visible rows under a panel, in hierarchy order.
        ///
        /// GetComponentsInChildren returns depth-first hierarchy order, which is
        /// the order the rows are laid out in, so the scores read top to bottom.
        /// If that call is unavailable on this build the search falls back to the
        /// scene and sorts by hierarchy position, which is still better than
        /// having no leaderboard at all.
        /// </summary>
        private static List<LeaderboardLineItem> RowsOf(CombinedLeaderboardPanel panel)
        {
            List<LeaderboardLineItem> rows = new List<LeaderboardLineItem>();

            if (Ui.Alive(panel))
            {
                try
                {
                    Il2CppReferenceArray<Component> found = panel.GetComponentsInChildren(
                        Il2CppType.Of<LeaderboardLineItem>(), true);

                    if (found != null)
                    {
                        for (int i = 0; i < found.Length; i++)
                        {
                            LeaderboardLineItem row = found[i]?.TryCast<LeaderboardLineItem>();
                            if (Usable(row))
                                rows.Add(row);
                        }
                    }

                    return rows;
                }
                catch (Exception ex)
                {
                    Log.Line("screen", "rows under panel failed, falling back: "
                        + ex.GetType().Name);
                }
            }

            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<LeaderboardLineItem>());

                if (found == null)
                    return rows;

                for (int i = 0; i < found.Length; i++)
                {
                    LeaderboardLineItem row = found[i]?.TryCast<LeaderboardLineItem>();
                    if (Usable(row))
                        rows.Add(row);
                }

                SortByHierarchy(rows);
            }
            catch (Exception ex)
            {
                Log.Line("screen", "rows scene search failed: " + ex.GetType().Name);
            }

            return rows;
        }

        /// <summary>Whether a row component is alive and actually being drawn.</summary>
        private static bool Usable(Component c)
        {
            try
            {
                return c != null && Ui.Alive(c) && Ui.Alive(c.gameObject)
                    && c.gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>One leaderboard row as words: rank, name, score.</summary>
        private static string LeaderboardLine(LeaderboardLineItem row)
        {
            try
            {
                string rank = Tmp(row.RankText);
                string alias = Tmp(row.AliasText);
                string score = Tmp(row.ScoreText);

                // Fall back to the numeric field when the rank label is a graphic.
                if (string.IsNullOrEmpty(rank))
                {
                    int n = row.Rank;
                    if (n > 0)
                        rank = n.ToString(CultureInfo.InvariantCulture);
                }

                List<string> parts = new List<string>();

                if (!string.IsNullOrEmpty(rank))
                    parts.Add(LeaderboardWindow.RankWords(rank, row.Rank));
                if (!string.IsNullOrEmpty(alias))
                    parts.Add(alias);
                if (!string.IsNullOrEmpty(score))
                {
                    // The score arrives as the game's own formatted TEXT, so it is
                    // put into the phrase as-is rather than parsed back into a
                    // number: re-parsing would lose the game's thousands separators
                    // and its localised digits.
                    parts.Add(Strings.Get("score.points.many", score));
                }

                // The row type says whether this line is the player's own score or
                // the top one. Worth speaking: the game shows it as a colour, which
                // is exactly the cue the player cannot get any other way.
                string note = RowNote(row);
                if (note != null)
                    parts.Add(note);

                return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The spoken form of a row's type, or null for an ordinary row.
        /// LeaderboardItemType is Normal, First, Me or MostRecent in this build.
        /// </summary>
        private static string RowNote(LeaderboardLineItem row)
        {
            try
            {
                switch (row.ItemType)
                {
                    case LeaderboardItemType.Me: return Strings.Get("leaderboard.you");
                    case LeaderboardItemType.First: return Strings.Get("leaderboard.topScore");
                    case LeaderboardItemType.MostRecent: return Strings.Get("leaderboard.mostRecent");
                    default: return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The track/device screen. This is CombinedLeaderboardPanel in its Game
        /// mode, so the rows are the same leaderboard rows, but what the player
        /// actually changes here is the track and the device.
        ///
        /// The chosen track and device are announced first, then the scores, so
        /// entering the screen answers "what am I about to play" rather than
        /// reading a score table at someone trying to pick a song.
        /// </summary>
        private static List<string> ReadTrackSelect(CombinedLeaderboardPanel panel,
            LeaderboardWindow window = null)
        {
            List<string> result = new List<string>();

            string track = CurrentTrackName(panel);
            result.Add(Strings.Get("track.song.spoken",
                string.IsNullOrEmpty(track) ? Strings.Get("track.unknown") : track));
            window?.AddContextLine();

            string device = CurrentDeviceName();
            if (!string.IsNullOrEmpty(device))
            {
                result.Add(Strings.Get("track.device.spoken", device));
                window?.AddContextLine();
            }

            // The scores for this track follow, so the player can hear what they
            // are aiming at.
            result.AddRange(ReadLeaderboard(panel, window));

            return result;
        }

        /// <summary>
        /// The selected track's name.
        ///
        /// The panel keeps a TrackImages list of TitleBackgroundTable entries,
        /// each pairing a TrackName with the sprite shown as the title. The
        /// panel's TitleImage holds whichever sprite is currently displayed, so
        /// matching the two gives the track name as a word rather than a picture.
        /// </summary>
        private static string CurrentTrackName(CombinedLeaderboardPanel board)
        {
            try
            {
                if (board == null || !Ui.Alive(board))
                    return null;

                UnityEngine.UI.Image title = board.TitleImage;
                if (title == null || !Ui.Alive(title))
                    return null;

                Sprite shown = title.sprite;
                if (shown == null || !Ui.Alive(shown))
                    return null;

                Il2CppSystem.Collections.Generic.List<TitleBackgroundTable> tables =
                    board.TrackImages;

                if (tables == null)
                    return LabelText.Clean(shown.name);

                for (int i = 0; i < tables.Count; i++)
                {
                    TitleBackgroundTable entry = tables[i];
                    if (entry == null)
                        continue;

                    if (Ui.Alive(entry.Image) && entry.Image.GetInstanceID() == shown.GetInstanceID())
                    {
                        string name = LabelText.Clean(entry.TrackName);
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }
                }

                // No table matched: the sprite's own name is a usable fallback and
                // better than saying nothing.
                return LabelText.Clean(shown.name);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Which device is selected, Classic or Extreme.
        ///
        /// Read from a live DeviceContainer's CurrentDevice rather than inferred
        /// from the player's key presses: the game can refuse a device change
        /// mid-transition, and a mod that tracked presses would then report a
        /// device the player is not on.
        ///
        /// Several objects in this build hold a DeviceContainer (the panel itself,
        /// LeaderboardManager, Player, AchievementsTracker), so the container is
        /// chosen by having a live current device rather than by being first in
        /// whatever order the engine returns.
        /// </summary>
        private static string CurrentDeviceName(DeviceContainer preferred = null)
        {
            try
            {
                if (Ui.Alive(preferred))
                    return DeviceWordsOf(preferred.CurrentDevice);

                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<DeviceContainer>());

                if (found == null)
                    return null;

                for (int i = 0; i < found.Length; i++)
                {
                    DeviceContainer container = found[i]?.TryCast<DeviceContainer>();
                    if (container == null || !Ui.Alive(container))
                        continue;

                    Device device = container.CurrentDevice;
                    if (device == null || !Ui.Alive(device))
                        continue;

                    string name = DeviceWordsOf(device);
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Translate the selected device's typed value, not its object name.</summary>
        private static string DeviceWordsOf(Device device)
        {
            if (!Ui.Alive(device))
                return null;
            switch (device.Type)
            {
                case Il2CppBopIt.DeviceType.Classic: return Strings.Get("device.classic");
                case Il2CppBopIt.DeviceType.Extreme: return Strings.Get("device.extreme");
                default: return null;
            }
        }

        /// <summary>Sort by the chain of sibling indices so repeated reads keep their order.</summary>
        private static void SortByHierarchy<T>(List<T> items) where T : Component
        {
            if (items.Count < 2)
                return;

            try
            {
                Dictionary<int, string> keys = new Dictionary<int, string>();

                for (int i = 0; i < items.Count; i++)
                {
                    int id = items[i].GetInstanceID();
                    if (!keys.ContainsKey(id))
                        keys[id] = HierarchyKey(items[i]);
                }

                items.Sort((a, b) =>
                {
                    string ka = keys[a.GetInstanceID()];
                    string kb = keys[b.GetInstanceID()];
                    return string.CompareOrdinal(ka, kb);
                });
            }
            catch (Exception ex)
            {
                // An unsorted list still reads; a crash here would lose the screen.
                Log.Line("screen", "hierarchy sort failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// A sortable string for an object's position in the scene tree: the
        /// sibling indices from the root down, each zero-padded so plain string
        /// order matches numeric order (row 10 after row 9, not before it).
        /// </summary>
        private static string HierarchyKey(Component c)
        {
            try
            {
                List<int> chain = new List<int>();
                Transform t = c.transform;
                int guard = 0;

                while (Ui.Alive(t) && guard++ < 64)
                {
                    chain.Add(t.GetSiblingIndex());
                    t = t.parent;
                }

                StringBuilder sb = new StringBuilder(chain.Count * 6);
                for (int i = chain.Count - 1; i >= 0; i--)
                    sb.Append(chain[i].ToString("D5", CultureInfo.InvariantCulture)).Append('.');

                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// The CalibratePanel to read, preferring the one detection handed over.
        ///
        /// Same reason BoardOf exists: the key handlers run on later frames than the
        /// detection did, so a null means "find the showing one again".
        /// </summary>
        private static CalibratePanel CalibrateOf(Panel panel)
        {
            try
            {
                if (Ui.Alive(panel))
                {
                    CalibratePanel direct = panel.TryCast<CalibratePanel>();
                    if (direct != null && Ui.Alive(direct))
                        return direct;
                }
            }
            catch
            {
                // Fall through to the search.
            }

            return FindShowing<CalibratePanel>();
        }

        /// <summary>
        /// What the audio latency window is showing, as reviewable lines.
        ///
        /// WHY THIS SCREEN NEEDED ITS OWN READER. The player reported on 2026-10-01
        /// that "the audio latency adjustment window doesn't read at all", and the
        /// launch-7 log proves why: from 11:51:32.713 to 11:51:54.964 MenuReader
        /// logged selected=NULL every poll and spoke nothing for nineteen seconds.
        /// The panel takes the input itself (OnSubmit and OnInput are bound to the
        /// player's actions) and leaves no selected widget behind, so a reader that
        /// follows the UI selection has nothing to follow.
        ///
        /// WHAT THE PLAYER ACTUALLY NEEDS HERE. This window asks them to press the
        /// Bop button in time with a beat, and then reports the measured delay. So
        /// the lines are: what to do, what the countdown says, and the measured
        /// latency. The instruction comes first because it is the only one that is
        /// useful before the test starts.
        ///
        /// EVERY MEMBER WAS VERIFIED against this build's Assembly-CSharp proxy
        /// metadata on 2026-10-01. Cpp2IL shows visualiser, countdown, latency and
        /// the rest as PRIVATE fields in the decompiled source, which would normally
        /// put them out of reach; the generated proxy, however, exposes each one as a
        /// property (get_countdown, get_latency, get_State, get_latencyContainer),
        /// and that is what this code uses. EstimateLatency() is public in both.
        /// </summary>
        private static List<string> ReadCalibrate(CalibratePanel panel)
        {
            List<string> result = new List<string>();

            if (panel == null || !Ui.Alive(panel))
                return result;

            // The instruction, which depends on the stage. Spoken as a line of its
            // own so the player can come back to it with the review keys.
            string stage = CalibrateStatus(panel);
            if (!string.IsNullOrEmpty(stage))
                result.Add(stage);

            // The countdown, while there is one. This is the game telling the player
            // when the beats start, and it is a TMP they cannot see.
            try
            {
                string counting = Tmp(panel.countdown);
                if (!string.IsNullOrEmpty(counting))
                    result.Add(Strings.Get("calibrate.countdown", counting));
            }
            catch (Exception ex)
            {
                Log.Line("screen", "calibrate countdown failed: " + ex.GetType().Name);
            }

            // The result. The panel's own latency text is preferred because it is
            // already formatted and localised by the game; EstimateLatency() is the
            // fallback, and is only asked once the measurement is over, because
            // before that it reports a half-finished average.
            try
            {
                string shown = Tmp(panel.latency);

                if (string.IsNullOrEmpty(shown) && Finished(panel))
                {
                    float ms = panel.EstimateLatency();
                    shown = ms.ToString("F0", CultureInfo.InvariantCulture)
                        + " " + Strings.Get("calibrate.milliseconds");
                }

                if (!string.IsNullOrEmpty(shown))
                    result.Add(Strings.Get("calibrate.latency", shown));
            }
            catch (Exception ex)
            {
                Log.Line("screen", "calibrate latency failed: " + ex.GetType().Name);
            }

            return result;
        }

        /// <summary>
        /// The calibration stage as an instruction, or null.
        ///
        /// The five stages come from the game's own CalibrateState enum (Start,
        /// Warmup, Calibrate, Finished, Result). Each is turned into what the player
        /// should DO rather than the state's name: "Calibrate" means nothing, while
        /// "press the Bop button on every beat" is the whole point of the screen.
        /// </summary>
        private static string CalibrateStatus(CalibratePanel panel)
        {
            if (panel == null || !Ui.Alive(panel))
                return null;

            try
            {
                switch (panel.State)
                {
                    case CalibrateState.Start:
                        return Strings.Get("calibrate.start");

                    case CalibrateState.Warmup:
                        return Strings.Get("calibrate.warmup");

                    case CalibrateState.Calibrate:
                        return Strings.Get("calibrate.measuring");

                    case CalibrateState.Finished:
                        return Strings.Get("calibrate.finished");

                    case CalibrateState.Result:
                        // Deliberately NO figure in this line. The measured delay is
                        // its own reviewable line built in ReadCalibrate from
                        // panel.latency, which is the game's own formatted text; the
                        // stage line only says the measurement finished. The
                        // catalogue's calibrate.result carries a {0} so a translator
                        // can put the figure back into one sentence, and Strings.Get
                        // leaves an unused placeholder alone when no argument is
                        // passed, so "Your audio delay is {0} milliseconds" would
                        // read the braces aloud. Use the no-figure wording instead.
                        return Strings.Get("calibrate.finishedResult");

                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Log.Line("screen", "calibrate state failed: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>Whether the measurement is over, so a latency figure is real.</summary>
        private static bool Finished(CalibratePanel panel)
        {
            try
            {
                return panel.State == CalibrateState.Finished
                    || panel.State == CalibrateState.Result;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Reads a TMP component defensively, cleaned for speech.</summary>
        private static string Tmp(TMP_Text tmp)
        {
            try
            {
                if (tmp == null || !Ui.Alive(tmp))
                    return null;

                if (!Ui.Alive(tmp.gameObject) || !tmp.gameObject.activeInHierarchy)
                    return null;

                return LabelText.Clean(tmp.text);
            }
            catch
            {
                return null;
            }
        }
    }
}
