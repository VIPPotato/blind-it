using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlindIt
{
    /// <summary>
    /// Says which key an action is bound to, for the Controls menu.
    ///
    /// WHY THIS EXISTS. The player reported on 2026-10-01 that the controls menu
    /// "doesn't announce to what key something is bound to". It is not a missing
    /// label: the game draws the binding as a PICTURE. Each row is a
    /// <c>ControlRow</c> whose binding is shown by a
    /// <c>ControlPromptSpriteSwapperV2</c>, which swaps an <c>Image</c>'s sprite for
    /// the current device (verified in the decompiled source: the class holds only
    /// backgroundImage, iconImage and a prompt database, and no text at all). A
    /// screen reader has nothing to read, so the key has to be recovered from the
    /// input system instead.
    ///
    /// HOW IT IS RECOVERED. ControlRow carries two public strings, ActionMapName and
    /// ActionName. Those name an action in the game's InputActionAsset, whose
    /// bindings carry an effectivePath such as <c>&lt;Keyboard&gt;/space</c>.
    /// InputControlPath.ToHumanReadableString turns that into words.
    ///
    /// EVERY MEMBER USED HERE WAS VERIFIED against this build's own proxy
    /// assemblies on 2026-10-01, by reading Unity.InputSystem.dll's metadata rather
    /// than trusting the package's public documentation:
    ///
    ///   InputActionAsset.FindActionMap(...)      present
    ///   InputActionMap.FindAction(...)           present (via the asset's maps)
    ///   InputAction.bindings                     present
    ///   InputBinding.effectivePath               present (override-aware)
    ///   InputBinding.isComposite/isPartOfComposite present
    ///   InputControlPath.ToHumanReadableString   present
    ///
    /// effectivePath is deliberately preferred over path: it accounts for a binding
    /// the player rebound themselves, which is the whole point of this screen.
    /// </summary>
    internal static class ControlBinding
    {
        /// <summary>
        /// The spoken binding for a selected object, as "bound to Space", or null
        /// when the object is not a control row.
        ///
        /// Returns only the clause, not the row's name: the caller speaks the label
        /// and joins the two into one utterance, the same way settings values work.
        /// </summary>
        internal static string Read(GameObject go)
        {
            if (!Ui.Alive(go))
                return null;

            try
            {
                // Non-generic GetComponent with Il2CppType, never GetComponent<T>():
                // IL2CPP compiles generic instantiations ahead of time, so a generic
                // call the game itself never made can fail or return null.
                ControlRow row = go.GetComponent(Il2CppType.Of<ControlRow>())
                    ?.TryCast<ControlRow>();

                if (row == null || !Ui.Alive(row))
                    return null;

                // While the player is actually rebinding, the live state is what
                // matters, not the old key: the game is waiting for a press.
                InputRebindingManager manager = row.InputRebindingManager;
                if (manager != null && Ui.Alive(manager) && manager.IsRebinding)
                    return Strings.Get("controls.rebinding");

                string keys = KeysFor(row.ActionMapName, row.ActionName);

                return string.IsNullOrEmpty(keys)
                    ? Strings.Get("controls.unbound")
                    : Strings.Get("controls.boundTo", keys);
            }
            catch (Exception ex)
            {
                Log.Line("controls", "binding read failed: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// The keyboard key(s) bound to an action, in words, or null.
        ///
        /// Only KEYBOARD bindings are spoken. The row shows one prompt for the
        /// device currently in use, but this player is on a keyboard, and reading
        /// out the gamepad and joystick paths as well would bury the one fact they
        /// asked for in a list of controls they are not holding.
        /// </summary>
        private static string KeysFor(string mapName, string actionName)
        {
            if (string.IsNullOrEmpty(actionName))
                return null;

            InputAction action = FindAction(mapName, actionName);
            if (action == null)
                return null;

            string spoken = null;

            try
            {
                // ReadOnlyArray, which is the type this build's proxy actually
                // returns (the compiler named it when IReadOnlyList was tried). It
                // indexes with Count and [i] like a list.
                UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputBinding> bindings =
                    action.bindings;

                for (int i = 0; i < bindings.Count; i++)
                {
                    InputBinding binding = bindings[i];

                    // A composite's own entry carries no path of its own; its parts
                    // follow it and are read on their own iterations.
                    if (binding.isComposite)
                        continue;

                    string path = binding.effectivePath;
                    if (string.IsNullOrEmpty(path))
                        continue;

                    // Keyboard only. The paths look like <Keyboard>/space.
                    if (path.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string words = Humanise(path);
                    if (string.IsNullOrEmpty(words))
                        continue;

                    // The FIRST keyboard binding is the answer. A player asking
                    // "what is this bound to" wants one key, and these actions have
                    // several equivalent ones (Bop is space AND the gamepad's south
                    // button; Spin is s as well as a dpad direction).
                    spoken = words;
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Line("controls", "bindings walk failed: " + ex.GetType().Name);
                return null;
            }

            return spoken;
        }

        /// <summary>
        /// The action named by a row, found through the game's own action asset.
        ///
        /// The asset is reached from a live PlayerInput rather than loaded fresh:
        /// the running instance is the one carrying the player's rebinds.
        /// </summary>
        private static InputAction FindAction(string mapName, string actionName)
        {
            try
            {
                InputActionAsset asset = Asset();
                if (asset == null)
                    return null;

                // The two-argument form (map/action) is what the row's pair of
                // strings describes. Searched through the map when it is named, and
                // across the asset otherwise.
                if (!string.IsNullOrEmpty(mapName))
                {
                    InputActionMap map = asset.FindActionMap(mapName, false);

                    if (map != null)
                    {
                        InputAction inMap = map.FindAction(actionName, false);
                        if (inMap != null)
                            return inMap;
                    }
                }

                return asset.FindAction(actionName, false);
            }
            catch (Exception ex)
            {
                Log.Line("controls", "action lookup failed: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>The live action asset, or null.</summary>
        private static InputActionAsset Asset()
        {
            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PlayerInput>());

                if (found == null)
                    return null;

                for (int i = 0; i < found.Length; i++)
                {
                    PlayerInput input = found[i]?.TryCast<PlayerInput>();
                    if (input == null || !Ui.Alive(input))
                        continue;

                    InputActionAsset asset = input.actions;
                    if (asset != null)
                        return asset;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// A control path as words: "&lt;Keyboard&gt;/space" becomes "Space".
        ///
        /// Unity's own converter is used so the wording matches what the game would
        /// print, and because it knows the odd cases (numpad keys, modifiers). The
        /// OmitDevice option keeps the device name out: the player knows they are on
        /// a keyboard, and "Keyboard Space" reads worse than "Space".
        ///
        /// If that call is unavailable the tail of the path is used instead, which is
        /// still the key's name and better than silence.
        /// </summary>
        private static string Humanise(string path)
        {
            try
            {
                string words = InputControlPath.ToHumanReadableString(
                    path,
                    InputControlPath.HumanReadableStringOptions.OmitDevice,
                    null);

                if (!string.IsNullOrEmpty(words))
                    return LabelText.Clean(words);
            }
            catch (Exception ex)
            {
                Log.Line("controls", "humanise failed, using path tail: "
                    + ex.GetType().Name);
            }

            int slash = path.LastIndexOf('/');
            string tail = slash >= 0 && slash < path.Length - 1
                ? path.Substring(slash + 1)
                : path;

            return LabelText.Clean(tail);
        }
    }
}
