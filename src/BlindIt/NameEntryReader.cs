using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;

namespace BlindIt
{
    /// <summary>
    /// Speaks the screens where the player puts a name on a high score.
    ///
    /// WHY THESE NEED THEIR OWN READER. The player had never met these screens
    /// (asked 2026-09-30, answer: "I do not know, I have never seen it"), which is
    /// itself the problem: a score good enough to reach the leaderboard is rare,
    /// and if the mod is silent at that moment the player loses the entry with no
    /// idea why. The decompiled source proves all three exist in this build:
    ///
    ///  - PartyLeaderboardNameSelect: a NameSelectMode (Waiting, Switch, Input,
    ///    Complete) with a Label, a TMP_InputField and a cycling list of names.
    ///  - LeaderBoardNameInputPanel: an on-screen keyboard of KeyButton objects
    ///    writing into a playerName label, with maxNameLength and an enter button.
    ///  - LeaderboardNameSelectionPanel: a list of LeaderboardNameSelectEntry
    ///    objects, one per existing name, plus an "add new score" button.
    ///
    /// Two of the three change their TEXT without moving the selection: cycling
    /// through names on the party screen rewrites the same label, and every letter
    /// pressed on the keyboard rewrites the same playerName. A reader that follows
    /// the focused widget cannot see either, so this watches the text itself.
    ///
    /// The name-selection LIST is deliberately NOT handled here: its entries are
    /// real buttons the player can move between, so MenuReader already speaks them
    /// through the ordinary path.
    ///
    /// UNVERIFIED IN GAME. Everything here is read from the decompiled source and
    /// compiles against the real assemblies, but no launch has reached one of these
    /// screens yet, so this is marked HYPOTHESIS in the notebook until a test
    /// proves it.
    /// </summary>
    internal static class NameEntryReader
    {
        /// <summary>The name text last spoken, so only real changes are announced.</summary>
        private static string _lastName;

        /// <summary>
        /// Which name screen the remembered text belongs to. Without this, walking
        /// straight from the party select onto the keyboard would compare the
        /// keyboard's empty box against the name spoken on the previous screen, and
        /// the new screen would never announce itself.
        /// </summary>
        private static string _lastScreen;

        /// <summary>The mode last spoken on the party name screen.</summary>
        private static NameSelectMode _lastMode;

        /// <summary>Whether a name screen was showing on the previous tick.</summary>
        private static bool _wasShowing;

        /// <summary>Frames since the scene was last searched.</summary>
        private static int _sinceDetect;

        /// <summary>
        /// How often to look for these panels.
        ///
        /// Slower than the other readers on purpose: this is a rare screen, and the
        /// search runs on every frame of ordinary play where nothing else claimed
        /// the frame. Twenty frames is a third of a second, unnoticeable when a
        /// name screen does appear, and a twentieth of the cost the rest of the
        /// time. The game is played to a beat, so a stutter here costs the player
        /// the round.
        /// </summary>
        private const int DetectEveryFrames = 20;

        /// <summary>Names for the two screens whose text this reader follows.</summary>
        private const string PartyScreen = "party";
        private const string KeyboardScreen = "keyboard";

