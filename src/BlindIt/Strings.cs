using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlindIt
{
    /// <summary>
    /// Every word the mod says, looked up by key so none of it is hardcoded.
    ///
    /// WHY THIS EXISTS. The player asked (2026-10-01) for the mod's own phrases to
    /// be translatable and shipped as JSON files people can contribute to. Before
    /// this, a sentence like "Press right bracket to read items" was a literal in
    /// the middle of the reader's logic, so translating it meant editing C# and
    /// rebuilding. Now the reader asks for a key and the catalogue decides the
    /// words.
    ///
    /// WHAT IS AND IS NOT TRANSLATED HERE. The mod's own words only. Text the GAME
    /// wrote - an achievement name, a player's alias, a song title - is spoken
    /// exactly as the game drew it and never passed through here: the game already
    /// localises its own text, and re-wording it would be the mod inventing content.
    ///
    /// DELIBERATELY FREE OF UNITY. Like <see cref="Announcement"/> and
    /// <see cref="LabelText"/>, this is plain .NET so the desktop harness can prove
    /// the catalogue loads and every key resolves without launching the game. A
    /// missing translation must never silence the mod, so lookup falls back:
    /// chosen language, then English, then the key's built-in English default.
    ///
    /// NO EXTERNAL JSON LIBRARY. The mod cannot assume Newtonsoft is present in the
    /// game's IL2CPP runtime, so <see cref="ParseFlatJson"/> reads the one shape
    /// these files use: a flat object of string keys to string values. That keeps
    /// the shipped files simple enough for a non-programmer to translate.
    /// </summary>
    internal static class Strings
    {
        /// <summary>
        /// The built-in English wording, used when no file is found and as the last
        /// fallback for a key a translator has not reached yet.
        ///
        /// These doubles as the authoritative key list: the English JSON file is
        /// generated from this table, so a new key cannot be forgotten in it.
        /// </summary>
        private static readonly Dictionary<string, string> Defaults =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // The mod announcing itself.
                { "mod.name", "Blind it!" },
                { "mod.ready", "{0} ready" },

                // Review-mode help and list plumbing.
                { "review.help", "Press right bracket to read items" },
                { "review.help.arrows", "Press down arrow or right bracket to read items" },
                { "list.items", "{0} items" },
                { "list.empty", "Empty" },
                { "list.first", "First" },
                { "list.last", "Last" },
                { "list.position", "{0} of {1}" },

                // Screen names.
                { "screen.achievements", "Achievements" },
                { "screen.leaderboard", "Leaderboard" },
                { "screen.trackSelect", "Track select" },
                { "screen.credits", "Credits" },
                { "screen.calibrate", "Audio latency" },
                { "screen.controls", "Controls" },

                // Scores.
                { "score.final", "Final score, {0}" },
                { "score.final.twoPlayers", "Final score, Player 1, {0}, Player 2, {1}, {2}" },
                { "score.points.one", "{0} point" },
                { "score.points.many", "{0} points" },
                { "score.player", "Player {0}" },
                { "score.player1Wins", "Player 1 wins" },
                { "score.player2Wins", "Player 2 wins" },
                { "score.draw", "A draw" },
                { "score.newBest", "New best score" },
                { "score.best", "Best {0}" },
                { "score.player1", "Player 1, {0}" },
                { "score.player2", "Player 2, {0}" },

                // Leaderboard rows.
                { "leaderboard.rank", "rank {0}" },
                { "leaderboard.items", "{0} shown scores" },
                { "leaderboard.window", "Only the scores shown by the game are listed" },
                { "leaderboard.windowAroundYou", "Showing the top score and scores around your position" },
                { "leaderboard.gap.one", "Rank {0} is not shown" },
                { "leaderboard.gap.many", "Ranks {0} through {1} are not shown" },
                { "leaderboard.you", "you" },
                { "leaderboard.topScore", "top score" },
                { "leaderboard.mostRecent", "most recent" },
                { "leaderboard.refreshed", "List updated" },

                // Achievements.
                { "achievement.locked", "locked" },
                { "achievement.unlocked", "unlocked" },

                // Controls menu.
                { "controls.boundTo", "bound to {0}" },
                { "controls.unbound", "not bound" },
                { "controls.rebinding", "Press the new key" },

                // Audio latency calibration.
                { "calibrate.start", "Audio latency test. Press space on each beat to measure your delay." },
                { "calibrate.warmup", "Listen to the beat" },
                { "calibrate.measuring", "Now press space on every beat" },
                { "calibrate.finished", "Finished. Working out your delay." },
                { "calibrate.result", "Your audio delay is {0} milliseconds" },
                { "calibrate.countdown", "{0}" },
                { "calibrate.title", "Audio latency" },
                { "calibrate.latency", "Delay, {0}" },
                { "calibrate.milliseconds", "milliseconds" },
                { "calibrate.finishedResult", "Measurement finished" },

                // Track and device. The bare labels are the row names used when the
                // screen is reviewed item by item; the .spoken forms carry the value
                // and are what the setter hooks announce, so a translator controls
                // the whole sentence and its word order.
                { "track.song", "Song" },
                { "track.device", "Bop it type" },
                { "track.selection", "Bop it type, {0}, song, {1}" },
                { "track.song.spoken", "Song, {0}" },
                { "track.device.spoken", "Bop it type, {0}" },
                { "device.classic", "Classic" },
                { "device.extreme", "Extreme" },
                { "track.unknown", "unknown" },

                // The four song names. The game shows these as artwork rather than
                // text, so the words are ours, matched to a verified track identity
                // (SHAPES, SPACE, CITY, OFFICE) and not to a tidied asset name. They
                // double as the achievement book's four page headings, which are the
                // same four pictures. A translator may change these words; the keys
                // they are matched on are game data and must not be translated.
                { "song.shapes", "Shapes" },
                { "song.space", "Space" },
                { "song.city", "City" },
                { "song.office", "Office" },

                // Leaderboard date filters. The game names these objects Today,
                // Month and AllTime; these are the words the player hears instead.
                { "filter.allTime", "All time" },
                { "filter.thisMonth", "This month" },
                { "filter.today", "Today" },

                // The achievements book's page counter.
                { "page.one", "Page {0} of {1}" },
                { "page.two", "Pages {0} and {1} of {2}" },

                // Entering a leaderboard name.
                { "name.enter", "Enter your name" },
                { "name.waiting", "Waiting" },
                { "name.choose", "Choose a name, use the game's own keys to cycle" },
                { "name.type", "Type a new name" },
                { "name.saved", "Name saved" },
            };

        private static readonly object Gate = new object();

        /// <summary>The chosen language's words, or null when only English applies.</summary>
        private static Dictionary<string, string> _chosen;

        /// <summary>The English file's words, the fallback behind a partial translation.</summary>
        private static Dictionary<string, string> _english;

        /// <summary>The language code actually in use, for the log and the dump.</summary>
        internal static string Language { get; private set; } = "en";

        /// <summary>Where the words came from, for the log and the dump.</summary>
        internal static string Source { get; private set; } = "built-in";

        /// <summary>
        /// The words for a key, with {0}, {1} placeholders filled in.
        ///
        /// Never returns null and never throws: a reader asking for a key must get
        /// something sayable even if the catalogue is broken, because a silent mod
        /// is the one failure the player cannot diagnose.
        /// </summary>
        internal static string Get(string key, params object[] args)
        {
            string format = Lookup(key);

            if (args == null || args.Length == 0)
                return format;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                // A translator wrote a placeholder the key does not have. Say the
                // untranslated English rather than nothing at all.
                string fallback = Defaults.TryGetValue(key, out string d) ? d : key;

                try
                {
                    return string.Format(CultureInfo.InvariantCulture, fallback, args);
                }
                catch (FormatException)
                {
                    return fallback;
                }
            }
        }

        /// <summary>
        /// A score with its unit, singular when it is exactly one. Separate from
        /// <see cref="Get"/> because the singular and plural are different keys and
        /// every caller would otherwise repeat that choice.
        /// </summary>
        internal static string Points(int value)
        {
            return Get(value == 1 ? "score.points.one" : "score.points.many",
                value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The raw words for a key, no placeholder filling.</summary>
        private static string Lookup(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            lock (Gate)
            {
                if (_chosen != null && _chosen.TryGetValue(key, out string chosen)
                    && !string.IsNullOrEmpty(chosen))
                    return chosen;

                if (_english != null && _english.TryGetValue(key, out string english)
                    && !string.IsNullOrEmpty(english))
                    return english;
            }

            // The built-in English is the floor: a key is always answerable.
            return Defaults.TryGetValue(key, out string fallback) ? fallback : key;
        }

        /// <summary>
        /// Chooses the language and loads its file.
        ///
        /// <paramref name="requested"/> is the mod's config override, or null to
        /// follow <paramref name="gameLanguage"/>, which is the game's own setting.
        /// The player chose that order on 2026-10-01: follow the game, with a config
        /// file to override it.
        ///
        /// <paramref name="read"/> returns a file's text or null when it is absent,
        /// so the desktop harness can prove this without a disk.
        /// </summary>
        internal static void Load(string requested, string gameLanguage,
            Func<string, string> read)
        {
            string code = Normalise(
                !string.IsNullOrWhiteSpace(requested) && requested.Trim() != "auto"
                    ? requested
                    : gameLanguage);

            Dictionary<string, string> english = null;
            Dictionary<string, string> chosen = null;
            string source;

            string englishText = Safely(read, "en");
            if (englishText != null)
                english = ParseFlatJson(englishText);

            if (code == "en" || string.IsNullOrEmpty(code))
            {
                code = "en";
                source = english != null ? "en.json" : "built-in";
            }
            else
            {
                string chosenText = Safely(read, code);

                if (chosenText == null && code.Length > 2)
                {
                    // "pt-BR" with no file of its own falls back to "pt".
                    string bare = code.Substring(0, 2);
                    chosenText = Safely(read, bare);
                    if (chosenText != null)
                        code = bare;
                }

                if (chosenText != null)
                {
                    chosen = ParseFlatJson(chosenText);
                    source = code + ".json";
                }
                else
                {
                    // No file for the game's language: English is not a failure,
                    // it is the mod still working.
                    source = english != null ? "en.json (no " + code + ".json)"
                        : "built-in (no " + code + ".json)";
                    code = "en";
                }
            }

            lock (Gate)
            {
                _english = english;
                _chosen = chosen;
                Language = code;
                Source = source;
            }
        }

        private static string Safely(Func<string, string> read, string code)
        {
            if (read == null)
                return null;

            try
            {
                return read(code);
            }
            catch
            {
                // An unreadable file is a missing file as far as the mod cares.
                return null;
            }
        }

        /// <summary>
        /// A language code reduced to the form the files use: lower case, hyphen
        /// separated, no surrounding space. "en-GB  " becomes "en-gb", "English"
        /// stays "english" and simply will not match a file, which is correct.
        /// </summary>
        internal static string Normalise(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "en";

            string trimmed = code.Trim().Replace('_', '-');
            return trimmed.ToLowerInvariant();
        }

        /// <summary>
        /// The English catalogue as pretty-printed JSON, used to write the shipped
        /// en.json so the file and the code can never disagree.
        ///
        /// Keys come out sorted, which keeps a translator's diff readable.
        /// </summary>
        internal static string EnglishJson()
        {
            List<string> keys = new List<string>(Defaults.Keys);
            keys.Sort(StringComparer.Ordinal);

            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");

            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append("  \"").Append(Escape(keys[i])).Append("\": \"")
                    .Append(Escape(Defaults[keys[i]])).Append('"');

                if (i < keys.Count - 1)
                    sb.Append(',');

                sb.Append('\n');
            }

            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>Every key the mod can say, for the harness's completeness test.</summary>
        internal static IEnumerable<string> Keys => Defaults.Keys;

        private static string Escape(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 8);

            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reads a flat JSON object of string to string.
        ///
        /// Only this one shape is supported, on purpose: the translation files are
        /// written by hand by people who are not programmers, and a full JSON parser
        /// would accept nested structures the mod has no use for. Anything it cannot
        /// make sense of is skipped rather than thrown, so one bad line in a
        /// community translation costs that one phrase and not the whole mod.
        /// </summary>
        internal static Dictionary<string, string> ParseFlatJson(string text)
        {
            Dictionary<string, string> map =
                new Dictionary<string, string>(StringComparer.Ordinal);

            if (string.IsNullOrEmpty(text))
                return map;

            int i = 0;
            int n = text.Length;

            while (i < n && text[i] != '{')
                i++;
            if (i < n)
                i++;

            while (i < n)
            {
                string key = NextString(text, ref i);
                if (key == null)
                    break;

                while (i < n && text[i] != ':' && text[i] != '}')
                    i++;
                if (i >= n || text[i] == '}')
                    break;
                i++;

                string value = NextString(text, ref i);
                if (value == null)
                    break;

                // A later duplicate key wins; that is what a hand-edited file most
                // likely means by it.
                map[key] = value;
            }

            return map;
        }

        /// <summary>
        /// The next double-quoted string at or after <paramref name="i"/>, with
        /// escapes decoded, or null when there is none left. Advances
        /// <paramref name="i"/> past the closing quote.
        /// </summary>
        private static string NextString(string text, ref int i)
        {
            int n = text.Length;

            while (i < n && text[i] != '"')
            {
                // A closing brace before the next quote ends the object.
                if (text[i] == '}')
                    return null;
                i++;
            }

            if (i >= n)
                return null;

            i++;
            StringBuilder sb = new StringBuilder();

            while (i < n)
            {
                char c = text[i];

                if (c == '"')
                {
                    i++;
                    return sb.ToString();
                }

                if (c == '\\' && i + 1 < n)
                {
                    i++;
                    char e = text[i];

                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'u':
                            if (i + 4 < n && TryHex(text, i + 1, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default:
                            sb.Append(e);
                            break;
                    }

                    i++;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            // Unterminated string: keep what was readable.
            return sb.ToString();
        }

        private static bool TryHex(string text, int start, out int value)
        {
            value = 0;

            for (int k = 0; k < 4; k++)
            {
                char c = text[start + k];
                int digit;

                if (c >= '0' && c <= '9')
                    digit = c - '0';
                else if (c >= 'a' && c <= 'f')
                    digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F')
                    digit = c - 'A' + 10;
                else
                    return false;

                value = (value << 4) | digit;
            }

            return true;
        }
    }
}
