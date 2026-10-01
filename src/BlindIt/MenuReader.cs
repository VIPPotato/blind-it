using System;
using System.Globalization;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BlindIt
{
    /// <summary>
    /// Speaks the menu item that just became selected.
    ///
    /// Channel: polling, once per frame, from MelonLoader's OnUpdate. Polling was
    /// chosen over a Harmony hook on EventSystem.SetSelectedGameObject for two
    /// reasons recorded in the notebook: a UI hook can crash an IL2CPP game at the
    /// first menu move, and this game's Panel.SetSelection runs on a DOTween delay,
    /// so a hook would fire before the selection commits and announce the wrong
    /// item. One channel speaks, so nothing can double a line.
    ///
    /// Identity, not text, is what gets compared: two menu items can carry the same
    /// caption, and comparing text would silence a real move between them. The
    /// identity here is the selected GameObject's instance id, which is unique per
    /// object and stable while the object lives.
    /// </summary>
    internal static class MenuReader
    {
        /// <summary>Instance id of the item last spoken. 0 means "nothing yet".</summary>
        private static int _lastSpokenId;

        /// <summary>
        /// The value last spoken for that item, so a slider or toggle that changes
        /// without moving the selection can be noticed. Null when the item has no
        /// value.
        /// </summary>
        private static string _lastSpokenValue;

        /// <summary>
        /// The item we are waiting to be able to read. The game fills a TMP label
        /// through Unity Localization, which can land a frame or two after the
        /// selection moves, so an empty label is retried rather than announced as
        /// unlabeled straight away.
        /// </summary>
        private static int _pendingId;
        private static int _pendingFrames;

        /// <summary>
        /// How many frames to wait for a late label before giving up and speaking
        /// the marker. At 60 fps this is a tenth of a second: long enough for
        /// localisation to land, short enough that the player hears no lag.
        /// </summary>
        private const int MaxPendingFrames = 6;

        /// <summary>Frames the selection has been null, for the reset rule.</summary>
        private static int _nullFrames;

        /// <summary>
        /// Once the selection has been gone this long, forget what was last spoken.
        /// Without this, leaving a menu and coming back to the same item would stay
        /// silent. Two frames of tolerance, because the selection legitimately
        /// blinks to null for a frame during a panel transition.
        /// </summary>
        private const int NullFramesBeforeReset = 2;

        /// <summary>
        /// How far a navigation-link walk may go before giving up.
        ///
        /// Each step calls the game's own FindSelectableOn* code, and a malformed or
        /// cyclic set of links would otherwise walk forever inside the game's update
        /// loop, which is a freeze, not a glitch. The explicit cycle checks in
        /// NavigationPosition handle well-formed wrapping menus; this bound is the
        /// backstop for the malformed ones. No menu in this game is anywhere near 64
        /// items long (the longest, settings, has nine).
        /// </summary>
        private const int MaxNavigationWalk = 64;

        /// <summary>Whether the heartbeat proof line has been written.</summary>
        private static bool _heartbeatLogged;

        /// <summary>Frames seen, used only for the heartbeats.</summary>
        private static int _frames;

        /// <summary>
        /// Frames between the periodic state heartbeats. About 5 seconds at 60 fps.
        /// These exist to make ONE test launch able to tell apart the two ways this
        /// stage fails silently: the update loop not running at all, and the loop
        /// running fine while the EventSystem selection is always null.
        /// </summary>
        private const int StateHeartbeatFrames = 300;

        /// <summary>
        /// Called every frame. Must stay cheap: it reads one object and, only when
        /// that object changed, searches inside it for a label. It never scans the
        /// scene, which is what would make the game stutter.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                _frames++;

                // Heartbeat, once early: proves OnUpdate is being called at all.
                if (!_heartbeatLogged && _frames >= 120)
                {
                    _heartbeatLogged = true;
                    Log.Line("menu", "heartbeat | OnUpdate is running | frames="
                        + _frames.ToString(CultureInfo.InvariantCulture));
                }

                GameObject sel = Ui.Selected();

                // Periodic state heartbeat. Cheap (once per ~5 s) and it is what
                // makes an unsuccessful launch still diagnostic: it says whether
                // the loop ran, whether an EventSystem existed, and whether
                // anything was selected, without another test run.
                if (_frames % StateHeartbeatFrames == 0)
                {
                    Log.Line("menu", "state | frames="
                        + _frames.ToString(CultureInfo.InvariantCulture)
                        + " | selected=" + (sel == null ? "NULL" : Ui.SafeName(sel))
                        + " | visible=" + (sel == null ? "n/a" : Ui.Visible(sel).ToString())
                        + " | lastSpokenId="
                        + _lastSpokenId.ToString(CultureInfo.InvariantCulture));
                }

                if (sel == null)
                {
                    _nullFrames++;
                    if (_nullFrames == NullFramesBeforeReset)
                    {
                        // Screen changed or a popup closed. Forget the last item so
                        // re-entering on the same one speaks again.
                        if (_lastSpokenId != 0)
                            Log.Line("menu", "selection cleared | forgetting last id "
                                + _lastSpokenId.ToString(CultureInfo.InvariantCulture));

                        _lastSpokenId = 0;
                        _lastSpokenValue = null;
                        _pendingId = 0;
                        _pendingFrames = 0;
                    }
                    return;
                }

                _nullFrames = 0;

                // Read only something the player can actually see. A closed panel
                // stays in the scene holding its old selection, and speaking that
                // would announce a screen the player already left.
                if (!Ui.Visible(sel))
                    return;

                int id = sel.GetInstanceID();

                // SAME ITEM, NEW VALUE. Pressing left or right on a slider or a
                // toggle changes the value without moving the selection, so the
                // identity check below would treat it as "nothing happened" and the
                // player would get silence on every volume step (launch-5 defect).
                // The value is therefore compared as well, but ONLY on the item
                // already being spoken, so this costs one settings read per frame
                // and cannot re-announce a name.
                if (id == _lastSpokenId)
                {
                    SpeakValueChange(sel, id);
                    return;
                }

                // A new identity: this is a real move.
                if (id != _pendingId)
                {
                    _pendingId = id;
                    _pendingFrames = 0;

                    Log.Line("menu", "selection changed | id="
                        + id.ToString(CultureInfo.InvariantCulture)
                        + " | name=" + Ui.SafeName(sel)
                        + " | type=" + Ui.TypeName(sel));
                }

                _pendingFrames++;

                string text = Label.Read(sel);

                if (string.IsNullOrEmpty(text))
                {
                    if (_pendingFrames < MaxPendingFrames)
                        return; // The label may still be filled in; try next frame.

                    // Give up and make the gap audible, with the path in the log so
                    // it can be fixed without another launch.
                    Log.Line("menu", "NO LABEL after "
                        + _pendingFrames.ToString(CultureInfo.InvariantCulture)
                        + " frames | path=" + Ui.Path(sel));

                    text = Label.Unlabeled;
                }

                // A settings row carries its value separately from its label, so
                // "Music" becomes "Music, 8". Appended rather than spoken as a
                // second line, so one move stays one utterance.
                string value = SettingsValue.Read(sel);

                // A controls row carries its key the same way, except the key is
                // drawn as a sprite and has to come from the input system. Same
                // slot, because a row is one or the other, never both.
                if (string.IsNullOrEmpty(value))
                    value = ControlBinding.Read(sel);

                // Where the item sits among its siblings, which the player asked
                // for on 2026-09-30 in exactly this shape: at the END, after the
                // name, as in "Credits, 4 of 6".
                string position = MenuPosition(sel);

                string spoken = Announcement.Detail(text, value, position);

                // THE END-OF-ROUND SCORE RIDES ALONG. When a round has just ended the
                // score is waiting to be spoken, and the player asked for it to arrive
                // joined to this menu rather than on its own two seconds earlier:
                // "Final score, 150 points, New best score. REPLAY, 1 of 2." as one
                // utterance (2026-10-01).
                //
                // Claiming it empties it, so it is said exactly once, and the hold
                // protects the combined line from the next focus change.
                string score = ScoreHandoff.Claim();
                if (!string.IsNullOrEmpty(score))
                {
                    spoken = Announcement.Sentences(score, spoken);
                    Speech.Speak(spoken, true);
                    Speech.Hold(ScoreHandoff.HoldSeconds);

                    Log.Line("score", "spoken with the menu | text=" + spoken);
                }
                else
                {
                    Speech.Speak(spoken, true);
                }

                Log.Line("menu", "spoke | id=" + id.ToString(CultureInfo.InvariantCulture)
                    + " | text=" + spoken);

                _lastSpokenId = id;
                _lastSpokenValue = value;
                _pendingId = 0;
                _pendingFrames = 0;
            }
            catch (Exception ex)
            {
                // A single bad frame must never end the session. Logged once per
                // occurrence so a recurring fault is visible in the log.
                try
                {
                    Log.Line("menu", "tick failed: " + ex.GetType().Name + ": " + ex.Message);
                }
                catch
                {
                    // Nothing left to do.
                }
            }
        }

        /// <summary>
        /// Speaks the new value of the item already selected, when it changed.
        ///
        /// Only the VALUE is spoken, not the name: the player just pressed left or
        /// right on a row they are already on, so repeating "Music" before every
        /// number would slow the whole adjustment down. One utterance, one value.
        ///
        /// Silence is the normal outcome here. This runs every frame the selection
        /// does not move, so it must do the cheapest possible thing when nothing
        /// changed, and it must never speak on its own the first time it sees a
        /// value it has no memory of.
        /// </summary>
        private static void SpeakValueChange(GameObject sel, int id)
        {
            string value = SettingsValue.Read(sel);

            // A controls row's "value" is its bound key, so a rebind the player just
            // finished is announced here without the row's name being repeated. This
            // is the same slot settings values use; see the Tick comment.
            if (string.IsNullOrEmpty(value))
                value = ControlBinding.Read(sel);

            // Unchanged, or this item has no value at all: nothing to say.
            if (string.Equals(value, _lastSpokenValue, StringComparison.Ordinal))
                return;

            // A value that vanished is not a change worth speaking: it usually
            // means the row's focused and unfocused copies swapped mid-transition,
            // and announcing that would be noise. Remember it so the next real
            // change is still caught.
            if (string.IsNullOrEmpty(value))
            {
                _lastSpokenValue = value;
                return;
            }

            _lastSpokenValue = value;

            Speech.Speak(Announcement.Detail(value), true);
            Log.Line("menu", "value changed | id=" + id.ToString(CultureInfo.InvariantCulture)
                + " | value=" + value);
        }

        /// <summary>
        /// What this reader would say as the selected item's position, for the dump
        /// key only. Read-only, changes nothing: it exists so "the position was never
        /// spoken" can be told apart from "the position was computed as null" without
        /// costing the player another launch.
        /// </summary>
        internal static string DebugPosition(GameObject selected)
        {
            try
            {
                return MenuPosition(selected);
            }
            catch (Exception ex)
            {
                return "<failed: " + ex.GetType().Name + ">";
            }
        }

        /// <summary>
        /// Where the selected item sits among the items the player can move
        /// between, as "4 of 6", or null when that cannot be worked out honestly.
        ///
        /// TWO WAYS, IN ORDER, BECAUSE THIS GAME BUILDS MENUS TWO WAYS.
        ///
        /// 1. Unity's own navigation links (NavigationPosition). Launch 6 proved
        ///    the main menu needs this: each of its six buttons has its OWN parent
        ///    (Canvas/MainMenu/MainButtons/ButtonContainer/PlayMenuButton,
        ///    ButtonContainer (1)/QuitMenuButton, ButtonContainer (2)/
        ///    LeaderboardsMenuButton and so on), so the sibling count below is
        ///    always 1 and the position was never spoken at all.
        ///
        /// 2. The selectable siblings (SiblingPosition), which is right for the
        ///    settings panel: its nine rows DO share one parent, and it correctly
        ///    said "1 of 9" on launch 6. Kept as the fallback so a screen whose
        ///    navigation links are unset still gets a position.
        ///
        /// WHY NOT TRANSFORM ORDER FOR THE MAIN MENU. The ButtonContainer (N)
        /// suffixes are NOT the on-screen order. Launch 6's selection log gives the
        /// real order as PLAY, LEADERBOARDS, ACHIEVEMENTS, SETTINGS, CREDITS, QUIT,
        /// while the suffixes would give PLAY, QUIT, LEADERBOARDS, ACHIEVEMENTS,
        /// SETTINGS, CREDITS. Ordering by sibling index would therefore announce
        /// confident nonsense ("QUIT, 2 of 6"), which is worse than silence.
        ///
        /// Either way, a position is spoken only when it was actually established:
        /// null means "say the label with no position", never a guess.
        /// </summary>
        private static string MenuPosition(GameObject selected)
        {
            try
            {
                string byNavigation = NavigationPosition(selected);
                if (byNavigation != null)
                    return byNavigation;

                return SiblingPosition(selected);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The position found by walking Unity's own navigation links: up from the
        /// selected item to the top of the list, then down through the whole list,
        /// which is exactly the order the player's arrow keys follow.
        ///
        /// Selectable.FindSelectableOnUp() and FindSelectableOnDown() are both
        /// present in this build's UnityEngine.UI proxy and take no arguments
        /// (verified by reflection over
        /// MelonLoader\Il2CppAssemblies\UnityEngine.UI.dll). They honour the
        /// Selectable's navigation mode, so under Automatic navigation Unity works
        /// the neighbour out geometrically and under Explicit it returns the
        /// authored link. Both give the true spoken order.
        ///
        /// Returns null when this screen has no usable links (a lone button, or
        /// navigation set to None), so the caller can fall back to the sibling
        /// count rather than inventing a number.
        ///
        /// EVERY STEP IS GUARDED. The walk is bounded to MaxNavigationWalk, and a
        /// link that points at a destroyed or off-screen widget ends the walk: a
        /// cycle or a stale link must never hang the game's update loop.
        /// </summary>
        private static string NavigationPosition(GameObject selected)
        {
            try
            {
                if (!Ui.Alive(selected))
                    return null;

                UnityEngine.UI.Selectable start =
                    selected.GetComponent(Il2CppType.Of<UnityEngine.UI.Selectable>())
                        ?.TryCast<UnityEngine.UI.Selectable>();

                if (start == null || !Ui.Alive(start))
                    return null;

                // The traversal itself lives in NavigationOrder, which has no Unity
                // types in it and is desktop-tested: the off-by-ones and the cycle
                // handling are proved there instead of costing the player a launch.
                int index;
                int count;
                bool found = NavigationOrder.TryFind(
                    start,
                    SafeUp,
                    SafeDown,
                    SafeId,
                    UsableForOrder,
                    MaxNavigationWalk,
                    ScreenOrderKey,
                    out index,
                    out count);

                if (!found)
                    return null;

                return Announcement.Position(index, count);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// An identity for a Selectable, safe on a destroyed one. Zero is returned
        /// for an unreadable object, which makes it compare equal only to another
        /// unreadable one; the walk's usable check rejects those first.
        /// </summary>
        private static int SafeId(UnityEngine.UI.Selectable sel)
        {
            try
            {
                if (sel == null || !Ui.Alive(sel))
                    return 0;

                return sel.GetInstanceID();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Ranks a widget by where it sits on screen, so a WRAPPING menu can still be
        /// numbered. Smaller comes first, and the value is the negated world height,
        /// which puts the topmost widget at position one.
        ///
        /// WHY THIS IS NEEDED. The main menu wraps: launch 6's log shows QUIT's "down"
        /// selecting PLAY again. In a ring the navigation links say who the members
        /// are but not who is first, so something outside the links has to decide.
        /// Screen height is the honest answer, because it is the order a sighted
        /// player reads the menu in, and it matches the order the player hears when
        /// they hold the down arrow: PLAY, LEADERBOARDS, ACHIEVEMENTS, SETTINGS,
        /// CREDITS, QUIT.
        ///
        /// WHY NOT THE SIBLING INDEX. The main menu's ButtonContainer (N) suffixes are
        /// in a different order from the screen: they would rank QUIT second. Proven
        /// from launch 6's dump against its selection log.
        ///
        /// position.y is used rather than the RectTransform's anchored position because
        /// the buttons live under six different parents, so their local coordinates are
        /// not comparable; the world position is. NaN is returned when the widget
        /// cannot be read, and NavigationOrder then declines to number the ring rather
        /// than guessing.
        /// </summary>
        private static double ScreenOrderKey(UnityEngine.UI.Selectable sel)
        {
            try
            {
                if (sel == null || !Ui.Alive(sel))
                    return double.NaN;

                Transform t = sel.transform;
                if (!Ui.Alive(t))
                    return double.NaN;

                // Negated: Unity's y grows upward, and item one is the topmost.
                return -(double)t.position.y;
            }
            catch
            {
                return double.NaN;
            }
        }

        /// <summary>
        /// Whether a Selectable found through a navigation link may be counted:
        /// alive, on screen, interactable. Mirrors Ui.Navigable, which takes a
        /// GameObject, for an object we already hold as a Selectable.
        /// </summary>
        private static bool UsableForOrder(UnityEngine.UI.Selectable sel)
        {
            try
            {
                if (sel == null || !Ui.Alive(sel))
                    return false;

                GameObject go = sel.gameObject;
                if (!Ui.Alive(go) || !Ui.Visible(go))
                    return false;

                return sel.interactable && sel.enabled;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The item above, or null. Wrapped: this runs the game's own native code.</summary>
        private static UnityEngine.UI.Selectable SafeUp(UnityEngine.UI.Selectable sel)
        {
            try
            {
                UnityEngine.UI.Selectable up = sel.FindSelectableOnUp();
                return Ui.Alive(up) ? up : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The item below, or null. Wrapped for the same reason as SafeUp.</summary>
        private static UnityEngine.UI.Selectable SafeDown(UnityEngine.UI.Selectable sel)
        {
            try
            {
                UnityEngine.UI.Selectable down = sel.FindSelectableOnDown();
                return Ui.Alive(down) ? down : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Where the selected item sits among its selectable siblings, as "4 of 6",
        /// or null when that cannot be worked out honestly.
        ///
        /// HOW THE COUNT IS FOUND. Unity menus are often built as one parent holding
        /// a row of selectable children, so the siblings of the selected object are
        /// the list. This is the settings panel's shape, and it is why settings
        /// correctly said "1 of 9" on launch 6. Counted are only siblings that are
        /// themselves selectable and actually on screen, which matters because this
        /// game leaves widgets in place while hiding them: the settings panel keeps
        /// rows that do not apply to the current device. Counting those would tell
        /// the player "6 of 9" on a menu with six reachable items, and a wrong
        /// number is worse than none.
        ///
        /// Returns null rather than guessing when the item has no selectable
        /// siblings: a lone button is not a list, and "1 of 1" on every screen is
        /// noise the player would have to listen past on every move. The main menu
        /// hits exactly that case (one button per parent), which is what
        /// NavigationPosition is for.
        /// </summary>
        private static string SiblingPosition(GameObject selected)
        {
            try
            {
                if (!Ui.Alive(selected))
                    return null;

                Transform t = selected.transform;
                if (!Ui.Alive(t))
                    return null;

                Transform parent = t.parent;
                if (!Ui.Alive(parent))
                    return null;

                int count = 0;
                int index = -1;
                int selectedId = selected.GetInstanceID();

                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (!Ui.Alive(child))
                        continue;

                    GameObject go = child.gameObject;
                    if (!Ui.Alive(go))
                        continue;

                    bool isSelected = go.GetInstanceID() == selectedId;

                    // The selected item always counts. Asking whether it is
                    // navigable would be circular, and it is on screen by
                    // definition: the reader only speaks visible items.
                    if (!isSelected && !Ui.Navigable(go))
                        continue;

                    if (isSelected)
                        index = count;

                    count++;
                }

                // The selected item was not among the counted siblings, so the
                // count describes a different list than the one the player is in.
                if (index < 0)
                    return null;

                // A list of one is not a list.
                if (count < 2)
                    return null;

                return Announcement.Position(index, count);
            }
            catch
            {
                return null;
            }
        }
    }
}
