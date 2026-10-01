using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlindIt;
using Il2Cpp;

namespace ReaderTests
{
    internal static class Program
    {
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const string StartWords = "Audio latency test. Press space on each beat to measure your delay.";
        private const string WarmupWords = "Listen to the beat";
        private const string MeasuringWords = "Now press space on every beat";
        private const string ResultWords = "Measurement finished";
        private static readonly Regex Position = new Regex(@"\b\d+ of \d+\b", RegexOptions.CultureInvariant);
        private static readonly Regex ItemCount = new Regex(@"\b\d+ items\b", RegexOptions.CultureInvariant);

        private sealed class AssertionFailure : Exception
        {
            internal AssertionFailure(string message) : base(message) { }
        }
        private sealed record TestResult(string Name, string Status, string Detail,
            Utterance[] Speech, string[] ReaderLog);

        public static int Main(string[] args)
        {
            string jsonPath = null;
            if (args.Length == 2 && args[0] == "--json") jsonPath = args[1];
            else if (args.Length != 0)
            {
                Console.Error.WriteLine("Usage: ReaderTests.dll [--json REPORT_PATH]");
                return 2;
            }

            var results = new List<TestResult>();
            var sources = EmbeddedSourceHashes();
            var metadata = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
            Console.WriteLine("COVERAGE: complete linked ScreenReader.cs; managed fake engine; no game, NVDA or hooks.");
            Console.WriteLine("READER_SOURCE: " + metadata["ReaderSource"]);
            foreach (var source in sources) Console.WriteLine("SOURCE_SHA256: " + source.Key + " " + source.Value);

            (string Name, Action Body)[] tests =
            {
                ("calibration_stage_cycle_one_automatic_each", StageCycle),
                ("calibration_context_change_does_not_poll_duplicate", PollBoundary),
                ("calibration_unchanged_stages_stay_silent", UnchangedStages),
                ("calibration_return_to_start_announces_again", ReturnToStart),
                ("calibration_manual_repeat_is_audible", ManualRepeat),
                ("calibration_repeat_before_late_poll_is_audible", RepeatBeforePoll),
                ("calibration_entry_has_no_spoken_positions", EntryHasNoPositions),
                ("calibration_repeat_has_no_spoken_positions", RepeatHasNoPositions),
                ("calibration_review_has_no_spoken_positions", ReviewHasNoPositions),
                ("calibration_item_counts_are_suppressed", CountsAreSuppressed),
                ("leaderboard_empty_to_filled_announces_once", LeaderboardLateFill),
                ("leaderboard_browse_marks_rank_gap_both_directions", LeaderboardGapRoundTrip),
                ("leaderboard_rank_snapshot_refreshes_with_text", LeaderboardSnapshotRefresh),
                ("leaderboard_late_fill_queues_behind_entry", LeaderboardFillQueues),
                ("calibration_exit_reentry_announces_again", Reentry)
            };

            foreach (var test in tests)
            {
                string status = "pass";
                string detail = "";
                try
                {
                    Reset();
                    test.Body();
                    CheckReaderErrors();
                    Check(Speech.Spoken.All(s => !string.IsNullOrWhiteSpace(s.Text)), "Reader emitted a blank utterance.");
                    Check(Speech.Spoken.Where(s => s.Input != "automatic").All(s => s.Interrupt),
                        "Manual navigation/repeat must interrupt. Automatic late fill may queue.");
                }
                catch (AssertionFailure ex) { status = "fail"; detail = ex.Message; }
                catch (Exception ex) { status = "error"; detail = ex.ToString(); }
                results.Add(new TestResult(test.Name, status, detail, Speech.Spoken.ToArray(), Log.Lines.ToArray()));
                Console.WriteLine(status.ToUpperInvariant() + ": " + test.Name);
                if (detail.Length > 0) Console.WriteLine("  " + detail.Replace("\n", "\n  "));
                foreach (Utterance utterance in Speech.Spoken)
                    Console.WriteLine($"  SAY frame={utterance.Frame} input={utterance.Input} interrupt={utterance.Interrupt}: {utterance.Text}");
            }

            int passed = results.Count(t => t.Status == "pass");
            int failed = results.Count(t => t.Status == "fail");
            int errors = results.Count(t => t.Status == "error");
            int exit = errors != 0 ? 2 : failed != 0 ? 1 : 0;
            Console.WriteLine($"RESULT: {passed} passed, {failed} failed, {errors} errors, {results.Count} total; exit={exit}");
            if (jsonPath != null)
            {
                string path = Path.GetFullPath(jsonPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    Coverage = "Full linked reader, fake scene/input/speech. Not Unity visibility, IL2CPP, hooks or NVDA proof.",
                    Metadata = metadata, Sources = sources,
                    Passed = passed, Failed = failed, Errors = errors, Total = results.Count, ExitCode = exit,
                    Tests = results
                }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            }
            return exit;
        }

