using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(BlindIt.Probe), "Blind it!", "0.3.0", "Blind it! project")]
[assembly: MelonGame(null, null)]

namespace BlindIt
{
    /// <summary>
    /// The mod's entry point.
    ///
    /// It still writes the injection probe line, which is what proves the
    /// deployed build is the one being tested, and it now starts the speech
    /// service and says its proof-of-life sentences.
    ///
    /// It deliberately touches no game type: doing that this early can run a
    /// game class's initialisation before the game's own data exists.
    /// </summary>
    public sealed class Probe : MelonMod
    {
        public override void OnInitializeMelon()
        {
            WriteLine("OnInitializeMelon");
        }

        public override void OnLateInitializeMelon()
        {
            WriteLine("OnLateInitializeMelon");

            // Started here, not in OnInitializeMelon and never from DllMain: Prism
            // starts threads and COM, which can deadlock the game under the loader
            // lock. By this point the loader is fully up.
            Speech.Start();

            // The words the mod says come from Localization\<code>.json, so they
            // are loaded before anything speaks. This also writes en.json, which is
            // the file a translator copies.
            Localization.Load();

            // Proof of life. The first sentence is plain letters; the second carries
            // accented ones, which is the real test: if the text reaches Prism as
            // anything other than valid UTF-8 the whole line is refused and nothing
            // is heard. interrupt=true on the first so it cuts whatever the screen
            // reader was saying about the game window, false on the second so it
            // follows on instead of cutting the first one short.
            Speech.Speak(Strings.Get("mod.ready", Strings.Get("mod.name")), true);

            // NO PatchAll CALL HERE, DELIBERATELY. MelonLoader applies this
            // assembly's Harmony patches by itself, in MelonBase.HarmonyInit(),
            // before OnInitializeMelon runs: it walks every type in the mod
            // assembly and calls CreateClassProcessor(type, false).Patch(), and it
            // skips that only when MelonAssembly.HarmonyDontPatchAll is set, which
            // this mod does not set.
            //
            // So the mod's own PatchAll was a SECOND pass over the same assembly,
            // and Harmony happily registered ScorePatch.Postfix twice. Launch 6
            // showed the result: two "score | final" lines 3 ms apart and the
            // player hearing "Final score. 21 points." twice. Proven on the
            // desktop against this game's own 0Harmony.dll (2.10.2.0), where two
            // passes give "postfix ran 2 time(s) for ONE call" and one pass gives
            // 1. It produced no error in any log, which is exactly what made it
            // look like a dedupe bug in the mod instead of a double registration.
            //
            // The loader's pass is the one kept: it runs earlier and it is what
            // every other MelonLoader mod relies on. The patch's proof is now a
            // single "score | final" line per round, not a log line from here.
        }

        /// <summary>
        /// One frame of the mod. MelonLoader calls this from the game's own update
        /// loop, which keeps running while a menu is open, so the menu reader sees
        /// every selection change.
        ///
        /// Order matters, for two reasons:
        ///
        ///  1. The dump key is checked first, so a dump still happens even if a
        ///     reader is having trouble on this screen.
        ///  2. Exactly one reader may speak per frame. ScreenReader handles the
        ///     screens that have no keyboard focus of their own; when it claims the
        ///     screen, MenuReader is skipped. Letting both run would mean the
        ///     leaderboard's own hidden selection and the mod's cursor could each
        ///     announce something, and the player would hear two items per press.
        ///  3. NameEntryReader sits between them because the name screens are a
        ///     mixture: they hold real buttons MenuReader should keep announcing,
        ///     but their text changes without the selection moving, which only this
        ///     reader can see. It speaks only when that text changed, and returns
        ///     false the rest of the time so the buttons still work.
        /// </summary>
        public override void OnUpdate()
        {
            if (Hotkeys.DumpPressed())
                Dump.Write("F8 pressed");

            // Before the readers: the end-of-round score waits here for a menu to
            // join it, and this speaks it alone if none does. Running first means a
            // claim happening on this very frame still wins the race.
            ScoreHandoff.Tick();

            // The pre-game choices have no leaderboard panel or selected widget.
            if (TrackChoiceReader.Tick())
            {
                ScreenReader.ReleaseForOtherReader();
                return;
            }

            // Returns true when it owns the current screen.
            if (ScreenReader.Tick())
                return;

            // Returns true only on the frame it actually spoke.
            if (NameEntryReader.Tick())
                return;

            MenuReader.Tick();
        }

        private static void WriteLine(string stage)
        {
            try
            {
                Assembly self = typeof(Probe).Assembly;

                string buildStamp;
                try
                {
                    // ModuleVersionId changes on every build, so two builds that
                    // share a version string can never be confused for each other.
                    buildStamp = self.ManifestModule.ModuleVersionId.ToString("N");
                }
                catch
                {
                    buildStamp = "unknown";
                }

                string selfWritten;
                try
                {
                    selfWritten = File.GetLastWriteTimeUtc(self.Location)
                        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z";
                }
                catch
                {
                    selfWritten = "unknown";
                }

                Process proc = Process.GetCurrentProcess();

                // Log.Line owns the file and the lock, so the probe and the speech
                // thread can never interleave a half-written line. It also writes
                // UTF-8, which matters for the accented spoken lines.
                Log.Line("stage=" + stage, string.Join(" | ", new[]
                {
                    "BlindIt probe alive",
                    "bits=" + (IntPtr.Size * 8),
                    "pid=" + proc.Id.ToString(CultureInfo.InvariantCulture),
                    "exe=" + SafeExePath(),
                    "loader=MelonLoader " + SafeLoaderVersion(),
                    "mod_mvid=" + buildStamp,
                    "mod_written=" + selfWritten,
                    "unity=" + SafeUnityVersion()
                }));
            }
            catch (Exception ex)
            {
                try
                {
                    MelonLogger.Error("probe failed: " + ex);
                }
                catch
                {
                    // Nothing left to do: never let the probe take the game down.
                }
            }
        }

        private static string SafeExePath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeLoaderVersion()
        {
            try
            {
                return typeof(MelonMod).Assembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeUnityVersion()
        {
            try
            {
                // MelonLoader resolves this before any game code is touched.
                return MelonLoader.InternalUtils.UnityInformationHandler.GameVersion ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
