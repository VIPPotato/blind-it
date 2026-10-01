using System;
using System.Diagnostics;
using Il2Cpp;
using DeviceType = Il2CppBopIt.DeviceType;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BlindIt
{
    /// <summary>Reads the committed choices while the pre-game start screen is visible.</summary>
    internal static class TrackChoiceReader
    {
        private const int PollFrames = 10;

        /// <summary>
        /// How long the reader stays quiet after the game is told to leave, before
        /// deciding the exit was cancelled. The real transition took under a second in
        /// launch 9 (told at 18:31:49.700, gone by 18:31:53.151), so this is generous.
        /// </summary>
        private const double LeavingTimeout = 5.0;

        private static readonly TrackChoiceState State = new TrackChoiceState();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static int _sincePoll;
        private static bool _showing;
        private static double _waitingSince;
        private static bool _reportedMissing;
        private static bool _leaving;
        private static double _leavingSince;
        private static string _lastError;
        private static string _diagnostic = "not observed";

        internal static void RequestPoll()
        {
            _sincePoll = PollFrames;
        }

        /// <summary>
        /// Called when the game has been told to leave this screen.
        ///
        /// WHY A LATCH AND NOT A PLAIN RESET. Leaving used to clear the state
        /// outright, but the screen does not disappear on the frame the player
        /// presses the key: the game plays a transition, and the start screen stays
        /// visible and GameManager stays in WaitingToStart for most of a second
        /// afterwards. With the state cleared, the very next poll saw a screen it did
        /// not recognise and announced it as a fresh entry, so pressing Backspace to
        /// return to the main menu read the device and song out again on the way out.
        ///
        /// Proven in launch 9: "entered" at 18:31:50.050 announcing "Bop it type,
        /// Classic, song, Office", then a SECOND "entered" at 18:31:52.400 repeating
        /// it, and only then "left" at 18:31:53.151 once the manager had gone. The
        /// player reported exactly this: "when pressing backspace to go back to the
        /// main menu it announces device type and song which it shouldn't".
        ///
        /// So leaving now only latches. The reader stays quiet from this moment until
        /// the screen genuinely goes away, which is where the state is cleared.
        /// </summary>
        internal static void BeginLeaving()
        {
            // Logged because the old defect had to be diagnosed by elimination: the
            // hooks were silent, so the only proof one had fired was a second
            // "entered" with no "left" between. One line here removes that guesswork.
            if (!_leaving)
                Log.Line("track", "leaving | going quiet until the screen is gone");

            _leaving = true;
            _leavingSince = Clock.Elapsed.TotalSeconds;
        }

        internal static void Reset()
        {
            State.Reset();
            _showing = false;
            _sincePoll = 0;
            _waitingSince = 0;
            _reportedMissing = false;
            _leaving = false;
            _leavingSince = 0;
        }

        internal static bool Tick()
        {
            bool repeat = Hotkeys.RepeatPressed();
            if (++_sincePoll < PollFrames && !repeat)
                return _showing;
            _sincePoll = 0;

            try
            {
                GameManager manager = FindActive<GameManager>();
                StartScreenPanel start = FindStart();
                bool waiting = Ui.Alive(manager) && manager.GameState == GameState.WaitingToStart;
                if (!waiting || !Ui.Alive(start))
                {
                    _diagnostic = "showing=false | gameState="
                        + (Ui.Alive(manager) ? manager.GameState.ToString() : "<no manager>")
                        + " | startVisible=" + Ui.Alive(start);
                    if (_showing)
                        Log.Line("track", "left | " + _diagnostic);
                    Reset();
                    return false;
                }

                // ON THE WAY OUT. The game has been told to leave but its transition
                // is still running, so the screen is technically still here. Stay
                // silent and keep owning the screen: speaking would repeat the choices
                // the player is walking away from, and releasing the screen would let
                // the focus reader announce the fading widgets instead.
                //
                // THE LATCH CANNOT STICK. If the screen is still here after
                // LeavingTimeout the transition did not happen (a cancelled exit, or a
                // future build that calls GoToMainMenu without leaving), so the latch
                // is dropped and the screen is announced again. A permanently silent
                // start screen would be a worse defect than one repeat, and the
                // player cannot escape it without restarting the game.
                if (_leaving)
                {
                    double leavingFor = Clock.Elapsed.TotalSeconds - _leavingSince;

                    if (leavingFor < LeavingTimeout)
                    {
                        _diagnostic = "leaving | gameState=" + manager.GameState
                            + " | startVisible=true | for="
                            + leavingFor.ToString("0.00",
                                System.Globalization.CultureInfo.InvariantCulture) + "s";
                        return true;
                    }

                    Log.Line("track", "still here after "
                        + LeavingTimeout.ToString("0.0",
                            System.Globalization.CultureInfo.InvariantCulture)
                        + "s, treating the exit as cancelled | gameState="
                        + manager.GameState);

                    _leaving = false;
                    _showing = false;
                    State.Reset();
                }

                if (!_showing)
                {
                    _waitingSince = Clock.Elapsed.TotalSeconds;
                    Log.Line("track", "entered | start=" + Ui.Path(start.gameObject));
                }
                _showing = true;

                App app = FindActive<App>();
                MusicTrack track = Ui.Alive(app) ? app.CurrentMusicTrack : null;
                Player player = manager.Player;
                Device device = Ui.Alive(player) ? player.Device : null;
                int trackId = Ui.Alive(track) ? track.GetInstanceID() : 0;
                int? deviceType = Ui.Alive(device) ? (int)device.Type : null;
                string song = TrackName(track);
                string deviceName = Ui.Alive(device) ? DeviceWords(device.Type) : null;
                _diagnostic = "showing=true | startId=" + start.GetInstanceID()
                    + " | trackId=" + trackId
                    + " | background=" + (Ui.Alive(track) ? track.BackgroundSceneName : "<none>")
                    + " | assetName=" + (Ui.Alive(track) ? track.name : "<none>")
                    + " | song=" + (song ?? "<unreadable>")
                    + " | device=" + (deviceType?.ToString() ?? "<unreadable>");

                // Allow initialization to finish before announcing the entry. If a
                // value remains unreadable, make the missing label audible and log it.
                if (string.IsNullOrEmpty(song) || string.IsNullOrEmpty(deviceName))
                {
                    if (Clock.Elapsed.TotalSeconds - _waitingSince < 2.0)
                        return true;
                    if (!_reportedMissing)
                    {
                        Log.Line("track", "missing choice label | " + _diagnostic);
                        _reportedMissing = true;
                    }
                    song ??= Strings.Get("track.unknown");
                    deviceName ??= Strings.Get("track.unknown");
                    deviceType ??= -1;
                }
                else
                {
                    _reportedMissing = false;
                }

                string spoken = State.Observe(start.GetInstanceID(), trackId, song, deviceType, deviceName);
                if (repeat)
                    spoken = State.Repeat();
                if (!string.IsNullOrEmpty(spoken))
                {
                    Log.Line("track", "choice | " + _diagnostic + " | spoke=" + spoken);
                    Speech.Speak(spoken, true);
                }
                _lastError = null;
                return true;
            }
            catch (Exception ex)
            {
                string error = ex.GetType().Name + ": " + ex.Message;
                if (!string.Equals(_lastError, error, StringComparison.Ordinal))
                    Log.Line("track", "read failed: " + error);
                _lastError = error;
                Reset();
                return false;
            }
        }

        private static StartScreenPanel FindStart()
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<GameUIManager>());
            if (found == null)
                return null;
            for (int i = 0; i < found.Length; i++)
            {
                GameUIManager ui = found[i]?.TryCast<GameUIManager>();
                if (!Ui.Alive(ui) || !Ui.Visible(ui.gameObject))
                    continue;
                StartScreenPanel start = ui.startScreen;
                if (Ui.Alive(start) && start.IsFullyVisible && !start.IsTransitioningHide
                    && Ui.Visible(start.gameObject))
                    return start;
            }
            return null;
        }

        private static T FindActive<T>() where T : Component
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<T>());
            if (found == null)
                return null;
            for (int i = 0; i < found.Length; i++)
            {
                T candidate = found[i]?.TryCast<T>();
                if (Ui.Alive(candidate) && Ui.Alive(candidate.gameObject)
                    && candidate.gameObject.activeInHierarchy)
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// The song's spoken name, or null when this build holds a track we have
        /// not verified.
        ///
        /// WHY A FIXED LIST AND NOT THE ASSET NAME. The game has no title field on
        /// MusicTrack at all: the four titles the player sees on the start screen are
        /// picture files, not text, which is why the screen's text dump contains the
        /// control prompts and no song name. The game picks which picture to show by
        /// testing the title object's name against the track's BackgroundSceneName,
        /// so that background string is the game's own identity for the song.
        ///
        /// For the four tracks in this build the background string and the word in
        /// the artwork are the same: SHAPES, SPACE, CITY and OFFICE. That was checked
        /// by reading the pictures. So the mapping below is a checked translation of
        /// four known values, NOT a rule that tidies up asset names: a fifth track
        /// added by an update returns null here and is reported as unknown instead of
        /// the mod reading out an internal filename like "Asset 17".
        ///
        /// Sources: evidence/launch9/source-review/findings.md, section
        /// "Proven title source".
        /// </summary>
        private static string TrackName(MusicTrack track)
        {
            if (!Ui.Alive(track))
                return null;

            // The background scene name is the game's own key for the song. The
            // asset's object name is used only if that is empty, because the two
            // agree on this build and the backgrounds are what the game compares.
            string key = track.BackgroundSceneName;
            if (string.IsNullOrWhiteSpace(key))
                key = track.name;

            return SongWords(key);
        }

        /// <summary>
        /// The spoken words for one verified track identity, or null when it is not
        /// one of the four this build ships. Matching ignores case and surrounding
        /// space; it does not accept a partial or decorated name.
        ///
        /// Kept here, free of Unity, so the desktop harness proves the shipped
        /// mapping. Both the start screen and the achievement book's page headings
        /// go through it.
        /// </summary>
        internal static string SongWords(string key)
        {
            return LabelText.SongWords(key);
        }

        internal static string DeviceWords(DeviceType type)
        {
            switch (type)
            {
                case DeviceType.Classic: return Strings.Get("device.classic");
                case DeviceType.Extreme: return Strings.Get("device.extreme");
                default: return null;
            }
        }

        internal static string DebugState() => _diagnostic;
    }
}
