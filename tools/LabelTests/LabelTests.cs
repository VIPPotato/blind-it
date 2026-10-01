using System;
using System.Collections.Generic;

namespace BlindIt.Harness
{
    /// <summary>
    /// Tests for <see cref="LabelText"/>, the part of the menu reader that turns a
    /// raw TextMeshPro string into the words the player hears.
    ///
    /// Why a test at all: this logic decides whether the player hears "Play" or
    /// hears markup spelled out, and it is the one piece of the screen-text stage
    /// that needs no game to exercise. Every in-game test run costs the player
    /// several minutes, so anything provable on the desktop is proved here first.
    ///
    /// Run:  bash tools/label-tests.sh
    /// </summary>
    internal static class LabelTests
    {
        private static int _passed;
        private static int _failed;

        internal static int Run()
        {
            Console.WriteLine("== label text tests ==");
            Console.WriteLine();

            // --- Plain captions survive untouched -----------------------------------
            Expect("Play", "Play", "a plain caption is unchanged");
            Expect("SOLO", "SOLO", "capitals are left alone (the reader says them fine)");
            Expect("1 Player", "1 Player", "digits are kept");

            // --- Accented text, which is the whole reason the log is UTF-8 -----------
            Expect("Configuración", "Configuración", "accented letters survive");
            Expect("Jouer", "Jouer", "French caption survives");

            // --- TMP rich text is markup the player never sees ----------------------
            Expect("<b>Play</b>", "Play", "bold tags are stripped");
            Expect("<color=#FF0000>Quit</color>", "Quit", "colour tags are stripped");
            Expect("<sprite=0> Continue", "Continue", "a sprite tag is stripped");
            Expect("<size=120%>Options</size>", "Options", "size tags are stripped");

            // --- Whitespace: TMP wraps captions with newlines ------------------------
            Expect("Main\nMenu", "Main Menu", "a newline becomes one space");
            Expect("  Play  ", "Play", "surrounding space is trimmed");
            Expect("Pass\n\n  It", "Pass It", "a run of whitespace collapses to one space");
            Expect("High\tScore", "High Score", "a tab becomes a space");

            // --- Things that are not words: these must be REJECTED -------------------
            ExpectNull(null, "null in, null out");
            ExpectNull("", "an empty string is not speakable");
            ExpectNull("   ", "whitespace only is not speakable");
            ExpectNull("\uE000", "a private-use icon glyph alone is not speakable");
            ExpectNull("<sprite=3>", "a sprite tag alone leaves nothing to say");
            ExpectNull("<b></b>", "empty tags leave nothing to say");

            // Punctuation with no letters or digits is markup or decoration, not a
            // caption. Rejecting it is what stops the reader saying "greater than".
            ExpectNull("---", "a separator row is not speakable");
            ExpectNull("...", "an ellipsis alone is not speakable");

            // --- Mixed: an icon next to a real word keeps the word -------------------
            Expect("\uE000 Back", "Back", "an icon glyph beside a word keeps the word");
            Expect("Play \uE001", "Play", "a trailing icon glyph is dropped");

            // --- Zero-width space, which TMP and localisers both emit ----------------
            Expect("Set\u200Btings", "Settings",
                "a zero-width space inside a word does not split it into two words");

            // --- The unclosed-tag case: the safer failure is keeping the text --------
            // A '<' with no '>' must not swallow the caption. Speaking one stray
            // bracket is far better than the item going silent.
            Expect("<Play", "<Play", "an unclosed tag keeps the text after it");
            Expect("5 < 10", "5 < 10", "a bare less-than between numbers is kept");

            // --- A real-world shape: caption plus a key hint in one string -----------
            Expect("<sprite name=\"btn_a\">  Start Game",
                   "Start Game",
                   "a button prompt sprite plus caption yields just the caption");

            Console.WriteLine();
            Console.WriteLine("passed: " + _passed + ", failed: " + _failed);

            if (_failed == 0)
            {
                Console.WriteLine("ALL LABEL TESTS PASS");
                return 0;
            }

            Console.WriteLine("THERE ARE FAILING LABEL TESTS");
            return 1;
        }

        private static void Expect(string input, string expected, string why)
        {
            string actual = LabelText.Clean(input);

            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                _passed++;
                Console.WriteLine("  PASS  " + why);
                return;
            }

            _failed++;
            Console.WriteLine("  FAIL  " + why);
            Console.WriteLine("          input    : " + Show(input));
            Console.WriteLine("          expected : " + Show(expected));
            Console.WriteLine("          actual   : " + Show(actual));
        }

        private static void ExpectNull(string input, string why)
        {
            string actual = LabelText.Clean(input);

            if (actual == null)
            {
                _passed++;
                Console.WriteLine("  PASS  " + why);
                return;
            }

            _failed++;
            Console.WriteLine("  FAIL  " + why);
            Console.WriteLine("          input    : " + Show(input));
            Console.WriteLine("          expected : null (rejected)");
            Console.WriteLine("          actual   : " + Show(actual));
        }

        /// <summary>
        /// Renders a string with invisible characters made visible, so a failure
        /// message is readable instead of a mystery.
        /// </summary>
        private static string Show(string s)
        {
            if (s == null)
                return "null";

            List<string> parts = new List<string>();
            foreach (char c in s)
            {
                if (c == '\n') parts.Add("\\n");
                else if (c == '\t') parts.Add("\\t");
                else if (c == '\u200B') parts.Add("\\u200B");
                else if (c >= '\uE000' && c <= '\uF8FF')
                    parts.Add("\\u" + ((int)c).ToString("X4"));
                else parts.Add(c.ToString());
            }

            return "\"" + string.Concat(parts) + "\"";
        }
    }
}
