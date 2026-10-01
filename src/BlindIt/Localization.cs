using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace BlindIt
{
    /// <summary>
    /// Loads the mod's words and decides which language to speak.
    ///
    /// This is the Unity-facing half of <see cref="Strings"/>, kept separate so the
    /// catalogue itself stays testable on the desktop. Everything here is wrapped:
    /// a language that cannot be read must cost the player English wording, never
    /// a silent mod.
    ///
    /// THE LANGUAGE THE PLAYER GETS. Their choice on 2026-10-01 was: follow the
    /// game's own setting, with a config file to override it. So:
    ///
    ///  1. <c>Mods\BlindIt-language.txt</c>, when it holds anything but "auto".
    ///  2. The game's selected locale, read from Unity's LocalizationSettings.
    ///  3. English.
    ///
    /// WHICH CODES EXIST. The game ships eleven string tables, named in its
    /// Addressables build (verified by listing
    /// <c>BopIt!_Data\StreamingAssets\aa\StandaloneWindows64</c> on 2026-10-01):
    /// en, de, es, es-mx, fr, it, ja, ko, pt-br, zh. Those are the codes a
    /// contributor should name a file after, because they are what the game itself
    /// will report.
    /// </summary>
    internal static class Localization
    {
        /// <summary>The folder translations live in, beside the mod's DLL.</summary>
        private const string FolderName = "Localization";

        /// <summary>The config file that can force one language.</summary>
        private const string ConfigFileName = "BlindIt-language.txt";

        /// <summary>
        /// Loads the catalogue. Called once, from
        /// <see cref="Probe.OnLateInitializeMelon"/>, before anything speaks.
        /// </summary>
        internal static void Load()
        {
            string folder = Folder();

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                Log.Line("i18n", "could not create " + folder + ": " + Describe(ex));
            }

            // Writing en.json from the built-in table keeps the shipped file and
            // the code in step, and gives a translator a complete file to copy.
            WriteEnglishTemplate(folder);

            string requested = ReadConfig();
            string fromGame = GameLanguage();

            Strings.Load(requested, fromGame, code => ReadFile(folder, code));

            Log.Line("i18n", string.Join(" | ", new[]
            {
                "language=" + Strings.Language,
                "source=" + Strings.Source,
                "config=" + (requested ?? "<none>"),
                "game=" + (fromGame ?? "<unknown>"),
                "folder=" + folder
            }));
        }

        /// <summary>
        /// Re-reads the catalogue, so a translator can edit a file and hear the
        /// result without restarting the game. Bound to a hotkey by
        /// <see cref="Hotkeys"/>.
        /// </summary>
        internal static string Reload()
        {
            Load();
            return Strings.Get("mod.name") + ", " + Strings.Language;
        }

        private static string Folder()
        {
            try
            {
                return Path.Combine(Paths.ModDir, FolderName);
            }
            catch
            {
                return FolderName;
            }
        }

        /// <summary>
        /// The language the mod was told to use, or null to follow the game.
        ///
        /// A plain text file holding one code, because the player is not a
        /// programmer and a one-line file is the least that can go wrong. Lines
        /// starting with # are comments, so the shipped file can explain itself.
        /// </summary>
        private static string ReadConfig()
        {
            try
            {
                string path = Path.Combine(Paths.ModDir, ConfigFileName);

                if (!File.Exists(path))
                {
                    WriteConfigTemplate(path);
                    return null;
                }

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw?.Trim();

                    if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    return line;
                }
            }
            catch (Exception ex)
            {
                Log.Line("i18n", "config unreadable: " + Describe(ex));
            }

            return null;
        }

        private static void WriteConfigTemplate(string path)
        {
            try
            {
                File.WriteAllText(path, string.Join(Environment.NewLine, new[]
                {
                    "# Which language Blind it! speaks in.",
                    "#",
                    "# auto    follow the game's own language setting (the default)",
                    "# en      English",
                    "#",
                    "# Any other code works if Localization\\<code>.json exists.",
                    "# The game itself ships: en, de, es, es-mx, fr, it, ja, ko, pt-br, zh.",
                    "#",
                    "# Put ONE code on a line of its own below.",
                    "auto",
                    string.Empty
                }));
            }
            catch (Exception ex)
            {
                Log.Line("i18n", "could not write " + ConfigFileName + ": " + Describe(ex));
            }
        }

        /// <summary>
        /// The code of the locale the GAME is using, or null when it cannot be read.
        ///
        /// Unity's localization system is the authority here rather than the OS
        /// language: the player may well run an English Windows and play the game
        /// in Polish, and it is the game they are listening to.
        /// </summary>
        private static string GameLanguage()
        {
            try
            {
                // LocalizationSettings.SelectedLocale touches the localization
                // system's own initialisation. It is safe by
                // OnLateInitializeMelon, but it is still the game's code, so it
                // gets its own guard rather than taking the mod down with it.
                Locale locale = LocalizationSettings.SelectedLocale;

                if (locale == null)
                    return null;

                LocaleIdentifier id = locale.Identifier;
                string code = id.Code;

                if (!string.IsNullOrWhiteSpace(code))
                    return code;

                return locale.name;
            }
            catch (Exception ex)
            {
                Log.Line("i18n", "game language unreadable: " + Describe(ex));
                return null;
            }
        }

        /// <summary>
        /// The text of <c>Localization\&lt;code&gt;.json</c>, or null when absent.
        /// </summary>
        private static string ReadFile(string folder, string code)
        {
            try
            {
                string path = Path.Combine(folder, code + ".json");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Writes the English file from the built-in table.
        ///
        /// Always rewritten, because the built-in table is the source of truth: a
        /// new key added in C# must appear in the file a translator copies, or they
        /// cannot translate a phrase they have no line for. Nobody is expected to
        /// edit en.json by hand; a translation is a new file named for its
        /// language.
        /// </summary>
        private static void WriteEnglishTemplate(string folder)
        {
            try
            {
                string path = Path.Combine(folder, "en.json");
                string json = Strings.EnglishJson();

                // Only write when it differs, so the file's timestamp means
                // something and a read-only install does not log every launch.
                if (File.Exists(path) && File.ReadAllText(path) == json)
                    return;

                File.WriteAllText(path, json);
                Log.Line("i18n", "wrote " + path);
            }
            catch (Exception ex)
            {
                Log.Line("i18n", "could not write en.json: " + Describe(ex));
            }
        }

        private static string Describe(Exception ex)
        {
            return ex == null ? "<null>" : ex.GetType().Name + ": " + ex.Message;
        }
    }
}