        /// <summary>
        /// One frame. Returns true when a name screen is showing and this reader
        /// spoke, so the caller can leave the other readers alone.
        ///
        /// Returning false while a name screen is up is deliberate and normal: the
        /// screens carry real buttons (enter, add new score), and MenuReader should
        /// keep announcing those. This reader only adds the part MenuReader cannot
        /// see, which is the text.
        /// </summary>
        internal static bool Tick()
        {
            try
            {
                _sinceDetect++;
                if (_sinceDetect < DetectEveryFrames && !_wasShowing)
                    return false;

                _sinceDetect = 0;

                if (SpeakPartyNameSelect())
                    return true;

                if (SpeakKeyboardEntry())
                    return true;

                if (_wasShowing)
                {
                    _wasShowing = false;
                    _lastName = null;
                    _lastScreen = null;
                    _lastMode = NameSelectMode.Waiting;
                    Log.Line("name", "name screen left");
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Line("name", "tick failed: " + ex.GetType().Name + " | " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The party name-select screen: says the mode when it changes and the name
        /// as the player cycles through the list.
        ///
        /// Mode and name are spoken together in ONE utterance when both change at
        /// once, which is what happens on entry. Two calls would cut the first off
        /// mid-word, the exact defect launch 5 showed on the leaderboard.
        /// </summary>
        private static bool SpeakPartyNameSelect()
        {
            PartyLeaderboardNameSelect select = FindLive<PartyLeaderboardNameSelect>();
            if (select == null)
                return false;

            _wasShowing = true;

            // Arrived from a different name screen: forget its text, or the name
            // spoken there would suppress this screen's first announcement.
            if (_lastScreen != PartyScreen)
            {
                _lastScreen = PartyScreen;
                _lastName = null;
                _lastMode = NameSelectMode.Waiting;
            }

            NameSelectMode mode = select.Mode;
            string name = CurrentPartyName(select);

            bool modeChanged = mode != _lastMode;
            bool nameChanged = !string.Equals(name, _lastName, StringComparison.Ordinal);

            if (!modeChanged && !nameChanged)
                return false;

            bool firstSight = _lastName == null && !modeChanged;

            _lastMode = mode;
            _lastName = name;

            // Nothing worth saying yet: the panel is up but has no name in it.
            if (firstSight && string.IsNullOrEmpty(name))
                return false;

            string spoken = Announcement.Sentences(
                modeChanged ? ModeWords(mode) : null,
                string.IsNullOrEmpty(name) ? null : name);

            if (string.IsNullOrEmpty(spoken))
                return false;

            Log.Line("name", "party select | mode=" + mode.ToString() + " | spoke=" + spoken);
            Speech.Speak(spoken, true);
            return true;
        }

        /// <summary>
        /// What each mode of the party name screen means for the player, in words
        /// that say what to DO. The enum names alone (Switch, Input) would not tell
        /// a player who cannot see the screen how to answer it.
        /// </summary>
        private static string ModeWords(NameSelectMode mode)
        {
            switch (mode)
            {
                case NameSelectMode.Waiting:
                    return Strings.Get("name.waiting");
                case NameSelectMode.Switch:
                    return Strings.Get("name.choose");
                case NameSelectMode.Input:
                    return Strings.Get("name.type");
                case NameSelectMode.Complete:
                    return Strings.Get("name.saved");
                default:
                    return null;
            }
        }

        /// <summary>
        /// The name currently shown on the party screen.
        ///
        /// The input field is preferred over the label: in Input mode the field
        /// holds what the player is typing, while the Label may still show the
        /// prompt. Both are public fields on the component, so both are reachable.
        /// </summary>
        private static string CurrentPartyName(PartyLeaderboardNameSelect select)
        {
            try
            {
                TMP_InputField input = select.NameInput;
                if (input != null && Ui.Alive(input) && Ui.Alive(input.gameObject)
                    && input.gameObject.activeInHierarchy)
                {
                    string typed = LabelText.Clean(input.text);
                    if (!string.IsNullOrEmpty(typed))
                        return typed;
                }
            }
            catch
            {
                // Fall through to the label.
            }

            try
            {
                TMP_Text label = select.Label;
                if (label != null && Ui.Alive(label))
                    return LabelText.Clean(label.text);
            }
            catch
            {
                // Nothing readable.
            }

            return null;
        }

        /// <summary>
        /// The on-screen keyboard panel: says the name so far each time it changes,
        /// so the player hears what a key press actually produced.
        ///
        /// Only the name is spoken, never a running commentary of the keys: the
        /// player knows which key they pressed, what they cannot check is the
        /// result, and this panel has a maxNameLength that silently refuses further
        /// letters. Speaking the whole name is what makes that refusal audible.
        /// </summary>
        private static bool SpeakKeyboardEntry()
        {
            LeaderBoardNameInputPanel panel = FindLivePanel<LeaderBoardNameInputPanel>();
            if (panel == null)
                return false;

            _wasShowing = true;

            if (_lastScreen != KeyboardScreen)
            {
                _lastScreen = KeyboardScreen;
                _lastName = null;
            }

            string name = KeyboardName(panel);

            if (string.Equals(name, _lastName, StringComparison.Ordinal))
                return false;

            bool firstSight = _lastName == null;
            _lastName = name;

            if (firstSight)
            {
                // Entering the screen: say where the player is and what is in the
                // box, as one utterance.
                string entry = Announcement.Sentences(
                    Strings.Get("name.enter"),
                    string.IsNullOrEmpty(name) ? "Empty" : name);

                Log.Line("name", "keyboard entered | spoke=" + entry);
                Speech.Speak(entry, true);
                return true;
            }

            // An emptied box must still say something, or deleting the last letter
            // is silent and the player cannot tell it worked.
            string spoken = string.IsNullOrEmpty(name) ? "Empty" : name;

            Log.Line("name", "keyboard name | " + spoken);
            Speech.Speak(spoken, true);
            return true;
        }

        /// <summary>
        /// The text in the keyboard panel's name label.
        ///
        /// playerName is a private serialised field, so it cannot be read through
        /// the proxy. The label is found among the panel's own TMP_Text children
        /// instead: it is the one that is NOT part of a KeyButton, since every key
        /// on the on-screen keyboard also carries a TextMeshProUGUI.
        /// </summary>
        private static string KeyboardName(LeaderBoardNameInputPanel panel)
        {
            try
            {
                Il2CppReferenceArray<Component> texts = panel.GetComponentsInChildren(
                    Il2CppType.Of<TMP_Text>(), true);

                if (texts == null)
                    return null;

                for (int i = 0; i < texts.Length; i++)
                {
                    TMP_Text t = texts[i]?.TryCast<TMP_Text>();
                    if (t == null || !Ui.Alive(t) || !Ui.Alive(t.gameObject))
                        continue;

                    if (!t.gameObject.activeInHierarchy)
                        continue;

                    // A key's own caption, not the name being typed.
                    if (InsideKeyButton(t))
                        continue;

                    string text = LabelText.Clean(t.text);
                    if (string.IsNullOrEmpty(text))
                        continue;

                    return text;
                }

                // Every text belonged to a key: the name box is genuinely empty.
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Line("name", "keyboard read failed: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// Whether this text is the caption of a key on the on-screen keyboard.
        /// Checked on the object and its parents, because KeyButton holds its
        /// buttonText as a child.
        /// </summary>
        private static bool InsideKeyButton(TMP_Text text)
        {
            try
            {
                Transform t = text.transform;
                int guard = 0;

                while (Ui.Alive(t) && guard++ < 8)
                {
                    Component key = t.GetComponent(Il2CppType.Of<KeyButton>());
                    if (key != null && Ui.Alive(key))
                        return true;

                    t = t.parent;
                }

                return false;
            }
            catch
            {
                // Unsure: treat it as a key, so a stray caption is never mistaken
                // for the player's name.
                return true;
            }
        }

        /// <summary>
        /// The live, showing instance of a MonoBehaviour type, or null.
        /// </summary>
        private static T FindLive<T>() where T : MonoBehaviour
        {
            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<T>());

                if (found == null)
                    return null;

                for (int i = 0; i < found.Length; i++)
                {
                    T c = found[i]?.TryCast<T>();
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
        /// The live, showing instance of a Panel subclass, using the game's own
        /// visibility flags as well as the object's.
        ///
        /// A Panel that is mid-hide is still active in the hierarchy, so its own
        /// IsVisible and IsTransitioningHide are what tell an open panel from one
        /// that is on its way out.
        /// </summary>
        private static T FindLivePanel<T>() where T : Panel
        {
            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<T>());

                if (found == null)
                    return null;

                for (int i = 0; i < found.Length; i++)
                {
                    T p = found[i]?.TryCast<T>();
                    if (p == null || !Ui.Alive(p) || !Ui.Alive(p.gameObject))
                        continue;

                    if (!p.gameObject.activeInHierarchy)
                        continue;

                    if (p.IsTransitioningHide)
                        continue;

                    if (!Ui.Visible(p.gameObject))
                        continue;

                    if (!p.IsVisible)
                        continue;

                    return p;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
