using System.Text;

namespace BlindIt
{
    /// <summary>
    /// Turning a raw UI string into something worth speaking.
    ///
    /// Deliberately separate from <see cref="Label"/> and free of every Unity and
    /// IL2CPP type, for one reason: this is the part most likely to be subtly
    /// wrong, and keeping it pure means the desktop harness can test it without
    /// the game. The project's speech harness already caught a crash-level bug
    /// that way, before it cost the player a test launch.
    /// </summary>
    internal static class LabelText
    {
        /// <summary>
        /// The spoken words for one of this build's four songs, or null for anything
        /// else.
        ///
        /// WHY A CHECKED LIST AND NOT A NAME-TIDYING RULE. The game stores no song
        /// title as text at all: the four titles on the start screen, and the four
        /// headings in the achievement book, are picture files. The game decides which
        /// picture to show by matching the picture object's name against the track's
        /// BackgroundSceneName, so that string is the game's own identity for a song.
        /// For the four shipped tracks those identities were read, and the words in
        /// the pictures were read, and they agree: SHAPES, SPACE, CITY, OFFICE.
        ///
        /// So this translates four known values. A fifth track from a future update
        /// is not in the list and returns null, which the callers report as unknown
        /// rather than speaking an internal asset name such as "Asset 17".
        ///
        /// Matching ignores case and surrounding space, and nothing else: a partial or
        /// decorated name is not accepted.
        ///
        /// Sources: evidence/launch9/source-review/findings.md, "Proven title source".
        /// </summary>
        internal static string SongWords(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            switch (key.Trim().ToUpperInvariant())
            {
                case "SHAPES": return Strings.Get("song.shapes");
                case "SPACE": return Strings.Get("song.space");
                case "CITY": return Strings.Get("song.city");
                case "OFFICE": return Strings.Get("song.office");
                default: return null;
            }
        }

        /// <summary>
        /// Makes a raw UI string speakable, or returns null to reject it.
        ///
        /// Rejects text with no letters or digits at all: TMP labels also carry
        /// icon glyphs and button prompts, whose "text" is a private-use codepoint
        /// that a screen reader reads as nothing or as junk.
        ///
        /// Strips TMP rich-text tags (sprite, colour, bold), which are markup the
        /// player never sees and the screen reader would otherwise spell out.
        ///
        /// Collapses whitespace, including the newlines TMP uses for wrapping,
        /// because a screen reader pauses oddly on them.
        /// </summary>
        internal static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            string stripped = StripTags(raw);

            StringBuilder sb = new StringBuilder(stripped.Length);
            bool lastWasSpace = false;
            bool hasContent = false;

            foreach (char c in stripped)
            {
                // Zero-width characters are invisible to the player: TMP and some
                // localisers insert them as line-break opportunities inside a word.
                // They must be DROPPED, not turned into a space, or a caption like
                // "Settings" is spoken as two words ("Set tings").
                if (c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF')
                    continue;

                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                    continue;
                }

                // Private-use area: a sprite or icon glyph, not a word.
                if (c >= '\uE000' && c <= '\uF8FF')
                    continue;

                if (char.IsLetterOrDigit(c))
                    hasContent = true;

                sb.Append(c);
                lastWasSpace = false;
            }

            if (!hasContent)
                return null;

            string result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }

        /// <summary>
        /// Removes TMP rich-text tags: anything from a '&lt;' to the next '&gt;'.
        ///
        /// An unclosed '&lt;' would otherwise swallow the rest of the string, so a
        /// '&lt;' with no matching '&gt;' is treated as a literal character and the
        /// text after it is kept. That is the safer failure: speaking one stray
        /// bracket beats silently losing a whole caption.
        /// </summary>
        internal static string StripTags(string s)
        {
            int firstOpen = s.IndexOf('<');
            if (firstOpen < 0)
                return s;

            StringBuilder sb = new StringBuilder(s.Length);
            int i = 0;

            while (i < s.Length)
            {
                if (s[i] == '<')
                {
                    int close = s.IndexOf('>', i + 1);
                    if (close < 0)
                    {
                        // No closing bracket: keep the rest literally.
                        sb.Append(s, i, s.Length - i);
                        break;
                    }

                    i = close + 1;
                    continue;
                }

                sb.Append(s[i]);
                i++;
            }

            return sb.ToString();
        }
    }
}
