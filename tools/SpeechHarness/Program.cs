using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace BlindIt.Harness
{
    /// <summary>
    /// Exercises the mod's real speech service outside the game.
    ///
    /// Speech, Log and Paths are internal to the mod, and this harness compiles
    /// them in directly, so it reaches them by reflection on its own assembly
    /// rather than forcing the mod to widen anything to public.
    ///
    /// It says the same two proof-of-life sentences the mod says at load, then
    /// prints what the speech log recorded, so a binding, encoding or backend
    /// problem shows up here instead of during a test launch.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool quiet = Array.Exists(args, a => a == "--quiet");

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("== BlindIt speech harness ==");
            Console.WriteLine("Running the mod's real Speech.cs outside the game.");
            Console.WriteLine();

            Assembly self = typeof(Program).Assembly;
            Type speech = self.GetType("BlindIt.Speech");
            if (speech == null)
            {
                Console.Error.WriteLine("FAIL: BlindIt.Speech was not compiled in.");
                return 1;
            }

            // The log lands next to this harness exe, so it never mixes with the
            // real mod log inside the game folder.
            string logPath = Path.Combine(AppContext.BaseDirectory, "BlindIt.log");
            long startOffset = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

            Console.WriteLine("prism.dll search:");
            Console.WriteLine("  game dir : " + Invoke<string>(self, "BlindIt.Paths", "GameDir"));
            Console.WriteLine("  mod dir  : " + Invoke<string>(self, "BlindIt.Paths", "ModDir"));
            Console.WriteLine();

            MethodInfo start = speech.GetMethod("Start",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo speak = speech.GetMethod("Speak",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (start == null || speak == null)
            {
                Console.Error.WriteLine("FAIL: Speech.Start or Speech.Speak not found.");
                return 1;
            }

            start.Invoke(null, null);

            // Give the speech thread a moment to load Prism and pick a backend.
            PropertyInfo available = speech.GetProperty("Available",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            PropertyInfo backendName = speech.GetProperty("BackendName",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            for (int i = 0; i < 100 && !(bool)available.GetValue(null); i++)
                Thread.Sleep(50);

            bool ok = (bool)available.GetValue(null);
            Console.WriteLine("backend available : " + ok);
            Console.WriteLine("backend name      : " + (backendName.GetValue(null) ?? "(none)"));
            Console.WriteLine();

            if (!ok)
            {
                Console.Error.WriteLine("FAIL: no backend. See the log below.");
                DumpLog(logPath, startOffset);
                return 1;
            }

            // The exact sentences the mod says at load.
            string[] lines =
            {
                "BlindIt loaded.",
                "Accent check: café, naïve, jalapeño, fiancée."
            };

            if (quiet)
            {
                Console.WriteLine("--quiet: not speaking, only checking the UTF-8 conversion.");
            }
            else
            {
                Console.WriteLine("Speaking:");
                for (int i = 0; i < lines.Length; i++)
                {
                    Console.WriteLine("  " + (i + 1) + ". " + lines[i]);
                    speak.Invoke(null, new object[] { lines[i], i == 0 });
                }

                // Let the queue drain and the reader finish talking.
                Thread.Sleep(4000);
            }

            Console.WriteLine();
            Console.WriteLine("Checking the UTF-8 conversion byte for byte:");
            if (!CheckUtf8(speech, lines[1]))
                return 1;

            Console.WriteLine();
            Console.WriteLine("Speech log for this run:");
            DumpLog(logPath, startOffset);

            Console.WriteLine();
            Console.WriteLine("== harness done ==");
            return 0;
        }

        /// <summary>
        /// Confirms ToUtf8Z produces exactly the bytes Prism requires: valid UTF-8,
        /// accents intact, one trailing zero and no BOM.
        /// </summary>
        private static bool CheckUtf8(Type speech, string text)
        {
            MethodInfo toUtf8 = speech.GetMethod("ToUtf8Z",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (toUtf8 == null)
            {
                Console.Error.WriteLine("FAIL: ToUtf8Z not found.");
                return false;
            }

            byte[] got = (byte[])toUtf8.Invoke(null, new object[] { text });
            byte[] want = System.Text.Encoding.UTF8.GetBytes(text);

            bool zeroTerminated = got.Length == want.Length + 1 && got[got.Length - 1] == 0;
            bool bodyMatches = true;
            for (int i = 0; i < want.Length && bodyMatches; i++)
                if (got[i] != want[i])
                    bodyMatches = false;

            bool noBom = !(got.Length >= 3 && got[0] == 0xEF && got[1] == 0xBB && got[2] == 0xBF);

            // Round-trip: the bytes must decode back to the same string.
            string roundTrip = System.Text.Encoding.UTF8.GetString(got, 0, got.Length - 1);
            bool roundTrips = roundTrip == text;

            Console.WriteLine("  bytes            : " + got.Length + " (text " + want.Length + " + 1 zero)");
            Console.WriteLine("  NUL-terminated   : " + Verdict(zeroTerminated));
            Console.WriteLine("  body matches     : " + Verdict(bodyMatches));
            Console.WriteLine("  no BOM           : " + Verdict(noBom));
            Console.WriteLine("  round-trips      : " + Verdict(roundTrips));

            // é must be the two bytes C3 A9, not the single byte E9 an ANSI marshal
            // would send. That single byte is what silently kills a whole line.
            int idx = text.IndexOf('é');
            if (idx >= 0)
            {
                byte[] prefix = System.Text.Encoding.UTF8.GetBytes(text.Substring(0, idx));
                bool twoByteE = got.Length > prefix.Length + 1
                                && got[prefix.Length] == 0xC3
                                && got[prefix.Length + 1] == 0xA9;
                Console.WriteLine("  e-acute = C3 A9  : " + Verdict(twoByteE) +
                                  " (got " + got[prefix.Length].ToString("X2") + " " +
                                  got[prefix.Length + 1].ToString("X2") + ")");
                if (!twoByteE)
                    return false;
            }

            return zeroTerminated && bodyMatches && noBom && roundTrips;
        }

        private static string Verdict(bool b) => b ? "PASS" : "FAIL";

        private static T Invoke<T>(Assembly asm, string typeName, string propertyName)
        {
            try
            {
                Type t = asm.GetType(typeName);
                PropertyInfo p = t?.GetProperty(propertyName,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                return (T)p?.GetValue(null);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  (" + propertyName + " threw: " + ex.Message + ")");
                return default;
            }
        }

        private static void DumpLog(string path, long fromOffset)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine("  (no log file at " + path + ")");
                    return;
                }

                using FileStream fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(fromOffset, SeekOrigin.Begin);
                using StreamReader sr = new StreamReader(fs, System.Text.Encoding.UTF8);

                string line;
                bool any = false;
                while ((line = sr.ReadLine()) != null)
                {
                    Console.WriteLine("  " + line);
                    any = true;
                }

                if (!any)
                    Console.WriteLine("  (nothing was logged)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  (could not read the log: " + ex.Message + ")");
            }
        }
    }
}
