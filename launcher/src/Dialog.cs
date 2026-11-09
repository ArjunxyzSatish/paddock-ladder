using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaddockLadder
{
    // Pop-up messages drawn in the app's own style instead of Windows' standard boxes.
    public static class Dialog
    {
        public static FrameworkElement BuildBody(string title, string message, string confirm, string cancel, System.Action onConfirm, System.Action onCancel)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 26, 0, 0) };
            if (cancel != null) buttons.Children.Add(U.Margin(U.Ghost(cancel, onCancel), 0, 0, 12, 0));
            var answer = U.Primary(confirm, onConfirm, false);
            answer.Tag = null; // agreeing to delete something shouldn't sound like a celebration
            buttons.Children.Add(answer);

            var content = new StackPanel { Width = 430 };
            content.Children.Add(U.Display(title, 26, null, true));
            content.Children.Add(U.Margin(U.Body(message, 14.5, U.Muted, true), 0, 10, 0, 0));
            content.Children.Add(buttons);

            var frame = new Border { Background = U.Surface, BorderBrush = U.Line, BorderThickness = new Thickness(1), Padding = new Thickness(30, 26, 30, 26), Child = content };
            var outer = new StackPanel();
            outer.Children.Add(new Border { Height = 2, Background = U.Volt });
            outer.Children.Add(frame);
            return outer;
        }

#if SELFTEST
        public static readonly System.Collections.Generic.Queue<bool> AnswersForTest = new System.Collections.Generic.Queue<bool>();
#endif

        static bool Run(Window owner, string title, string message, string confirm, string cancel)
        {
#if SELFTEST
            if (AnswersForTest.Count > 0) return AnswersForTest.Dequeue();
#endif
            var accepted = false;
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false,
                Background = U.Surface,
                FontFamily = U.BodyFont,
                UseLayoutRounding = true,
                Title = title,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            };
            if (owner != null) window.Owner = owner;

            window.Content = BuildBody(title, message, confirm, cancel,
                delegate { accepted = true; window.Close(); },
                delegate { window.Close(); });

            window.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape) window.Close();
                if (e.Key == Key.Enter)
                {
                    accepted = true;
                    window.Close();
                }
            };
            window.ShowDialog();
            return accepted;
        }

        public static bool Confirm(Window owner, string title, string message, string confirm)
        {
            return Run(owner, title, message, confirm, "Cancel");
        }

        public static void Tell(Window owner, string title, string message)
        {
            Run(owner, title, message, "OK", null);
        }
    }
}
