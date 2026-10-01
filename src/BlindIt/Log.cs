using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using MelonLoader;

namespace BlindIt
{
    /// <summary>
    /// The mod's log file, <c>Mods\BlindIt.log</c>. Appended across launches
    /// and never rotated, so the whole history of the project's test runs stays
    /// readable.
    ///
    /// Every line is flushed to disk at once, so a crash a moment later still
    /// leaves the line behind. Written as UTF-8 so accented text in a spoken line
    /// survives into the log, which is how an accent problem gets diagnosed
    /// without asking the player to describe what they heard.
    /// </summary>
    internal static class Log
    {
        private const string LogFileName = "BlindIt.log";

        private static readonly object Gate = new object();

        private static readonly UTF8Encoding Utf8NoBom =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static string LogPath => Path.Combine(Paths.ModDir, LogFileName);

        /// <summary>
        /// Writes one line: timestamp, tag, then the message.
        /// Never throws: logging must not be able to take the game down.
        /// </summary>
        internal static void Line(string tag, string message)
        {
            string line = string.Join(" | ", new[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                tag,
                message
            });

            lock (Gate)
            {
                try
                {
                    using (FileStream fs = new FileStream(
                               LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter sw = new StreamWriter(fs, Utf8NoBom))
                    {
                        sw.WriteLine(line);
                        sw.Flush();
                        fs.Flush(true);
                    }
                }
                catch
                {
                    // A locked or full disk must not stop the game.
                }
            }

            // Also into MelonLoader's own log, so the two can be compared.
            // This MUST go through a separate non-inlined method: if the
            // MelonLoader assembly cannot be resolved, the failure happens while
            // the JIT compiles the method that references it, BEFORE any
            // try/catch inside that method is active. Keeping the reference in
            // its own method means the caller's catch really does catch it,
            // instead of an unhandled exception killing the speech thread.
            TryMelonLog(line);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MelonLog(string line)
        {
            MelonLogger.Msg(line);
        }

        private static void TryMelonLog(string line)
        {
            try
            {
                MelonLog(line);
            }
            catch
            {
                // Running outside MelonLoader (the speech harness), or the logger
                // is gone at shutdown. The file log above is the one that matters.
            }
        }
    }
}
