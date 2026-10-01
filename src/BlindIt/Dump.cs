using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BlindIt
{
    /// <summary>
    /// The mod's eyes: writes everything on the current screen to a file.
    ///
    /// The player cannot describe a screen, and a game launch costs them several
    /// minutes, so this exists to make one launch answer as many questions as
    /// possible. It records what the EventSystem considers selected, every visible
    /// text with its path, and the game's real input bindings.
    ///
    /// Dump key is F8, chosen because the game binds no F-key anywhere: its only
    /// keyboard bindings are w/a/s/d and the four arrows, read out of the
    /// InputActionAsset embedded in global-metadata.dat. The dump also logs the
    /// live bindings so that claim is verified from inside the running game
    /// rather than trusted.
    ///
    /// Each dump appends to <c>Mods\BlindIt-dump.txt</c> with a header, so
    /// several presses in one session stay separately readable.
    /// </summary>
    internal static class Dump
    {
        private const string DumpFileName = "BlindIt-dump.txt";

        /// <summary>Guard against one dump running into the next.</summary>
        private static bool _busy;

        /// <summary>How many dumps this session, for the header.</summary>
        private static int _count;

        private static string DumpPath => Path.Combine(Paths.ModDir, DumpFileName);

        /// <summary>
        /// Writes one dump. Never throws: a failed dump must not end the session
        /// the player spent minutes reaching.
        /// </summary>
        internal static void Write(string reason)
        {
            if (_busy)
                return;

            _busy = true;

            try
            {
                StringBuilder sb = new StringBuilder(16384);

                _count++;
                sb.AppendLine();
                sb.AppendLine("================================================================");
                sb.AppendLine("dump #" + _count.ToString(CultureInfo.InvariantCulture)
                    + "  reason=" + reason
                    + "  at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture));
                sb.AppendLine("================================================================");

                AppendScenes(sb);
                AppendSelection(sb);
                AppendBindings(sb);
                AppendModState(sb);
                AppendVisibleText(sb);

                Append(sb.ToString());

                Log.Line("dump", "wrote dump #" + _count.ToString(CultureInfo.InvariantCulture)
                    + " | reason=" + reason + " | file=" + DumpFileName);
            }
            catch (Exception ex)
            {
                Log.Line("dump", "FAILED: " + Describe(ex));
            }
            finally
            {
                _busy = false;
            }
        }

        private static void AppendScenes(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("-- scenes --");

            try
            {
                sb.AppendLine("active: " + SceneManager.GetActiveScene().name);
                int n = SceneManager.sceneCount;
                for (int i = 0; i < n; i++)
                {
                    Scene s = SceneManager.GetSceneAt(i);
                    sb.AppendLine("  [" + i.ToString(CultureInfo.InvariantCulture) + "] "
                        + s.name + " loaded=" + s.isLoaded);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  <failed: " + Describe(ex) + ">");
            }
        }

        /// <summary>
        /// What the EventSystem considers selected. This is the single most
        /// important line in the dump: it is the identity the menu reader follows.
        /// </summary>
        private static void AppendSelection(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("-- selection (EventSystem) --");

            try
            {
                EventSystem es = EventSystem.current;
                if (!Ui.Alive(es))
                {
                    sb.AppendLine("  EventSystem.current is NULL - no uGUI focus exists right now");
                    return;
                }

                sb.AppendLine("  EventSystem object: " + Ui.Path(es.gameObject));
                sb.AppendLine("  sendNavigationEvents: " + es.sendNavigationEvents);
                sb.AppendLine("  input module: " + Ui.TypeName(es.currentInputModule));

                GameObject sel = es.currentSelectedGameObject;
                if (!Ui.Alive(sel))
                {
                    sb.AppendLine("  currentSelectedGameObject: NULL");
                }
                else
                {
                    sb.AppendLine("  currentSelectedGameObject: " + Ui.SafeName(sel));
                    sb.AppendLine("    instance id : " + sel.GetInstanceID()
                        .ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("    path        : " + Ui.Path(sel));
                    sb.AppendLine("    visible     : " + Ui.Visible(sel));
                    sb.AppendLine("    label read  : "
                        + (Label.Read(sel) ?? "<none - would speak \"" + Label.Unlabeled + "\">"));
                    sb.AppendLine("    components  :");
                    AppendComponents(sb, sel, "      ");
                }

                GameObject first = es.firstSelectedGameObject;
                sb.AppendLine("  firstSelectedGameObject: "
                    + (Ui.Alive(first) ? Ui.Path(first) : "NULL"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  <failed: " + Describe(ex) + ">");
            }
        }

        private static void AppendComponents(StringBuilder sb, GameObject go, string indent)
        {
            try
            {
                Il2CppReferenceArray<Component> comps =
                    go.GetComponents(Il2CppType.Of<Component>());

                if (comps == null)
                {
                    sb.AppendLine(indent + "<none>");
                    return;
                }

                for (int i = 0; i < comps.Length; i++)
                {
                    Component c = comps[i];
                    sb.AppendLine(indent + (Ui.Alive(c) ? Ui.TypeName(c) : "<destroyed>"));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine(indent + "<failed: " + Describe(ex) + ">");
            }
        }

        /// <summary>
        /// The game's real, live input bindings. This settles from inside the
        /// process which keys the game itself uses, so the mod's own hotkey can be
        /// proven not to collide. effectivePath, not path: path returns the shipped
        /// default and ignores a player's rebinds, and this game ships a rebinding
        /// screen.
        /// </summary>
        private static void AppendBindings(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("-- input bindings (live, effectivePath) --");

            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PlayerInput>());

                if (found == null || found.Length == 0)
                {
                    sb.AppendLine("  no PlayerInput in the scene right now");
                    return;
                }

                for (int i = 0; i < found.Length; i++)
                {
                    PlayerInput pi = found[i]?.TryCast<PlayerInput>();
                    if (pi == null || !Ui.Alive(pi))
                        continue;

                    sb.AppendLine("  PlayerInput on " + Ui.Path(pi.gameObject));
                    sb.AppendLine("    control scheme: " + (pi.currentControlScheme ?? "<null>"));

                    // InputActionMap is an Il2CppSystem.Object, not a Unity
                    // object, so null is the only check that applies.
                    InputActionMap cur = pi.currentActionMap;
                    sb.AppendLine("    current map   : "
                        + (cur != null ? cur.name : "<null>"));

                    InputActionAsset asset = pi.actions;
                    if (asset == null || !Ui.Alive(asset))
                    {
                        sb.AppendLine("    actions asset : <null>");
                        continue;
                    }

                    var maps = asset.actionMaps;
                    for (int m = 0; m < maps.Count; m++)
                    {
                        InputActionMap map = maps[m];
                        if (map == null)
                            continue;

                        sb.AppendLine("    map: " + map.name + " enabled=" + map.enabled);

                        var actions = map.actions;
                        for (int a = 0; a < actions.Count; a++)
                        {
                            InputAction act = actions[a];
                            if (act == null)
                                continue;

                            var binds = act.bindings;
                            StringBuilder paths = new StringBuilder();
                            for (int b = 0; b < binds.Count; b++)
                            {
                                if (paths.Length > 0)
                                    paths.Append(", ");
                                paths.Append(binds[b].effectivePath ?? "<null>");
                            }

                            sb.AppendLine("      action " + act.name + " -> " + paths);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  <failed: " + Describe(ex) + ">");
            }
        }

        /// <summary>
        /// What the mod itself currently thinks, for every surface added this
        /// session: which screen it claims, what it would say for the filters, the
        /// book page and the menu position, and whether each new panel type is on
        /// screen.
        ///
        /// WHY THIS IS IN THE DUMP. A launch costs the player minutes, so a silent
        /// surface must diagnose itself from one press of the dump key rather than
        /// needing another launch. If the leaderboard says nothing when the filter
        /// changes, this section shows whether the mod could not FIND the tabs or
        /// found them and composed an empty sentence, which are different bugs with
        /// different fixes.
        /// </summary>
        private static void AppendModState(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("-- mod state (readers) --");

            try
            {
                sb.AppendLine("  ScreenReader claims : " + (ScreenReader.DebugScreen ?? "<none>"));
                sb.AppendLine("  cursor              : " + ScreenReader.DebugCursor
                    .ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("  context would say   : "
                    + (ScreenReader.DebugContext() ?? "<null>"));
                sb.AppendLine("  book page would say : "
                    + (ScreenReader.DebugBookPage() ?? "<null>"));
                sb.AppendLine("  items on this screen: "
                    + ScreenReader.DebugItemCount().ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  <screen reader failed: " + Describe(ex) + ">");
            }

            try
            {
                GameObject sel = Ui.Selected();
                sb.AppendLine("  menu position would say: "
                    + (MenuReader.DebugPosition(sel) ?? "<null>"));
                sb.AppendLine("  menu value would say   : "
                    + (sel != null ? (SettingsValue.Read(sel) ?? "<null>") : "<no selection>"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  <menu reader failed: " + Describe(ex) + ">");
            }

            // The pre-game screen's own view of itself, including whether it has gone
            // quiet because the player is leaving. Without this, a silent start screen
            // and a missing start screen look identical in a dump.
            try
            {
                sb.AppendLine("  pre-game choices    : " + TrackChoiceReader.DebugState());
            }
            catch (Exception ex)
            {
                sb.AppendLine("  <track reader failed: " + Describe(ex) + ">");
            }

            // Whether an end-of-round score is still waiting for a menu to carry it.
            // A score stuck here would be a score the player never hears.
            try
            {
                sb.AppendLine("  end-of-round score  : " + ScoreHandoff.DebugState());
            }
            catch (Exception ex)
            {
                sb.AppendLine("  <score handoff failed: " + Describe(ex) + ">");
            }

            // Panel presence, by type. "Found but not showing" is the answer that
            // explains most silences, so present and showing are reported apart.
            AppendPanelPresence(sb, "CreditsContent", Il2CppType.Of<Il2Cpp.CreditsContent>());
            AppendPanelPresence(sb, "BookController", Il2CppType.Of<Il2Cpp.BookController>());
            AppendPanelPresence(sb, "GroupFilterTab", Il2CppType.Of<Il2Cpp.GroupFilterTab>());
            AppendPanelPresence(sb, "DateFilterTab", Il2CppType.Of<Il2Cpp.DateFilterTab>());
            AppendPanelPresence(sb, "FinalScorePanel", Il2CppType.Of<Il2Cpp.FinalScorePanel>());
            AppendPanelPresence(sb, "PartyLeaderboardNameSelect",
                Il2CppType.Of<Il2Cpp.PartyLeaderboardNameSelect>());
            AppendPanelPresence(sb, "LeaderBoardNameInputPanel",
                Il2CppType.Of<Il2Cpp.LeaderBoardNameInputPanel>());
            AppendPanelPresence(sb, "LeaderboardNameSelectionPanel",
                Il2CppType.Of<Il2Cpp.LeaderboardNameSelectionPanel>());
        }

        /// <summary>
        /// How many objects of a type exist, and how many are actually showing.
        /// </summary>
        private static void AppendPanelPresence(StringBuilder sb, string label,
            Il2CppSystem.Type type)
        {
            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(type);

                int total = found?.Length ?? 0;
                int showing = 0;

                for (int i = 0; found != null && i < found.Length; i++)
                {
                    Component c = found[i]?.TryCast<Component>();
                    if (c == null || !Ui.Alive(c) || !Ui.Alive(c.gameObject))
                        continue;

                    if (Ui.Visible(c.gameObject))
                        showing++;
                }

                sb.AppendLine("  " + label + ": in memory="
                    + total.ToString(CultureInfo.InvariantCulture)
                    + ", showing=" + showing.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  " + label + ": <failed: " + Describe(ex) + ">");
            }
        }

        /// <summary>
        /// Every visible TMP text on screen, with its path. This is what tells a
        /// later session what a menu actually contains, and it is how an unlabeled
        /// item gets diagnosed without another launch.
        /// </summary>
        private static void AppendVisibleText(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("-- visible text (TextMeshPro) --");

            try
            {
                Il2CppReferenceArray<UnityEngine.Object> found =
                    UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<TMP_Text>());

                if (found == null || found.Length == 0)
                {
                    sb.AppendLine("  none found");
                    return;
                }

                int shown = 0;
                int hidden = 0;

                for (int i = 0; i < found.Length; i++)
                {
                    TMP_Text tmp = found[i]?.TryCast<TMP_Text>();
                    if (tmp == null || !Ui.Alive(tmp) || !Ui.Alive(tmp.gameObject))
                        continue;

                    bool visible = Ui.Visible(tmp.gameObject);
                    if (!visible)
                    {
                        hidden++;
                        continue;
                    }

                    string raw = null;
                    try
                    {
                        raw = tmp.text;
                    }
                    catch
                    {
                        raw = null;
                    }

                    string clean = Label.Clean(raw);

                    shown++;
                    sb.AppendLine("  \"" + (clean ?? "<no words>") + "\"");
                    sb.AppendLine("      type: " + Ui.TypeName(tmp));
                    sb.AppendLine("      path: " + Ui.Path(tmp.gameObject));
                }

                sb.AppendLine();
                sb.AppendLine("  visible: " + shown.ToString(CultureInfo.InvariantCulture)
                    + ", hidden/faded (skipped): " + hidden.ToString(CultureInfo.InvariantCulture)
                    + ", total in memory: " + found.Length.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  <failed: " + Describe(ex) + ">");
            }
        }

        private static void Append(string text)
        {
            try
            {
                using (FileStream fs = new FileStream(
                           DumpPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter sw = new StreamWriter(
                           fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    sw.Write(text);
                    sw.Flush();
                    fs.Flush(true);
                }
            }
            catch (Exception ex)
            {
                Log.Line("dump", "could not write the dump file: " + Describe(ex));
            }
        }

        /// <summary>
        /// One-line exception description. The full stack goes nowhere useful in a
        /// dump header, and the type plus message is what identifies the problem.
        /// </summary>
        private static string Describe(Exception ex)
        {
            try
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
            catch
            {
                return "<unprintable exception>";
            }
        }
    }
}
