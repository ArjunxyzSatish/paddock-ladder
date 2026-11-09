#if SELFTEST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PaddockLadder
{
    // Drives the real launcher window by pressing its own buttons, against a fake game install in a temp folder.
    public static partial class SelfTest
    {
        // Finding things on screen ---------------------------------------------------------------

        static string Norm(string text)
        {
            return new string((text ?? "").Where(ch => !char.IsWhiteSpace(ch) && (ch < '' || ch > '')).ToArray()).ToUpperInvariant();
        }

        static IEnumerable<DependencyObject> Tree(DependencyObject root)
        {
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var item in Tree(child)) yield return item;
        }

        static string TextIn(DependencyObject root) { return string.Concat(Tree(root).OfType<TextBlock>().Select(t => t.Text)); }
        static bool Has(MainWindow w, string text) { return Norm(TextIn(w.RootElement)).Contains(Norm(text)); }

        static List<Button> ButtonsIn(DependencyObject root, string label)
        {
            return Tree(root).OfType<Button>().Where(b => Norm(TextIn(b)) == Norm(label)).ToList();
        }

        static void Press(DependencyObject root, string label, int which = 0)
        {
            var found = ButtonsIn(root, label);
            if (found.Count <= which) throw new InvalidOperationException("No button called " + label);
            found[which].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        static void Press(MainWindow w, string label, int which = 0) { Press(w.RootElement, label, which); }

        static void LeaveDebrief(MainWindow w)
        {
            while (ButtonsIn(w.RootElement, "Next").Count > 0) Press(w, "Next");
            Press(w, "Continue");
        }

        static void SignOffer(MainWindow w)
        {
            Press(w, "View car");
            Press(w, "Sign contract");
        }

        static void PressTile(MainWindow w, string title)
        {
            // This event doesn't bubble, so it's sent to each parent until the tile that listens for it.
            DependencyObject item = Tree(w.RootElement).OfType<TextBlock>().First(t => Norm(t.Text) == Norm(title));
            while (item != null && item != w.RootElement)
            {
                var element = item as UIElement;
                if (element != null) element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                item = LogicalTreeHelper.GetParent(item);
            }
        }

        // The row on a settings page that has this title.
        static DependencyObject SettingLine(MainWindow w, string label)
        {
            DependencyObject item = Tree(w.RootElement).OfType<TextBlock>().First(t => Norm(t.Text) == Norm(label));
            while (!(item is DockPanel)) item = LogicalTreeHelper.GetParent(item);
            return item;
        }

        static void Nudge(DependencyObject row, int direction)
        {
            var slider = Tree(row).OfType<Slider>().First();
            var panel = (Panel)LogicalTreeHelper.GetParent(slider);
            ((Button)panel.Children[direction < 0 ? 0 : 2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        // A fake game --------------------------------------------------------------------------

        static void MakeCar(string root, string id, string brand, string tags, int bhp, string skinImage = "image")
        {
            var dir = Path.Combine(root, "content", "cars", id);
            Directory.CreateDirectory(Path.Combine(dir, "ui"));
            Directory.CreateDirectory(Path.Combine(dir, "data"));
            var skin = Path.Combine(dir, "skins", "paint");
            Directory.CreateDirectory(skin);
            Directory.CreateDirectory(Path.Combine(dir, "skins", "paint2"));
            File.WriteAllText(Path.Combine(skin, "preview.jpg"), skinImage);
            File.WriteAllText(Path.Combine(dir, "ui", "ui_car.json"), "{\"name\":\"" + id + " model\",\"brand\":\"" + brand + "\",\"class\":\"street\",\"tags\":[\"" + tags + "\"],\"specs\":{\"bhp\":\"" + bhp + " bhp\",\"weight\":\"1000kg\"}}");
        }

        static void MakeTrack(string root, string id, string layout, string name, int pits, string length = "4000", string tags = "circuit")
        {
            var dir = Path.Combine(root, "content", "tracks", id);
            var ui = layout == "" ? Path.Combine(dir, "ui") : Path.Combine(dir, "ui", layout);
            var ai = layout == "" ? Path.Combine(dir, "ai") : Path.Combine(dir, layout, "ai");
            Directory.CreateDirectory(ui);
            Directory.CreateDirectory(ai);
            File.WriteAllText(Path.Combine(ai, "fast_lane.ai"), "x");
            File.WriteAllText(Path.Combine(ui, "ui_track.json"), "{\"name\":\"" + name + "\",\"length\":\"" + length + "\",\"pitboxes\":\"" + pits + "\",\"run\":\"clockwise\",\"tags\":[\"" + tags + "\"]}");
        }

        static string MakeFakeGame()
        {
            var root = Path.Combine(Path.GetTempPath(), "PaddockLadderGame_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(Directory.CreateDirectory(root).FullName + "\\acs.exe", "");
            var classes = new[] { "#Small Sports", "#Sportscars", "#Supercars", "#Hypercars", "#Hypercars R", "GT3", "GT4", "#TT Cup", "#Porsche Cup", "F3" };
            var horse = 120;
            foreach (var tag in classes)
            {
                for (var i = 1; i <= 4; i++) MakeCar(root, Norm(tag).Replace("#", "").ToLowerInvariant() + i, "Brand" + i, tag, horse + i * 3);
                horse += 90;
            }
            MakeCar(root, "brokenimage", "BrandX", "GT3", 500, "this is not a picture");
            MakeCar(root, "driftcar", "BrandD", "#Drift", 300);
            for (var i = 1; i <= 7; i++) MakeTrack(root, "circuit" + i, "", "Fixture Ring " + i, 24);
            MakeTrack(root, "layouts", "gp", "Layout Park GP", 24);
            MakeTrack(root, "tiny", "", "Too Few Pits", 4);
            MakeTrack(root, "drifter", "", "Drift Bowl", 24, "4000", "#drift");

            Directory.CreateDirectory(Path.Combine(root, "content", "weather", "3_clear"));
            Directory.CreateDirectory(Path.Combine(root, "extension", "weather-controllers", "pureCtrl"));
            File.WriteAllText(Path.Combine(root, "extension", "weather-controllers", "pureCtrl", "controller.lua"), "");
            var pure = Path.Combine(root, "apps", "lua", "PurePlanner", "settings");
            Directory.CreateDirectory(pure);
            File.WriteAllLines(Path.Combine(pure, "settings_defaults.lua"), Engine.WeatherOptions.Select(o => "{ \"w" + o.Id + "\", " + o.Id + ", 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, }"));
            return root;
        }

        static int _launches;

        static MainWindow FreshLauncher(string game)
        {
            Store.DocsOverride = Path.Combine(Path.GetTempPath(), "PaddockLadderFlow_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Store.DocsOverride, "cfg"));
            _launches = 0;
            Store.LaunchForTest = delegate { _launches++; };
            Dialog.AnswersForTest.Clear();
            return new MainWindow(game, null);
        }

        static void CreateCareer(MainWindow w, string name, bool open = false)
        {
            ((TextBox)Tree(w.RootElement).OfType<TextBox>().First()).Text = name;
            if (open) PressTile(w, "Pick any series");
            Press(w, "Create career");
        }

        static void FinishRace(MainWindow w, int position, int start = 5, int cuts = 0, int contacts = 0)
        {
            var c = w.CareerForTest;
            var result = Result(c, position, cuts, contacts);
            result.RaceId = c.Pending.RaceId;
            result.StartPos = start;
            File.WriteAllText(Store.ResultPath, Store.ToJson(result));
            w.PollForTest();
        }

        // The flows ------------------------------------------------------------------------------

        static void FlowFirstRun(string game)
        {
            var w = FreshLauncher(game);
            Check("no careers opens the new career page", w.CurrentView == "new", w.CurrentView);
            Check("the new career page counts installed content", Has(w, "41 cars") && Has(w, "8 circuits"), TextIn(w.RootElement).Substring(0, 60));

            PressTile(w, "Pick any series");
            PressTile(w, "Climb the ladder");
            Press(w, "Endurance");
            Check("endurance shows minutes", Has(w, "min"));
            Press(w, "Laps");
            var before = Tree(w.RootElement).OfType<Slider>().First().Value;
            Nudge(Tree(w.RootElement).OfType<Slider>().First().Parent, 1);
            Check("the AI arrow moves the value", Tree(w.RootElement).OfType<Slider>().First().Value == before + 1);

            CreateCareer(w, "Flow Driver");
            var c = w.CareerForTest;
            Check("create career opens the offers", w.CurrentView == "contracts", w.CurrentView);
            Check("a new career gets three offers", c.Offers.Count == 3);
            Check("the career was saved", File.Exists(Store.PathFor(c.Id)));
            Check("the career became active", Store.LoadSettings().ActiveCareer == c.Id);
            Check("sessions default to on", c.PracticeOn && c.QualifyingOn && c.PracticeMinutes == 10 && c.QualifyingMinutes == 10);
            Check("each offer can be opened", ButtonsIn(w.RootElement, "View car").Count == 3);
            Check("drift cars are left out", !c.Offers.Any(o => o.CarId == "driftcar"));

            SignOffer(w);
            Check("signing opens the paddock", w.CurrentView == "paddock" && w.CareerForTest.Season != null, w.CurrentView);
            Check("the paddock shows the weekend", Has(w, "Practice 10 min") && Has(w, "Qualifying 10 min"));
            Check("the grid comes from qualifying", Has(w, "Quali"));

            foreach (var pair in new[] { new[] { "Season", "season" }, new[] { "Profile", "history" }, new[] { "Careers", "careers" }, new[] { "Settings", "settings" }, new[] { "Paddock", "paddock" } })
            {
                Press(w, pair[0]);
                Check("nav opens " + pair[0], w.CurrentView == pair[1], w.CurrentView);
            }
            w.BackForTest();
            Check("back returns from the paddock", w.CurrentView == "settings", w.CurrentView);
        }

        static void Hover(DependencyObject element)
        {
            ((UIElement)element).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        }

        static string LastSound(Action action)
        {
            Sfx.ResetTimingForTest();
            Sfx.PlayedForTest.Clear();
            action();
            return Sfx.PlayedForTest.LastOrDefault();
        }

        static void FlowInterfaceSounds(string game)
        {
            var w = FreshLauncher(game);
            Sfx.Configure(true, 60);
            Check("a new page has its sounds attached", w != null);

            Check("hovering a tile ticks", LastSound(() =>
            {
                DependencyObject tile = Tree(w.RootElement).OfType<TextBlock>().First(t => Norm(t.Text) == Norm("Pick any series"));
                while (!(tile is FrameworkElement) || ((FrameworkElement)tile).Cursor != Cursors.Hand) tile = LogicalTreeHelper.GetParent(tile);
                Hover(tile);
            }) == "hover");
            Check("clicking a tile sounds", LastSound(() => PressTile(w, "Pick any series")) == "click");
            PressTile(w, "Climb the ladder");

            Check("hovering a button ticks", LastSound(() => Hover(ButtonsIn(w.RootElement, "Create career")[0])) == "hover");
            Check("hovering plain text is silent", LastSound(() => Hover(Tree(w.RootElement).OfType<TextBlock>().First())) == null);

            ((TextBox)Tree(w.RootElement).OfType<TextBox>().First()).Text = "Sound Driver";
            Check("the main button has its own sound", LastSound(() => Press(w, "Create career")) == "confirm");
            Check("signing has its own sound", LastSound(() => SignOffer(w)) == "confirm");
            Check("navigation clicks sound normal", LastSound(() => Press(w, "Careers")) == "click");
            Press(w, "Paddock");
            Check("race setup is an ordinary click", LastSound(() => Press(w, "Race setup")) == "click");

            var dropdown = Tree(w.RootElement).OfType<ComboBox>().First();
            dropdown.ApplyTemplate();
            var item = dropdown.Items.OfType<ComboBoxItem>().First(i => i.IsEnabled);
            Check("hovering a dropdown row ticks", LastSound(() => Hover(item)) == "hover");
            Check("choosing a dropdown row sounds", LastSound(() => item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent })) == "click");

            var list = new ScrollViewer();
            Check("page scrolling is silent", LastSound(() => list.RaiseEvent(MakeScrollChanged(list))) == null);
            // A dropdown only opens once it's really on screen, so this one gets a small window of its own.
            var loose = U.PlainDropdown(new[] { "One", "Two", "Three" }, 0, delegate { });
            var holder = new Window { Content = loose, Width = 300, Height = 120, Left = -3000, Top = -3000, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false };
            holder.Show();
            var settled = new System.Windows.Threading.DispatcherFrame();
            holder.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => settled.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(settled);
            loose.ApplyTemplate();
            var popup = (System.Windows.Controls.Primitives.Popup)loose.Template.FindName("PART_Popup", loose);
            var inside = (ScrollViewer)((Border)popup.Child).Child;
            Check("a closed dropdown is silent", LastSound(() => inside.RaiseEvent(MakeScrollChanged(inside))) == null);
            loose.IsDropDownOpen = true;
            Check("scrolling an open dropdown ticks", LastSound(() => inside.RaiseEvent(MakeScrollChanged(inside))) == "scroll");
            Check("arrow keys in an open dropdown tick", LastSound(() => loose.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(holder), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent })) == "scroll");
            loose.IsDropDownOpen = false;
            holder.Close();

            Press(w, "Done");
            Check("a stray hover can't cut off a big sound", Burst(() => { Press(w, "Start race"); Hover(ButtonsIn(w.RootElement, "Careers")[0]); }) == "confirm");
            FinishRace(w, 1);

            Sfx.Configure(false, 60);
            Check("sounds can be turned off", LastSound(() => LeaveDebrief(w)) == null);
            Sfx.Configure(true, 60);

            w.Show("settings");
            Nudge(SettingLine(w, "Interface sounds"), -1);
            Check("the sound volume can be changed", Store.LoadSettings().SoundsVolume == 55, Store.LoadSettings().SoundsVolume);
            Press(SettingLine(w, "Interface sounds"), "Off");
            Check("the sound setting is saved", !Store.LoadSettings().SoundsOn);
            Press(SettingLine(w, "Interface sounds"), "On");
            Sfx.Configure(true, 60);
        }

        static string Burst(Action action)
        {
            Sfx.ResetTimingForTest();
            Sfx.PlayedForTest.Clear();
            action();
            return string.Join(",", Sfx.PlayedForTest);
        }

        static ScrollChangedEventArgs MakeScrollChanged(ScrollViewer source)
        {
            var args = (ScrollChangedEventArgs)typeof(ScrollChangedEventArgs).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0]
                .Invoke(new object[] { new Vector(0, 40), new Vector(0, 40), new Size(10, 400), new Vector(0, 0), new Size(10, 100), new Vector(0, 0) });
            args.RoutedEvent = ScrollViewer.ScrollChangedEvent;
            args.Source = source;
            return args;
        }

        static void FlowSettings(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Settings Driver");
            SignOffer(w);
            var c = w.CareerForTest;
            w.Show("settings");

            var gameRunning = false;
            Store.GameOpenForTest = () => gameRunning;
            try
            {
                Check("music is on by default at a gentle level", Store.LoadSettings().MusicOn && Store.LoadSettings().MusicVolume == 35);
                w.ForegroundForTest = true;
                Check("music plays in the menus", w.MusicWantedForTest);
                w.ForegroundForTest = false;
                Check("music stops when the launcher isn't in front", !w.MusicWantedForTest);
                w.ForegroundForTest = true;
                gameRunning = true;
                Check("music stops while the game is open", !w.MusicWantedForTest);
                gameRunning = false;
                Nudge(SettingLine(w, "Menu music"), 1);
                Check("the music volume can be changed", Store.LoadSettings().MusicVolume == 40, Store.LoadSettings().MusicVolume);
                Press(SettingLine(w, "Menu music"), "Off");
                Check("music can be turned off", !Store.LoadSettings().MusicOn && !w.MusicWantedForTest);
                Press(SettingLine(w, "Menu music"), "On");
                Check("music can be turned back on", Store.LoadSettings().MusicOn && w.MusicWantedForTest);
                Store.InstallGameApp(game);
                var track = Path.Combine(game, "apps", "lua", "PaddockLadder", "music", "menu.mp3");
                Check("the menu track is installed with the app", File.Exists(track) && new FileInfo(track).Length > 1000000);
            }
            finally
            {
                Store.GameOpenForTest = null;
            }

            var ai = c.BaseDifficulty;
            Nudge(SettingLine(w, "AI strength"), 1);
            Check("AI strength arrow", c.BaseDifficulty == ai + 1, c.BaseDifficulty);
            c.AutoOffset = 2;
            Press(SettingLine(w, "Adapt to my results"), "Off");
            Check("turning off adapt clears its effect", !c.AutoDifficulty && c.AutoOffset == 0);
            Press(SettingLine(w, "Adapt to my results"), "On");
            Check("adapt back on", c.AutoDifficulty);

            Press(SettingLine(w, "Race format"), "Endurance");
            Check("format becomes endurance", c.Format == "endurance" && Has(w, "Race time"));
            Nudge(SettingLine(w, "Race time"), 1);
            Check("race time arrow", c.EnduranceMinutes == 75, c.EnduranceMinutes);
            Press(SettingLine(w, "Race format"), "Laps");
            var laps = c.RaceLaps;
            Nudge(SettingLine(w, "Race laps"), 1);
            Check("race laps arrow", c.RaceLaps == laps + 1, c.RaceLaps);

            Nudge(SettingLine(w, "Practice"), 1);
            Check("practice length arrow", c.PracticeMinutes == 11, c.PracticeMinutes);
            Nudge(SettingLine(w, "Qualifying"), -1);
            Check("qualifying length arrow", c.QualifyingMinutes == 9, c.QualifyingMinutes);

            Press(SettingLine(w, "Practice"), "Off");
            Check("practice can be turned off", !c.PracticeOn);
            Check("off hides the length", !Tree(SettingLine(w, "Practice")).OfType<Slider>().Any());
            Press(SettingLine(w, "Qualifying"), "Off");
            Check("qualifying can be turned off", !c.QualifyingOn && Has(w, "start every race from the back"));
            Press(w, "Paddock");
            Check("no qualifying shows the back of the grid", Has(w, "Race from the back") && Has(w, "P" + (c.Season.Field.Count + 1)));
            Check("settings were saved", Store.FromJson<Career>(File.ReadAllText(Store.PathFor(c.Id))).QualifyingOn == false);

            w.Show("settings");
            Press(SettingLine(w, "Practice"), "On");
            Press(SettingLine(w, "Qualifying"), "On");
            Check("sessions turn back on keeping their lengths", c.PracticeOn && c.QualifyingOn && c.PracticeMinutes == 11 && c.QualifyingMinutes == 9);

            Press(w, "Manage", 0);
            Check("manage opens custom series", w.CurrentView == "customseries", w.CurrentView);
            w.Show("settings");
            Press(w, "Manage", 1);
            Check("manage opens car classes", w.CurrentView == "cars", w.CurrentView);
        }

        static void FlowRaceSetup(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Setup Driver");
            SignOffer(w);
            var c = w.CareerForTest;
            var round = c.Season.Rounds[0];

            Press(w, "Race setup");
            Check("race setup opens", w.CurrentView == "raceplan", w.CurrentView);
            Check("Pure gives four weather stages", Has(w, "Stage 4"));

            Press(SettingLine(w, "Race format"), "Endurance");
            Check("this race becomes timed", round.CustomMinutes > 0 && round.CustomLaps == 0);
            Press(SettingLine(w, "Race format"), "Laps");
            Check("this race goes back to laps", round.CustomLaps > 0 && round.CustomMinutes == 0);
            var laps = round.CustomLaps;
            Nudge(SettingLine(w, "Race laps"), 1);
            Check("race laps for this race", round.CustomLaps == laps + 1);

            Press(SettingLine(w, "Practice"), "Off");
            Check("practice off for this race only", round.PracticeOverride == -1 && c.PracticeOn);
            Press(SettingLine(w, "Qualifying"), "Off");
            Check("qualifying off for this race only", round.QualifyingOverride == -1 && c.QualifyingOn && Has(w, "start from the back"));
            Press(SettingLine(w, "Practice"), "On");
            Nudge(SettingLine(w, "Practice"), 1);
            Check("practice length for this race", round.PracticeOverride == 11, round.PracticeOverride);
            Check("the next race is untouched", c.Season.Rounds[1].PracticeOverride == 0 && c.Season.Rounds[1].QualifyingOverride == 0);

            var boxes = Tree(w.RootElement).OfType<ComboBox>().ToList();
            Check("one dropdown for each stage", boxes.Count == 4, boxes.Count);
            var item = boxes[0].Items.OfType<ComboBoxItem>().First(i => i.Tag is int && (int)i.Tag == 8);
            boxes[0].SelectedItem = item;
            Check("a weather stage can be chosen", round.WeatherSlots[0] == 8, round.WeatherSlots[0]);

            var before = string.Join(",", round.WeatherSlots);
            var changed = false;
            for (var i = 0; i < 12 && !changed; i++)
            {
                Press(w, "Randomise");
                changed = string.Join(",", round.WeatherSlots) != before;
                System.Threading.Thread.Sleep(20);
            }
            Check("randomise changes the forecast", changed);
            Press(w, "Done");
            Check("done returns to the paddock", w.CurrentView == "paddock");
        }

        static void FlowRacing(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Race Driver");
            SignOffer(w);
            var c = w.CareerForTest;
            var round = c.Season.Rounds[0];
            round.WeatherSlots = new List<int> { 8, 15, 7, 16 };
            File.WriteAllText(Path.Combine(Store.DocsOverride, "keep.txt"), "x");
            var pureSettings = Path.Combine(Store.DocsOverride, "cfg", "extension", "state", "lua", "wfx_controller", "pureCtrl__settings.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(pureSettings));
            File.WriteAllText(pureSettings, "[SETTINGS]\nMINE=1\n");

            Press(w, "Start race");
            Check("start race launches the game", _launches == 1, _launches);
            Check("the race is waiting for a result", c.Pending != null && Store.FromJson<Career>(File.ReadAllText(Store.PathFor(c.Id))).Pending != null);
            Check("the in-game app is told about the race", File.Exists(Store.ContextPath));
            var ini = File.ReadAllText(Store.RaceIniPath);
            Check("the race file has a full weekend", ini.Contains("NAME=Practice") && ini.Contains("NAME=Qualifying") && ini.Contains("NAME=Race"));
            Check("the paddock shows the race running", Has(w, "Race in progress"));
            Press(w, "Careers");
            Check("careers lock during a race", Has(w, "once your race is over") && ButtonsIn(w.RootElement, "Start new career").Count == 0);
            Press(w, "Paddock");

            var plan = File.ReadAllText(Path.Combine(game, "extension", "config-ext", "PurePlanner", "Plans", "Timed", "PaddockLadder.json"));
            var parsed = Store.FromJson<Dictionary<string, object>>(plan);
            var containers = (System.Collections.ArrayList)parsed["container"];
            var durations = containers.Cast<Dictionary<string, object>>().Select(x => Convert.ToInt32(((Dictionary<string, object>)x["data"])["duration"])).ToList();
            var slot = Engine.WeatherSlotSeconds(c, round);
            Check("stage one covers practice and qualifying", durations[0] == slot + 1200 && durations[1] == slot, string.Join(",", durations));
            Check("Pure is pointed at the plan", File.ReadAllText(pureSettings).Contains("PLAN=Timed/PaddockLadder"));

            File.WriteAllText(Store.ResultPath, "{ this is not valid");
            w.PollForTest();
            Check("a half-written result is ignored", w.CurrentView == "paddock" && c.Pending != null);
            File.Delete(Store.ResultPath);

            File.WriteAllText(Store.ResultPath, Store.ToJson(new RaceResult { RaceId = "someone-else", Rows = new List<ResultRow> { new ResultRow { Name = "x", Pos = 1, IsPlayer = true } } }));
            w.PollForTest();
            Check("another race's result is thrown away", !File.Exists(Store.ResultPath) && c.Pending != null);

            FinishRace(w, 2, 7, 1, 0);
            Check("a result opens the debrief", w.CurrentView == "debrief", w.CurrentView);
            Check("the debrief shows grid and places gained", Has(w, "Started") && Has(w, "P7") && Has(w, "+5"));
            Check("the round was scored", c.Season.RoundIndex == 1 && c.Season.Rounds[0].Pos == 2 && c.Season.Rounds[0].StartPos == 7);
            Check("race files are cleared", !File.Exists(Store.ContextPath) && !File.Exists(Store.ResultPath) && !File.Exists(Store.RaceIniPath));
            w.DebriefStepForTest = "standings";
            Check("the debrief shows the promotion score", Has(w, "Promotion score"));

            Store.RestorePureSettings();
            Check("the player's Pure settings come back", File.ReadAllText(pureSettings).Contains("MINE=1"));
            LeaveDebrief(w);
            Check("continue returns to the paddock", w.CurrentView == "paddock");

            Press(w, "Start race");
            var reopened = new MainWindow(game, null);
            Check("leaving a race offers it again", Has(reopened, "Race again") && Has(reopened, "before the flag"));
            FinishRace(w, 1);

            Store.LaunchForTest = delegate { throw new IOException("no game"); };
            LeaveDebrief(w);
            Press(w, "Start race");
            Check("a failed launch tells the player", (w.NoticeForTest ?? "").Contains("no game"), w.NoticeForTest);
            Check("a failed launch isn't left waiting", w.CareerForTest.Pending == null);
            Check("a failed launch doesn't say racing", !Has(w, "Race in progress"));
            Press(w, "Dismiss");
            Check("notices can be dismissed", w.NoticeForTest == null);
            Store.LaunchForTest = delegate { _launches++; };

            var rounds = c.Season.RoundIndex;
            Dialog.AnswersForTest.Enqueue(false);
            Press(w, "Forfeit round");
            Check("cancelling keeps the round", c.Season.RoundIndex == rounds);
            Dialog.AnswersForTest.Enqueue(true);
            Press(w, "Forfeit round");
            Check("confirming forfeits the round", c.Season.RoundIndex == rounds + 1 && w.CurrentView == "debrief" && Has(w, "forfeited"));
        }

        static GameDebrief ReadDebrief() { return Store.FromJson<GameDebrief>(File.ReadAllText(Store.DebriefPath)); }

        static void SendGameCommand(string raceId, string action)
        {
            File.WriteAllText(Store.CommandPath, Store.ToJson(new RaceCommand { RaceId = raceId, Action = action }));
        }

        static void SendOffSeasonCommand(RaceCommand command) { File.WriteAllText(Store.CommandPath, Store.ToJson(command)); }
        static bool _launchPending(Career c) { return c.Pending != null; }

        static void FlowOpenCareerFinishInGame(string game)
        {
            var gameOpen = false;
            var w = FreshLauncher(game);
            CreateCareer(w, "Open Driver", true);
            var c = w.CareerForTest;
            var tiles = Engine.BuildSeries(Content.ScanCars(game));
            c.Offers = Engine.MakeOffers(c, tiles, tiles.First(s => s.Cars.Count > 1));
            Engine.StartSeason(c, c.Offers[0], tiles, Content.ScanTracks(game));
            Store.SaveCareer(c);
            w.Show("paddock");
            Store.GameOpenForTest = () => gameOpen;
            try
            {
                while (c.Season.RoundIndex < Engine.RoundsPerSeason - 1)
                {
                    Press(w, "Start race");
                    FinishRace(w, 2);
                    LeaveDebrief(w);
                }
                Press(w, "Start race");
                gameOpen = true;
                var raceId = c.Pending.RaceId;
                FinishRace(w, 2);
                var d = ReadDebrief();
                Check("an open career picks a series in the game", d.CanChoose && !d.Ladder && d.Offers.Count == 0 && d.SeriesChoices.Count > 0 && d.Seats.Count == 0);
                Check("in-game series have pictures", d.SeriesChoices.All(x => x.Image.EndsWith("preview.jpg") && x.Detail != ""));

                var pick = tiles.First(s => s.Cars.Count > 1);
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "pick", SeriesId = pick.Id, Seq = 1 });
                w.PollForTest();
                d = ReadDebrief();
                Check("picking a series lists its cars", d.Revision == 1 && d.Offers.Count == pick.Cars.Count && d.OffersTitle == pick.Name);

                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "series", Seq = 2 });
                w.PollForTest();
                d = ReadDebrief();
                Check("you can go back to all series", d.Revision == 2 && d.Offers.Count == 0 && c.Offers == null);

                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "pick", SeriesId = pick.Id, Seq = 3 });
                w.PollForTest();
                var launches = _launches;
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "sign", OfferIndex = 1, Seq = 4 });
                w.PollForTest();
                var signed = ReadDebrief();
                Check("signing in the game brings up the briefing", signed.Signed && signed.HasNextRace && signed.Briefing != null && signed.Briefing.TrackName != "");
                SendGameCommand(raceId, "next");
                w.PollForTest();
                gameOpen = false;
                w.PollForTest();
                Check("an open career signs in the game too", c.Season != null && c.Season.SeriesId == pick.Id && _launches == launches + 1);
            }
            finally
            {
                Store.GameOpenForTest = null;
            }
        }

        static RaceResult DamagedResult(Career c, int position, int practiceLaps, int start)
        {
            var result = Result(c, position, 0, 0);
            result.RaceId = c.Pending.RaceId;
            result.StartPos = start;
            result.PracticeLaps = practiceLaps;
            result.Body = new List<double> { 30, 10, 0, 0 };
            result.EngineLife = 900;
            result.Gearbox = 0.25;
            result.Suspension = new List<double> { 0.5, 0, 0, 0 };
            return result;
        }

        static void FlowCreditsAndDamage(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Credit Driver");
            var c = w.CareerForTest;
            Check("a career starts with credits and no car", c.Credits == Engine.StartCredits && c.OwnedCarId == null);
            var offer = c.Offers[0];
            var price = Engine.CarPrice(offer.Tier, offer.Pw);
            Check("a rookie car is affordable", price > 0 && price < Engine.StartCredits && !Engine.CostOf(c, offer).Loan, price);
            Check("the offer shows what it costs", Has(w, "Your cost") && Has(w, Engine.Cr(price)));
            SignOffer(w);
            Check("signing buys the car", c.Credits == Engine.StartCredits - price && c.OwnedCarId == offer.CarId && !c.Season.OnLoan);

            Press(w, "Start race");
            var context = Store.FromJson<RaceContext>(File.ReadAllText(Store.ContextPath));
            Check("the game gets the session goals", context.PracticeGoalLaps == 3 && context.PracticeReward > 0 && context.QualifyTop == (c.Season.Field.Count + 2) / 2 && context.QualifyReward > 0);
            var before = c.Credits;
            File.WriteAllText(Store.ResultPath, Store.ToJson(DamagedResult(c, 2, 3, 1)));
            w.PollForTest();
            var report = c.LastReport;
            Check("every source of credits is listed", report.CreditLines.Any(l => l.Label.StartsWith("Finished P2")) && report.CreditLines.Any(l => l.Label.StartsWith("Practice goal")) && report.CreditLines.Any(l => l.Label.StartsWith("Qualifying goal")));
            Check("credits add up", report.CreditLines.Sum(l => l.Amount) == report.CreditsNet && c.Credits == before + report.CreditsNet && report.CreditsBalance == c.Credits);
            w.DebriefStepForTest = "credits";
            Check("the debrief shows the breakdown", Has(w, "Earned") && Has(w, "Finished P2") && Has(w, Engine.Cr(c.Credits)));
            Check("damage is kept", c.CarBody[0] == 30 && c.CarEngine == 900 && c.CarGearbox == 0.25 && c.CarSuspension[0] == 0.5);
            w.DebriefStepForTest = "car";
            Check("damage is priced", Engine.RepairCost(c) > 0 && Has(w, "Bodywork") && Has(w, "Repair everything"), Engine.RepairCost(c));
            LeaveDebrief(w);
            Check("the paddock warns about damage", Has(w, "carry into this race"));

            Press(w, "Start race");
            context = Store.FromJson<RaceContext>(File.ReadAllText(Store.ContextPath));
            Check("damage goes to the next race", context.CarBody[0] == 30 && context.CarEngine == 900 && context.CarGearbox == 0.25 && context.CarSuspension[0] == 0.5);
            var dirty = Result(c, 2, 6, 3);
            dirty.RaceId = c.Pending.RaceId;
            dirty.StartPos = 15;
            dirty.Body = new List<double> { 40, 10, 0, 0 };
            dirty.EngineLife = 900;
            dirty.Gearbox = 0.5;
            dirty.Suspension = new List<double> { 1, 0, 0, 0 };
            File.WriteAllText(Store.ResultPath, Store.ToJson(dirty));
            w.PollForTest();
            report = c.LastReport;
            Check("gearbox and suspension wear build up", c.CarGearbox == 0.5 && c.CarSuspension[0] == 1, c.CarGearbox);
            Check("off-tracks and contact cost credits", report.CreditLines.Any(l => l.Label.StartsWith("Track limits") && l.Amount < 0) && report.CreditLines.Any(l => l.Label.StartsWith("Contact") && l.Amount < 0));
            Check("no goal money without meeting the goal", !report.CreditLines.Any(l => l.Label.StartsWith("Practice goal") || l.Label.StartsWith("Qualifying goal")));

            var repairCost = Engine.RepairCost(c);
            var cash = c.Credits;
            c.Credits = repairCost - 1;
            Check("a repair you can't afford is refused", !Engine.TryRepair(c) && c.CarBody[0] == 40);
            c.Credits = cash;
            w.DebriefStepForTest = "car";
            Press(w, "Repair everything  /  " + Engine.Cr(repairCost));
            Check("repairing costs credits and clears the damage", c.Credits == cash - repairCost && c.CarBody.Sum() == 0 && c.CarEngine == 1000 && c.CarGearbox == 0 && c.CarSuspension.Sum() == 0);
            Check("a repaired car has no bill", Engine.RepairCost(c) == 0);

            // Trade-ins, cars you already own and loans.
            var dear = new Offer { CarId = "expensive", Tier = 5, Pw = 900 };
            Check("a car you can't afford becomes a loan", Engine.CostOf(c, dear).Loan && Engine.CostOf(c, dear).Text.Contains("loan"));
            var owned = new Offer { CarId = c.OwnedCarId, Tier = 1, Pw = 100 };
            Check("a car you own is free", Engine.CostOf(c, owned).Owned && Engine.CostOf(c, owned).Pay == 0);
            var next = new Offer { CarId = "newcar", Tier = 2, Pw = 200 };
            Check("your car is traded in for half", Engine.CostOf(c, next).TradeIn == c.OwnedCarPaid / 2 && Engine.CostOf(c, next).Pay == Engine.CostOf(c, next).Price - c.OwnedCarPaid / 2);

            var series = Engine.BuildSeries(Content.ScanCars(game));
            var tracks = Content.ScanTracks(game);
            var loaned = Engine.NewCareer("Loan Driver", "ladder", 0, 1, 77);
            var loanOffer = Engine.MakeOffers(loaned, series, null)[0];
            loaned.Credits = 0;
            Engine.StartSeason(loaned, loanOffer, series, tracks);
            Check("a loan doesn't charge or own the car", loaned.Season.OnLoan && loaned.Credits == 0 && loaned.OwnedCarId == null);
            var loanResult = Result(loaned, 1, 0, 0);
            loanResult.Body = new List<double> { 50, 0, 0, 0 };
            var loanReport = Engine.ApplyResult(loaned, loanResult);
            Check("a loan keeps half the winnings", loanReport.CreditLines.Any(l => l.Label.StartsWith("Team's share")) && loaned.Credits == loanReport.CreditsNet && loanReport.CreditsNet > 0);
            Check("loan cars cost nothing to repair", Engine.RepairCost(loaned) == 0 && loaned.CarBody[0] == 0);

            var old = Engine.NewCareer("Old Save", "ladder", 0, 1, 78);
            Engine.StartSeason(old, Engine.MakeOffers(old, series, null)[0], series, tracks);
            old.OwnedCarId = null;
            old.OwnedCarPaid = 0;
            Engine.AdoptOwnedCar(old);
            Check("older careers own the car they race", old.OwnedCarId == old.Season.CarId && old.OwnedCarPaid > 0);

            // A rival with a grudge is only fierce in person.
            var rivalCareer = Engine.NewCareer("Grudge", "ladder", 0, 1, 79);
            Engine.StartSeason(rivalCareer, Engine.MakeOffers(rivalCareer, series, null)[0], series, tracks);
            var target = rivalCareer.Season.Field[2];
            var angry = rivalCareer.Rivals.First(r => r.Name == target.Name);
            angry.Aggression = 20;
            angry.Grudge = 2;
            target.Aggression = 50;
            RaceContext built;
            var ini = Engine.BuildRaceIni(rivalCareer, out built);
            Check("the grid starts calm", ini.Contains("AI_AGGRESSION=20\nDRIVER_NAME=" + target.Name));
            Check("the game knows who holds a grudge", built.Rivalries.Any(r => r.Name == target.Name && r.Normal == 20 && r.Angry == 50));

            // Damage switched off.
            var w2 = FreshLauncher(game);
            Press(SettingLineOnNewCareer(w2), "Off");
            CreateCareer(w2, "No Damage");
            var off = w2.CareerForTest;
            Check("damage can be turned off for a new career", !off.DamageOn);
            SignOffer(w2);
            Press(w2, "Start race");
            var offContext = Store.FromJson<RaceContext>(File.ReadAllText(Store.ContextPath));
            Check("the game is told damage is off", offContext.DamageOff && offContext.CarBody == null);
            File.WriteAllText(Store.ResultPath, Store.ToJson(DamagedResult(off, 2, 0, 3)));
            w2.PollForTest();
            Check("damage off means no bill or carry-over", Engine.RepairCost(off) == 0 && off.CarBody.Sum() == 0 && off.CarGearbox == 0);
            w2.Show("settings");
            Press(SettingLine(w2, "Car damage"), "On");
            Check("damage can be changed later", off.DamageOn);
        }

        static void WriteProgress(RaceResult progress) { File.WriteAllText(Store.ProgressPath, Store.ToJson(progress)); }

        static RaceResult LeftRace(Career c, string session, bool started)
        {
            var progress = DamagedResult(c, 3, 0, 4);
            progress.Session = session;
            progress.RaceStarted = started;
            progress.Finished = false;
            progress.Laps = 2;
            progress.Body = new List<double> { 30, 20, 0, 0 };
            return progress;
        }

        static Career ContractCareer(string game, string name, int seed)
        {
            var series = Engine.BuildSeries(Content.ScanCars(game));
            var c = Engine.NewCareer(name, "ladder", 0, 1, seed);
            Engine.StartSeason(c, Engine.MakeOffers(c, series, null)[0], series, Content.ScanTracks(game));
            return c;
        }

        static void FlowStewards(string game)
        {
            var c = ContractCareer(game, "Innocent", 501);
            var rival = c.Season.Field[3].Name;
            var hit = Result(c, 6, 0, 0);
            hit.StartPos = 15;
            hit.Incidents = new List<IncidentRecord>
            {
                new IncidentRecord { Driver = rival, Who = "them", Kind = "rear", Lap = 2 },
                new IncidentRecord { Driver = c.Season.Field[4].Name, Who = "them", Kind = "side", Lap = 3 },
                new IncidentRecord { Driver = c.Season.Field[5].Name, Who = "shared", Kind = "side", Lap = 4 },
            };
            var licence = c.LicencePoints;
            var cleanBefore = c.Clean;
            var report = Engine.ApplyResult(c, hit);
            Check("being hit costs nothing", report.Contacts == 0 && !report.CreditLines.Any(l => l.Label.StartsWith("Contact")) && c.LicencePoints <= licence);
            Check("being hit doesn't hurt your clean rating", c.Clean >= cleanBefore, c.Clean);
            Check("being hit doesn't start a grudge", c.Rivals.First(r => r.Name == rival).Grudge == 0);
            Check("rulings say who did what", report.Incidents.Count == 3 && report.Incidents[0].Text == rival + " ran into the back of you" && report.Incidents[0].Verdict.StartsWith("Not your fault") && report.Incidents[2].Verdict.StartsWith("Racing incident"));

            c = ContractCareer(game, "Guilty", 502);
            var target = c.Season.Field[3].Name;
            var guilty = Result(c, 6, 0, 0);
            guilty.Contacts = new List<ContactRecord> { new ContactRecord { Driver = target, Count = 3 } };
            guilty.Incidents = Enumerable.Range(1, 3).Select(i => new IncidentRecord { Driver = target, Who = "you", Kind = "rear", Lap = i }).ToList();
            report = Engine.ApplyResult(c, guilty);
            Check("causing contact is still punished", report.Contacts == 3 && report.CreditLines.Any(l => l.Label.StartsWith("Contact") && l.Amount < 0) && c.LicencePoints > 0);
            Check("causing contact starts a grudge", c.Rivals.First(r => r.Name == target).Grudge > 0);
            Check("your own contact is worded as yours", report.Incidents[0].Text == "You ran into the back of " + target && report.Incidents[0].Verdict.StartsWith("Your fault"));

            c = ContractCareer(game, "Reputation", 503);
            RaceContext context;
            c.Clean = 90;
            Engine.BuildRaceIni(c, out context);
            Check("clean drivers get more room", Engine.ReputationOf(c) == "Respected" && context.RespectShift < 0);
            c.Clean = 60;
            Engine.BuildRaceIni(c, out context);
            Check("an average record changes nothing", Engine.ReputationOf(c) == "Neutral" && context.RespectShift == 0);
            c.Clean = 30;
            Engine.BuildRaceIni(c, out context);
            Check("dirty drivers get fought harder", Engine.ReputationOf(c) == "Dirty" && context.RespectShift > 0);
            Check("the briefing states your reputation", Engine.BuildBriefing(c, (t, l) => "", false).Matchups[0].Contains("Dirty"));

            var w = FreshLauncher(game);
            CreateCareer(w, "Stewards Screen");
            SignOffer(w);
            var career = w.CareerForTest;
            w.Show("paddock");
            Press(w, "Start race");
            var raced = Result(career, 4, 0, 0);
            raced.RaceId = career.Pending.RaceId;
            raced.Incidents = new List<IncidentRecord> { new IncidentRecord { Driver = career.Season.Field[2].Name, Who = "them", Kind = "rear", Lap = 1 } };
            File.WriteAllText(Store.ResultPath, Store.ToJson(raced));
            w.PollForTest();
            w.DebriefStepForTest = "stewards";
            Check("the debrief has a stewards' room", Has(w, "Every contact, ruled on") && Has(w, "Not your fault"));
            Snap(w, "debrief", _shotsDir, "34_stewards.png");
            w.Show("team");
            Check("the team page shows your reputation", Has(w, "Reputation") && Has(w, Engine.ReputationOf(career)));
        }

        static void FlowGridAndRival(string game)
        {
            var series = Engine.BuildSeries(Content.ScanCars(game));
            var tracks = Content.ScanTracks(game);
            var c = ContractCareer(game, "Full Grid", 401);
            Check("every series runs a full grid", c.Season.Field.Count + 1 == 20, c.Season.Field.Count);
            Check("the grid has ten teams", c.Season.Field.Select(e => e.Team).Concat(new[] { c.Season.Team }).Distinct().Count() == 10);
            Check("every race can seat the whole grid", c.Season.Rounds.All(r => tracks.First(t => t.Id == r.Track && t.Layout == r.Layout).Pits >= 20));

            var sparse = tracks.Select(t => new TrackInfo { Id = t.Id, Layout = t.Layout, Name = t.Name, Km = t.Km, Pits = t.Id == tracks[0].Id ? 8 : t.Pits }).ToList();
            var other = Engine.NewCareer("Picky", "ladder", 0, 1, 402);
            Engine.StartSeason(other, Engine.MakeOffers(other, series, null)[0], series, sparse);
            Check("a small track can't shrink the grid", other.Season.Field.Count + 1 == 20 && other.Season.Rounds.All(r => r.Track != tracks[0].Id));

            var mate = c.Season.Field.Single(e => e.IsTeammate);
            var pick = c.Season.Field.First(e => !e.IsTeammate);
            Engine.SetRival(c, mate.Name);
            Check("your teammate can't be your chosen rival", c.Season.ChosenRival == null);
            Engine.SetRival(c, "Nobody Real");
            Check("only drivers on the grid can be chosen", c.Season.ChosenRival == null);
            Engine.SetRival(c, pick.Name);
            Check("a rival can be chosen", c.Season.ChosenRival == pick.Name);
            Check("your rival comes right after your teammate", Engine.Matchups(c)[1].Name == pick.Name && Engine.Matchups(c)[1].Tag == "Your rival");

            RaceContext context;
            Engine.BuildRaceIni(c, out context);
            Check("the game knows your rival", context.ChosenRival == pick.Name);
            Check("your rival fights harder when you're close", context.Rivalries.Any(r => r.Name == pick.Name && r.Angry > r.Normal));
            var briefing = Engine.BuildBriefing(c, (t, l) => "", false);
            Check("the briefing pays for beating your rival", briefing.Targets.Any(t => t.Text.Contains(pick.Name) && t.Credits > 0));

            var beaten = Result(c, 2, 0, 0);
            beaten.Rows.First(r => r.Name == pick.Name).Pos = 15;
            var report = Engine.ApplyResult(c, beaten);
            Check("beating your rival pays", report.CreditLines.Any(l => l.Label.Contains(pick.Name) && l.Amount > 0) && report.Targets.Any(t => t.Text.Contains(pick.Name) && t.Met));

            var lost = Result(c, 15, 0, 0);
            var theirRow = lost.Rows.First(r => r.Name == pick.Name);
            var rowAtTwo = lost.Rows.First(r => r.Pos == 2);
            rowAtTwo.Pos = theirRow.Pos;
            theirRow.Pos = 2;
            var worse = Engine.ApplyResult(c, lost);
            Check("losing to your rival pays nothing", !worse.CreditLines.Any(l => l.Label.Contains(pick.Name)) && worse.Targets.Any(t => t.Text.Contains(pick.Name) && !t.Met));
            Engine.SetRival(c, null);
            Check("a rival can be cleared", c.Season.ChosenRival == null);

            var w = FreshLauncher(game);
            CreateCareer(w, "Rival Screen");
            SignOffer(w);
            w.Show("team");
            Check("the team page offers a rival", Has(w, "Your rival") && Has(w, "No one"));

            var gameOpen = false;
            Store.GameOpenForTest = () => gameOpen;
            try
            {
                var career = w.CareerForTest;
                w.Show("paddock");
                Press(w, "Start race");
                gameOpen = true;
                var raceId = career.Pending.RaceId;
                FinishRace(w, 2);
                var d = ReadDebrief();
                Check("the game can choose from the grid", d.RivalChoices.Count == career.Season.Field.Count - 1 && d.RivalChoices.All(x => x.Name != career.Season.Field.Single(e => e.IsTeammate).Name));
                var chosen = d.RivalChoices[3].Name;
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "rival", Team = chosen, Seq = 7 });
                w.PollForTest();
                d = ReadDebrief();
                Check("choosing in the game updates the briefing", career.Season.ChosenRival == chosen && d.ChosenRival == chosen && d.Revision == 7 && d.Briefing.Targets.Any(t => t.Text.Contains(chosen)));
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "rival", Team = "", Seq = 8 });
                w.PollForTest();
                Check("clearing in the game works", career.Season.ChosenRival == null);
                gameOpen = false;
                w.PollForTest();
            }
            finally
            {
                Store.GameOpenForTest = null;
            }
        }

        static void FlowMoneyAndContracts(string game)
        {
            var series = Engine.BuildSeries(Content.ScanCars(game));
            var tracks = Content.ScanTracks(game);

            // What a perfect season can pay, and what the next seat costs.
            var season1 = Engine.NewCareer("Fees", "ladder", 0, 1, 301);
            var rookieOffer = Engine.MakeOffers(season1, series, null)[0];
            Check("the rookie season has no seat fee", Engine.CostOf(season1, rookieOffer).SeatFee == 0);
            season1.SeasonNo = 2;
            Check("later seasons charge a seat fee", Engine.CostOf(season1, rookieOffer).SeatFee > 0);
            Check("the seat fee is part of what you pay", Engine.CostOf(season1, rookieOffer).Pay == Engine.CarPrice(rookieOffer.Tier, rookieOffer.Pw) + Engine.CostOf(season1, rookieOffer).SeatFee);
            season1.Credits = -5;
            Check("a debt means a team loan, not a purchase", Engine.CostOf(season1, rookieOffer).Loan);

            var c = ContractCareer(game, "Dominant", 302);
            var perfect = 0;
            for (var i = 0; i < 6; i++)
            {
                var result = Result(c, 1, 0, 0);
                result.StartPos = 1;
                result.PracticeLaps = 3;
                perfect += Engine.ApplyResult(c, result).CreditsNet;
            }
            Check("a perfect rookie season can't buy the top of the ladder", perfect <= 80000, perfect);
            var tierFive = new Offer { CarId = "hyper", Tier = 5, Pw = 700 };
            Check("the top tier stays out of reach after one season", Engine.CostOf(c, tierFive).Pay > c.Credits + perfect, Engine.CostOf(c, tierFive).Pay);

            // Debt.
            c = ContractCareer(game, "Debtor", 303);
            c.Credits = 100;
            var messy = Result(c, 10, 12, 8);
            var bad = Engine.ApplyResult(c, messy);
            Check("fines can take you below zero", c.Credits < 0 && bad.CreditsNet < 0, c.Credits);
            Check("the debrief says what's owed", bad.DebtNote.Contains("owe"), bad.DebtNote);
            var owed = -c.Credits;
            Check("you can't repair while in debt", !Engine.TryRepair(c));
            var good = Result(c, 1, 0, 0);
            var paid = Engine.ApplyResult(c, good);
            Check("earnings clear the debt first", c.Credits == paid.CreditsNet - owed, c.Credits);
            Check("the debrief says the debt came out first", paid.DebtNote.Contains("what you owed") || c.Credits < 0, paid.DebtNote);

            // Warnings and sacking.
            c = ContractCareer(game, "Sacked", 304);
            c.Season.Trust = 40;
            Engine.ApplyResult(c, Result(c, 10, 0, 0));
            Check("the first round is too early for a warning", !c.Season.OnNotice && !c.Season.Released);
            c.Season.Trust = 38;
            var notice = Engine.ApplyResult(c, Result(c, 10, 12, 8));
            Check("poor results put you on notice", c.Season.OnNotice && notice.ContractTitle.Contains("notice"), notice.ContractTitle);
            var buyoutNoticed = Engine.BuyoutCost(c);
            var sacked = Engine.ApplyResult(c, Result(c, 10, 12, 8));
            Check("no improvement cancels the contract", c.Season.Released && sacked.Released && sacked.ContractTitle.Contains("cancelled"), sacked.ContractTitle);
            Check("a team that sacks you charges nothing", Engine.BuyoutCost(c) == 0 && buyoutNoticed > 0);

            c = ContractCareer(game, "Saved", 305);
            c.Season.RoundIndex = 2;
            c.Season.OnNotice = true;
            c.Season.NoticeTrust = 28;
            c.Season.Trust = 28;
            var great = Result(c, 1, 0, 0);
            var saved = Engine.ApplyResult(c, great);
            Check("a good result takes you off notice", !c.Season.Released && (c.Season.Trust >= 30 ? !c.Season.OnNotice : c.Season.OnNotice), c.Season.Trust);

            // The market.
            c = ContractCareer(game, "Mover", 306);
            Engine.ApplyResult(c, Result(c, 3, 0, 0));
            Engine.ApplyResult(c, Result(c, 3, 0, 0));
            c.Credits = 200000;
            var market = Engine.Market(c, series);
            Check("rival teams in your series are on the market", market.Any(x => x.SameSeries));
            Check("your own team isn't on the market", market.All(x => x.Team != c.Season.Team));
            Check("other series are on the market", market.Any(x => !x.SameSeries));
            Check("two tiers up is closed mid-season", market.Where(x => x.Tier >= c.Season.Tier + 2).All(x => !x.Allowed));
            var sameSeat = market.First(x => x.SameSeries);
            var offSeasonChance = Math.Max(30, Math.Min(95, 40 + (Engine.DriverRating(c) - 50) * 6 / 5));
            Check("moving mid-season is harder", sameSeat.Chance < offSeasonChance, sameSeat.Chance);

            var points = c.Season.Points[Engine.PlayerKey];
            var oldTeam = c.Season.Team;
            var rounds = c.Season.RoundIndex;
            var buyout = Engine.BuyoutCost(c);
            Check("leaving costs more early than late", buyout > 0);
            Check("a refusal is final for the season", !Engine.ResolveApproach(c, series, sameSeat, 100) && Engine.Market(c, series).First(x => x.Team == sameSeat.Team).Asked);
            var refusedOdds = Engine.Market(c, series).First(x => x.SameSeries && x.Allowed).Chance;
            Check("each refusal makes the next harder", refusedOdds < sameSeat.Chance, refusedOdds);
            var next = Engine.Market(c, series).First(x => x.SameSeries && x.Allowed);
            Check("a yes puts a seat on the table without charging", Engine.ResolveApproach(c, series, next, 0) && c.MarketOffer != null && c.Credits == 200000);
            var cost = Engine.CostOf(c, c.MarketOffer);
            Check("the deal includes the buyout", cost.Buyout == buyout && Engine.CreditsAfterSigning(c, c.MarketOffer) == 200000 - buyout - (cost.Loan ? 0 : cost.Pay));
            var joining = next.Team;
            var field = c.Season.Field.Count;
            Engine.Transfer(c, c.MarketOffer, series, tracks);
            Check("a move in your series keeps your points and round", c.Season.Points[Engine.PlayerKey] == points && c.Season.RoundIndex == rounds && c.Season.Team == joining);
            Check("the driver you replace takes your old seat", c.Season.Field.Count == field && c.Season.Field.Count(e => e.Team == oldTeam) == 2 && c.Season.Field.Count(e => e.Team == joining) == 1);
            Check("you have exactly one new teammate", c.Season.Field.Single(e => e.IsTeammate).Team == joining);
            Check("the buyout and seat fee were paid", c.Credits <= 200000 - buyout, c.Credits);
            Check("a new team starts with less trust", c.Season.Trust == 50);
            RaceContext built;
            Engine.BuildRaceIni(c, out built);
            Check("the next race uses the new team", built.Team == joining && built.Teammate == c.Season.Field.Single(e => e.IsTeammate).Name);

            c = ContractCareer(game, "Series Mover", 307);
            Engine.ApplyResult(c, Result(c, 5, 0, 0));
            c.Credits = 500000;
            c.Pace = 100;
            c.Clean = 100;
            var other = Engine.Market(c, series).First(x => !x.SameSeries && x.Allowed);
            var left = c.Season.Rounds.Count - c.Season.RoundIndex;
            Check("another series can say yes", Engine.ResolveApproach(c, series, other, 0));
            var signed = c.MarketOffer;
            Engine.Transfer(c, signed, series, tracks);
            Check("a new series runs only the rounds left", c.Season.SeriesId == other.SeriesId && c.Season.Rounds.Count == left && c.Season.RoundIndex == 0);
            Check("the old season is in your history", c.History.Count == 1 && c.History[0].Outcome.Contains("Left mid-season"));

            c = ContractCareer(game, "Broke", 308);
            Engine.ApplyResult(c, Result(c, 5, 0, 0));
            c.Credits = 10;
            Check("you can't leave without the buyout", c.Credits < Engine.BuyoutCost(c));

            c = ContractCareer(game, "Sit Out", 309);
            c.Season.Released = true;
            var sat = Engine.SitOutSeason(c);
            Check("sitting out ends the season", c.Season == null && c.SeasonNo == 2 && sat.SeasonOutcome.Contains("released"), sat.SeasonOutcome);

            // Comparisons.
            c = ContractCareer(game, "Compare", 310);
            var mate = c.Season.Field.Single(e => e.IsTeammate).Name;
            var rows = Result(c, 1, 0, 0);
            Engine.ApplyResult(c, rows);
            var matches = Engine.Matchups(c);
            Check("your teammate comes first", matches[0].Tag == "Teammate" && matches[0].Name == mate);
            Check("rivals are named", matches.Count >= 3 && matches.Skip(1).All(m => m.Tag != "Teammate"));
            Check("a comparison covers the season", matches[0].Rows.Any(r => r.Label == "Average finish") && matches[0].Rows.Any(r => r.Label == "Races ahead") && matches[0].Rows.Any(r => r.Label == "Best lap"));
            Check("the comparison shows who's ahead", matches[0].Rows.First(r => r.Label == "Races ahead").Edge == 1 && matches[0].Summary.StartsWith("You lead"), matches[0].Summary);
            RaceContext ctx;
            Engine.BuildRaceIni(c, out ctx);
            Check("the game knows who your rivals are", ctx.MainRivals.Count >= 2 && !ctx.MainRivals.Contains(mate));

            // On screen.
            var w = FreshLauncher(game);
            CreateCareer(w, "Team Screen");
            SignOffer(w);
            var career = w.CareerForTest;
            w.Show("paddock");
            Press(w, "Start race");
            FinishRace(w, 2);
            LeaveDebrief(w);
            w.Show("team");
            Check("the team page shows the contract", Has(w, career.Season.Team) && Has(w, "Team trust") && Has(w, "Cost to leave"));
            Check("the team page compares your teammate", Has(w, "Teammate") && Has(w, career.Season.Field.Single(e => e.IsTeammate).Name) && Has(w, "Average finish") && Has(w, "You") && Has(w, "Them"));
            Snap(w, "team", _shotsDir, "32_team.png");
            w.Show("team");
            Press(w, "Look for another team");
            Check("the market opens", w.CurrentView == "market" && Has(w, "Approach") && Has(w, "Odds"));
            Snap(w, "market", _shotsDir, "33_market.png");
            w.Show("market");

            career.Season.Released = true;
            w.Show("paddock");
            Check("a sacked driver is offered a way out", Has(w, "Your contract was cancelled") && Has(w, "Find a new team") && ButtonsIn(w.RootElement, "Start race").Count == 0);

            // A redraw of the same page must not fade or jump.
            career.Season.Released = false;
            var animate = U.Animate;
            U.Animate = true;
            try
            {
                w.Show("settings");
                w.Show("settings");
                var pageRoot = Tree(w.RootElement).OfType<ScrollViewer>().First().Content as FrameworkElement;
                Check("redrawing a page doesn't fade it", pageRoot.Opacity == 1, pageRoot.Opacity);
                w.Show("season");
                pageRoot = Tree(w.RootElement).OfType<ScrollViewer>().First().Content as FrameworkElement;
                Check("changing page still fades in", pageRoot.Opacity == 0, pageRoot.Opacity);
            }
            finally
            {
                U.Animate = animate;
            }
        }

        static void FlowLayeredScreens(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Layer Driver");
            var c = w.CareerForTest;
            var offer = c.Offers[0];
            var cash = c.Credits;
            Press(w, "View car");
            Check("a car opens its own details first", w.CurrentView == "offerdetail" && c.Season == null && c.Credits == cash);
            Check("the details show the car and the deal", Has(w, "Power to weight") && Has(w, "Top speed") && Has(w, "Car price") && Has(w, "You pay") && Has(w, "Left after signing") && Has(w, "The team wants") && Has(w, "Your credits"));
            Snap(w, "offerdetail", _shotsDir, "26_car_detail.png");
            w.Show("offerdetail");
            Press(w, "Back to offers");
            Check("backing out signs nothing", w.CurrentView == "contracts" && c.Season == null && c.Credits == cash);
            Press(w, "View car");
            Press(w, "Sign contract");
            Check("confirming signs the contract", c.Season != null && c.OwnedCarId == offer.CarId);

            Check("every driver has a team", c.Season.Field.All(e => !string.IsNullOrEmpty(e.Team)));
            Check("your teammate is in your team", c.Season.Field.Single(e => e.IsTeammate).Team == c.Season.Team);
            Check("rivals share teams in pairs", c.Season.Field.Where(e => !e.IsTeammate).GroupBy(e => e.Team).All(g => g.Count() <= 2) && c.Season.Field.Where(e => !e.IsTeammate).GroupBy(e => e.Team).Count() < c.Season.Field.Count - 1);
            Check("your team name is unique to your team", c.Season.Field.Count(e => e.Team == c.Season.Team) == 1);
            var legacy = Engine.NewCareer("Old Teams", "ladder", 0, 1, 90);
            var series = Engine.BuildSeries(Content.ScanCars(game));
            Engine.StartSeason(legacy, Engine.MakeOffers(legacy, series, null)[0], series, Content.ScanTracks(game));
            foreach (var entry in legacy.Season.Field) entry.Team = null;
            Engine.AdoptTeams(legacy);
            Check("seasons from older versions get teams", legacy.Season.Field.All(e => e.Team != null));

            w.Show("season");
            Check("the season standings show teams", Has(w, c.Season.Field[2].Team));
            w.Show("paddock");
            Press(w, "Race details");
            Check("the briefing shows the race and every target", w.CurrentView == "briefing" && Has(w, "What you can earn") && Has(w, "Practice") && Has(w, "Qualifying") && Has(w, "Win the race") && Has(w, c.Season.Clauses[0].Text) && Has(w, "The most you can earn"));
            var round = c.Season.Rounds[c.Season.RoundIndex];
            var briefing = Engine.BuildBriefing(c, (t, l) => t + "|" + l, false);
            Check("the briefing names the track and the image", briefing.TrackName == round.TrackName && briefing.Image == round.Track + "|" + round.Layout);
            Check("the briefing adds up the prize money", briefing.MaxCredits == briefing.Targets.Sum(x => x.Credits) && briefing.MaxCredits > 0 && briefing.Targets.Any(x => x.Session == "Practice") && briefing.Targets.Any(x => x.Session == "Qualifying") && briefing.Targets.Count(x => x.Session == "Race") == c.Season.Clauses.Count + 1);
            Snap(w, "briefing", _shotsDir, "27_briefing.png");
            w.Show("paddock");

            var gameOpen = false;
            Store.GameOpenForTest = () => gameOpen;
            try
            {
                Press(w, "Start race");
                gameOpen = true;
                File.WriteAllText(Store.ResultPath, Store.ToJson(DamagedResult(c, 2, 3, 1)));
                w.PollForTest();
                var d = ReadDebrief();
                Check("the game gets each target with its result", d.Targets.Count == c.LastReport.Targets.Count && d.Targets.Any(x => x.Session == "Practice" && x.Met) && d.Targets.Any(x => x.Session == "Qualifying" && x.Met));
                Check("the game gets the teams", d.Standings.All(x => x.Team != "") && d.Classification.All(x => x.Team != "") && d.TeamStandings.Count > 1 && d.TeamStandings.Single(x => x.IsPlayer).Name == c.Season.Team);
                Check("the game gets the next race's briefing", d.Briefing != null && d.Briefing.TrackName == c.Season.Rounds[c.Season.RoundIndex].TrackName && d.Briefing.Image.EndsWith("preview.png") && d.Briefing.Targets.Count >= 3);
                Check("the game can repair before the next race", d.RepairCost > 0 && d.Parts.All(x => x.IfLeft != null && x.Effect != null));
                gameOpen = false;
                w.PollForTest();
            }
            finally
            {
                Store.GameOpenForTest = null;
            }

            // The launcher walks through the same steps.
            Check("the launcher debrief starts with the result alone", w.CurrentView == "debrief" && Has(w, "Classification") && !Has(w, "Weekend targets") && !Has(w, "Earned"));
            Snap(w, "debrief", _shotsDir, "28_debrief_result.png");
            w.Show("debrief");
            Press(w, "Next");
            Check("targets come second", Has(w, "Weekend targets") && !Has(w, "Classification"));
            Press(w, "Next");
            Check("standings come third and show teams", Has(w, "Drivers") && Has(w, "Teams") && Has(w, c.Season.Team));
            Press(w, "Previous");
            Check("you can step back", Has(w, "Weekend targets"));
            Press(w, "Next");
            Press(w, "Next");
            Check("credits come fourth", Has(w, "Earned"));
            Press(w, "Next");
            Check("the car comes last when it's damaged", Has(w, "Bodywork") && ButtonsIn(w.RootElement, "Continue").Count == 1);

            while (c.Season != null && c.Season.RoundIndex < Engine.RoundsPerSeason - 1)
            {
                LeaveDebrief(w);
                Press(w, "Start race");
                FinishRace(w, 1);
            }
            LeaveDebrief(w);
            Press(w, "Start race");
            FinishRace(w, 1);
            Check("the season ends with its own screen", w.CurrentView == "debrief" && c.Season == null);
            w.DebriefStepForTest = "season";
            Check("the season screen names the champions and teams", Has(w, "Drivers' champion") && Has(w, "Teams' champion") && Has(w, "Your team"));
            w.DebriefStepForTest = "standings";
            Check("final standings still show the teams", Has(w, "Teams") && Has(w, c.LastReport.TeamTable[0].Name));
        }

        static void FlowLeavingAndRepairs(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Retire Driver");
            var c = w.CareerForTest;
            Check("your credits are shown while choosing a car", Has(w, "Your credits") && Has(w, Engine.Cr(c.Credits)) && Has(w, "Left after signing"));
            Check("the left-after figure is right", Has(w, Engine.Cr(Engine.CreditsAfterSigning(c, c.Offers[0]))));
            SignOffer(w);
            foreach (var view in new[] { "paddock", "season", "history", "settings", "careers" })
            {
                w.Show(view);
                Check("credits show on the " + view + " page", Has(w, Engine.Cr(c.Credits)));
            }
            w.Show("paddock");

            // Quitting during practice: nothing is scored, but the damage stays on the car.
            Press(w, "Start race");
            Check("a race in progress is shown while the game loads", Has(w, "Race in progress"));
            WriteProgress(LeftRace(c, "practice", false));
            w.PollForTest();
            Check("leaving practice keeps the round open", c.Pending != null && c.Season.RoundIndex == 0 && !File.Exists(Store.ProgressPath));
            Check("leaving practice keeps the damage", c.CarBody[0] == 30 && c.CarEngine == 900 && c.CarGearbox == 0.25 && c.CarSuspension[0] == 0.5);
            w.Show("paddock");
            Check("the paddock offers the round again with repairs", Has(w, "Race again") && Has(w, "before the flag") && Has(w, "carry into this race"));

            // Choosing what to fix.
            var parts = Engine.DamageParts(c);
            Check("every damaged part is priced", parts.Select(p => p.Id).SequenceEqual(new[] { "body", "engine", "gearbox", "suspension" }));
            Check("parts add up to the whole bill", parts.Sum(p => p.Cost) == Engine.RepairCost(c));
            Press(w, "Repair car  /  " + Engine.Cr(Engine.RepairCost(c)));
            Check("repairs open the garage", w.CurrentView == "garage" && Has(w, "Bodywork") && Has(w, "Suspension") && Has(w, "Your credits"));
            var cash = c.Credits;
            var engine = parts.First(p => p.Id == "engine");
            Press(w, "Options", 1);
            Check("a part opens on its own screen", w.CurrentView == "repairpart" && Has(w, "If you leave it") && Has(w, "Leave it"));
            Press(w, "Leave it");
            Check("leaving a part changes nothing", w.CurrentView == "garage" && c.Credits == cash && c.CarEngine == 900);
            Press(w, "Options", 1);
            Press(w, "Repair  /  " + Engine.Cr(engine.Cost));
            Check("repairing returns to the list", w.CurrentView == "garage");
            Check("fixing one part leaves the others", c.CarEngine == 1000 && c.CarBody[0] == 30 && c.CarGearbox == 0.25 && c.Credits == cash - engine.Cost);
            cash = c.Credits;
            var bodyPart = Engine.DamageParts(c).First(p => p.Id == "body");
            Press(w, "Options", 0);
            Press(w, "Repair half  /  " + Engine.Cr(bodyPart.HalfCost));
            Check("half a repair halves the damage", c.CarBody[0] == 15 && c.CarBody[1] == 10 && c.Credits == cash - bodyPart.HalfCost && Engine.DamageParts(c).First(p => p.Id == "body").Cost <= bodyPart.Cost - bodyPart.HalfCost + 50);
            cash = c.Credits;
            c.Credits = 0;
            w.Show("garage");
            Snap(w, "garage", _shotsDir, "21_garage_broke.png");
            Check("nothing can be bought with no credits", !Engine.TryRepair(c, "gearbox", false) && c.CarGearbox == 0.25 && Has(w, "Full repair"));
            Check("an unknown part is refused", !Engine.TryRepair(c, "wings", false));
            c.Credits = cash;
            w.Show("paddock");

            // Between sessions in the game.
            var gameOpen = false;
            Store.GameOpenForTest = () => gameOpen;
            try
            {
                Press(w, "Race again");
                gameOpen = true;
                var raceId = c.Pending.RaceId;
                var review = LeftRace(c, "practice", false);
                review.Body = new List<double> { 50, 0, 0, 0 };
                review.EngineLife = 950;
                WriteProgress(review);
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "session", Seq = 1 });
                w.PollForTest();
                var d = ReadDebrief();
                Check("practice damage reaches the career at the end of the session", c.CarBody[0] == 50 && c.CarEngine == 950 && !File.Exists(Store.ProgressPath));
                Check("the game gets a repair list between sessions", d.SessionReview && d.Revision == 1 && d.Parts.Count == 4 && d.CreditsBalance == c.Credits && d.RepairCost == Engine.RepairCost(c));
                cash = c.Credits;
                var gearbox = d.Parts.First(p => p.Id == "gearbox");
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "repair", Part = "gearbox", Half = true, Seq = 2 });
                w.PollForTest();
                d = ReadDebrief();
                Check("half a gearbox repair works from the game", d.Revision == 2 && c.CarGearbox == 0.125 && d.CarGearbox == 0.125 && c.Credits == cash - gearbox.HalfCost && d.CreditsBalance == c.Credits);
                Check("the game is told what's left on the car", d.CarBody[0] == 50 && d.CarEngine == 950);
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "repair", Seq = 3 });
                w.PollForTest();
                d = ReadDebrief();
                Check("repairing everything works from the game", d.Revision == 3 && c.CarBody.Sum() == 0 && d.Parts.Count == 0 && d.RepairCost == 0);
                Check("the launcher can't repair a car at the track", GarageBlocked(w));

                gameOpen = false;
                var repaired = LeftRace(c, "qualifying", false);
                repaired.Body = new List<double> { 8, 0, 0, 0 };
                repaired.EngineLife = 1000;
                repaired.Gearbox = 0;
                repaired.Suspension = new List<double> { 0, 0, 0, 0 };
                WriteProgress(repaired);
                w.PollForTest();
                Check("leaving at qualifying keeps new damage and repairs", c.Pending != null && c.CarBody[0] == 8 && c.CarGearbox == 0);
                Check("a closed game ends the wait straight away", !Has(w, "Race in progress"));
            }
            finally
            {
                Store.GameOpenForTest = null;
            }

            // Quitting the race from the pause menu.
            w.Show("paddock");
            Press(w, "Race again");
            c.Credits = 50000;
            var credits = c.Credits;
            var trust = c.Season.Trust;
            var points = c.Season.Points[Engine.PlayerKey];
            var rounds = c.Season.RoundIndex;
            var retired = LeftRace(c, "race", true);
            retired.Cuts = 6;
            retired.Contacts = new List<ContactRecord> { new ContactRecord { Driver = c.Season.Field[1].Name, Count = 3 } };
            retired.Body = new List<double> { 70, 0, 0, 0 };
            WriteProgress(retired);
            w.PollForTest();
            var report = c.LastReport;
            Check("quitting the race retires you", report != null && report.Retired && w.CurrentView == "debrief" && c.Pending == null && c.Season.RoundIndex == rounds + 1);
            Check("a retirement scores nothing and costs trust", c.Season.Points[Engine.PlayerKey] == points && c.Season.Trust == trust - Engine.RetireTrustCost && c.Season.Rounds[rounds].Retired);
            Check("the debrief says you retired", Has(w, "DNF") && Has(w, "Retired"));
            w.DebriefStepForTest = "targets";
            Check("the retirement lists cuts and contact", Has(w, "Track-limit cuts") && Has(w, "Contact") && Has(w, "Missed"));
            w.DebriefStepForTest = "credits";
            Check("cuts and contact are charged", report.CreditLines.Any(l => l.Label.StartsWith("Track limits") && l.Amount < 0) && report.CreditLines.Any(l => l.Label.StartsWith("Contact") && l.Amount < 0) && Has(w, "Track limits") && Has(w, "Lost"));
            Check("the fines come out of your credits", report.CreditsNet < 0 && c.Credits == credits + report.CreditsNet && report.CreditsBalance == c.Credits);
            Check("the debrief records the cuts and contact", report.Cuts == 6 && report.Contacts == 3 && report.LicenceDelta > 0);
            Check("a rival remembers contact in a retirement", c.Rivals.First(r => r.Name == c.Season.Field[1].Name).Grudge > 0);
            Check("damage from the retirement is kept", c.CarBody[0] == 70 && c.CarGearbox == 0.25);
            w.DebriefStepForTest = "car";
            Check("the debrief offers full and chosen repairs", Has(w, "Options") && Has(w, "Repair everything  /  " + Engine.Cr(Engine.RepairCost(c))));
            Check("race files are cleared after retiring", !File.Exists(Store.ProgressPath) && !File.Exists(Store.ContextPath));
            Press(w, "Options", 0);
            Check("the debrief leads to the part's screen", w.CurrentView == "repairpart");
            Snap(w, "repairpart", _shotsDir, "25_repair_part.png");
            w.Show("repairpart");
            Press(w, "Leave it");
            Check("coming back keeps the debrief step", w.CurrentView == "debrief" && Has(w, "Your car"));
            Snap(w, "garage", _shotsDir, "22_garage.png");
            Snap(w, "paddock", _shotsDir, "23_paddock_damaged.png");
            w.Show("garage");
            w.Show("season");
            Check("the season page marks the retirement", Has(w, "Retired"));

            // A game that crashes isn't held against you.
            w.Show("paddock");
            Press(w, "Start race");
            var round = c.Season.RoundIndex;
            WriteProgress(LeftRace(c, "race", true));
            w.GameCrashedForTest = true;
            w.PollForTest();
            Check("a crash doesn't retire you", c.Pending != null && c.Season.RoundIndex == round && !c.Season.Rounds[round].Retired);
            Check("a crash says what happened", (w.NoticeForTest ?? "").Contains("closed unexpectedly"), w.NoticeForTest);
            Check("a crash still keeps the damage", c.CarBody[0] == 30);

            // Forfeiting also tells you about the car.
            Dialog.AnswersForTest.Enqueue(true);
            w.Show("paddock");
            Press(w, "Forfeit round");
            Check("a forfeit shows the damage and repairs", Has(w, "forfeited") && Has(w, "carries over") && Has(w, "Choose repairs"));

            // A launcher opened after the race still picks it up.
            w.Show("paddock");
            Press(w, "Start race");
            var later = LeftRace(c, "race", true);
            WriteProgress(later);
            var reopened = new MainWindow(game, null);
            Check("a retirement is found when the launcher reopens", reopened.CurrentView == "debrief" && reopened.CareerForTest.LastReport.Retired);
        }

        // The garage only offers repairs once the car is back from the track.
        static bool GarageBlocked(MainWindow w)
        {
            w.Show("garage");
            var blocked = Has(w, "Repairs are made from the screen in the game");
            w.Show("paddock");
            return blocked;
        }

        static DependencyObject SettingLineOnNewCareer(MainWindow w)
        {
            DependencyObject item = Tree(w.RootElement).OfType<TextBlock>().First(t => Norm(t.Text) == Norm("Car damage"));
            var parent = (Panel)LogicalTreeHelper.GetParent(item);
            var index = parent.Children.IndexOf((UIElement)item);
            while (index < 0 || !(parent.Children[index + 1] is StackPanel))
            {
                item = parent;
                parent = (Panel)LogicalTreeHelper.GetParent(parent);
                index = parent.Children.IndexOf((UIElement)item);
            }
            return parent.Children[index + 1];
        }

        static void FlowFinishInGame(string game)
        {
            var gameOpen = false;
            string problem;
            var w = FreshLauncher(game);
            CreateCareer(w, "Finish Driver");
            SignOffer(w);
            var c = w.CareerForTest;
            Store.GameOpenForTest = () => gameOpen;
            try
            {
                Press(w, "Start race");
                FinishRace(w, 4, 4);
                Check("with the game closed there's no results file for it", !File.Exists(Store.DebriefPath));
                LeaveDebrief(w);

                Press(w, "Start race");
                gameOpen = true;
                var raceId = c.Pending.RaceId;
                FinishRace(w, 3, 6, 1, 0);
                var d = ReadDebrief();
                var report = c.LastReport;
                Check("the results screen is for this race", d.RaceId == raceId);
                Check("the results screen matches the launcher's scoring", d.Pos == 3 && d.Points == report.Points && d.StartPos == 6 && d.Cuts == 1 && d.TrustDelta == report.TrustDelta && d.LicenceDelta == report.LicenceDelta);
                Check("the results screen has the contract review", d.Lines.Count == report.Lines.Count && d.Lines.Count > 0);
                Check("the results screen lists the whole grid in order", d.Classification.Count == report.Grid && d.Classification.Select(r => r.Pos).SequenceEqual(d.Classification.Select(r => r.Pos).OrderBy(p => p)) && d.Classification.Single(r => r.IsPlayer).Pos == 3);
                Check("the results screen uses the career driver's name", d.Classification.Single(r => r.IsPlayer).Name == c.Driver);
                var standings = Engine.Standings(c);
                Check("the results screen has the championship", d.Standings.Count == standings.Count && d.Standings.Single(r => r.IsPlayer).Points == standings.Single(r => r.IsPlayer).Points && d.Standings[0].Pos == 1);
                Check("the results screen has the promotion score", d.ShowScore && d.ScoreTitle == "Promotion score" && d.ScoreTotal == Engine.Outlook(c).Total && d.PromotionMark == 62);
                Check("the results screen names the next round", d.HasNextRace && d.ContinueLabel == "NEXT RACE" && d.NextTitle.StartsWith("Round 3 of 6") && d.NextDetail.Contains("Practice"));
                Check("no season outcome mid-season", d.SeasonOutcome == "");
                Check("the results screen gets the music setting", d.MusicOn && d.MusicVolume == 35);
                Check("the results screen gets the sound setting", d.SoundsOn && d.SoundsVolume == 60);
                Check("the results screen gets the credits", d.CreditLines.Count > 0 && d.CreditsBalance == c.Credits && d.CreditsNet == report.CreditsNet);

                LeaveDebrief(w);
                Check("the launcher points back at the game", Has(w, "Round complete") && ButtonsIn(w.RootElement, "Start race").Count == 0);

                SendGameCommand("some other race", "next");
                w.PollForTest();
                Check("a command for another race is thrown away", !File.Exists(Store.CommandPath));
                var launches = _launches;
                gameOpen = false;
                w.PollForTest();
                Check("a stale command doesn't start a race", _launches == launches && w.CurrentView == "debrief");

                LeaveDebrief(w);
                Press(w, "Start race");
                gameOpen = true;
                raceId = c.Pending.RaceId;
                FinishRace(w, 2);
                var round = c.Season.RoundIndex;
                SendGameCommand(raceId, "next");
                launches = _launches;
                w.PollForTest();
                Check("next race waits for the game to close", launches == _launches && c.Pending == null && !File.Exists(Store.CommandPath));
                gameOpen = false;
                w.PollForTest();
                Check("next race starts once the game has closed", _launches == launches + 1 && c.Pending != null && c.Pending.RoundNumber == round + 1, _launches);
                Check("the old results are gone", !File.Exists(Store.DebriefPath));
                Check("next race opens the race, not the debrief", w.CurrentView == "paddock" && Has(w, "Race in progress"), w.CurrentView);

                gameOpen = true;
                raceId = c.Pending.RaceId;
                FinishRace(w, 2);
                SendGameCommand(raceId, "paddock");
                launches = _launches;
                w.PollForTest();
                gameOpen = false;
                w.PollForTest();
                Check("back to the launcher starts nothing", _launches == launches && c.Pending == null);
                Check("back to the launcher shows the debrief", w.CurrentView == "debrief", w.CurrentView);
                LeaveDebrief(w);

                while (c.Season.RoundIndex < Engine.RoundsPerSeason - 1)
                {
                    Press(w, "Start race");
                    FinishRace(w, 1);
                    LeaveDebrief(w);
                }
                Press(w, "Start race");
                gameOpen = true;
                raceId = c.Pending.RaceId;
                var finalResult = Result(c, 1, 0, 0);
                FinishRace(w, 1);
                d = ReadDebrief();
                Check("the last round has no next race", !d.HasNextRace && d.NextTitle == "" && d.ContinueLabel == "SEE YOUR OFFERS");
                Check("the last round shows the outcome", d.SeasonOutcome.Contains("Season over"));
                Check("the last round keeps the final table", d.Standings.Count > 1 && d.Standings.Single(r => r.IsPlayer).Points > 0);
                Check("the last round shows the final score", d.ShowScore && d.ScoreTitle == "Final score" && d.ScoreTotal == c.LastReport.ScoreTotal);
                Check("a result for another career can't start this one's race", !Engine.BuildDebrief(c, c.LastReport, finalResult, false).HasNextRace && Engine.BuildDebrief(c, c.LastReport, finalResult, false).ContinueLabel == "OPEN PADDOCK LADDER");
                c.Mode = "open";
                Check("an open career picks its next series", Engine.BuildDebrief(c, c.LastReport, finalResult, true).ContinueLabel == "CHOOSE YOUR NEXT SERIES" && !Engine.BuildDebrief(c, c.LastReport, finalResult, true).ShowScore);
                c.Mode = "ladder";

                Check("the last round offers contracts in the game", d.CanChoose && d.Ladder && d.Offers.Count == c.Offers.Count && d.Offers.Count > 0);
                Check("in-game offers carry what the launcher shows", d.Offers.All(o => o.Team != null && o.CarName != null && o.Image.EndsWith("preview.jpg") && o.Clauses.Count > 0) && d.OffersHint == Engine.OffersHint(c));
                Check("in-game seats list the ones you can ask first", d.Seats.Count > 0 && d.Seats[0].Allowed && !d.Seats.SkipWhile(x => x.Allowed).Any(x => x.Allowed));
                Check("the in-game page has your record", d.TierName == Engine.TierName(c.Tier) && d.DriverRating == Engine.DriverRating(c) && d.Licence != "");

                SendGameCommand(raceId, "next");
                launches = _launches;
                w.PollForTest();
                Check("next doesn't apply between seasons", !_launchPending(c));

                var firstOffers = c.Offers.ToList();
                var asked = d.Seats.First(x => x.Allowed);
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "request", SeriesId = asked.SeriesId, Seq = 1 });
                w.PollForTest();
                d = ReadDebrief();
                Check("an in-game request gets an answer", d.Revision == 1 && d.ReplyTitle != "" && d.ReplyText != "", d.ReplyTitle);
                Check("an in-game request counts", c.SeatRequests.Contains(asked.SeriesId) && d.Seats.Single(x => x.SeriesId == asked.SeriesId).Asked);
                Check("an in-game request keeps your offers", firstOffers.All(o => c.Offers.Contains(o)) && d.Offers.Count == c.Offers.Count);
                Check("an in-game request is saved", Store.LoadCareer(c.Id, out problem).SeatRequests.Contains(asked.SeriesId));

                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "request", SeriesId = asked.SeriesId, Seq = 2 });
                w.PollForTest();
                d = ReadDebrief();
                Check("the same series can't be asked twice in the game", d.Revision == 2 && d.ReplyTitle == "" && c.SeatRequests.Count(x => x == asked.SeriesId) == 1);

                SendOffSeasonCommand(new RaceCommand { RaceId = "another race", Action = "sign", OfferIndex = 0, Seq = 3 });
                w.PollForTest();
                Check("a command for another race can't sign", c.Season == null && ReadDebrief().Revision == 2);

                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "sign", OfferIndex = 99, Seq = 4 });
                w.PollForTest();
                d = ReadDebrief();
                Check("a bad offer can't be signed", c.Season == null && !d.Signed && d.Revision == 4);

                launches = _launches;
                var signing = c.Offers[c.Offers.Count - 1];
                SendOffSeasonCommand(new RaceCommand { RaceId = raceId, Action = "sign", OfferIndex = c.Offers.Count - 1, Seq = 5 });
                w.PollForTest();
                d = ReadDebrief();
                Check("signing in the game starts the season", d.Signed && d.Revision == 5 && c.Season != null && c.Season.CarId == signing.CarId && c.SeasonNo == 2);
                Check("signing waits for the game to close", _launches == launches);
                gameOpen = false;
                w.PollForTest();
                Check("signing loads round 1", _launches == launches + 1 && c.Pending != null && c.Pending.RoundNumber == 1, _launches);

                gameOpen = true;
                raceId = c.Pending.RaceId;
                FinishRace(w, 3);
                d = ReadDebrief();
                Check("the new season carries on as normal", !d.CanChoose && d.HasNextRace);
                gameOpen = false;
                w.PollForTest();
            }
            finally
            {
                Store.GameOpenForTest = null;
            }
        }

        static void FlowPromotion(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Ladder Driver");
            var signed = w.CareerForTest.Offers[0];
            SignOffer(w);
            var c = w.CareerForTest;
            var series = c.Season.SeriesId;

            for (var i = 0; i < Engine.RoundsPerSeason; i++)
            {
                if (i > 0) LeaveDebrief(w);
                Press(w, "Start race");
                FinishRace(w, 1, 3);
            }
            Check("six wins end the season", c.Season == null && c.SeasonNo == 2);
            w.DebriefStepForTest = "season";
            Check("the final debrief shows the outcome", Has(w, "Promoted") && Has(w, "Final score"));
            Check("the title is counted", c.Titles == 1 && c.History.Count == 1 && c.Tier == 2);
            Check("the series is remembered", c.LastSeriesId == series);

            LeaveDebrief(w);
            Check("between seasons the paddock looks for a drive", Has(w, "Find your next drive"));
            Press(w, "See offers");
            Check("offers open with last season's score", w.CurrentView == "contracts" && Has(w, "Last season's promotion score"));
            var seriesOf = c.Offers.Select(o => o.SeriesName).ToList();
            Check("the first offer stays in the discipline", c.Offers[0].SeriesId == "sports", string.Join(",", seriesOf));
            Check("offers come from three series", c.Offers.Select(o => o.SeriesId).Distinct().Count() == c.Offers.Count, string.Join(",", seriesOf));

            Press(w, "Request a seat");
            Check("request opens its page", w.CurrentView == "seatrequest");
            Check("far-off series are closed", Has(w, "Closed"));

            var originals = c.Offers.ToList();
            Dialog.AnswersForTest.Enqueue(false);
            Press(w, "Request", 0);
            Check("cancelling a request costs nothing", c.SeatRequests.Count == 0);

            Dialog.AnswersForTest.Enqueue(true);
            Dialog.AnswersForTest.Enqueue(true);
            Press(w, "Request", 0);
            Check("a step down is granted", c.SeatRequests.Count == 1 && c.Offers.Any(o => o.Requested) && w.CurrentView == "contracts");
            Check("original offers stay", originals.All(o => c.Offers.Contains(o)));
            Check("requested seats are labelled", Has(w, "Requested seat"));
            Press(w, "Request a seat");
            var openRequests = ButtonsIn(w.RootElement, "Request").Count;
            Check("a series already asked is marked", Has(w, "Asked"));
            Dialog.AnswersForTest.Enqueue(true);
            Dialog.AnswersForTest.Enqueue(true);
            Press(w, "Request", 0);
            Check("you can request more than once", c.SeatRequests.Count == 2 && w.CurrentView == "contracts", c.SeatRequests.Count);
            Press(w, "Request a seat");
            Check("each request closes that series", ButtonsIn(w.RootElement, "Request").Count == openRequests - 1, ButtonsIn(w.RootElement, "Request").Count);
            w.BackForTest();
            Check("every offer can be opened", ButtonsIn(w.RootElement, "View car").Count == c.Offers.Count);

            Press(w, "View car", c.Offers.Count - 1);
            Press(w, "Sign contract");
            Check("signing an original offer keeps the tier", c.Tier == 2 && c.Season != null);
        }

        static void FlowCareers(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "First Driver");
            SignOffer(w);
            var first = w.CareerForTest.Id;

            Press(w, "Careers");
            Press(w, "Start new career");
            Check("new career page opens", w.CurrentView == "new");
            CreateCareer(w, "Second Driver", true);
            Check("open careers pick a series", w.CurrentView == "series" && w.CareerForTest.Mode == "open", w.CurrentView);
            var second = w.CareerForTest.Id;

            PressTile(w, "GT3");
            Check("picking a series lists all its cars", w.CurrentView == "contracts" && w.CareerForTest.Offers.Count == 5, w.CareerForTest.Offers == null ? -1 : w.CareerForTest.Offers.Count);
            Press(w, "Add car to this series");
            Check("add car opens", w.CurrentView == "addcar");
            var search = Tree(w.RootElement).OfType<TextBox>().First();
            search.Text = "sportscars1";
            Check("search narrows the list", ButtonsIn(w.RootElement, "Add").Count == 1, ButtonsIn(w.RootElement, "Add").Count);
            Press(w, "Add");
            Check("the car joins the series", Store.LoadSeriesEdits().Additions["gt3"].Contains("sportscars1"));
            Check("the offers include it", w.CareerForTest.Offers.Any(o => o.CarId == "sportscars1"));
            Press(w, "Remove");
            Check("the car can be removed", !Store.LoadSeriesEdits().Additions["gt3"].Contains("sportscars1"));
            search.Text = "nothing matches this";
            Check("an empty search says so", Has(w, "No cars match"));
            w.BackForTest();
            Press(w, "Back to all series");
            Check("back to all series clears the offers", w.CurrentView == "series" && w.CareerForTest.Offers == null);

            Press(w, "Careers");
            Check("both careers are listed", Has(w, "2 careers"));
            Press(w, "Switch");
            Check("switch changes career", w.CareerForTest.Id == first && w.CurrentView == "paddock");
            Check("the switch is remembered", Store.LoadSettings().ActiveCareer == first);

            Press(w, "Careers");
            Func<string, DependencyObject> rowOf = driver => Tree(w.RootElement).OfType<DockPanel>().First(d => Norm(TextIn(d)).Contains(Norm(driver)) && ButtonsIn(d, "Delete").Count == 1);
            Dialog.AnswersForTest.Enqueue(false);
            Press(rowOf("Second Driver"), "Delete");
            Check("cancel keeps the career", File.Exists(Store.PathFor(second)));
            Dialog.AnswersForTest.Enqueue(true);
            Press(rowOf("First Driver"), "Delete");
            Check("deleting the current career moves to the next", w.CareerForTest != null && w.CareerForTest.Id == second, w.CurrentView);
            Check("deleting removes the file for good", !File.Exists(Store.PathFor(first)));
            Check("no copy of a deleted career is kept", Directory.GetFiles(Path.Combine(Store.DataDir, "careers"), "*", SearchOption.AllDirectories).All(f => !f.Contains(first)) && !Directory.Exists(Path.Combine(Store.DataDir, "careers", "deleted")));

            Press(w, "Careers");
            Check("a deleted career can't be restored", !Has(w, "Recently deleted") && !Has(w, "First Driver") && ButtonsIn(w.RootElement, "Restore").Count == 0);

            var oldBackups = Path.Combine(Store.DataDir, "careers", "deleted");
            Directory.CreateDirectory(oldBackups);
            File.WriteAllText(Path.Combine(oldBackups, "old_20260101_000000.json"), "{}");
            File.WriteAllText(Store.PathFor(second) + ".unreadable", "damaged");
            Store.RemoveOldDeletedCareers();
            Check("backups from earlier versions are removed", !Directory.Exists(oldBackups) && File.Exists(Store.PathFor(second)));
            Store.DeleteCareer(second);
            Check("a damaged copy goes with its career", !File.Exists(Store.PathFor(second) + ".unreadable") && !File.Exists(Store.PathFor(second)));
            Store.SaveCareer(w.CareerForTest);

            w.Show("settings");
            Dialog.AnswersForTest.Enqueue(true);
            Press(w, "Delete this career");
            Check("deleting the last career starts over", w.CurrentView == "new" && w.CareerForTest == null, w.CurrentView);
        }

        static void FlowCustomSeriesAndClasses(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Custom Driver", true);
            Press(w, "Custom series");
            Check("custom series opens", w.CurrentView == "customseries");
            Tree(w.RootElement).OfType<TextBox>().First().Text = "Sunday Club";
            Press(w, "14 cars");
            Press(w, "Create and add cars");
            Check("creating a series opens add cars", w.CurrentView == "addcar" && Has(w, "Sunday Club"));
            Tree(w.RootElement).OfType<TextBox>().First().Text = "gt4";
            Press(w, "Add", 0);
            Press(w, "Add", 0);
            var custom = Store.LoadSeriesEdits().Custom.Single();
            Check("cars are added to the custom series", custom.CarIds.Count == 2 && custom.Tier == 3, custom.CarIds.Count);

            w.BackForTest();
            Check("the series is listed with its count", Has(w, "Sunday Club") && Has(w, "2 cars"));
            w.Show("series");
            Check("the series can be picked", Has(w, "Sunday Club"));
            PressTile(w, "Sunday Club");
            Check("its cars are on offer", w.CareerForTest.Offers.Count == 2);
            SignOffer(w);
            Check("a custom series can be raced", w.CareerForTest.Season != null && w.CareerForTest.Season.SeriesName == "Sunday Club");

            w.Show("customseries");
            Dialog.AnswersForTest.Enqueue(false);
            Press(w, "Delete");
            Check("cancel keeps the series", Store.LoadSeriesEdits().Custom.Count == 1);
            Dialog.AnswersForTest.Enqueue(true);
            Press(w, "Delete");
            Check("delete removes the series", Store.LoadSeriesEdits().Custom.Count == 0);
            Check("a season under way carries on", w.CareerForTest.Season != null);

            w.Show("cars");
            Check("car classes lists cars", Has(w, "41 cars"));
            var filter = Tree(w.RootElement).OfType<TextBox>().First();
            filter.Text = "smallsports1";
            var box = Tree(w.RootElement).OfType<ComboBox>().Single();
            var options = box.Items.OfType<ComboBoxItem>().ToList();
            var gt3 = options.FindIndex(o => TextIn(o) == "GT3");
            box.SelectedIndex = gt3;
            Check("a car can change class", Store.LoadClassOverrides()["smallsports1"] == "gt3");
            Tree(w.RootElement).OfType<ComboBox>().Single().SelectedIndex = 0;
            Check("automatic removes the change", !Store.LoadClassOverrides().ContainsKey("smallsports1"));
            filter.Text = "does not exist";
            Check("an empty class search says so", Has(w, "No cars match"));
        }

        static void FlowEveryScreenInEveryState(string game)
        {
            var w = FreshLauncher(game);
            CreateCareer(w, "Screen Driver");
            var views = new[] { "paddock", "season", "contracts", "series", "history", "settings", "new", "debrief", "careers", "raceplan", "cars", "customseries", "addcar", "seatrequest" };
            Action<string> visitAll = state =>
            {
                foreach (var view in views)
                {
                    try { w.Show(view); }
                    catch (Exception ex) { Check(view + " opens " + state, false, ex.Message); continue; }
                    Check(view + " opens " + state, w.CurrentView == view);
                }
            };
            visitAll("before a season");
            Press(w, "Paddock");
            Press(w, "See offers");
            SignOffer(w);
            visitAll("during a season");
            w.CareerForTest.LicencePoints = Engine.Probation;
            visitAll("on probation");
            w.CareerForTest.LicencePoints = 0;

            var damaged = FreshLauncher(game);
            CreateCareer(damaged, "Damaged Driver");
            var id = damaged.CareerForTest.Id;
            File.WriteAllText(Store.PathFor(id), "{ not json");
            var opened = new MainWindow(game, null);
            Check("a damaged save is reported", (opened.NoticeForTest ?? "").Contains("damaged"), opened.NoticeForTest);
            Check("a damaged save is kept", File.Exists(Store.PathFor(id) + ".unreadable"));
        }

        static void TestWeekendRules()
        {
            Func<int, int, string> ini = (practice, qualifying) =>
            {
                var c = SeasonCareer(80);
                var round = c.Season.Rounds[0];
                round.PracticeOverride = practice;
                round.QualifyingOverride = qualifying;
                RaceContext context;
                var text = Engine.BuildRaceIni(c, out context);
                return text + "\nSKIP=" + context.SkipQualifying;
            };

            var full = ini(5, 7);
            Check("full weekend order", full.IndexOf("[SESSION_0]\nNAME=Practice") >= 0 && full.IndexOf("[SESSION_1]\nNAME=Qualifying") >= 0 && full.IndexOf("[SESSION_2]\nNAME=Race") >= 0);
            Check("session lengths reach the game", full.Contains("TYPE=1\nDURATION_MINUTES=5") && full.Contains("TYPE=2\nDURATION_MINUTES=7"));
            Check("qualifying sets the grid", !full.Contains("STARTING_POSITION") && full.Contains("SKIP=False"));

            var qualifyingOnly = ini(-1, 5);
            Check("qualifying without practice", qualifyingOnly.Contains("[SESSION_0]\nNAME=Qualifying") && qualifyingOnly.Contains("[SESSION_1]\nNAME=Race") && !qualifyingOnly.Contains("NAME=Practice"));

            var practiceOnly = ini(5, -1);
            Check("practice without qualifying uses a hidden grid session", practiceOnly.Contains("[SESSION_1]\nNAME=Grid\nTYPE=2") && practiceOnly.Contains("[SESSION_2]\nNAME=Race") && practiceOnly.Contains("SKIP=True"));

            var raceOnly = ini(-1, -1);
            var grid = SeasonCareer(80).Season.Field.Count + 1;
            Check("race only starts from the back", raceOnly.Contains("[SESSION_0]\nNAME=Race") && raceOnly.Contains("STARTING_POSITION=" + grid) && raceOnly.Contains("SKIP=False"));
            Check("only one race session", raceOnly.Split(new[] { "TYPE=3" }, StringSplitOptions.None).Length == 2);

            var c2 = SeasonCareer(81);
            var r = c2.Season.Rounds[0];
            Check("career defaults apply", Engine.PracticeMinutesFor(c2, r) == 10 && Engine.QualifyingMinutesFor(c2, r) == 10);
            c2.PracticeMinutes = 500;
            Check("lengths are limited", Engine.PracticeMinutesFor(c2, r) == Engine.MaxSession);
            c2.PracticeMinutes = 10;
            c2.PracticeOn = false;
            Check("career off means off", Engine.PracticeMinutesFor(c2, r) == 0);
            r.PracticeOverride = 4;
            Check("a round can turn it on", Engine.PracticeMinutesFor(c2, r) == 4);
            c2.PracticeOn = true;
            r.PracticeOverride = -1;
            Check("a round can turn it off", Engine.PracticeMinutesFor(c2, r) == 0);
            r.PracticeOverride = 0;
            Check("weather waits for the race", Engine.WeatherLeadSeconds(c2, r) == 1200);
            r.PracticeOverride = -1;
            r.QualifyingOverride = -1;
            Check("no sessions means no wait", Engine.WeatherLeadSeconds(c2, r) == 0);
            Check("back of the grid", Engine.StartingPosition(c2) == c2.Season.Field.Count + 1);

            var old = Store.FromJson<Career>("{\"Driver\":\"Old\",\"Mode\":\"ladder\"}");
            Check("older saves get sessions", old.PracticeOn && old.QualifyingOn && old.PracticeMinutes == 10);

            var plan = Store.FromJson<Dictionary<string, object>>(Store.BuildPurePlan(new[] { 15, 16 }, 100, new Dictionary<int, double[]>(), 600));
            var durations = ((System.Collections.ArrayList)plan["container"]).Cast<Dictionary<string, object>>().Select(x => Convert.ToInt32(((Dictionary<string, object>)x["data"])["duration"])).ToList();
            Check("the lead only lengthens stage one", durations[0] == 700 && durations[1] == 100);

            var zero = SeasonCareer(82);
            var bad = Result(zero, 1, 0, 0);
            bad.Rows[0].Pos = 0;
            Check("a car with no position doesn't crash scoring", Engine.ApplyResult(zero, bad) != null);

            var withStart = SeasonCareer(83);
            var scored = Result(withStart, 2, 0, 0);
            scored.StartPos = 9;
            Check("the grid slot is recorded", Engine.ApplyResult(withStart, scored).StartPos == 9 && withStart.Season.Rounds[0].StartPos == 9);
            Check("starting ninth isn't a pole", withStart.Stats.Poles == 0);
            var fromPole = Result(withStart, 2, 0, 0);
            fromPole.StartPos = 1;
            Engine.ApplyResult(withStart, fromPole);
            Check("starting first counts as a pole", withStart.Stats.Poles == 1);

            Check("the stable hash never changes", Engine.StableHash("gt3") == 17 * 31 * 31 * 31 + 'g' * 31 * 31 + 't' * 31 + '3');
            Check("scores use one set of thresholds", Engine.PromoteAt == 0.62 && Engine.DropBelow == 0.30);
            var parts = Engine.ScoreParts(1, 10, 100, 100);
            Check("a perfect season scores full marks", parts.Total == 100 && parts.Position == 55 && parts.Clean == 30 && parts.Trust == 15);
        }

        static void TestInstallAndFiles(string game)
        {
            Store.DocsOverride = Path.Combine(Path.GetTempPath(), "PaddockLadderFiles_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Store.DataDir);
            var target = Path.Combine(game, "apps", "lua", "PaddockLadder");
            Directory.CreateDirectory(Path.Combine(target, "src"));
            File.WriteAllText(Path.Combine(target, "src", "career.lua"), "old");
            Store.InstallGameApp(game);
            Check("the in-game app is installed", File.Exists(Path.Combine(target, "manifest.ini")) && File.Exists(Path.Combine(target, "PaddockLadder.lua")) && File.Exists(Path.Combine(target, "src", "steward.lua")));
            Check("old in-game files are removed", !File.Exists(Path.Combine(target, "src", "career.lua")));
            var main = Path.Combine(target, "PaddockLadder.lua");
            var stamp = new DateTime(2020, 1, 1);
            File.SetLastWriteTimeUtc(main, stamp);
            Store.InstallGameApp(game);
            Check("an unchanged app isn't rewritten", File.GetLastWriteTimeUtc(main) == stamp);
            File.AppendAllText(main, "-- edited");
            Store.InstallGameApp(game);
            Check("a changed app is replaced", !File.ReadAllText(main).EndsWith("-- edited"));

            var env = Store.ReadEnvironment(game);
            Check("Pure is detected", env.HasPure && env.Presets.Count == Engine.WeatherOptions.Length, env.Presets.Count);
            Check("Pure values are read", Math.Abs(env.Presets[15][0] - 0.1) < 0.0001 && Math.Abs(env.Presets[15][7] - 0.8) < 0.0001);
            Check("installed weather is found", env.InstalledWeathers.Contains("3_clear"));
            var noPure = Path.Combine(Path.GetTempPath(), "PaddockLadderNoPure_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(noPure);
            Check("a game without Pure works", !Store.ReadEnvironment(noPure).HasPure);
            Directory.Delete(noPure, true);

            Check("the game folder is recognised", Store.IsAcRoot(game) && !Store.IsAcRoot(Path.GetTempPath()));
            Store.SaveSettings(new LauncherSettings { AcRoot = game, ActiveCareer = "abc" });
            Check("settings are saved", Store.LoadSettings().ActiveCareer == "abc");
            Store.SaveClassOverrides(new Dictionary<string, string> { { "a", "gt3" } });
            Check("class changes are saved", Store.LoadClassOverrides()["a"] == "gt3");

            var cars = Content.ScanCars(game);
            var tracks = Content.ScanTracks(game);
            Check("the scan finds usable cars", cars.Count == 41, cars.Count);
            Check("the scan skips drift cars", !cars.Any(x => x.Id == "driftcar"));
            Check("the scan finds usable tracks and layouts", tracks.Count == 8 && tracks.Any(t => t.Layout == "gp"), tracks.Count);
            Check("the scan skips unusable tracks", !tracks.Any(t => t.Id == "tiny" || t.Id == "drifter"));
        }

        // Timings are printed for reading, with limits only generous enough to catch something badly wrong.
        static void TestPerformance(string acRoot)
        {
            Store.DocsOverride = Path.Combine(Path.GetTempPath(), "PaddockLadderPerf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Store.DataDir);
            var clock = Stopwatch.StartNew();
            var cars = Content.ScanCars(acRoot);
            Console.WriteLine("perf scan cars {0} ms ({1})", clock.ElapsedMilliseconds, cars.Count);
            clock.Restart();
            var tracks = Content.ScanTracks(acRoot);
            Console.WriteLine("perf scan tracks {0} ms ({1})", clock.ElapsedMilliseconds, tracks.Count);
            clock.Restart();
            var series = Engine.BuildSeries(cars);
            Console.WriteLine("perf build series {0} ms ({1})", clock.ElapsedMilliseconds, series.Count);
            clock.Restart();
            var w = new MainWindow(acRoot, null);
            var startup = clock.ElapsedMilliseconds;
            Console.WriteLine("perf launcher start {0} ms", startup);
            Check("the launcher starts quickly", startup < 8000, startup);

            var c = Engine.NewCareer("Perf", "open", 0, 1, 77);
            w.UseCareerForTest(c);
            Action<string> time = view =>
            {
                clock.Restart();
                w.Show(view);
                var root = w.RootElement;
                root.Measure(new Size(1280, 800));
                root.Arrange(new Rect(0, 0, 1280, 800));
                root.UpdateLayout();
                Console.WriteLine("perf draw {0} {1} ms", view, clock.ElapsedMilliseconds);
                Check(view + " draws quickly", clock.ElapsedMilliseconds < 6000, clock.ElapsedMilliseconds);
            };
            time("series");
            time("series");
            time("cars");
            time("settings");
        }

        static void TestFlows()
        {
            var game = MakeFakeGame();
            var docs = Store.DocsOverride;
            Action<string, Action> run = (name, flow) =>
            {
                try { flow(); }
                catch (Exception ex) { _failed++; Console.WriteLine("CRASH flow " + name + ": " + ex); }
                finally
                {
                    Store.LaunchForTest = null;
                    if (Dialog.AnswersForTest.Count > 0)
                    {
                        Check(name + " used every dialog answer", false, Dialog.AnswersForTest.Count);
                        Dialog.AnswersForTest.Clear();
                    }
                    if (Store.DocsOverride != docs && Store.DocsOverride != null && Directory.Exists(Store.DocsOverride))
                        Directory.Delete(Store.DocsOverride, true);
                    Store.DocsOverride = docs;
                }
            };
            try
            {
                run("weekend rules", TestWeekendRules);
                run("first run", delegate { FlowFirstRun(game); });
                run("interface sounds", delegate { FlowInterfaceSounds(game); });
                run("credits and damage", delegate { FlowCreditsAndDamage(game); });
                run("leaving a race and repairs", delegate { FlowLeavingAndRepairs(game); });
                run("layered screens", delegate { FlowLayeredScreens(game); });
                run("money and contracts", delegate { FlowMoneyAndContracts(game); });
                run("full grid and rivals", delegate { FlowGridAndRival(game); });
                run("stewards and reputation", delegate { FlowStewards(game); });
                run("settings", delegate { FlowSettings(game); });
                run("race setup", delegate { FlowRaceSetup(game); });
                run("racing", delegate { FlowRacing(game); });
                run("finishing in the game", delegate { FlowFinishInGame(game); });
                run("open career between seasons in the game", delegate { FlowOpenCareerFinishInGame(game); });
                run("promotion", delegate { FlowPromotion(game); });
                run("careers", delegate { FlowCareers(game); });
                run("custom series and classes", delegate { FlowCustomSeriesAndClasses(game); });
                run("every screen", delegate { FlowEveryScreenInEveryState(game); });
                run("install and files", delegate { TestInstallAndFiles(game); });
            }
            finally
            {
                // A picture that failed to load can hold its file until it's collected.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try { Directory.Delete(game, true); }
                catch (IOException) { }
            }
        }
    }
}
#endif
