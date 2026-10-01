namespace BlindIt
{
    /// <summary>Announcement state without any retained Unity object.</summary>
    internal sealed class TrackChoiceState
    {
        private int _screenId;
        private int _trackId;
        private int? _deviceType;
        private string _trackName;
        private string _deviceName;
        private bool _announced;

        internal string Observe(int screenId, int trackId, string trackName,
            int? deviceType, string deviceName)
        {
            if (_screenId != screenId)
                Reset();
            if (string.IsNullOrWhiteSpace(trackName) || !deviceType.HasValue
                || string.IsNullOrWhiteSpace(deviceName))
                return null;

            bool entered = !_announced;
            bool songChanged = entered || _trackId != trackId;
            bool deviceChanged = entered || _deviceType != deviceType;
            _screenId = screenId;
            _trackId = trackId;
            _deviceType = deviceType;
            _trackName = trackName;
            _deviceName = deviceName;
            _announced = true;

            if (songChanged && deviceChanged)
                return Selection(deviceName, trackName);
            if (songChanged)
                return Announcement.Detail(Strings.Get("track.song.spoken", trackName));
            if (deviceChanged)
                return Announcement.Detail(Strings.Get("track.device.spoken", deviceName));
            return null;
        }

        internal string Repeat()
        {
            return _announced ? Selection(_deviceName, _trackName) : null;
        }

        internal void Reset()
        {
            _screenId = 0;
            _trackId = 0;
            _deviceType = null;
            _trackName = null;
            _deviceName = null;
            _announced = false;
        }

        private static string Selection(string deviceName, string trackName)
        {
            return Announcement.Detail(Strings.Get("track.selection", deviceName, trackName));
        }
    }
}
