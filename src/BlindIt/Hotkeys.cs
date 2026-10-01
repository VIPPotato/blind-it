using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BlindIt
{
    /// <summary>
    /// The mod's own keys, read through one place.
    ///
    /// WHY ITS OWN KEYS RATHER THAN THE ARROWS. On the screens this mod has to
    /// navigate itself (achievements, leaderboard, track and device select) the
    /// game already uses the arrows for real actions: CombinedLeaderboardPanel
    /// binds them to OnChangeGroup, OnDateRangeChange, OnChangeMusicTrack and
    /// OnChangeDevice. Taking the arrows over would mean suppressing the game's
    /// own input path, and a mod that swallows a press the game acts on can
    /// leave the two disagreeing about what happened. Separate keys keep every
    /// native control working exactly as it does without the mod, which is the
    /// more reliable of the two options.
    ///
    /// WHICH KEYS. The player asked for the three navigation keys to sit together
    /// under one hand on the right of the keyboard, which the scattered F6/F7/F9
    /// did not:
    ///
    ///   [ = previous item     ] = next item     \ = repeat, with the screen name
    ///
    /// PROVEN FREE on this build, twice over. A scan of global-metadata.dat for
    /// every embedded binding path finds 56 of them, and not one is a bracket, a
    /// backslash, a quote or a semicolon; launch 5's live effectivePath dump agrees
    /// (the complete set of keys this game binds is a, d, i, k, l, o, p, r, s, w,
    /// x, space, enter, escape, backspace, shift, f11 and the four arrows). The
    /// game DOES bind pageUp, pageDown, home, end, leftCtrl, leftShift and leftAlt,
    /// so those are NOT free despite being the usual second choice.
    ///
    /// F8 stays as the developer dump: it is not a player key, and it is proven
    /// working (it wrote two dumps on launch 5). F10, F11 and F12 are deliberately
    /// left alone: F11 is the game's own fullscreen toggle, and the other two are
    /// commonly taken by the window manager, store overlays and screen-reader
    /// tooling.
    ///
    /// TWO INPUT ROUTES. The first test launch produced no dump at all from F8,
    /// so every read goes through both the new Input System and legacy Input, and
    /// the available routes are logged once. Whichever answers, wins.
    /// </summary>
    internal static class Hotkeys
    {
        // The spoken names below are used in every hint the mod speaks. They live
        // here so a key change can never leave the mod telling the player to press
        // a key that no longer does anything: the launch-5 build said "Press F9 to
        // read items", which this remap would have turned into a lie.

        /// <summary>Read the item the cursor is on again, with the screen name.</summary>
        internal const string Repeat = "backslash";

        /// <summary>Move the mod's cursor to the previous item.</summary>
        internal const string Previous = "left bracket";

        /// <summary>Write a full screen dump to a file for the developer.</summary>
        internal const string DumpScreen = "F8";

        /// <summary>Move the mod's cursor to the next item.</summary>
        internal const string Next = "right bracket";

        /// <summary>Spoken name of the arrow pair, for the help sentence.</summary>
        internal const string ReviewArrows = "down arrow";

        /// <summary>
        /// The help line naming the keys that read the list, matching what is
        /// actually live on this screen.
        ///
        /// <paramref name="arrowsToo"/> is true on a screen where the arrows have
        /// been proven inert (see <see cref="ReviewNextPressed"/>), so the player is
        /// told about the easier keys exactly where they work and never sent to a key
        /// that does nothing.
        /// </summary>
        internal static string ReviewHelp(bool arrowsToo = false)
        {
            return arrowsToo
                ? Strings.Get("review.help.arrows")
                : Strings.Get("review.help");
        }

        private static bool _diagnosed;
        private static bool _legacyBroken;

        /// <summary>True on the frame the repeat key (backslash) went down.</summary>
        internal static bool RepeatPressed()
        {
            return Pressed(k => k.backslashKey, KeyCode.Backslash);
        }

        /// <summary>True on the frame the previous key (left bracket) went down.</summary>
        internal static bool PreviousPressed()
        {
            return Pressed(k => k.leftBracketKey, KeyCode.LeftBracket);
        }

        /// <summary>
        /// Next item, counting the DOWN ARROW as well as the right bracket.
        ///
        /// WHY THE ARROWS ARE SAFE HERE, AND ONLY HERE. The player asked on
        /// 2026-10-01 for the arrows to review things, "but only in menus where up and
        /// down arrows don't move through lists". The binding dump from launch 5
        /// (logs/launch5-screentext-dump.txt, the live effectivePath list) says
        /// exactly where that is:
        ///
        ///   UI/Navigate        upArrow, downArrow, w, s, leftArrow, a, rightArrow, d
        ///   UI/FlipAchievementPages   leftArrow, rightArrow
        ///   Leaderboard/ChangeDevice  rightArrow
        ///   Gameplay/Twist, Gameplay/Pull   leftArrow, rightArrow
        ///
        /// So up and down reach ONE game action, UI/Navigate, which moves the focus
        /// between selectable widgets. On the screens this method is used for, the
        /// game holds NO selection at all, which is the very test ScreenReader uses to
        /// claim them: with nothing selected and no selectables on the panel, Navigate
        /// has nothing to move and the press is inert. That is why the caller must
        /// only use this on a fully claimed screen.
        ///
        /// LEFT AND RIGHT ARE NOT OFFERED, deliberately. They carry a real action on
        /// these very screens (flipping the achievement book, changing the device),
        /// and taking them would mean suppressing the game's own input path.
        ///
        /// The brackets keep working everywhere, so nothing the player already learned
        /// is taken away.
        /// </summary>
        internal static bool ReviewNextPressed()
        {
            return NextPressed() || Pressed(k => k.downArrowKey, KeyCode.DownArrow);
        }

        /// <summary>
        /// Previous item, counting the UP ARROW as well as the left bracket. See
        /// <see cref="ReviewNextPressed"/> for why the arrows are safe here.
        /// </summary>
        internal static bool ReviewPreviousPressed()
        {
            return PreviousPressed() || Pressed(k => k.upArrowKey, KeyCode.UpArrow);
        }

        /// <summary>True on the frame F8 went down.</summary>
        internal static bool DumpPressed()
        {
            return Pressed(k => k.f8Key, KeyCode.F8);
        }

        /// <summary>True on the frame the next key (right bracket) went down.</summary>
        internal static bool NextPressed()
        {
            return Pressed(k => k.rightBracketKey, KeyCode.RightBracket);
        }

        /// <summary>
        /// Resolves one key through whichever input route works.
        /// </summary>
        private static bool Pressed(Func<Keyboard, ButtonControl> pick, KeyCode legacyKey)
        {
            DiagnoseOnce();

            try
            {
                // Keyboard is an InputDevice, not a UnityEngine.Object, so the
                // destroyed-object check does not apply; null is the only guard.
                Keyboard kb = Keyboard.current;
                if (kb != null)
                {
                    ButtonControl button = pick(kb);
                    if (button != null && button.wasPressedThisFrame)
                        return true;
                }
            }
            catch
            {
                // Fall through to the legacy route.
            }

            if (_legacyBroken)
                return false;

            try
            {
                return LegacyGetKeyDown(legacyKey);
            }
            catch
            {
                _legacyBroken = true;
                return false;
            }
        }

        /// <summary>
        /// Logs which input routes exist, once. This is what makes a single launch
        /// able to explain a dead hotkey instead of leaving it a guess.
        ///
        /// Each navigation key is named individually: if a remap ever lands on a
        /// member this build's proxy assemblies do not carry, this line says WHICH
        /// one, instead of leaving a silent key to be diagnosed by another launch.
        /// </summary>
        private static void DiagnoseOnce()
        {
            if (_diagnosed)
                return;

            _diagnosed = true;

            string newRoute;
            try
            {
                Keyboard kb = Keyboard.current;

                if (kb == null)
                {
                    newRoute = "Keyboard.current=NULL";
                }
                else
                {
                    newRoute = "Keyboard.current=ok added=" + kb.added
                        + " backslash=" + Present(kb.backslashKey)
                        + " leftBracket=" + Present(kb.leftBracketKey)
                        + " rightBracket=" + Present(kb.rightBracketKey)
                        + " f8=" + Present(kb.f8Key);
                }
            }
            catch (Exception ex)
            {
                newRoute = "Keyboard.current THREW " + ex.GetType().Name;
            }

            string legacyRoute;
            try
            {
                bool ignored = LegacyGetKeyDown(KeyCode.Backslash);
                legacyRoute = "legacy Input=ok";
            }
            catch (Exception ex)
            {
                legacyRoute = "legacy Input THREW " + ex.GetType().Name;
                _legacyBroken = true;
            }

            Log.Line("keys", "routes | " + newRoute + " | " + legacyRoute);
        }

        /// <summary>Whether one key control resolved, safe on a build without it.</summary>
        private static string Present(ButtonControl control)
        {
            try
            {
                return control != null ? "ok" : "MISSING";
            }
            catch
            {
                return "THREW";
            }
        }

        /// <summary>
        /// The legacy input call, isolated and never inlined.
        ///
        /// Assembly resolution happens when a method is compiled, BEFORE any
        /// try/catch inside it is active, so a call into a module that might be
        /// absent must sit in its own method with callers wrapping the CALL.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool LegacyGetKeyDown(KeyCode key)
        {
            return Input.GetKeyDown(key);
        }
    }
}