        private static Dictionary<string, string> EmbeddedSourceHashes()
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] required = { "ScreenReader.cs", "LabelText.cs", "Announcement.cs", "Strings.cs" };
            Assembly assembly = typeof(Program).Assembly;
            string[] resources = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("Production.", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            foreach (string name in required)
                if (!resources.Contains("Production." + name))
                    throw new InvalidOperationException("Missing embedded source receipt: " + name);
            foreach (string resource in resources)
            {
                using Stream stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException("Unreadable embedded source receipt: " + resource);
                hashes.Add(resource.Substring("Production.".Length), Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
            }
            return hashes;
        }

        private static void Reset()
        {
            // These anchors are shared by old and current source. Never skip a missing anchor.
            foreach (string method in new[] { "Tick", "Enter", "Release", "SpeakContextChange", "PollForLateItems",
                "CountItems", "Announce", "Repeat", "Resnapshot", "ReadContext", "ReadItems", "CalibrateStatus" })
                _ = Method(method);
            Invoke("Release", "harness fixture reset");
            Set("_screen", null);
            Set("_cursor", 0);
            Set("_items", new List<string>());
            Set("_lastCount", 0);
            Set("_sinceDetect", 0);
            Set("_sinceEmptyPoll", 0);
            Set("_lastContext", null);
            FakeWorld.Reset();
            Strings.Load("en", "en", _ => null);
        }

        private static MethodInfo Method(string name) => typeof(ScreenReader).GetMethod(name, PrivateStatic)
            ?? throw new InvalidOperationException("Required reader method missing or changed: " + name);
        private static object Invoke(string name, params object[] args) => Method(name).Invoke(null, args);
        private static void Set(string name, object value)
        {
            var field = typeof(ScreenReader).GetField(name, PrivateStatic)
                ?? throw new InvalidOperationException("Required reader field missing or changed: " + name);
            field.SetValue(null, value);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new AssertionFailure(message);
        }
        private static void CheckReaderErrors()
        {
            string[] errors = Log.Lines.Where(s => s.Contains("failed", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException("Reader exception/error path reached; not accepted as regression proof: "
                    + string.Join(" | ", errors));
        }
        private static bool Frame(string input = "automatic", bool requireClaimed = true)
        {
            FakeWorld.Frame++;
            FakeWorld.Input = input;
            Hotkeys.NextDown = input == "next";
            Hotkeys.PreviousDown = input == "previous";
            Hotkeys.RepeatDown = input == "repeat";
            bool claimed;
            try { claimed = ScreenReader.Tick(); }
            finally { Hotkeys.Reset(); FakeWorld.Input = "automatic"; }
            CheckReaderErrors();
            if (requireClaimed && !claimed)
                throw new InvalidOperationException("Fake fixture lost its claimed screen at frame " + FakeWorld.Frame);
            return claimed;
        }
        private static void Idle(int frames = 60)
        {
            for (int i = 0; i < frames; i++) Frame();
        }
        private static void Claim(string screen)
        {
            for (int i = 0; i < 20 && ScreenReader.DebugScreen != screen; i++) Frame(requireClaimed: false);
            if (ScreenReader.DebugScreen != screen)
                throw new InvalidOperationException("Fake fixture did not enter " + screen + "; got " + ScreenReader.DebugScreen);
        }
        private static CalibratePanel OpenCalibrate(CalibrateState stage = CalibrateState.Start)
        {
            var panel = FakeWorld.Add<CalibratePanel>("Calibration fixture");
            panel.countdown = FakeWorld.Text("Countdown fixture", null, panel.gameObject);
            panel.latency = FakeWorld.Text("Latency fixture", null, panel.gameObject);
            SetStage(panel, stage);
            Claim("calibrate");
            return panel;
        }
        private static void SetStage(CalibratePanel panel, CalibrateState stage)
        {
            panel.State = stage;
            panel.countdown.text = stage == CalibrateState.Result || stage == CalibrateState.Finished ? null : "3";
            panel.latency.text = stage == CalibrateState.Result ? "42 milliseconds" : null;
        }
        private static Utterance[] Since(int offset) => Speech.Spoken.Skip(offset).ToArray();
        private static void OneStage(int offset, string words, string reason)
        {
            Utterance[] utterances = Since(offset);
            Check(utterances.Length == 1, reason + ": expected 1 utterance, got " + utterances.Length);
            Check(utterances[0].Text.Contains(words.TrimEnd('.'), StringComparison.Ordinal),
                reason + ": wrong stage: " + utterances[0].Text);
        }
        private static void NoPositions(IEnumerable<Utterance> utterances, string reason)
        {
            foreach (Utterance utterance in utterances)
                Check(!Position.IsMatch(utterance.Text), reason + ": unwanted position in " + utterance.Text);
        }

        private static void StageCycle()
        {
            var panel = OpenCalibrate();
            var counts = new List<string>();
            Idle();
            counts.Add("Start=" + Speech.Spoken.Count);
            foreach (CalibrateState stage in new[] { CalibrateState.Warmup, CalibrateState.Calibrate,
                CalibrateState.Result, CalibrateState.Start })
            {
                int offset = Speech.Spoken.Count;
                SetStage(panel, stage);
                Frame();
                Idle(); // Sixty genuinely idle calls to the production Tick after EVERY stage.
                counts.Add(stage + "=" + Since(offset).Length);
            }
            string[] expected =
            {
                "Audio latency. Audio latency test. Press space on each beat to measure your delay.",
                "Listen to the beat.", "Now press space on every beat.", "Measurement finished.", StartWords
            };
            Check(Speech.Spoken.Count == expected.Length,
                "Expected one automatic utterance per visit (5 total); actual " + string.Join(", ", counts)
                + "; total=" + Speech.Spoken.Count);
            Check(Speech.Spoken.Select(s => s.Text).SequenceEqual(expected),
                "Stage speech order/text differs from the exact five expected English utterances.");
            Check(Speech.Spoken.All(s => s.Input == "automatic"), "Cycle unexpectedly used manual input.");
        }

        private static void PollBoundary()
        {
            var panel = OpenCalibrate();
            Idle();
            int offset = Speech.Spoken.Count;
            SetStage(panel, CalibrateState.Warmup);
            Frame();
            int immediately = Since(offset).Length;
            Idle(29);
            int after29 = Since(offset).Length;
            Idle(1);
            int after30 = Since(offset).Length;
            Idle(30);
            int after60 = Since(offset).Length;
            Check(immediately == 1 && after29 == 1 && after30 == 1 && after60 == 1,
                $"Automatic utterance counts at +0/+29/+30/+60 idle frames must be 1/1/1/1, got "
                + $"{immediately}/{after29}/{after30}/{after60}. Late-items poll must not re-speak the stage.");
            OneStage(offset, WarmupWords, "Warmup transition");
        }

        private static void UnchangedStages()
        {
            var panel = OpenCalibrate();
            Idle();
            foreach (CalibrateState stage in new[] { CalibrateState.Start, CalibrateState.Warmup,
                CalibrateState.Calibrate, CalibrateState.Finished, CalibrateState.Result })
            {
                SetStage(panel, stage);
                Frame();
                Idle();
                int offset = Speech.Spoken.Count;
                for (int repetition = 0; repetition < 3; repetition++)
                {
                    SetStage(panel, stage);
                    Idle();
                }
                Check(Since(offset).Length == 0, "Settled unchanged stage repeated: " + stage);
            }
        }

        private static void ReturnToStart()
        {
            var panel = OpenCalibrate();
            Idle();
            foreach (CalibrateState stage in new[] { CalibrateState.Warmup, CalibrateState.Calibrate, CalibrateState.Result })
            {
                SetStage(panel, stage);
                Frame();
                Idle();
            }
            int offset = Speech.Spoken.Count;
            SetStage(panel, CalibrateState.Start);
            Frame();
            OneStage(offset, StartWords, "A genuine return to Start must be audible immediately");
            Idle(); // Whole-cycle test also requires that this visit is not repeated later.
        }

        private static void ManualRepeat()
        {
            OpenCalibrate(CalibrateState.Warmup);
            Idle();
            int first = Speech.Spoken.Count;
            Frame("repeat");
            OneStage(first, WarmupWords, "First manual repeat");
            int second = Speech.Spoken.Count;
            Frame("repeat");
            OneStage(second, WarmupWords, "Second identical manual repeat");
            Check(Speech.Spoken[first].Text == Speech.Spoken[second].Text,
                "Unchanged manual repeats should carry the same words, not be globally deduplicated.");
            Check(Since(first).All(s => s.Input == "repeat"), "Repeat used the wrong input path.");
            int offset = Speech.Spoken.Count;
            Idle();
            Check(Since(offset).Length == 0, "Manual repeat caused unsolicited later speech.");
        }

        private static void RepeatBeforePoll()
        {
            var panel = OpenCalibrate();
            Idle();
            SetStage(panel, CalibrateState.Calibrate);
            Frame();
            int offset = Speech.Spoken.Count;
            Frame("repeat"); // Snapshot refresh is production Repeat -> Resnapshot -> ReadItems.
            OneStage(offset, MeasuringWords, "Repeat directly after a context change");
            int afterRepeat = Speech.Spoken.Count;
            Idle();
            Check(Since(afterRepeat).Length == 0, "Repeat introduced a late automatic duplicate.");
        }

        private static void EntryHasNoPositions()
        {
            OpenCalibrate();
            Idle();
            OneStage(0, StartWords, "Calibration entry");
            NoPositions(Speech.Spoken, "Calibration entry");
            Check(Speech.Spoken[0].Text == "Audio latency. " + StartWords, "Wrong calibration entry wording.");
        }

        private static void RepeatHasNoPositions()
        {
            OpenCalibrate(CalibrateState.Result);
            Idle();
            int offset = Speech.Spoken.Count;
            Frame("repeat");
            OneStage(offset, ResultWords, "Result manual repeat");
            NoPositions(Since(offset), "Calibration manual repeat");
            Check(Speech.Spoken[offset].Text == "Audio latency. Measurement finished.", "Wrong result repeat wording.");
        }

        private static void ReviewHasNoPositions()
        {
            var panel = OpenCalibrate(CalibrateState.Result);
            // Include all three kinds of review line, through actual ReadCalibrate.
            panel.countdown.text = "3";
            Idle();
            Frame("repeat"); // Refresh the snapshot to include the test countdown.
            int offset = Speech.Spoken.Count;
            Frame("next");
            Frame("next");
            Frame("previous");
            Utterance[] spoken = Since(offset);
            Check(spoken.Length == 3, "Expected one utterance per review move.");
            NoPositions(spoken, "Calibration review");
            Check(spoken.Select(s => s.Text.TrimEnd('.')).SequenceEqual(new[] { "3", "Delay, 42 milliseconds", "3" }),
                "Review did not expose countdown, latency, then countdown from actual source.");
        }

        private static void CountsAreSuppressed()
        {
            Check(Invoke("CountItems", "calibrate", 3) == null, "Calibration must not announce item counts.");
            string ordinaryCount = (string)Invoke("CountItems", "leaderboard", 3);
            Check(!string.IsNullOrWhiteSpace(ordinaryCount) && ordinaryCount.Contains("3", StringComparison.Ordinal),
                "Ordinary list counts were suppressed too. Exact leaderboard wording is outside this scheduling test.");
            OpenCalibrate();
            Idle();
            Check(Speech.Spoken.All(s => !ItemCount.IsMatch(s.Text)), "Calibration entry announced an item count.");
        }

        private static void LeaderboardLateFill()
        {
            var board = FakeWorld.Add<CombinedLeaderboardPanel>("Leaderboard fixture");
            board.Mode = CombinedLeaderboardMode.MainMenu;
            var group = FakeWorld.Add<GroupFilterTab>("Global", board.gameObject);
            group.IsActive = true;
            group.Text = FakeWorld.Text("Group text", "Global", group.gameObject);
            FakeWorld.Add<DateFilterTab>("AllTime", board.gameObject).IsActive = true;
            Claim("leaderboard");
            Idle();
            Check(Speech.Spoken.Count == 1 && Speech.Spoken[0].Text.Contains("Empty", StringComparison.Ordinal),
                "An empty leaderboard must announce empty once, then wait.");

            var row = FakeWorld.Add<LeaderboardLineItem>("First score fixture", board.gameObject);
            row.Rank = 1;
            row.RankText = FakeWorld.Text("Rank", "1", row.gameObject);
            row.AliasText = FakeWorld.Text("Alias", "Test Player", row.gameObject);
            row.ScoreText = FakeWorld.Text("Score", "739", row.gameObject);
            row.ItemType = LeaderboardItemType.First;
            int offset = Speech.Spoken.Count;
            int fillFrame = FakeWorld.Frame;
            Idle();
            Utterance[] filled = Since(offset);
            Check(filled.Length == 1, "Empty-to-filled leaderboard: expected 1 automatic utterance, got " + filled.Length);
            Check(filled[0].Frame > fillFrame && filled[0].Frame <= fillFrame + 30,
                "Late leaderboard content was not reported within the existing 30-frame poll interval.");
            Check(filled[0].Text.Contains("Test Player", StringComparison.Ordinal)
                && filled[0].Text.Contains("739", StringComparison.Ordinal), "Late fill spoke the wrong row.");
            Check(filled[0].Text.Contains("1 of 1", StringComparison.Ordinal), "Leaderboard positions were suppressed globally.");
            Check(filled[0].Input == "automatic", "Late fill required manual input.");
            int afterFill = Speech.Spoken.Count;
            row.ScoreText.text = "800";
            Idle();
            Check(Since(afterFill).Length == 0, "A non-empty leaderboard refresh should not interrupt the player.");
        }

        private static LeaderboardLineItem AddRank(CombinedLeaderboardPanel board, int rank, bool own = false)
        {
            var row = FakeWorld.Add<LeaderboardLineItem>("Rank fixture " + rank, board.gameObject);
            row.Rank = rank;
            row.RankText = FakeWorld.Text("Rank", "# " + rank, row.gameObject);
            row.AliasText = FakeWorld.Text("Alias", "Fixture " + rank, row.gameObject);
            row.ScoreText = FakeWorld.Text("Score", "100", row.gameObject);
            row.ItemType = own ? LeaderboardItemType.Me : rank == 1
                ? LeaderboardItemType.First : LeaderboardItemType.Normal;
            return row;
        }

        private static (CombinedLeaderboardPanel Board, LeaderboardLineItem Middle) OpenRankWindow()
        {
            var board = FakeWorld.Add<CombinedLeaderboardPanel>("Rank window fixture");
            board.Mode = CombinedLeaderboardMode.MainMenu;
            AddRank(board, 1);
            var middle = AddRank(board, 81);
            AddRank(board, 82, true);
            Claim("leaderboard");
            Idle();
            return (board, middle);
        }

        private static void LeaderboardGapRoundTrip()
        {
            OpenRankWindow();
            Check(Speech.Spoken[0].Text.Contains("Showing the top score and scores around your position"),
                "Entry must explain the observed top/own-score window.");
            int offset = Speech.Spoken.Count;
            Frame("next");
            Check(Since(offset).Length == 1 && Speech.Spoken[offset].Text ==
                "Ranks 2 through 80 are not shown. rank 81, Fixture 81, 100 points, 2 of 3.",
                "Forward gap and rank must reach speech once, with no hash marker.");
            offset = Speech.Spoken.Count;
            Frame("next");
            Check(Since(offset).Length == 1 && !Speech.Spoken[offset].Text.Contains("not shown"),
                "Adjacent ranks must not announce a gap.");
            Frame("previous");
            offset = Speech.Spoken.Count;
            Frame("previous");
            Check(Since(offset).Length == 1 && Speech.Spoken[offset].Text ==
                "Ranks 2 through 80 are not shown. rank 1, Fixture 1, 100 points, top score, 1 of 3.",
                "Backward crossing must announce the same missing range in one call.");
        }

        private static void LeaderboardSnapshotRefresh()
        {
            var fixture = OpenRankWindow();
            fixture.Middle.Rank = 2;
            fixture.Middle.RankText.text = "# 2";
            fixture.Middle.AliasText.text = "Refreshed fixture";
            int offset = Speech.Spoken.Count;
            Frame("next");
            Check(Since(offset).Length == 1 && Speech.Spoken[offset].Text ==
                "Ranks 2 through 80 are not shown. rank 81, Fixture 81, 100 points, 2 of 3.",
                "Live row edits must not change the captured rank metadata or row text.");
            offset = Speech.Spoken.Count;
            Frame("repeat");
            Check(Since(offset).Length == 1 && Speech.Spoken[offset].Text.Contains("rank 2, Refreshed fixture"),
                "Explicit repeat must refresh the captured row text.");
            offset = Speech.Spoken.Count;
            Frame("previous");
            Check(Since(offset).Length == 1 && !Speech.Spoken[offset].Text.Contains("not shown"),
                "Explicit repeat must refresh rank metadata together with its text.");
        }

        private static void LeaderboardFillQueues()
        {
            LeaderboardLateFill();
            Check(!Speech.Spoken.Last().Interrupt,
                "Automatic late fill must queue rather than cut off the entry explanation.");
        }

        private static void Reentry()
        {
            var panel = OpenCalibrate();
            Idle();
            panel.IsVisible = false; // Controlled fake input only; not a visibility correctness test.
            for (int i = 0; i < 10; i++) Frame(requireClaimed: false);
            Check(ScreenReader.DebugScreen == null, "Fixture did not release its closed panel.");
            int offset = Speech.Spoken.Count;
            panel.IsVisible = true;
            Claim("calibrate");
            Idle();
            OneStage(offset, StartWords, "Reentering the same stage must announce it again");
        }
    }
}
