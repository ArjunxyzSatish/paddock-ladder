using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace PaddockLadder
{
    // The menu track. It fades in and out, so it never starts or stops abruptly.
    public class MenuMusic
    {
        const double FadeStep = 0.008;

        readonly string _path;
        readonly DispatcherTimer _fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        MediaPlayer _player;
        double _level, _target;
        bool _playing, _broken;

        public MenuMusic(string path)
        {
            _path = path;
            _fade.Tick += delegate { Step(); };
        }

        public void Apply(bool play, int volumePercent)
        {
            if (_broken) return;
            _target = play ? Math.Max(0, Math.Min(1, volumePercent / 100.0)) : 0;
            if (_player == null)
            {
                if (!play || !File.Exists(_path)) return;
                Open();
            }
            if (play && !_playing)
            {
                _player.Play();
                _playing = true;
            }
            if (!_fade.IsEnabled) _fade.Start();
        }

        void Open()
        {
            _player = new MediaPlayer { Volume = 0 };
            _player.MediaEnded += delegate
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
            };
            _player.MediaFailed += delegate
            {
                _broken = true;
                _fade.Stop();
            };
            _player.Open(new Uri(_path));
        }

        void Step()
        {
            _level = _level < _target ? Math.Min(_target, _level + FadeStep) : Math.Max(_target, _level - FadeStep);
            _player.Volume = _level;
            if (_level != _target) return;
            _fade.Stop();
            if (_level > 0) return;
            _player.Pause();
            _playing = false;
        }

#if SELFTEST
        public TimeSpan PositionForTest { get { return _player == null ? TimeSpan.Zero : _player.Position; } }
        public TimeSpan DurationForTest { get { return _player != null && _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero; } }
        public bool BrokenForTest { get { return _broken; } }
#endif
    }
}
