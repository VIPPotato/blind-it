using System.Globalization;
using System.Text;

namespace BlindIt
{
    /// <summary>
    /// Composes what the mod says into ONE utterance.
    ///
    /// WHY THIS EXISTS. Launch 5 proved the failure it prevents: entering the
    /// leaderboard spoke "Leaderboard. Empty. Press F9 to read items." and 0.75 s
    /// later "6 items. rank # 1, elielgamer17, 739 points, top score. 1 of 6." with
    /// interrupt=1, so the second call cut the first mid-sentence and the player
    /// never heard it. A screen reader cannot be asked to remember a line it was
    /// interrupted out of. The rule that follows from that:
    ///
    ///     ONE event, ONE Speech.Speak call, with every part already joined.
    ///
    /// So nothing in the mod builds a sentence by speaking twice. A caller collects
    /// its parts, hands them to <see cref="Sentences"/> or <see cref="Detail"/>,
    /// and speaks the single string that comes back.
    ///
    /// Deliberately free of every Unity and IL2CPP type, like
    /// <see cref="LabelText"/> and for the same reason: this is composition logic
    /// that decides what the player hears, so the desktop harness must be able to
    /// test it without the game and without spending a player test launch.
    /// </summary>
    internal static class Announcement
    {
        /// <summary>
        /// Joins parts as separate spoken sentences: "Achievements. 8 items."
        ///
        /// Empty and null parts are dropped, so a caller may pass an optional part
        /// without guarding it first. Each part gets a full stop unless it already
        /// ends in one, because a screen reader runs two parts together without it.
        /// Returns null when there was nothing to say.
        /// </summary>
        internal static string Sentences(params string[] parts)
        {
            if (parts == null)
                return null;

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < parts.Length; i++)
            {
                string part = Tidy(parts[i]);
                if (part == null)
                    continue;

                if (sb.Length > 0)
                    sb.Append(' ');

                sb.Append(part);

                if (!EndsSentence(part[part.Length - 1]))
                    sb.Append('.');
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>
        /// Joins a thing and its attributes into one clause: "Credits, 4 of 6."
        ///
        /// This is the form the player asked for on 2026-09-30: the item name, then
        /// its details, separated by commas, in one breath. Commas are what make a
        /// screen reader pause briefly instead of stopping, which is right for
        /// "Music, 8" and "Credits, 4 of 6" where the parts belong to one item.
        ///
        /// A full stop already ending the main part is removed before the comma, so
        /// "Music." plus "8" reads as "Music, 8." and not "Music., 8".
        /// </summary>
        internal static string Detail(string main, params string[] details)
        {
            StringBuilder sb = new StringBuilder();

            string head = Tidy(main);
            if (head != null)
                sb.Append(head);

            if (details != null)
            {
                for (int i = 0; i < details.Length; i++)
                {
                    string detail = Tidy(details[i]);
                    if (detail == null)
                        continue;

                    if (sb.Length > 0)
                    {
                        // Only a full stop is dropped. An exclamation mark is part
                        // of the game's own wording ("25 SUCCESSFUL HITS!") and
                        // removing it would change what the player hears.
                        if (sb[sb.Length - 1] == '.')
                            sb.Length--;

                        sb.Append(", ");
                    }

                    sb.Append(detail);
                }
            }

            if (sb.Length == 0)
                return null;

            if (!EndsSentence(sb[sb.Length - 1]))
                sb.Append('.');

            return sb.ToString();
        }

        /// <summary>
        /// Where an item sits in a list, as words: "4 of 6" for index 3 of 6.
        ///
        /// Spoken on every move by the player's choice (2026-09-30). Returns null
        /// for a list whose size is not known, because "4 of 0" is worse than
        /// saying nothing.
        /// </summary>
        internal static string Position(int index, int count)
        {
            if (count <= 0 || index < 0 || index >= count)
                return null;

            return Strings.Get("list.position",
                (index + 1).ToString(CultureInfo.InvariantCulture),
                count.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A score with its unit, singular when it is exactly one: "1 point",
        /// "260 points", "0 points".
        ///
        /// Both forms come from the catalogue, so a translator supplies the whole
        /// phrase including where the number sits. Languages whose plural rules do
        /// not split at exactly one can give the two keys the same text; nothing
        /// here assumes the English pattern beyond choosing which key to ask for.
        ///
        /// Pure, so the wording is proved by a desktop test rather than by asking
        /// the player to reach a kill screen, which needs a whole round played.
        /// </summary>
        internal static string Points(int value)
        {
            string number = value.ToString(CultureInfo.InvariantCulture);

            return Strings.Get(value == 1 ? "score.points.one" : "score.points.many",
                number);
        }

        /// <summary>
        /// The result of a round, as ONE utterance.
        ///
        /// ONE UTTERANCE IS A REQUIREMENT, not a style choice: the player asked for
        /// it on 2026-10-01 ("The final score should be spoken as one utterance").
        /// Split into two lines, the second half can be cut off by the kill screen
        /// focusing its REPLAY button, which is exactly what happened in launch 7.
        /// Use a comma-separated clause: launch 8 made one speech call, but the
        /// full stop after "Final score" still sounded like two announcements.
        ///
        /// Solo and party rounds say "Final score, 260 points." A one-on-one
        /// round carries two scores, so both are named along with
        /// the outcome. Player numbers rather than the screen's green and yellow
        /// graphics: those are pictures with no text to read, so the colours would
        /// be the mod's invention, while the player numbers come from the score
        /// fields themselves (PlayerStats.Score and Player2Score).
        ///
        /// <paramref name="highScore"/> carries the game's OWN verdict, read from
        /// SoloKillScreenPanel.isHighScore. It is not recomputed here: the game
        /// decides what counts as a personal best, and a second opinion from the mod
        /// could congratulate the player on a record they did not set. Null means
        /// the screen did not say, in which case nothing is claimed either way.
        /// </summary>
        internal static string FinalScore(int score, int otherScore, bool twoPlayers,
            bool? highScore = null)
        {
            // Keep the game's record verdict with the score, without adding a
            // sentence boundary that splits the spoken result.
            string record = highScore == true ? Strings.Get("score.newBest") : null;

            if (!twoPlayers)
                return Detail(Strings.Get("score.final", Points(score)), record);

            string outcome;

            if (score > otherScore)
                outcome = Strings.Get("score.player1Wins");
            else if (otherScore > score)
                outcome = Strings.Get("score.player2Wins");
            else
                outcome = Strings.Get("score.draw");

            return Detail(Strings.Get("score.final.twoPlayers",
                Points(score), Points(otherScore), outcome), record);
        }

        /// <summary>
        /// Which pages of a two-page book are open: "Pages 2 and 3 of 8", or "Page 1
        /// of 8" when only one side is showing.
        ///
        /// Returns null when the numbers cannot be trusted: a closed or mid-turn
        /// EndlessBook reports 0, and a page number beyond the count means the read
        /// caught the book between states. Saying nothing is right in both cases,
        /// because the next tick sees the settled book.
        /// </summary>
        internal static string BookPages(int left, int right, int last)
        {
            if (last <= 0)
                return null;

            if (left > last || right > last)
                return null;

            if (left <= 0 && right <= 0)
                return null;

            // Built from whole sentences in the catalogue rather than glued
            // together from " of " and a number: word order differs between
            // languages, and a translator cannot fix a sentence assembled by code.
            string total = last.ToString(CultureInfo.InvariantCulture);

            if (left > 0 && right > 0 && left != right)
                return Strings.Get("page.two",
                    left.ToString(CultureInfo.InvariantCulture),
                    right.ToString(CultureInfo.InvariantCulture),
                    total);

            int single = left > 0 ? left : right;
            return Strings.Get("page.one",
                single.ToString(CultureInfo.InvariantCulture), total);
        }

        /// <summary>
        /// One achievement as words: its group, its name, its locked state.
        ///
        /// TWO NAMES, ONE ACHIEVEMENT. The game holds the same text twice in
        /// different case: BopItAchievement.Title is title case ("25 Successful
        /// Hits!") and AchievementListItem.Description is upper case ("25
        /// SUCCESSFUL HITS!"). Launch 5 spoke both, so six of eight achievements
        /// said their name twice. They are compared with <see cref="SameWords"/>
        /// here, which ignores case, so the duplicate is dropped. Two of the eight
        /// really are different sentences ("YOU PLAYED BOP IT!" and its Extreme
        /// twin) and must still be heard in full.
        ///
        /// The book also draws a list letter in front of every entry ("a. YOU
        /// PLAYED BOP IT!", from the dump). That letter is dropped: the mod already
        /// says the position ("1 of 8"), so a stray "a" adds nothing but confusion.
        /// </summary>
        internal static string Achievement(string group, string title, string description, string state)
        {
            string drawn = WithoutListMarker(description);
            string name;

            if (string.IsNullOrEmpty(title))
            {
                name = drawn;
            }
            else if (string.IsNullOrEmpty(drawn) || SameWords(title, drawn))
            {
                name = title;
            }
            else
            {
                name = Sentences(title, drawn);
            }

            return Sentences(group, name, state);
        }

        /// <summary>
        /// A caption without the list marker the book draws in front of it, e.g.
        /// "a. YOU PLAYED BOP IT!" becomes "YOU PLAYED BOP IT!". Unchanged when
        /// there is no marker.
        /// </summary>
        internal static string WithoutListMarker(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            int start = SkipListMarker(s);
            return start == 0 ? s : s.Substring(start);
        }

        /// <summary>
        /// Whether two strings say the same words, ignoring case, the list marker
        /// the book draws in front of an entry ("b. ") and any punctuation at the
        /// end. Used to decide whether a second copy of a name adds anything.
        /// </summary>
        internal static bool SameWords(string a, string b)
        {
            if (a == null || b == null)
                return a == null && b == null;

            return string.Equals(Core(a), Core(b), System.StringComparison.Ordinal);
        }

        /// <summary>
        /// The comparable core of a caption: lower case, no leading list marker, no
        /// trailing punctuation, single spaces.
        /// </summary>
        private static string Core(string s)
        {
            int start = SkipListMarker(s);

            StringBuilder sb = new StringBuilder(s.Length - start);
            bool pendingSpace = false;

            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];

                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            // Trailing punctuation carries no words: "hits!" and "hits" are the
            // same name as far as a duplicate check is concerned.
            while (sb.Length > 0 && !char.IsLetterOrDigit(sb[sb.Length - 1]))
                sb.Length--;

            return sb.ToString();
        }

        /// <summary>
        /// The index just past a leading list marker such as "a. " or "12. ", or 0
        /// when there is none. The achievements book draws these in front of every
        /// entry, and they are not part of the name.
        ///
        /// A marker is ONE letter ("a.", "b." on the entries) or ONE OR TWO digits
        /// ("1.", "2." on the subgroup rows, both from the launch-5 dump). Two
        /// letters are NOT a marker: "Ex. tra" is a caption, and cutting it would
        /// make two different names compare as equal and silence a real entry.
        /// </summary>
        private static int SkipListMarker(string s)
        {
            int i = 0;

            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;

            int markerStart = i;
            bool allDigits = true;

            while (i < s.Length && char.IsLetterOrDigit(s[i]))
            {
                if (!char.IsDigit(s[i]))
                    allDigits = false;
                i++;
            }

            int markerLength = i - markerStart;

            if (markerLength == 0)
                return 0;
            if (markerLength > 2)
                return 0;
            if (markerLength == 2 && !allDigits)
                return 0;

            if (i >= s.Length || (s[i] != '.' && s[i] != ')'))
                return 0;

            i++;

            if (i < s.Length && !char.IsWhiteSpace(s[i]))
                return 0;

            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;

            // Nothing after the marker: it was the whole string, so keep it.
            return i >= s.Length ? 0 : i;
        }

        /// <summary>
        /// A caption tidied for speech: no list marker, and ALL CAPS turned into
        /// ordinary words ("CLASSIC" becomes "Classic", "2. EXTREME" becomes
        /// "Extreme").
        ///
        /// Why: this game shouts. Its achievement groups and headings are stored and
        /// drawn upper case, and NVDA can be configured to change pitch on capitals
        /// or to spell them out, which makes a shouted heading tiring to hear on
        /// every move. Mixed-case text is left exactly as it is, because that is the
        /// game's own wording and not something to second-guess.
        ///
        /// Only used for the mod's own headings. An item's real name is never
        /// re-cased: the player should hear what the game wrote.
        /// </summary>
        internal static string Sentence(string s)
        {
            string text = Tidy(WithoutListMarker(s));
            if (text == null)
                return null;

            if (!IsShouted(text))
                return text;

            StringBuilder sb = new StringBuilder(text.Length);
            bool startOfWord = true;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (!char.IsLetter(c))
                {
                    sb.Append(c);
                    // An apostrophe keeps a word going: "PLAYER'S" must not become
                    // "Player'S".
                    startOfWord = c != '\'';
                    continue;
                }

                sb.Append(startOfWord ? c : char.ToLowerInvariant(c));
                startOfWord = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Whether a caption is shouted: it has at least two letters and every one
        /// of them is upper case. A single letter is not enough to tell.
        /// </summary>
        private static bool IsShouted(string s)
        {
            int letters = 0;

            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsLetter(s[i]))
                    continue;

                if (char.IsLower(s[i]))
                    return false;

                letters++;
            }

            return letters >= 2;
        }

        /// <summary>A part with no surrounding space, or null when it says nothing.</summary>
        private static string Tidy(string part)
        {
            if (string.IsNullOrEmpty(part))
                return null;

            string trimmed = part.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static bool EndsSentence(char c)
        {
            return c == '.' || c == '!' || c == '?';
        }
    }
}
