using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;

namespace BlindIt
{
    /// <summary>
    /// Reads the VALUE beside a settings row, so the player hears "Music, 8" rather
    /// than just "Music".
    ///
    /// The game's own <c>SettingsRow</c> keeps the label and the value in different
    /// places, which is why the first version of the menu reader announced the label
    /// only. All fields used here are public on <c>Il2Cpp.SettingsRow</c>, confirmed
    /// against this build's generated proxy:
    ///
    ///   IsFocused        bool               - true while the row has focus
    ///   ValueText        TextMeshProUGUI    - the value shown when NOT focused
    ///   ActiveValueText  TextMeshProUGUI    - the value shown WHILE focused
    ///
    /// The row's subclasses say what kind of control it is:
    ///   SettingsSlider  - a value that changes with left/right (volume, language)
    ///   SettingsToggle  - on/off, state in its own IsOn bool
    ///   SettingsButton  - opens another screen, its "value" is a hint
    ///
    /// Because the row swaps between two containers when it gains focus, the
    /// selected row's live value is in ActiveValueText, and ValueText still holds
    /// the unfocused copy. Reading the wrong one shows a stale value, so the focused
    /// field is preferred and the other is the fallback.
    /// </summary>
    internal static class SettingsValue
    {
        /// <summary>
        /// The spoken value for a selected object, or null when this object is not
        /// a settings row or has nothing worth adding.
        ///
        /// Returns just the value, not the label: the caller already speaks the
        /// label and joins the two.
        /// </summary>
        internal static string Read(GameObject go)
        {
            if (!Ui.Alive(go))
                return null;

            try
            {
                // Non-generic GetComponent with Il2CppType, never GetComponent<T>():
                // IL2CPP compiles generic instantiations ahead of time, so a generic
                // call the original game never made can fail or return null. This is
                // the same rule the label reader follows.
                //
                // SettingsRow is the base of slider, toggle and button, so one
                // lookup covers all three; the cast finds the subclass instance.
                SettingsRow row = go.GetComponent(Il2CppType.Of<SettingsRow>())
                    ?.TryCast<SettingsRow>();

                if (row == null || !Ui.Alive(row))
                    return null;

                // A toggle states its value as a bool, not as text. Read it from the
                // field rather than the label, because the on/off caption is drawn
                // with sprites that carry no words.
                SettingsToggle toggle = go.GetComponent(Il2CppType.Of<SettingsToggle>())
                    ?.TryCast<SettingsToggle>();

                if (toggle != null && Ui.Alive(toggle))
                {
                    // The row's own text may also carry a localised On/Off; prefer
                    // that when present so the player hears the game's wording in
                    // their language, and fall back to the bool.
                    string toggleText = FromTextFields(row);
                    if (!string.IsNullOrEmpty(toggleText))
                        return toggleText;

                    return toggle.IsOn ? "on" : "off";
                }

                return FromTextFields(row);
            }
            catch
            {
                // A settings row that cannot be read is not worth ending a frame
                // over; the label still gets spoken without it.
                return null;
            }
        }

        /// <summary>
        /// The value text, preferring whichever of the two fields belongs to the
        /// row's current focus state.
        /// </summary>
        private static string FromTextFields(SettingsRow row)
        {
            // While focused, the game shows ActiveValueText and hides ValueText.
            // Trying the focused one first avoids reading the stale copy.
            if (row.IsFocused)
            {
                string active = TextOf(row.ActiveValueText);
                if (!string.IsNullOrEmpty(active))
                    return active;
            }

            string normal = TextOf(row.ValueText);
            if (!string.IsNullOrEmpty(normal))
                return normal;

            // Not focused yet, or the game filled only the active copy.
            return TextOf(row.ActiveValueText);
        }

        /// <summary>
        /// Reads a TMP field defensively and cleans it with the same rules the
        /// labels use, so an icon-only value is rejected rather than spoken as junk.
        /// </summary>
        private static string TextOf(TextMeshProUGUI tmp)
        {
            try
            {
                if (tmp == null || !Ui.Alive(tmp))
                    return null;

                // A value drawn into a hidden container is the stale copy.
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
