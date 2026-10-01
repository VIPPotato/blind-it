using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BlindIt
{
    /// <summary>
    /// The mod's only voice. Every sentence the mod says goes through
    /// <see cref="Speak"/>, so the UTF-8 conversion, the owning thread and the
    /// log all live in one place.
    ///
    /// Prism (https://github.com/ethindp/prism, MPL-2.0, v0.18.2) hands the text
    /// to whichever screen reader is running, or to a Windows voice when none is.
    ///
    /// Threading: a Prism backend is not thread safe, so exactly one thread ever
    /// touches it. Callers only push onto a queue, which also means a slow
    /// speech call can never stall a game frame.
    /// </summary>
    internal static class Speech
    {
        // ---- Prism native binding -------------------------------------------------
        //
        // The DLL is loaded by FULL PATH and every DllImport is pinned to that exact
        // handle through a resolver. A bare name lookup could bind to another mod's
        // prism.dll of a different version already loaded in this process.

        private const string PrismLib = "prism";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PrismLogCallback(IntPtr userdata, int level, IntPtr source, IntPtr message);

        [DllImport(PrismLib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_init(IntPtr cfg);

        [DllImport(PrismLib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_registry_create_best(IntPtr ctx);

        [DllImport(PrismLib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_backend_name(IntPtr backend);

        // text is passed as a raw byte[] that we NUL-terminate ourselves: an ANSI
        // marshal would send accented characters as invalid UTF-8 and Prism would
        // silently refuse the whole line.
        // interrupt is a C bool: one byte, not .NET's default four.
        [DllImport(PrismLib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int prism_backend_output(
            IntPtr backend, byte[] text, [MarshalAs(UnmanagedType.U1)] bool interrupt);

        [DllImport(PrismLib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void prism_backend_free(IntPtr backend);

        [DllImport(PrismLib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_error_string(int error);

        [DllImport(PrismLib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_version_string();

        // PrismError values actually used by name, read from
        // lib/prism/include/prism.h of the v0.18.2 release zip.
        private const int PRISM_OK = 0;
        private const int PRISM_ERROR_INTERNAL = 9;
        private const int PRISM_ERROR_INVALID_UTF8 = 13;

        // ---- State ----------------------------------------------------------------

        private readonly struct Utterance
        {
            internal readonly string Text;
            internal readonly bool Interrupt;

            internal Utterance(string text, bool interrupt)
            {
                Text = text;
                Interrupt = interrupt;
            }
        }

        private static readonly BlockingCollection<Utterance> Queue =
            new BlockingCollection<Utterance>(new ConcurrentQueue<Utterance>());

        private static Thread _thread;
        private static IntPtr _ctx = IntPtr.Zero;
        private static IntPtr _backend = IntPtr.Zero;
        private static bool _resolverInstalled;
        private static int _started;

        // The moment until which an interrupt is downgraded, as a Stopwatch tick
        // count. See Hold.
        private static long _holdUntil;

        private static readonly System.Diagnostics.Stopwatch Clock =
            System.Diagnostics.Stopwatch.StartNew();

        /// <summary>True once a backend was created and the mod can be heard.</summary>
        internal static bool Available => _backend != IntPtr.Zero;

        /// <summary>Backend actually chosen (NVDA, JAWS, OneCore...), or null.</summary>
        internal static string BackendName { get; private set; }

        /// <summary>
        /// Protects the sentence just spoken from being cut off for a while.
        ///
        /// WHY THIS EXISTS. The final score was being destroyed by the game's own
        /// menu. Launch 7 proves it: the score went out at 11:58:36.515, and at
        /// 11:58:38.983 the kill screen gave focus to its REPLAY button, which
        /// MenuReader announced with interrupt=true. The player heard the start of
        /// their score and then "retry". The number the whole round was played for
        /// was the one thing they could not hear.
        ///
        /// WHAT A HOLD DOES. During it, a later interrupting line is QUEUED instead
        /// of interrupting: it is downgraded to interrupt=false so the screen reader
        /// finishes the protected sentence and then reads it. Nothing is dropped and
        /// no speech is delayed by a timer, which is why this is preferred to
        /// silencing the menu for a moment: the player still learns that REPLAY has
        /// the focus, just after they learn their score.
        ///
        /// Only the clock is held, not the queue, so a hold left behind by a crash
        /// expires on its own and can never make the mod go quiet.
        /// </summary>
        /// <param name="seconds">
        /// How long to protect. Keep it to the length of the sentence being
        /// protected: a hold longer than that makes the mod feel unresponsive.
        /// </param>
        internal static void Hold(double seconds)
        {
            try
            {
                if (seconds <= 0)
                    return;

                long until = Clock.ElapsedTicks
                    + (long)(seconds * System.Diagnostics.Stopwatch.Frequency);

                // Never shorten a hold already running: two protected lines in a row
                // should both survive.
                if (until > _holdUntil)
                    _holdUntil = until;
            }
            catch
            {
                // A failed hold just means the old behaviour, never a crash.
            }
        }

        // ---- Public API -------------------------------------------------------------

        /// <summary>
        /// Starts the speech thread. Safe to call more than once; only the first
        /// call does anything. Must NOT be called from DllMain: Prism starts
        /// threads and COM, which can deadlock under the loader lock.
        /// </summary>
        internal static void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
                return;

            _thread = new Thread(ThreadMain)
            {
                Name = "BlindIt.Speech",
                IsBackground = true   // never keeps the game alive at exit
            };
            _thread.Start();
        }

        /// <summary>
        /// Says a sentence.
        /// </summary>
        /// <param name="text">What to say. Accents are kept.</param>
        /// <param name="interrupt">
        /// true when this text REPLACES what is being said (the player moved to
        /// another item, an answer to a keypress); false when it follows on (a
        /// description, an extra detail). Passed through untouched: always
        /// interrupting cuts our own sentences, never interrupting lags behind.
        /// </param>
        internal static void Speak(string text, bool interrupt)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                // A protected sentence is still being read: let this one follow it
                // instead of cutting it off. See Hold for why the final score needs
                // this. Downgraded here, at the one place every line passes, so no
                // caller can forget it.
                if (interrupt && Clock.ElapsedTicks < _holdUntil)
                {
                    interrupt = false;
                    Log.Line("speech", "interrupt held back | text=" + Sanitise(text));
                }

                if (!Queue.IsAddingCompleted)
                    Queue.Add(new Utterance(text, interrupt));
            }
            catch (Exception ex)
            {
                // A failure to speak must never take the game down.
                Log.Line("speech", "queue failed: " + ex.Message);
            }
        }

        // ---- The speech thread ------------------------------------------------------

        private static void ThreadMain()
        {
            try
            {
                if (!InstallResolver())
                    return;

                string version = PtrToUtf8(prism_version_string()) ?? "unknown";

                _ctx = prism_init(IntPtr.Zero);
                if (_ctx == IntPtr.Zero)
                {
                    Log.Line("speech", "prism_init returned NULL | prism=" + version +
                                       " | the mod cannot speak");
                    return;
                }

                if (!CreateBackend(version))
                    return;

                foreach (Utterance u in Queue.GetConsumingEnumerable())
                    SpeakNow(u);
            }
            catch (Exception ex)
            {
                Log.Line("speech", "speech thread stopped: " + ex);
            }

            // Deliberately no prism_backend_free / prism_shutdown here: this mod
            // cannot unload while the game keeps running, and tearing down COM and
            // RPC at process exit can crash the game. Prism dies with the process.
        }

        private static bool CreateBackend(string version)
        {
            _backend = prism_registry_create_best(_ctx);
            if (_backend == IntPtr.Zero)
            {
                Log.Line("speech", "no backend available (no screen reader and no " +
                                   "usable Windows voice) | prism=" + version);
                return false;
            }

            BackendName = PtrToUtf8(prism_backend_name(_backend)) ?? "unknown";
            Log.Line("speech", "ready | prism=" + version + " | backend=" + BackendName);
            return true;
        }

        private static void SpeakNow(Utterance u)
        {
            try
            {
                // Valid UTF-8 with a trailing zero. Prism refuses anything else and
                // then says nothing at all, so this conversion is the whole ball game.
                byte[] utf8 = ToUtf8Z(u.Text);

                int rc = prism_backend_output(_backend, utf8, u.Interrupt);

                string result = rc == PRISM_OK
                    ? "ok"
                    : rc + " " + (PtrToUtf8(prism_error_string(rc)) ?? "?");

                // One line per spoken text: this is how the log proves what was really
                // sent, without asking the player to describe anything.
                Log.Line("say", "interrupt=" + (u.Interrupt ? "1" : "0") +
                                " | result=" + result +
                                " | text=" + Sanitise(u.Text));

                if (rc == PRISM_ERROR_INVALID_UTF8)
                {
                    Log.Line("speech", "INVALID_UTF8: the text was not converted " +
                                       "correctly. Fix the conversion, do not strip accents.");
                }
                else if (rc == PRISM_ERROR_INTERNAL)
                {
                    // Typically the screen reader restarted: the old connection is
                    // dead and never retried, so build a fresh backend.
                    Log.Line("speech", "internal error: rebuilding the backend " +
                                       "(the screen reader may have restarted)");
                    RebuildBackend();
                }
                // Any other non-zero code is logged and ignored on purpose. On NVDA
                // and JAWS prism_backend_output speaks first and then returns the
                // result of its braille half, so the line may already have been heard;
                // sending it again would speak it twice.
            }
            catch (Exception ex)
            {
                Log.Line("speech", "output failed: " + ex.Message);
            }
        }

        private static void RebuildBackend()
        {
            try
            {
                IntPtr dead = _backend;
                _backend = IntPtr.Zero;
                if (dead != IntPtr.Zero)
                    prism_backend_free(dead);

                _backend = prism_registry_create_best(_ctx);
                if (_backend == IntPtr.Zero)
                {
                    Log.Line("speech", "rebuild failed: no backend available");
                    return;
                }

                BackendName = PtrToUtf8(prism_backend_name(_backend)) ?? "unknown";
                Log.Line("speech", "backend rebuilt | backend=" + BackendName);
            }
            catch (Exception ex)
            {
                Log.Line("speech", "rebuild threw: " + ex.Message);
            }
        }

        // ---- Helpers ------------------------------------------------------------------

        /// <summary>
        /// Points every [DllImport("prism")] at our own copy, loaded by full path.
        /// </summary>
        private static bool InstallResolver()
        {
            if (_resolverInstalled)
                return true;

            string path = FindPrism();
            if (path == null)
            {
                Log.Line("speech", "prism.dll not found: looked in the mod folder and " +
                                   "in UserLibs. The mod cannot speak.");
                return false;
            }

            if (!NativeLibrary.TryLoad(path, out IntPtr handle))
            {
                Log.Line("speech", "failed to load " + path);
                return false;
            }

            NativeLibrary.SetDllImportResolver(
                typeof(Speech).Assembly,
                (name, asm, search) => name == PrismLib ? handle : IntPtr.Zero);

            _resolverInstalled = true;
            Log.Line("speech", "prism.dll loaded | path=" + path);
            return true;
        }

        /// <summary>
        /// UserLibs is MelonLoader's folder for native libraries; next to the mod is
        /// the fallback. Both are checked as real files so the full path is certain.
        /// </summary>
        private static string FindPrism()
        {
            foreach (string candidate in new[]
                     {
                         Path.Combine(Paths.GameDir, "UserLibs", "prism.dll"),
                         Path.Combine(Paths.ModDir, "prism.dll")
                     })
            {
                try
                {
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch
                {
                    // Unreadable path: just try the next one.
                }
            }

            return null;
        }

        private static byte[] ToUtf8Z(string text)
        {
            // Valid UTF-8 with a trailing zero. Prism refuses anything else and then
            // says nothing at all, so this conversion is the whole ball game.
            // Replacement (not throwing) on a malformed surrogate is deliberate: a
            // stray bad character becomes U+FFFD, which is still valid UTF-8, so the
            // rest of the sentence is spoken instead of the line being dropped.
            byte[] body = Utf8Strict.GetBytes(text);
            byte[] withZero = new byte[body.Length + 1];
            Buffer.BlockCopy(body, 0, withZero, 0, body.Length);
            withZero[body.Length] = 0;
            return withZero;
        }

        private static readonly UTF8Encoding Utf8Strict =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        private static string PtrToUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUTF8(p);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Keeps one log line on one line.</summary>
        private static string Sanitise(string text)
        {
            return text.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
