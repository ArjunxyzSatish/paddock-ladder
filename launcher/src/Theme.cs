using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PaddockLadder
{
    // Design language: photography on black, one quiet accent, thin rules instead of boxes.
    static class U
    {
        public static bool Animate = true;

        static Brush B(string hex)
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }

        public static readonly Brush Ink = B("#08090B"), Surface = B("#111317"), Raised = B("#191C21"), Line = B("#24272E");
        public static readonly Brush Text = B("#F3F1EC"), Muted = B("#8E939D"), Faint = B("#5C616C");
        public static readonly Brush Volt = B("#E6FF57"), Good = B("#6FE39A"), Bad = B("#FF6A5E"), Warn = B("#FFC04D");

        public static readonly FontFamily DisplayFont = new FontFamily("Bahnschrift, Segoe UI");
        public static readonly FontFamily BodyFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe MDL2 Assets");

        public const string IconBack = "", IconCheck = "", IconCross = "", IconArrow = "", IconMin = "", IconMax = "", IconRestore = "", IconClose = "";

        const string StyleXaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Primary' TargetType='Button'>
    <Setter Property='Foreground' Value='#08090B'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Grid>
        <Border Background='#F3F1EC'/>
        <Border x:Name='hover' Background='#E6FF57' Opacity='0'/>
        <ContentPresenter Margin='30,13' HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
          <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hover' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.14'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
          <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hover' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.2'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
        </Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Ghost' TargetType='Button'>
    <Setter Property='Foreground' Value='#F3F1EC'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Grid>
        <Border BorderBrush='#3B3F47' BorderThickness='1'/>
        <Border x:Name='hover' Background='#F3F1EC' Opacity='0'/>
        <ContentPresenter x:Name='content' Margin='24,12' HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
          <Setter Property='Foreground' Value='#08090B'/>
          <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hover' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.12'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
          <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hover' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.16'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
        </Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Bare' TargetType='Button'>
    <Setter Property='Foreground' Value='#8E939D'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border Background='Transparent'><ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Center'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='#F3F1EC'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Window' TargetType='Button'>
    <Setter Property='Foreground' Value='#8E939D'/>
    <Setter Property='Width' Value='46'/>
    <Setter Property='Height' Value='34'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='back' Background='Transparent'><ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Center'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='back' Property='Background' Value='#22252B'/><Setter Property='Foreground' Value='#F3F1EC'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Segment' TargetType='Button'>
    <Setter Property='Foreground' Value='#8E939D'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='back' Background='Transparent' Padding='20,10'><ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Center'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='back' Property='Background' Value='#191C21'/><Setter Property='Foreground' Value='#F3F1EC'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='SegmentOn' TargetType='Button'>
    <Setter Property='Foreground' Value='#08090B'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border Background='#F3F1EC' Padding='20,10'><ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Center'/></Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='DropToggle' TargetType='ToggleButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'>
      <Border x:Name='frame' Background='Transparent' BorderBrush='#3B3F47' BorderThickness='1'>
        <TextBlock Text='&#xE70D;' FontFamily='Segoe MDL2 Assets' FontSize='10' Foreground='#8E939D' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='#F3F1EC'/></Trigger>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='#E6FF57'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='#F3F1EC'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>
      <Border x:Name='row' Background='Transparent' Padding='14,8'><ContentPresenter/></Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='row' Property='Background' Value='#262A33'/></Trigger>
        <Trigger Property='IsSelected' Value='True'><Setter Property='Foreground' Value='#E6FF57'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='#F3F1EC'/>
    <Setter Property='Height' Value='38'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='MaxDropDownHeight' Value='340'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
      <Grid>
        <ToggleButton Style='{StaticResource DropToggle}' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'/>
        <ContentPresenter IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' Margin='14,0,34,0' VerticalAlignment='Center' HorizontalAlignment='Left'/>
        <Popup x:Name='PART_Popup' IsOpen='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
          <Border Background='#191C21' BorderBrush='#3B3F47' BorderThickness='1' MinWidth='{TemplateBinding ActualWidth}' MaxHeight='{TemplateBinding MaxDropDownHeight}'>
            <ScrollViewer CanContentScroll='False' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer>
          </Border>
        </Popup>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='SliderFill' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'>
      <Grid Background='Transparent'><Rectangle Height='2' Fill='#F3F1EC' VerticalAlignment='Center'/></Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='SliderRest' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'>
      <Grid Background='Transparent'><Rectangle Height='2' Fill='#3B3F47' VerticalAlignment='Center'/></Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='Slider'>
    <Setter Property='Height' Value='26'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='IsMoveToPointEnabled' Value='True'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
      <Track x:Name='PART_Track'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource SliderFill}'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource SliderRest}'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'>
          <Border x:Name='grip' Width='10' Height='20' Background='#F3F1EC'/>
          <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='grip' Property='Background' Value='#E6FF57'/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template>
        <ControlTemplate TargetType='Thumb'><Border Background='#2E323A' Margin='2,2'/></ControlTemplate>
      </Thumb.Template></Thumb></Track.Thumb></Track>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='#F3F1EC'/>
    <Setter Property='CaretBrush' Value='#E6FF57'/>
    <Setter Property='SelectionBrush' Value='#E6FF57'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
      <Border x:Name='rule' BorderBrush='#3B3F47' BorderThickness='0,0,0,1' Padding='0,6'><ScrollViewer x:Name='PART_ContentHost'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='rule' Property='BorderBrush' Value='#E6FF57'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";

        public static void ApplyTheme(Application app)
        {
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(StyleXaml));
        }

        static TextBlock Block(string s, double size, Brush color, FontFamily font, FontWeight weight, bool wrap)
        {
            var block = new TextBlock
            {
                Text = s,
                FontSize = size,
                FontFamily = font,
                FontWeight = weight,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            };
            if (color != null) block.Foreground = color;
            return block;
        }

        public static TextBlock Body(string s, double size = 14, Brush color = null, bool wrap = false)
        {
            return Block(s, size, color ?? Text, BodyFont, FontWeights.Normal, wrap);
        }

        public static TextBlock Display(string s, double size, Brush color = null, bool wrap = false, bool strong = false)
        {
            return Block(s, size, color ?? Text, DisplayFont, strong ? FontWeights.SemiBold : FontWeights.Light, wrap);
        }

        // Small tracked capitals. WPF has no letter spacing, so hair spaces do the job.
        public static string Tracked(string s)
        {
            return string.Join(" ", s.ToUpperInvariant().Select(c => c.ToString()));
        }

        public static TextBlock Eyebrow(string s, Brush color = null, double size = 11)
        {
            return Block(Tracked(s), size, color ?? Muted, DisplayFont, FontWeights.SemiBold, false);
        }

        public static TextBlock Icon(string glyph, double size, Brush color = null)
        {
            var block = new TextBlock { Text = glyph, FontSize = size, FontFamily = IconFont, VerticalAlignment = VerticalAlignment.Center };
            if (color != null) block.Foreground = color;
            return block;
        }

        public static T Margin<T>(T e, double left, double top, double right, double bottom) where T : FrameworkElement
        {
            e.Margin = new Thickness(left, top, right, bottom);
            return e;
        }

        public static StackPanel Stack(params UIElement[] items)
        {
            var panel = new StackPanel();
            foreach (var i in items) panel.Children.Add(i);
            return panel;
        }

        public static StackPanel Row(params UIElement[] items)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items) panel.Children.Add(i);
            return panel;
        }

        public static Rectangle Rule(double top = 0, double bottom = 0)
        {
            return new Rectangle { Height = 1, Fill = Line, Margin = new Thickness(0, top, 0, bottom) };
        }

        static Button MakeButton(string style, object content, Action click)
        {
            var b = new Button { Content = content, Style = (Style)Application.Current.Resources[style] };
            b.Click += delegate { click(); };
            return b;
        }

        static UIElement ButtonLabel(string label, bool arrow)
        {
            var text = new TextBlock { Text = Tracked(label), FontFamily = DisplayFont, FontWeight = FontWeights.SemiBold, FontSize = 12.5 };
            if (!arrow) return text;
            return Row(text, Margin(Icon(IconArrow, 12), 12, 0, 0, 0));
        }

        // The main button on a page moves you forward, so it has its own sound.
        public static Button Primary(string label, Action click, bool arrow = true)
        {
            var button = MakeButton("Primary", ButtonLabel(label, arrow), click);
            button.Tag = Sfx.ConfirmTag;
            return button;
        }

        public static Button Ghost(string label, Action click) { return MakeButton("Ghost", ButtonLabel(label, false), click); }
        public static Button Bare(UIElement content, Action click) { return MakeButton("Bare", content, click); }

        public static Button Link(string label, Action click)
        {
            return Bare(new TextBlock { Text = Tracked(label), FontFamily = DisplayFont, FontWeight = FontWeights.SemiBold, FontSize = 11.5 }, click);
        }

        public static Button WindowButton(string glyph, Action click)
        {
            var b = MakeButton("Window", new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 10 }, click);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        public static Border Segments<TValue>(string[] labels, TValue[] values, TValue current, Action<TValue> pick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (var i = 0; i < labels.Length; i++)
            {
                var value = values[i];
                var selected = EqualityComparer<TValue>.Default.Equals(value, current);
                var text = new TextBlock { Text = Tracked(labels[i]), FontFamily = DisplayFont, FontWeight = FontWeights.SemiBold, FontSize = 11.5 };
                row.Children.Add(MakeButton(selected ? "SegmentOn" : "Segment", text, delegate { pick(value); }));
            }
            return new Border { BorderBrush = Line, BorderThickness = new Thickness(1), Child = row, HorizontalAlignment = HorizontalAlignment.Left };
        }

        const string IconLeft = "", IconRight = "";

        static Button ArrowButton(string glyph, Action click)
        {
            var button = Bare(new Border { Width = 34, Height = 34, BorderBrush = Line, BorderThickness = new Thickness(1), Background = Brushes.Transparent, Child = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }, click);
            button.VerticalAlignment = VerticalAlignment.Center;
            return button;
        }

        // A slider with arrows either side and the value beside it.
        public static FrameworkElement Stepper(int value, int min, int max, int step, string unit, Action<int> commit, double width = 230)
        {
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Width = width, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            var number = Display(value.ToString(), 24);
            number.Width = 58;
            number.TextAlignment = TextAlignment.Right;
            number.VerticalAlignment = VerticalAlignment.Center;
            slider.ValueChanged += delegate { number.Text = ((int)slider.Value).ToString(); };

            Action apply = delegate
            {
                var chosen = (int)slider.Value;
                if (chosen != value) commit(chosen);
            };
            slider.PreviewMouseLeftButtonUp += delegate { apply(); };
            slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler((s, e) => apply()));

            Action<int> nudge = direction =>
            {
                var next = Math.Max(min, Math.Min(max, value + direction * step));
                if (next != value) commit(next);
            };

            var label = Margin(Eyebrow(unit), 8, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            return Row(ArrowButton(IconLeft, delegate { nudge(-1); }), slider, ArrowButton(IconRight, delegate { nudge(1); }), number, label);
        }

        public static ComboBox PlainDropdown(string[] names, int index, Action<int> pick, double width = 250)
        {
            var box = new ComboBox { Width = width };
            foreach (var name in names) box.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = name, FontFamily = DisplayFont, FontSize = 15 } });
            box.SelectedIndex = index;
            box.SelectionChanged += delegate { if (box.SelectedIndex >= 0 && box.SelectedIndex != index) pick(box.SelectedIndex); };
            return box;
        }

        // A scrolling list with section headings that can't be selected.
        public static ComboBox GroupedDropdown(IEnumerable<WeatherDef> options, int selectedId, Action<int> pick, double width = 250)
        {
            var box = new ComboBox { Width = width };
            string group = null;
            ComboBoxItem selected = null;
            foreach (var option in options)
            {
                if (option.Group != group)
                {
                    group = option.Group;
                    box.Items.Add(new ComboBoxItem { IsEnabled = false, Content = Eyebrow(group, Faint, 10) });
                }
                var item = new ComboBoxItem { Tag = option.Id, Content = new TextBlock { Text = option.Name, FontFamily = DisplayFont, FontSize = 16 } };
                box.Items.Add(item);
                if (option.Id == selectedId) selected = item;
            }

            box.SelectedItem = selected;
            box.SelectionChanged += delegate
            {
                var chosen = box.SelectedItem as ComboBoxItem;
                if (chosen != null && chosen.Tag is int && (int)chosen.Tag != selectedId) pick((int)chosen.Tag);
            };
            return box;
        }

        static readonly Dictionary<string, BitmapImage> Images = new Dictionary<string, BitmapImage>();

        static BitmapImage Load(string path, int width)
        {
            var key = path + "|" + width;
            BitmapImage cached;
            if (Images.TryGetValue(key, out cached)) return cached;
            BitmapImage bmp = null;
            try
            {
                if (File.Exists(path))
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path);
                    bmp.DecodePixelWidth = width;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                }
            }
            catch (Exception) { bmp = null; }
            Images[key] = bmp;
            return bmp;
        }

        static LinearGradientBrush Fade(Point start, Point end, Color colour, double from, double to)
        {
            var transparent = Color.FromArgb(0, colour.R, colour.G, colour.B);
            var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            brush.GradientStops.Add(new GradientStop(transparent, from));
            brush.GradientStops.Add(new GradientStop(colour, to));
            brush.Freeze();
            return brush;
        }

        // A photo that fills its slot, optionally melting into the page at the bottom.
        public static Grid Photo(string path, double height, int decodeWidth, bool fadeBottom)
        {
            var frame = new Grid { Height = height, ClipToBounds = true, Background = Surface };
            var bmp = Load(path, decodeWidth);
            if (bmp != null) frame.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            if (fadeBottom) frame.Children.Add(new Rectangle { Fill = Fade(new Point(0, 0), new Point(0, 1), Color.FromRgb(8, 9, 11), 0.5, 1) });
            return frame;
        }

        // The car stage: the image dissolves into the page on its left edge.
        public static Grid Stage(string path, double width, double height)
        {
            var frame = new Grid { Width = width, Height = height, ClipToBounds = true };
            var bmp = Load(path, (int)(width * 1.5));
            if (bmp != null)
            {
                var image = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                image.OpacityMask = Fade(new Point(0, 0), new Point(1, 0), Colors.Black, 0.02, 0.34);
                frame.Children.Add(image);
            }
            frame.Children.Add(new Rectangle { Fill = Fade(new Point(0, 0), new Point(0, 1), Color.FromRgb(8, 9, 11), 0.72, 1) });
            return frame;
        }

        public static void AddHoverZoom(FrameworkElement host, Grid photo)
        {
            var image = photo.Children.OfType<Image>().FirstOrDefault();
            if (image == null) return;
            var scale = new ScaleTransform(1, 1);
            image.RenderTransform = scale;
            image.RenderTransformOrigin = new Point(0.5, 0.5);
            Action<double> run = target =>
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var x = new DoubleAnimation(target, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, x);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, x);
            };
            host.MouseEnter += delegate { run(1.045); };
            host.MouseLeave += delegate { run(1); };
        }

        public static Grid Meter(double fraction, Brush color)
        {
            var bar = new Grid { Height = 3, Background = Line, ClipToBounds = true };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, fraction), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - fraction), GridUnitType.Star) });
            var fill = new Rectangle { Fill = color };
            Grid.SetColumn(fill, 0);
            bar.Children.Add(fill);
            return bar;
        }

        public static void Reveal(UIElement element, int delayMs, double rise = 14)
        {
            if (!Animate) return;
            var move = new TranslateTransform(0, rise);
            element.RenderTransform = move;
            element.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var duration = TimeSpan.FromMilliseconds(340);
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, duration) { BeginTime = begin, EasingFunction = ease });
        }
    }
}
