using System;
using System.Diagnostics;
using System.IO;

namespace BlindIt
{
    /// <summary>
    /// The folders the mod needs, worked out once from the running process.
    /// </summary>
    internal static class Paths
    {
        private static readonly Lazy<string> LazyModDir = new Lazy<string>(() =>
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(Paths).Assembly.Location);
                if (!string.IsNullOrEmpty(dir))
                    return dir;
            }
            catch
            {
                // Fall through to the current directory.
            }

            return ".";
        });

        private static readonly Lazy<string> LazyGameDir = new Lazy<string>(() =>
        {
            // The mod lives in <game>\Mods, so the game folder is its parent. The
            // executable's own folder is the cross-check, because a mod loaded from
            // somewhere unexpected would otherwise point at the wrong place.
            try
            {
                string fromExe = Path.GetDirectoryName(
                    Process.GetCurrentProcess().MainModule?.FileName);
                if (!string.IsNullOrEmpty(fromExe) && Directory.Exists(fromExe))
                    return fromExe;
            }
            catch
            {
                // MainModule can be refused; the parent of Mods is good enough.
            }

            try
            {
                string parent = Path.GetDirectoryName(LazyModDir.Value);
                if (!string.IsNullOrEmpty(parent))
                    return parent;
            }
            catch
            {
                // Fall through.
            }

            return ".";
        });

        /// <summary>Folder holding BlindIt.dll, normally &lt;game&gt;\Mods.</summary>
        internal static string ModDir => LazyModDir.Value;

        /// <summary>The game's install folder.</summary>
        internal static string GameDir => LazyGameDir.Value;
    }
}
