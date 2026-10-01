using System;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlindIt
{
    /// <summary>
    /// Small helpers for reading Unity's UI from IL2CPP, shared by the dump and
    /// the menu reader.
    ///
    /// Everything here follows the Il2CppInterop rules recorded in the engine
    /// reference: never use <c>is T</c> on a proxy object (it is always false),
    /// use <see cref="Il2CppObjectBase.TryCast{T}"/>; never use the generic
    /// <c>FindObjectsOfType&lt;T&gt;()</c> (it can fail silently), use the
    /// non-generic overload with <c>Il2CppType.Of&lt;T&gt;()</c>.
    ///
    /// Every method here is defensive: a UI object can be destroyed between one
    /// frame and the next, and a getter can run native code that throws. The
    /// menu reader must never be the reason the game stops.
    /// </summary>
    internal static class Ui
    {
        /// <summary>
        /// True when the object is alive. A destroyed Unity object is not null on
        /// the C# side; its native pointer is gone. Comparing against null goes
        /// through Unity's overloaded operator, which is exactly the check we want,
        /// but it can still throw on a half-torn-down object, so it is wrapped.
        /// </summary>
        internal static bool Alive(UnityEngine.Object o)
        {
            try
            {
                return o != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The GameObject the EventSystem currently considers selected, or null.</summary>
        internal static GameObject Selected()
        {
            try
            {
                EventSystem es = EventSystem.current;
                if (!Alive(es))
                    return null;

                GameObject go = es.currentSelectedGameObject;
                return Alive(go) ? go : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// A stable name for an object, safe on a destroyed one.
        /// </summary>
        internal static string SafeName(UnityEngine.Object o)
        {
            try
            {
                if (!Alive(o))
                    return "<destroyed>";
                return o.name ?? "<null name>";
            }
            catch
            {
                return "<unreadable>";
            }
        }

        /// <summary>
        /// The object's full path in the scene tree, root first. This is what makes
        /// a dump readable: it says exactly where an item lives, so a later session
        /// can find the same widget again without guessing.
        /// </summary>
        internal static string Path(GameObject go)
        {
            try
            {
                if (!Alive(go))
                    return "<destroyed>";

                StringBuilder sb = new StringBuilder(SafeName(go));
                Transform t = go.transform;
                int guard = 0;

                // The guard stops a malformed or cyclic hierarchy from hanging the
                // game. No real UI is anywhere near 64 deep.
                while (Alive(t) && Alive(t.parent) && guard++ < 64)
                {
                    t = t.parent;
                    sb.Insert(0, SafeName(t) + "/");
                }

                return sb.ToString();
            }
            catch
            {
                return "<unreadable path>";
            }
        }

        /// <summary>
        /// The real IL2CPP class name of an object, not the proxy type of the
        /// reference. GetType() on a proxy returns the type of the variable, so a
        /// panel found through its base class would report the base class; this
        /// reports the true subclass.
        /// </summary>
        internal static string TypeName(Il2CppSystem.Object o)
        {
            try
            {
                if (o == null)
                    return "<null>";
                Il2CppSystem.Type t = o.GetIl2CppType();
                return t != null ? t.Name : "<no type>";
            }
            catch
            {
                return "<unreadable type>";
            }
        }

        /// <summary>
        /// Whether the object is actually being shown. "Present is not shown":
        /// closed Unity menus usually stay in the scene, merely disabled or faded
        /// to alpha 0, still holding their old text. Reading one of those is how a
        /// mod ends up speaking a screen the player left.
        ///
        /// Checks, in order: the GameObject is active in the hierarchy, and no
        /// CanvasGroup above it has faded it out.
        /// </summary>
        internal static bool Visible(GameObject go)
        {
            try
            {
                if (!Alive(go) || !go.activeInHierarchy)
                    return false;

                // Walk up looking for a CanvasGroup that hides this subtree. The
                // game's own Panel class fades with a CanvasGroup, so this is the
                // check that distinguishes an open panel from a closed one.
                Transform t = go.transform;
                int guard = 0;
                while (Alive(t) && guard++ < 64)
                {
                    CanvasGroup cg = t.GetComponent(Il2CppType.Of<CanvasGroup>())
                        ?.TryCast<CanvasGroup>();

                    if (cg != null && Alive(cg))
                    {
                        if (cg.alpha <= 0.01f)
                            return false;
                    }

                    t = t.parent;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether the player can actually move the menu cursor onto this object:
        /// it carries a Selectable (button, slider, toggle), that Selectable is
        /// interactable, and the object is on screen.
        ///
        /// Used for counting the items in a menu. Unity's own navigation skips a
        /// disabled or hidden Selectable, so counting those would make the mod
        /// announce "6 of 9" on a menu with six reachable items. This game does
        /// leave such widgets in place: the settings panel keeps rows that do not
        /// apply to the current device.
        ///
        /// GetComponent with Il2CppType plus TryCast, never the generic form: the
        /// generic instantiation may not exist in the compiled game. Selectable is
        /// the base class of Button, Slider and Toggle, so one check covers them.
        /// </summary>
        internal static bool Navigable(GameObject go)
        {
            try
            {
                if (!Visible(go))
                    return false;

                UnityEngine.UI.Selectable sel =
                    go.GetComponent(Il2CppType.Of<UnityEngine.UI.Selectable>())
                      ?.TryCast<UnityEngine.UI.Selectable>();

                if (sel == null || !Alive(sel))
                    return false;

                return sel.interactable && sel.enabled;
            }
            catch
            {
                return false;
            }
        }
    }
}
