using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PaddockLadder
{
    // The interface sounds. One set of handlers covers every button, tile and dropdown, so new screens get sounds for free.
    public static class Sfx
    {
        [DllImport("winmm.dll")] static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
        const uint Async = 0x1, NoDefault = 0x2, Memory = 0x4;

        const string Hover = "hover", Click = "click", Scroll = "scroll", Confirm = "confirm";
        public const string ConfirmTag = "confirm";

        // How long each sound keeps quieter ones from cutting it off. A new page under a still mouse shouldn't tick either.
        static readonly Dictionary<string, double> HoldMs = new Dictionary<string, double> { { Hover, 45 }, { Click, 220 }, { Scroll, 45 }, { Confirm, 500 } };

        class Clip
        {
            public short[] Original;
            public byte[] Data;
            public IntPtr Pinned;
        }

        static readonly Dictionary<string, Clip> Clips = new Dictionary<string, Clip>();
        static bool _on = true, _attached;
        static int _volume = 60;
        static DateTime _heldUntil = DateTime.MinValue;

#if SELFTEST
        public static readonly List<string> PlayedForTest = new List<string>();
        public static bool ReallyPlayForTest;
        public static void ResetTimingForTest() { _heldUntil = DateTime.MinValue; }
#endif

        public static void Configure(bool on, int volumePercent)
        {
            _on = on;
            _volume = volumePercent;
            foreach (var clip in Clips.Values) Scale(clip);
        }

        // Wave files with a plain 44-byte header, written by tools/make_sounds.py.
        static Clip Load(string name)
        {
            Clip clip;
            if (Clips.TryGetValue(name, out clip)) return clip;
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("game/sounds/" + name + ".wav"))
            {
                var data = new byte[input.Length];
                input.Read(data, 0, data.Length);
                var samples = new short[(data.Length - 44) / 2];
                Buffer.BlockCopy(data, 44, samples, 0, samples.Length * 2);
                clip = new Clip { Original = samples, Data = data };
            }
            clip.Pinned = GCHandle.Alloc(clip.Data, GCHandleType.Pinned).AddrOfPinnedObject();
            Scale(clip);
            Clips[name] = clip;
            return clip;
        }

        static void Scale(Clip clip)
        {
            var gain = _volume / 100.0;
            var scaled = clip.Original.Select(s => (short)(s * gain)).ToArray();
            Buffer.BlockCopy(scaled, 0, clip.Data, 44, scaled.Length * 2);
        }

        static void Play(string name)
        {
            if (!_on) return;
            var now = DateTime.UtcNow;
            if (now < _heldUntil && name != Confirm && (name != Click || _heldUntil - now > TimeSpan.FromMilliseconds(HoldMs[Confirm] - 60))) return;
            _heldUntil = now.AddMilliseconds(HoldMs[name]);
#if SELFTEST
            PlayedForTest.Add(name);
            if (!ReallyPlayForTest) return;
#endif
            PlaySound(Load(name).Pinned, IntPtr.Zero, Async | Memory | NoDefault);
        }

#if SELFTEST
        public static bool PlayForCheck(string name)
        {
            return PlaySound(Load(name).Pinned, IntPtr.Zero, Async | Memory | NoDefault);
        }
#endif

        static bool IsClickable(object sender)
        {
            var element = sender as FrameworkElement;
            if (element == null || !element.IsEnabled || sender is Thumb || sender is RepeatButton) return false;
            return sender is ComboBoxItem || element.Cursor == Cursors.Hand;
        }

        public static void Attach()
        {
            if (_attached) return;
            _attached = true;

            EventManager.RegisterClassHandler(typeof(FrameworkElement), Mouse.MouseEnterEvent, new MouseEventHandler((sender, e) =>
            {
                if (IsClickable(sender)) Play(Hover);
            }), true);

            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
            {
                if (sender is RepeatButton) return;
                Play((sender as FrameworkElement).Tag as string == ConfirmTag ? Confirm : Click);
            }), true);

            // Tiles and dropdown rows aren't buttons, so they're caught when the mouse is released on them.
            EventManager.RegisterClassHandler(typeof(FrameworkElement), UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler((sender, e) =>
            {
                if (sender is ButtonBase || sender is Slider || sender is ComboBox) return;
                if (IsClickable(sender)) Play(Click);
            }), true);

            // Only the list inside a dropdown ticks, not the long pages behind it.
            EventManager.RegisterClassHandler(typeof(ScrollViewer), ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((sender, e) =>
            {
                var box = ((ScrollViewer)sender).TemplatedParent as ComboBox;
                if (box != null && box.IsDropDownOpen && e.VerticalChange != 0) Play(Scroll);
            }), true);

            EventManager.RegisterClassHandler(typeof(ComboBox), Keyboard.PreviewKeyDownEvent, new KeyEventHandler((sender, e) =>
            {
                if (((ComboBox)sender).IsDropDownOpen && (e.Key == Key.Up || e.Key == Key.Down)) Play(Scroll);
            }), true);
        }
    }
}
