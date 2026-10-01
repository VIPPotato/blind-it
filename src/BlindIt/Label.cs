using System;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;

namespace BlindIt
{
    /// <summary>
    /// Turns a selected UI object into the words a player would read on screen.
    ///
    /// Source order, following the screen-text stage's rule "take the first
    /// source that gives real words":
    ///
    ///  1. A TextMeshPro label inside the item (this game's UI is TMP throughout,
    ///     proven in recon). Searched downward first, because a button's own
    ///     caption is its most specific label.
    ///  2. A legacy uGUI Text inside the item, in case any widget predates TMP.
    ///  3. A TMP label on a parent, a few levels up, for items whose caption sits
    ///     beside the selectable rather than inside it.
    ///  4. The marker "unlabeled", so the gap is audible and the log says which
    ///     object caused it.
    ///
    /// The game ships NO accessible text of its own: a search of
    /// global-metadata.dat for Accessib, ScreenReader, Narrat and MenuReader
    /// found nothing, so step 1 of the skill's label order does not apply here
    /// and scraping the TMP label is the correct source.
    ///
    /// Text is taken already localised. The game uses Unity Localization, which
    /// writes the translated string into the TMP component itself, so reading
    /// TMP_Text.text yields the player's language with no call into the
    /// localisation system.
    /// </summary>
    internal static class Label
    {
        /// <summary>Spoken when an item has no readable text at all.</summary>
        internal const string Unlabeled = "unlabeled";

        /// <summary>How many parent levels to search when the item has no text of its own.</summary>
        private const int ParentSearchDepth = 3;

        /// <summary>
        /// Reads the label for a selected object. Returns null when nothing
        /// readable was found, so the caller can decide between retrying (the
        /// game may fill the text a frame later) and speaking the marker.
        /// </summary>
        internal static string Read(GameObject go)
        {
            if (!Ui.Alive(go))
                return null;

            string text = FromChildren(go);
            if (!string.IsNullOrEmpty(text))
                return text;

            text = FromLegacyChildren(go);
            if (!string.IsNullOrEmpty(text))
                return text;

            text = FromParents(go);
            if (!string.IsNullOrEmpty(text))
                return text;

            return null;
        }

        /// <summary>TMP labels inside the item, nearest first.</summary>
        private static string FromChildren(GameObject go)
        {
            try
            {
                // Non-generic overload with Il2CppType: the generic
                // GetComponentsInChildren<T>() can fail silently on IL2CPP.
                Il2CppReferenceArray<Component> found =
                    go.GetComponentsInChildren(Il2CppType.Of<TMP_Text>());

                if (found == null)
                    return null;

                string best = null;

                for (int i = 0; i < found.Length; i++)
                {
                    TMP_Text tmp = found[i]?.TryCast<TMP_Text>();
                    if (tmp == null || !Ui.Alive(tmp))
                        continue;

                    // Skip a label the player cannot see: hidden children hold
                    // stale text, and button prompt glyphs are often inactive.
                    if (!Ui.Alive(tmp.gameObject) || !tmp.gameObject.activeInHierarchy)
                        continue;

                    string candidate = Clean(tmp.text);
                    if (candidate == null)
                        continue;

                    // Prefer the first usable one: GetComponentsInChildren returns
                    // the item's own component before its descendants', so the
                    // first hit is the most specific label.
                    if (best == null)
                        best = candidate;
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Legacy uGUI Text inside the item.</summary>
        private static string FromLegacyChildren(GameObject go)
        {
            try
            {
                Il2CppReferenceArray<Component> found =
                    go.GetComponentsInChildren(Il2CppType.Of<UnityEngine.UI.Text>());

                if (found == null)
                    return null;

                for (int i = 0; i < found.Length; i++)
                {
                    UnityEngine.UI.Text t = found[i]?.TryCast<UnityEngine.UI.Text>();
                    if (t == null || !Ui.Alive(t))
                        continue;

                    if (!Ui.Alive(t.gameObject) || !t.gameObject.activeInHierarchy)
                        continue;

                    string candidate = Clean(t.text);
                    if (candidate != null)
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
        /// TMP labels on a parent, for an item whose caption is a sibling rather
        /// than a child. Bounded depth: searching the whole way to the canvas
        /// would pick up the screen's title for every item on it.
        /// </summary>
        private static string FromParents(GameObject go)
        {
            try
            {
                Transform t = go.transform;

                for (int level = 0; level < ParentSearchDepth; level++)
                {
                    if (!Ui.Alive(t) || !Ui.Alive(t.parent))
                        return null;

                    t = t.parent;

                    if (!Ui.Alive(t.gameObject))
                        return null;

                    string candidate = FromChildren(t.gameObject);
                    if (!string.IsNullOrEmpty(candidate))
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
        /// Makes a raw UI string speakable, or rejects it.
        ///
        /// The work lives in <see cref="LabelText.Clean"/>, which is free of Unity
        /// types so the desktop harness can test it without the game. This wrapper
        /// exists so callers in the mod read naturally.
        /// </summary>
        internal static string Clean(string raw)
        {
            return LabelText.Clean(raw);
        }
    }
}
