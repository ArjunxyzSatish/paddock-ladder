using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Installed = PaddockLadder.Content;

namespace PaddockLadder
{
    public class MainWindow : Window
    {
        const double PagePad = 48;

        readonly string _acRoot;
        readonly List<TrackInfo> _tracks;
        List<SeriesDef> _series;
        readonly List<CarInfo> _cars;
        Dictionary<string, string> _classOverrides;
        SeriesEdits _edits;
        string _carFilter = "";
        string _addTarget;
        string _addFilter = "";
        string _newSeriesName = "";
        int _newSeriesTier = 4;
        readonly List<string> _history = new List<string>();
        bool _hasShown;
        Button _back;
        readonly StackPanel _nav = new StackPanel { Orientation = Orientation.Horizontal };
        readonly ScrollViewer _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly Grid _root = new Grid { Background = U.Ink };
        readonly Border _currentDriver = new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 22, 0) };
        Button _maximize;

        Career _c;
        string _view = "paddock";
        string _notice;
        DateTime _focusUntil = DateTime.MinValue;
        DateTime _startingUntil = DateTime.MinValue;
        bool _wasRacing;

        // Set when a race is scored with the game still showing its results screen.
        bool _finishedInGame, _gameWasOpen;
        DateTime _comeBackAt = DateTime.MaxValue;
        Process _gameProcess;
        DateTime _nextRaceAt = DateTime.MaxValue, _cleanUpUntil = DateTime.MinValue;
        bool _freshStep;
        int _debriefStep;
        bool _debriefHadDamage;
        Offer _detailOffer;
        string _repairPart;
        bool _gameCrashed;
        bool _nextRaceWanted;
        string _debriefRaceId;
        GameDebrief _gameDebrief;
        MenuMusic _music;
        bool _musicOn;
        int _musicVolume;
        bool _soundsOn;
        int _soundsVolume;

        static readonly TimeSpan NormalPoll = TimeSpan.FromSeconds(2);
        static readonly TimeSpan ResultsScreenPoll = TimeSpan.FromMilliseconds(300);
#if SELFTEST
        public bool? ForegroundForTest;
#else
        static readonly bool? ForegroundForTest = null;
#endif
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = NormalPoll };

        string _newName = "";
        string _newMode = "ladder";
        bool _newDamage = true;
        int _newDifficulty;
        string _newFormat = "laps";
        int _newLaps = 5, _newMinutes = 60;
        readonly RaceEnv _env;

        public UIElement RootElement { get { return _root; } }

        public MainWindow(string acRoot, string startupNotice)
        {
            _acRoot = acRoot;
            _notice = startupNotice;
            _cars = Installed.ScanCars(acRoot);
            _tracks = Installed.ScanTracks(acRoot);
            _classOverrides = Store.LoadClassOverrides();
            _edits = Store.LoadSeriesEdits();
            RebuildSeries();
            _env = Store.ReadEnvironment(acRoot);

            Title = "Paddock Ladder";
            Width = 1280;
            Height = 800;
            MinWidth = 1100;
            MinHeight = 700;
            Background = U.Ink;
            FontFamily = U.BodyFont;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 60,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });

            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            _root.RowDefinitions.Add(new RowDefinition());
            var bar = BuildTopBar();
            Grid.SetRow(bar, 0);
            Grid.SetRow(_scroll, 1);
            _root.Children.Add(bar);
            _root.Children.Add(_scroll);
            Content = _root;

            var settings = Store.LoadSettings();
            _musicOn = settings.MusicOn;
            _musicVolume = settings.MusicVolume;
            _soundsOn = settings.SoundsOn;
            _soundsVolume = settings.SoundsVolume;
            Sfx.Configure(_soundsOn, _soundsVolume);
            Sfx.Attach();
            _music = new MenuMusic(System.IO.Path.Combine(_acRoot, "apps", "lua", "PaddockLadder", "music", "menu.mp3"));
            Activated += delegate { UpdateMusic(); };
            Deactivated += delegate { UpdateMusic(); };

            StateChanged += delegate
            {
                UpdateMusic();
                _root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                ((TextBlock)_maximize.Content).Text = WindowState == WindowState.Maximized ? U.IconRestore : U.IconMax;
            };

            string problem;
            _c = Store.LoadActiveCareer(out problem);
            if (problem != null) _notice = problem;
            DropStaleOffers();
            var hadResult = CheckForResult() || CheckForLeftRace();
            if (!Store.IsGameOpen()) _cleanUpUntil = DateTime.UtcNow.AddSeconds(3);
            Show(_c != null ? (hadResult ? "debrief" : "paddock") : Store.ListCareers().Count > 0 ? "careers" : "new");

            try { Store.RestorePureSettings(); }
            catch (Exception) { }
            _timer.Tick += delegate { Poll(); };
            _timer.Start();
        }

        // Only while you're actually looking at the menus, and never over the game or another program.
        bool MusicWanted()
        {
            var foreground = ForegroundForTest ?? (IsActive && WindowState != WindowState.Minimized);
            return _musicOn && foreground && DateTime.UtcNow >= _startingUntil && !Store.IsGameOpen();
        }

        void UpdateMusic() { _music.Apply(MusicWanted(), _musicVolume); }

        // Runs every couple of seconds: watches for the game starting and for a race result.
        void Poll()
        {
            TrackGame();
            UpdateMusic();
            ComeBackAfterContentManager();
            CleanUpContentManager();
            if (DateTime.UtcNow < _startingUntil && !Store.IsGameOpen() && Store.GameOpenForTestIsOff)
            {
                try { Store.DismissCrashPrompt(); }
                catch (Exception) { }
            }
            if (DateTime.UtcNow >= _nextRaceAt)
            {
                _nextRaceAt = DateTime.MaxValue;
                StartRace();
                return;
            }
            BringGameForward();
            if (DateTime.UtcNow > _startingUntil)
            {
                try { Store.RestorePureSettings(); }
                catch (Exception) { }
            }
            if (CheckForResult())
            {
                Show("debrief");
                return;
            }
            CheckForGameCommand();
            if (FollowGameClosing()) return;
            if (CheckForLeftRace())
            {
                ComeForward(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                _comeBackAt = DateTime.UtcNow.AddSeconds(4);
                Show("debrief");
                return;
            }
            var racing = IsRaceRunning();
            if (racing != _wasRacing)
            {
                _wasRacing = racing;
                if (_view == "paddock") Show("paddock");
            }
        }

        // Once the game has gone there's no race to wait for, even if it only just started loading.
        void TrackGame()
        {
            if (Store.IsGameOpen())
            {
                _gameWasOpen = true;
                WatchGameProcess();
                SetPollSpeed(ResultsScreenPoll);
            }
            else if (_gameWasOpen)
            {
                _gameWasOpen = false;
                _gameCrashed = GameCrashed();
                _startingUntil = DateTime.MinValue;
                _cleanUpUntil = DateTime.UtcNow.AddSeconds(12);
                SetPollSpeed(ResultsScreenPoll);
            }
        }

        // Windows only keeps a game's exit code for a program that was holding on to it while it ran.
        void WatchGameProcess()
        {
            try
            {
                if (_gameProcess != null && !_gameProcess.HasExited) return;
                _gameProcess = Process.GetProcessesByName("acs").FirstOrDefault();
                if (_gameProcess != null) GC.KeepAlive(_gameProcess.Handle);
            }
            catch (Exception) { _gameProcess = null; }
        }

        // A normal exit is the player's choice. Anything else is the game falling over.
        bool GameCrashed()
        {
            try { return _gameProcess != null && _gameProcess.HasExited && _gameProcess.ExitCode != 0; }
            catch (Exception) { return false; }
            finally { _gameProcess = null; }
        }

        // Content Manager opens its own results a moment after the game closes, on top of this window.
#if SELFTEST
        public bool GameCrashedForTest { set { _gameCrashed = value; } }
        public string DebriefStepForTest
        {
            set
            {
                var index = DebriefSteps(_c.LastReport).IndexOf(value);
                if (index < 0) throw new InvalidOperationException("No debrief step called " + value);
                _debriefStep = index;
                Show("debrief", false);
            }
        }
#endif

        void ComeBackAfterContentManager()
        {
            if (DateTime.UtcNow < _comeBackAt) return;
            _comeBackAt = DateTime.MaxValue;
            ComeForward(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }

        // Content Manager opens a results window a moment after each race, and they pile up if left alone.
        void CleanUpContentManager()
        {
            if (_cleanUpUntil == DateTime.MinValue) return;
            if (DateTime.UtcNow >= _cleanUpUntil)
            {
                _cleanUpUntil = DateTime.MinValue;
                if (!Store.IsGameOpen()) SetPollSpeed(NormalPoll);
                return;
            }
            if (Store.IsGameOpen() || DateTime.UtcNow < _startingUntil) return;
            CloseFinishedRaceWindows();
        }

        static void CloseFinishedRaceWindows()
        {
#if SELFTEST
            if (Store.GameOpenForTest != null) return;
#endif
            try { Store.CloseFinishedRaceWindows(); }
            catch (Exception) { }
        }

        static double SecondsBeforeNextRace
        {
            get
            {
#if SELFTEST
                if (Store.GameOpenForTest != null) return 0;
#endif
                return 4;
            }
        }

        void SetPollSpeed(TimeSpan speed)
        {
            if (_timer.Interval != speed) _timer.Interval = speed;
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

        // The in-game contract display only draws while the game is the active window. A game
        // started through another program can open behind it, so this brings it forward once.
        void BringGameForward()
        {
            if (DateTime.UtcNow > _focusUntil) return;
            var game = Process.GetProcessesByName("acs").FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (game == null) return;
            _focusUntil = DateTime.MinValue;
            if (GetForegroundWindow() == game.MainWindowHandle) return;
            ComeForward(game.MainWindowHandle);
        }

        // Windows only lets a program change the active window right after keyboard input.
        static void ComeForward(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            const byte altKey = 0x12;
            keybd_event(altKey, 0, 0, UIntPtr.Zero);
            keybd_event(altKey, 0, 2, UIntPtr.Zero);
            ShowWindow(window, 9);
            SetForegroundWindow(window);
        }

        bool IsRaceRunning()
        {
            if (_c == null) return false;
            if (_nextRaceAt != DateTime.MaxValue) return true;
            if (_c.Pending == null) return _finishedInGame && Store.IsGameOpen();
            return Store.IsGameOpen() || DateTime.UtcNow < _startingUntil;
        }

        void CheckForGameCommand()
        {
            RaceCommand command;
            try
            {
                command = Store.ReadCommand();
                if (command != null) Store.ClearCommand();
            }
            catch (Exception) { return; }
            if (command == null) return;
            if (command.Action == "session")
            {
                ReviewSession(command);
                return;
            }
            if (command.RaceId != _debriefRaceId) return;
            if (command.Action == "next" || command.Action == "paddock")
            {
                _nextRaceWanted = command.Action == "next";
                return;
            }
            if (command.Action == "repair" && _gameDebrief != null && _c != null && _gameDebrief.RepairCost > 0)
            {
                if (Engine.TryRepair(_c, command.Part, command.Half)) Save();
                Engine.FillCredits(_gameDebrief, _c, true);
                if (_gameDebrief.CanChoose && _c.Season == null) FillOffSeason();
                if (_c.Season != null && !_gameDebrief.SessionReview) FillMarket(_gameDebrief, _c);
                _gameDebrief.ReplyTitle = "";
                _gameDebrief.ReplyText = "";
                _gameDebrief.Revision = command.Seq;
                WriteGameDebrief();
                return;
            }
            if (command.Action == "approach" || command.Action == "transfer" || command.Action == "sitout" || command.Action == "rival")
            {
                if (_gameDebrief == null || _c == null || _c.Season == null) return;
                RunMarketCommand(command);
                Engine.FillCredits(_gameDebrief, _c, true);
                if (_c.Season != null) FillMarket(_gameDebrief, _c);
                else FillOffSeason();
                _gameDebrief.Revision = command.Seq;
                WriteGameDebrief();
                return;
            }
            if (_gameDebrief == null || !_gameDebrief.CanChoose || _c == null || _c.Season != null) return;

            RunOffSeasonCommand(command);
            Engine.FillCredits(_gameDebrief, _c, true);
            FillOffSeason();
            _gameDebrief.Revision = command.Seq;
            WriteGameDebrief();
        }

        // Practice or qualifying has ended: this damage is now the career's, and the game shows what can be fixed.
        void ReviewSession(RaceCommand command)
        {
            if (_c == null || _c.Pending == null || _c.Pending.RaceId != command.RaceId) return;
            RaceResult progress = null;
            try { progress = Store.ReadProgress(); }
            catch (Exception) { }
            if (progress != null && progress.RaceId == command.RaceId)
            {
                Engine.RecordDamage(_c, progress);
                Save();
                try { Store.ClearProgress(); }
                catch (Exception) { }
            }
            _debriefRaceId = command.RaceId;
            _gameDebrief = new GameDebrief { RaceId = command.RaceId, SessionReview = true, Revision = command.Seq, SoundsOn = _soundsOn, SoundsVolume = _soundsVolume };
            Engine.FillCredits(_gameDebrief, _c, true);
            WriteGameDebrief();
        }

        // What the player chose on the in-game offers screen. It gets the same treatment as the launcher's own pages.
        void RunOffSeasonCommand(RaceCommand command)
        {
            _gameDebrief.ReplyTitle = "";
            _gameDebrief.ReplyText = "";
            var series = _series.FirstOrDefault(s => s.Id == command.SeriesId);
            switch (command.Action)
            {
                case "request":
                    if (series == null || !Engine.SeatRequestOdds(_c, series).Allowed) break;
                    var accepted = Engine.ResolveSeatRequest(_c, _series, series, Engine.SeatRoll(_c, series));
                    Save();
                    _gameDebrief.ReplyTitle = accepted ? "A team said yes" : "No seat this time";
                    _gameDebrief.ReplyText = _c.SeatNote;
                    break;
                case "pick":
                    if (series == null) break;
                    _c.Offers = Engine.MakeOffers(_c, _series, series);
                    Save();
                    break;
                case "series":
                    _c.Offers = null;
                    Save();
                    break;
                case "sign":
                    if (_c.Offers == null || command.OfferIndex < 0 || command.OfferIndex >= _c.Offers.Count) break;
                    try
                    {
                        Engine.StartSeason(_c, _c.Offers[command.OfferIndex], _series, _tracks);
                        Save();
                        _gameDebrief.Signed = true;
                        _gameDebrief.HasNextRace = true;
                        FillBriefing(_gameDebrief, _c);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _gameDebrief.ReplyTitle = "That contract can't start";
                        _gameDebrief.ReplyText = ex.Message;
                    }
                    break;
            }
        }

        void FillOffSeason()
        {
            if (_c.Season != null) return;
            EnsureOffers();
            Engine.FillOffSeason(_gameDebrief, _c, _series, (car, skin) => Installed.CarImage(_acRoot, car, skin));
        }

        void FillMarket(GameDebrief debrief, Career career)
        {
            Engine.FillMarket(debrief, career, _series, (car, skin) => Installed.CarImage(_acRoot, car, skin));
        }

        // Approaches and signing from the in-game market. They get the same treatment as the launcher's own page.
        void RunMarketCommand(RaceCommand command)
        {
            _gameDebrief.ReplyTitle = "";
            _gameDebrief.ReplyText = "";
            switch (command.Action)
            {
                case "approach":
                    var seat = Engine.Market(_c, _series).FirstOrDefault(x => x.SeriesId == command.SeriesId && (x.Team ?? "") == (command.Team ?? ""));
                    if (seat == null || !seat.Allowed || _c.Credits < Engine.BuyoutCost(_c)) break;
                    var accepted = Engine.ResolveApproach(_c, _series, seat, Engine.MarketRoll(_c, seat));
                    Save();
                    _gameDebrief.ReplyTitle = accepted ? "A team said yes" : "No seat this time";
                    _gameDebrief.ReplyText = _c.SeatNote;
                    break;
                case "transfer":
                    if (_c.MarketOffer == null) break;
                    try
                    {
                        Engine.Transfer(_c, _c.MarketOffer, _series, _tracks);
                        Save();
                        _gameDebrief.Signed = true;
                        _gameDebrief.HasNextRace = true;
                        _gameDebrief.Released = false;
                        FillBriefing(_gameDebrief, _c);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _gameDebrief.ReplyTitle = "That move can't go ahead";
                        _gameDebrief.ReplyText = ex.Message;
                    }
                    break;
                case "rival":
                    Engine.SetRival(_c, command.Team);
                    Save();
                    if (_gameDebrief.HasNextRace) FillBriefing(_gameDebrief, _c);
                    break;
                case "sitout":
                    var report = Engine.SitOutSeason(_c);
                    Save();
                    _gameDebrief.Released = false;
                    _gameDebrief.HasNextRace = false;
                    _gameDebrief.CanChoose = true;
                    _gameDebrief.ReplyTitle = "Season over";
                    _gameDebrief.ReplyText = report != null ? report.SeasonOutcome : "";
                    break;
            }
        }

        void FillBriefing(GameDebrief debrief, Career career)
        {
            debrief.Briefing = Engine.BuildBriefing(career, (track, layout) => Installed.TrackImage(_acRoot, track, layout), _env.HasPure);
        }

        void WriteGameDebrief()
        {
            try { Store.WriteDebrief(_gameDebrief); }
            catch (Exception) { }
        }

        // The results screen closes the game. Next race starts once it has gone, otherwise this window comes back.
        bool FollowGameClosing()
        {
            if (!_finishedInGame || Store.IsGameOpen()) return false;
            _finishedInGame = false;
            var next = _nextRaceWanted && _c != null && _c.Season != null && _c.Pending == null;
            _nextRaceWanted = false;
            _debriefRaceId = null;
            _gameDebrief = null;
            _timer.Interval = NormalPoll;
            if (next)
            {
                // A short wait lets Content Manager open its results window, so it can be closed before the next race starts.
                if (SecondsBeforeNextRace <= 0) StartRace();
                else
                {
                    _nextRaceAt = DateTime.UtcNow.AddSeconds(SecondsBeforeNextRace);
                    Show("paddock");
                }
                return true;
            }

            ComeForward(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Show(_c != null && _c.LastReport != null ? "debrief" : _view);
            return true;
        }

        // Scoring happens behind the game, which needs to keep the focus to show its results screen.
        void AfterScoring(Career career, RoundReport report, RaceResult result, bool isActiveCareer)
        {
            if (!Store.IsGameOpen())
            {
                if (isActiveCareer) Activate();
                return;
            }
            _finishedInGame = true;
            _nextRaceWanted = false;
            _debriefRaceId = result.RaceId;
            _timer.Interval = ResultsScreenPoll;
            if (report == null) return;
            _gameDebrief = Engine.BuildDebrief(career, report, result, isActiveCareer);
            _gameDebrief.MusicOn = _musicOn;
            _gameDebrief.MusicVolume = _musicVolume;
            _gameDebrief.SoundsOn = _soundsOn;
            _gameDebrief.SoundsVolume = _soundsVolume;
            if (isActiveCareer) FillOffSeason();
            if (isActiveCareer && career.Season != null)
            {
                FillBriefing(_gameDebrief, career);
                FillMarket(_gameDebrief, career);
            }
            WriteGameDebrief();
        }

        void RebuildSeries()
        {
            Engine.ApplyClassOverrides(_cars, _classOverrides);
            _series = Engine.ApplySeriesEdits(Engine.BuildSeries(_cars), _cars, _edits);
        }

        // Offers made before a series changed can no longer be signed, so new ones are made instead.
        void DropStaleOffers()
        {
            if (_c != null && Engine.AdoptPathFromHistory(_c, _series)) Save();
            if (_c == null || _c.Offers == null) return;
            if (_c.Offers.All(o => _series.Any(s => s.Id == o.SeriesId && s.Cars.Any(x => x.Id == o.CarId)))) return;
            _c.Offers = null;
            Save();
        }

        bool CheckForResult()
        {
            RaceResult result;
            try { result = Store.ReadResult(); }
            catch (Exception) { return false; }
            if (result == null) return false;

            if (_c != null && _c.Pending != null && result.RaceId == _c.Pending.RaceId)
            {
                var report = Engine.ApplyResult(_c, result);
                Save();
                ResetDebrief();
                Store.ClearRaceFiles();
                _notice = null;
                AfterScoring(_c, report, result, true);
                return true;
            }

            // A result can only belong to a career that was waiting for it, active or not.
            var owner = Store.FindCareerAwaiting(result.RaceId);
            if (owner == null)
            {
                Store.ClearResult();
                return false;
            }
            var ownerReport = Engine.ApplyResult(owner, result);
            Store.SaveCareer(owner);
            Store.ClearRaceFiles();
            AfterScoring(owner, ownerReport, result, false);
            _notice = "A race result was saved to " + owner.Driver + "'s career.";
            Show(_view);
            return false;
        }

        // The game closed before the flag. Returns true when that retired the player from the open career.
        bool CheckForLeftRace()
        {
            if (Store.IsGameOpen()) return false;
            RaceResult progress;
            try { progress = Store.ReadProgress(); }
            catch (Exception) { return false; }
            if (progress == null) return false;
            _startingUntil = DateTime.MinValue;

            var isActive = _c != null && _c.Pending != null && _c.Pending.RaceId == progress.RaceId;
            var career = isActive ? _c : Store.FindCareerAwaiting(progress.RaceId);
            _debriefRaceId = null;
            _gameDebrief = null;
            if (career == null)
            {
                Store.ClearProgress();
                return false;
            }

            // Closing the game ends your race. Only a real crash, which isn't your fault, leaves the round open.
            var crashed = _gameCrashed;
            _gameCrashed = false;
            var retired = progress.RaceStarted && !crashed;
            if (retired) Engine.Retire(career, progress);
            else Engine.RecordDamage(career, progress);
            Store.SaveCareer(career);
            if (retired) Store.ClearRaceFiles();
            else Store.ClearProgress();
            if (retired && isActive) ResetDebrief();

            if (!isActive) _notice = retired ? "A retirement was saved to " + career.Driver + "'s career." : null;
            else if (progress.RaceStarted && !retired) _notice = "The game closed unexpectedly, so this race wasn't scored. Any damage has been kept.";
            else _notice = null;
            return retired && isActive;
        }

        void Save()
        {
            try { Store.SaveCareer(_c); }
            catch (Exception ex) { _notice = "Your career couldn't be saved: " + ex.Message; }
        }

        // Shell ------------------------------------------------------------------------------

        // Three rungs climbing to a bright top step. Drawn from the same shape as the app icon.
        static FrameworkElement LogoMark(double size)
        {
            var canvas = new Canvas { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            var slant = Math.Tan(14 * Math.PI / 180);
            Func<double, double, Point> at = (x, y) => new Point((x + (512 - y) * slant) / 1024 * size, y / 1024 * size);
            var tops = new[] { 644.0, 430.0, 216.0 };
            for (var i = 0; i < tops.Length; i++)
            {
                var left = 150.0 + i * 92;
                canvas.Children.Add(new Polygon
                {
                    Fill = i == 2 ? U.Volt : U.Text,
                    Points = new PointCollection { at(left, tops[i]), at(left + 540, tops[i]), at(left + 540, tops[i] + 164), at(left, tops[i] + 164) },
                });
            }
            return canvas;
        }

        FrameworkElement BuildTopBar()
        {
            var mark = U.Row(U.Margin(LogoMark(24), 0, 0, 11, 0), U.Display("PADDOCK", 16, U.Text, false, true), U.Margin(U.Display("LADDER", 16, U.Muted), 7, 0, 0, 0));
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Margin = new Thickness(PagePad - 40, 0, 44, 0);

            _back = U.Bare(new Border { Width = 32, Height = 32, Background = Brushes.Transparent, Child = new TextBlock { Text = U.IconBack, FontFamily = U.IconFont, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }, GoBack);
            _back.Margin = new Thickness(8, 0, 0, 0);
            _back.VerticalAlignment = VerticalAlignment.Center;
            _back.ToolTip = "Back";
            WindowChrome.SetIsHitTestVisibleInChrome(_back, true);
            DockPanel.SetDock(_back, Dock.Left);

            _maximize = U.WindowButton(U.IconMax, delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; });
            var controls = U.Row(U.WindowButton(U.IconMin, delegate { WindowState = WindowState.Minimized; }), _maximize, U.WindowButton(U.IconClose, Close));
            controls.VerticalAlignment = VerticalAlignment.Top;

            var dock = new DockPanel();
            DockPanel.SetDock(mark, Dock.Left);
            DockPanel.SetDock(controls, Dock.Right);
            DockPanel.SetDock(_currentDriver, Dock.Right);
            dock.Children.Add(_back);
            dock.Children.Add(mark);
            dock.Children.Add(controls);
            dock.Children.Add(_currentDriver);
            dock.Children.Add(_nav);
            return dock;
        }

        public void Show(string view) { Show(view, true); }

        bool CanGoBack { get { return _history.Count > 0 || (_c != null && _view != "paddock"); } }

        void GoBack()
        {
            if (_history.Count > 0)
            {
                var previous = _history[_history.Count - 1];
                _history.RemoveAt(_history.Count - 1);
                Show(previous, false);
            }
            else if (_c != null && _view != "paddock")
            {
                Show("paddock", false);
            }
        }

        void ClearHistory() { _history.Clear(); }

        // Changing a setting redraws the page it's on. That must look like nothing happened: no fade, no jump.
        void Show(string view, bool remember)
        {
            var redraw = _hasShown && view == _view && !_freshStep;
            _freshStep = false;
            var offset = _scroll.VerticalOffset;
            var animate = U.Animate;
            if (redraw) U.Animate = false;
            try { Render(view, remember); }
            finally { U.Animate = animate; }
            if (redraw)
            {
                _scroll.UpdateLayout();
                _scroll.ScrollToVerticalOffset(offset);
            }
            else _scroll.ScrollToTop();
        }

        void Render(string view, bool remember)
        {
            if (remember && _hasShown && view != _view)
            {
                _history.Add(_view);
                if (_history.Count > 40) _history.RemoveAt(0);
            }
            _hasShown = true;
            if (view == "debrief" && _view != "debrief" && _view != "garage" && _view != "repairpart") ResetDebrief();
            _view = view;
            DrawNav();
            _back.IsEnabled = CanGoBack;
            _back.Opacity = CanGoBack ? 1 : 0.25;
            FrameworkElement body;
            switch (view)
            {
                case "season": body = SeasonView(); break;
                case "contracts": body = ContractsView(); break;
                case "series": body = SeriesView(); break;
                case "history": body = CareerView(); break;
                case "settings": body = SettingsView(); break;
                case "new": body = NewCareerView(); break;
                case "debrief": body = DebriefView(); break;
                case "careers": body = CareersView(); break;
                case "raceplan": body = RacePlanView(); break;
                case "garage": body = GarageView(); break;
                case "team": body = TeamView(); break;
                case "market": body = MarketView(); break;
                case "repairpart": body = RepairPartView(); break;
                case "offerdetail": body = OfferDetailView(); break;
                case "briefing": body = BriefingView(); break;
                case "cars": body = CarClassesView(); break;
                case "customseries": body = CustomSeriesView(); break;
                case "addcar": body = AddCarView(); break;
                case "seatrequest": body = SeatRequestView(); break;
                default: body = PaddockView(); break;
            }
            body.Margin = new Thickness(PagePad, 10, PagePad, 44);

            var page = new StackPanel();
            if (_notice != null) page.Children.Add(NoticeBar());
            page.Children.Add(body);
            _scroll.Content = page;
            U.Reveal(page, 0);
        }

        FrameworkElement NoticeBar()
        {
            var dismiss = U.Link("Dismiss", delegate { _notice = null; Show(_view); });
            var dock = new DockPanel();
            DockPanel.SetDock(dismiss, Dock.Right);
            dock.Children.Add(dismiss);
            dock.Children.Add(U.Body(_notice, 13.5, U.Text, true));
            return U.Margin(new Border { BorderBrush = U.Volt, BorderThickness = new Thickness(2, 0, 0, 0), Background = U.Surface, Padding = new Thickness(16, 11, 16, 11), Child = dock }, PagePad, 0, PagePad, 8);
        }

        void DrawNav()
        {
            _nav.Children.Clear();
            var items = new List<KeyValuePair<string, string>>();
            if (_c != null)
            {
                items.Add(new KeyValuePair<string, string>("paddock", "Paddock"));
                if (_c.Season != null) items.Add(new KeyValuePair<string, string>("season", "Season"));
                else items.Add(new KeyValuePair<string, string>(_c.Mode == "open" && _c.Offers == null ? "series" : "contracts", "Next season"));
                if (_c.Season != null) items.Add(new KeyValuePair<string, string>("team", "Team"));
                items.Add(new KeyValuePair<string, string>("history", "Profile"));
                items.Add(new KeyValuePair<string, string>("careers", "Careers"));
                items.Add(new KeyValuePair<string, string>("settings", "Settings"));
            }
            else if (Store.ListCareers().Count > 0)
            {
                items.Add(new KeyValuePair<string, string>("careers", "Careers"));
            }

            if (_c == null)
            {
                _currentDriver.Child = null;
            }
            else
            {
                var who = U.Bare(U.Row(U.Eyebrow("Driver", U.Faint), U.Margin(U.Eyebrow(_c.Driver, U.Text), 8, 0, 0, 0), U.Margin(U.Eyebrow("Credits", U.Faint), 22, 0, 0, 0), U.Margin(U.Eyebrow(Engine.Cr(_c.Credits), _c.Credits < 0 ? U.Bad : U.Volt), 8, 0, 0, 0)), delegate { Show("careers"); });
                WindowChrome.SetIsHitTestVisibleInChrome(who, true);
                _currentDriver.Child = who;
            }

            foreach (var item in items)
            {
                var key = item.Key;
                var active = key == _view || (_c != null && _c.Season == null && (key == "series" || key == "contracts") && (_view == "series" || _view == "contracts"));
                var label = U.Eyebrow(item.Value, active ? U.Text : null, 11.5);
                if (!active) label.ClearValue(TextBlock.ForegroundProperty);
                var marker = new Rectangle { Height = 2, Fill = active ? U.Volt : Brushes.Transparent, Margin = new Thickness(0, 7, 0, 0) };
                var button = U.Bare(U.Stack(label, marker), delegate { Show(key); });
                button.VerticalAlignment = VerticalAlignment.Center;
                button.Margin = new Thickness(0, 7, 34, 0);
                WindowChrome.SetIsHitTestVisibleInChrome(button, true);
                _nav.Children.Add(button);
            }
        }

        static FrameworkElement Fact(string label, string value, double size = 24, Brush color = null)
        {
            return U.Stack(U.Eyebrow(label), U.Margin(U.Display(value, size, color), 0, 5, 0, 0));
        }

        static FrameworkElement Readout(string label, int value, Brush color)
        {
            return U.Stack(U.Eyebrow(label), U.Margin(U.Display(value.ToString(), 24, color), 0, 5, 0, 7), U.Meter(value / 100.0, color == U.Text ? U.Muted : color));
        }

        // Cells separated by thin rules, spread across the page.
        static FrameworkElement Strip(params FrameworkElement[] cells)
        {
            var grid = new UniformGrid { Rows = 1, Columns = cells.Length };
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = new Border { Child = cells[i], Padding = new Thickness(i == 0 ? 0 : 24, 0, 24, 0) };
                if (i > 0)
                {
                    cell.BorderBrush = U.Line;
                    cell.BorderThickness = new Thickness(1, 0, 0, 0);
                }
                grid.Children.Add(cell);
            }
            return U.Stack(U.Rule(0, 22), grid);
        }

        static FrameworkElement RoundName(string name, double size)
        {
            var at = name.IndexOf(" - ", StringComparison.Ordinal);
            if (at < 0) return U.Display(name, size, null, true);
            return U.Stack(U.Display(name.Substring(0, at), size, null, true), U.Margin(U.Display(name.Substring(at + 3), size * 0.42, U.Muted), 0, 2, 0, 0));
        }

        // New career -------------------------------------------------------------------------

        FrameworkElement ModeTile(string key, string title, string body)
        {
            var selected = _newMode == key;
            var tile = new Border
            {
                Width = 262,
                Margin = new Thickness(0, 0, 14, 0),
                Padding = new Thickness(18, 16, 18, 16),
                BorderBrush = selected ? U.Volt : U.Line,
                BorderThickness = new Thickness(selected ? 0 : 1, selected ? 2 : 1, selected ? 0 : 1, selected ? 0 : 1),
                Background = selected ? U.Surface : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = U.Stack(U.Display(title, 19), U.Margin(U.Body(body, 13, U.Muted, true), 0, 7, 0, 0)),
            };
            tile.MouseLeftButtonUp += delegate { _newMode = key; Show("new"); };
            return tile;
        }

        FrameworkElement NewCareerView()
        {
            var nameBox = new TextBox { Text = _newName, Width = 360, FontSize = 24, FontFamily = U.DisplayFont, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Left };
            nameBox.TextChanged += delegate { _newName = nameBox.Text; };

            var form = U.Stack(
                U.Eyebrow(string.Format("{0} cars  /  {1} circuits  /  {2} series", _cars.Count, _tracks.Count, _series.Count)),
                U.Margin(U.Display("Your career starts here", 50, null, true), 0, 8, 0, 26),
                U.Eyebrow("Driver name"),
                U.Margin(nameBox, 0, 4, 0, 24),
                U.Eyebrow("Path"),
                U.Margin(U.Row(
                    ModeTile("ladder", "Climb the ladder", "Start in slow road cars. Teams judge your results and how cleanly you drive."),
                    ModeTile("open", "Pick any series", "Jump into any series you like, each season. Your driving record still counts.")), 0, 10, 0, 24),
                U.Row(NewCareerAiControl(), U.Margin(NewCareerDistanceControl(), 40, 0, 0, 0)),
                U.Margin(U.Eyebrow("Car damage"), 0, 24, 0, 8),
                U.Row(U.Segments(new[] { "On", "Off" }, new[] { true, false }, _newDamage, v => { _newDamage = v; Show("new"); }), U.Margin(U.Body(_newDamage ? "Repairs cost credits, and unrepaired damage carries into the next race." : "No repair bills. Damage is cleared during races and never carries over.", 13.5, U.Muted), 18, 8, 0, 0)),
                U.Margin(U.Row(U.Primary("Create career", CreateCareer)), 0, 30, 0, 0));
            form.Margin = new Thickness(0, 30, 0, 0);
            if (_c != null)
            {
                var cancel = U.Link("Back to careers", delegate { Show("careers"); });
                cancel.HorizontalAlignment = HorizontalAlignment.Left;
                form.Children.Add(U.Margin(cancel, 0, 20, 0, 0));
            }

            var view = new Grid();
            if (_series.Count > 0)
            {
                var first = _series[0].Cars[_series[0].Cars.Count / 2];
                var stage = U.Stage(Installed.CarImage(_acRoot, first.Id, first.Skins[0]), 660, 372);
                stage.HorizontalAlignment = HorizontalAlignment.Right;
                stage.VerticalAlignment = VerticalAlignment.Top;
                stage.Margin = new Thickness(0, -8, -PagePad, 0);
                view.Children.Add(stage);
            }
            view.Children.Add(form);
            return view;
        }

        static readonly string[] FormatLabels = { "Laps", "Endurance" };
        static readonly string[] FormatValues = { "laps", "endurance" };

        static int ClampAi(int value) { return Math.Max(70, Math.Min(100, value)); }

        FrameworkElement NewCareerAiControl()
        {
            var tier = Engine.Tiers[0];
            var hint = _newMode == "open" ? "Used in every series you pick." : "Your starting strength. It rises 3% with each tier you climb.";
            return U.Stack(
                U.Eyebrow("AI strength"),
                U.Margin(U.Stepper(ClampAi(tier.Ai + _newDifficulty), 70, 100, 1, "%", v => { _newDifficulty = v - tier.Ai; Show("new"); }, 170), 0, 8, 0, 6),
                U.Body(hint, 12.5, U.Muted));
        }

        FrameworkElement NewCareerDistanceControl()
        {
            var endurance = _newFormat == "endurance";
            var control = endurance
                ? U.Stepper(_newMinutes, Engine.MinEndurance, Engine.MaxEndurance, 15, "min", v => { _newMinutes = v; Show("new"); }, 150)
                : U.Stepper(_newLaps, Engine.MinLaps, Engine.MaxLaps, 1, "laps", v => { _newLaps = v; Show("new"); }, 150);
            return U.Stack(
                U.Row(U.Eyebrow("Race format"), U.Margin(U.Segments(FormatLabels, FormatValues, _newFormat, v => { _newFormat = v; Show("new"); }), 12, -7, 0, 0)),
                U.Margin(control, 0, 6, 0, 6),
                U.Body("You can change this for any single race too.", 12.5, U.Muted));
        }

        void CreateCareer()
        {
            _c = Engine.NewCareer(_newName, _newMode, _newDifficulty, 1, Environment.TickCount & 0x7FFFFFF);
            _c.Format = _newFormat;
            _c.RaceLaps = _newLaps;
            _c.EnduranceMinutes = _newMinutes;
            _c.DamageOn = _newDamage;
            if (_c.Mode == "ladder") _c.Offers = Engine.MakeOffers(_c, _series, null);
            Save();
            Store.SetActiveCareer(_c.Id);
            ClearHistory();
            Show(_c.Mode == "ladder" ? "contracts" : "series");
        }

        // Careers ----------------------------------------------------------------------------

        void StartNewCareer()
        {
            _newName = "";
            _newMode = "ladder";
            _newDamage = true;
            _newDifficulty = 0;
            _newFormat = "laps";
            _newLaps = 5;
            _newMinutes = 60;
            Show("new");
        }

        void SwitchTo(string id)
        {
            string problem;
            var career = Store.LoadCareer(id, out problem);
            if (career == null)
            {
                _notice = problem ?? "That career couldn't be found.";
                Show("careers");
                return;
            }
            _c = career;
            Store.SetActiveCareer(id);
            _notice = null;
            DropStaleOffers();
            ClearHistory();
            Show("paddock");
        }

        void ConfirmDeleteCareer(string id, string driver)
        {
            if (!Dialog.Confirm(this, "Delete this career?", driver + "'s career and its saved files will be permanently deleted. You won't be able to get it back.", "Delete")) return;

            var wasActive = _c != null && _c.Id == id;
            try { Store.DeleteCareer(id); }
            catch (Exception ex)
            {
                _notice = "That career couldn't be deleted: " + ex.Message;
                Show(_view);
                return;
            }
            if (wasActive)
            {
                _c = null;
                Store.ClearRaceFiles();
                var next = Store.ListCareers().FirstOrDefault();
                if (next != null)
                {
                    SwitchTo(next.Id);
                    return;
                }
                StartNewCareer();
                return;
            }
            Show("careers");
        }

        static string PlayedLabel(DateTime when)
        {
            var days = (DateTime.Now.Date - when.Date).Days;
            return days <= 0 ? "Today" : days == 1 ? "Yesterday" : when.ToString("d MMM yyyy");
        }

        FrameworkElement CareerRow(CareerSummary s, bool locked)
        {
            var active = _c != null && s.Id == _c.Id;
            var image = s.CarId != null ? Installed.CarImage(_acRoot, s.CarId, s.Skin) : "";
            var photo = U.Photo(image, 104, 300, false);
            photo.Width = 184;

            var details = U.Stack(
                U.Display(s.Driver, 26),
                U.Margin(U.Eyebrow(string.Format("{0} tier  /  Season {1}  /  {2}", Engine.TierName(s.Tier), s.SeasonNo, s.Mode == "ladder" ? "Career ladder" : "Open series")), 0, 5, 0, 8),
                U.Body(s.Status, 14, U.Muted));
            details.Margin = new Thickness(22, 0, 16, 0);
            details.VerticalAlignment = VerticalAlignment.Center;

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(U.Margin(U.Stack(U.Eyebrow("Last played"), U.Margin(U.Body(PlayedLabel(s.LastPlayed), 14), 0, 3, 0, 0)), 0, 0, 34, 0));
            if (active)
            {
                actions.Children.Add(U.Margin(U.Eyebrow("Current", U.Volt), 0, 0, 24, 0));
                ((FrameworkElement)actions.Children[1]).VerticalAlignment = VerticalAlignment.Center;
            }
            else if (!locked)
            {
                actions.Children.Add(U.Margin(U.Ghost("Switch", delegate { SwitchTo(s.Id); }), 0, 0, 24, 0));
            }
            if (!locked) actions.Children.Add(U.Link("Delete", delegate { ConfirmDeleteCareer(s.Id, s.Driver); }));

            var row = new DockPanel { Margin = new Thickness(0, 14, 0, 14) };
            DockPanel.SetDock(photo, Dock.Left);
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(photo);
            row.Children.Add(actions);
            row.Children.Add(details);
            return U.Stack(row, U.Rule());
        }

        FrameworkElement CareersView()
        {
            var locked = IsRaceRunning();
            var list = Store.ListCareers();
            var count = list.Count + (list.Count == 1 ? " career" : " careers");

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            if (!locked)
            {
                var create = U.Primary("Start new career", StartNewCareer);
                create.VerticalAlignment = VerticalAlignment.Bottom;
                DockPanel.SetDock(create, Dock.Right);
                header.Children.Add(create);
            }
            header.Children.Add(U.Stack(U.Eyebrow(count), U.Margin(U.Display("Careers", 46, null, true), 0, 6, 0, 0)));

            var page = U.Stack(header);
            if (locked) page.Children.Add(U.Margin(U.Body("You can switch careers once your race is over.", 14, U.Warn), 0, 0, 0, 14));
            page.Children.Add(U.Rule());
            foreach (var summary in list) page.Children.Add(CareerRow(summary, locked));

            return page;
        }

        // Paddock ----------------------------------------------------------------------------

        FrameworkElement ProfileStrip()
        {
            var licence = Engine.LicenceStatus(_c.LicencePoints);
            var licenceColor = licence == "Clear" ? U.Good : licence == "Caution" ? U.Warn : U.Bad;
            var cells = new List<FrameworkElement>
            {
                Fact("Tier", Engine.TierName(_c.Season != null ? _c.Season.Tier : _c.Tier)),
                Readout("Pace", (int)_c.Pace, U.Text),
                Readout("Clean driving", (int)_c.Clean, _c.Clean >= 60 ? U.Good : U.Bad),
                Fact("Licence", licence, 24, licenceColor),
                Fact(_c.Credits < 0 ? "You owe" : "Credits", Engine.Cr(Math.Abs(_c.Credits)), 24, _c.Credits < 0 ? U.Bad : U.Volt),
            };
            if (_c.Season != null)
            {
                var table = Engine.Standings(_c);
                var rank = table.FindIndex(r => r.IsPlayer) + 1;
                cells.Add(Readout("Team trust", _c.Season.Trust, _c.Season.Trust >= 40 ? U.Text : U.Bad));
                cells.Add(Fact("Championship", string.Format("P{0}  /  {1} pts", rank, _c.Season.Points[Engine.PlayerKey])));
            }
            else
            {
                cells.Add(Fact("Titles", _c.Titles.ToString()));
            }
            return Strip(cells.ToArray());
        }

        FrameworkElement PaddockView()
        {
            if (_c.Season == null)
            {
                var target = _c.Mode == "open" && _c.Offers == null ? "series" : "contracts";
                var idle = U.Stack(
                    U.Eyebrow("Season " + _c.SeasonNo),
                    U.Margin(U.Display("Find your next drive", 58, null, true), 0, 10, 0, 10),
                    U.Margin(U.Body(_c.Mode == "open" ? "Choose the series you want to race in next." : "Teams have looked at your record. See who wants you.", 15, U.Muted), 0, 0, 0, 26),
                    U.Row(U.Primary(_c.Mode == "open" ? "Choose a series" : "See offers", delegate { EnsureOffers(); Show(target); })));
                idle.Margin = new Thickness(0, 50, 0, 80);
                var page = U.Stack(idle, ProfileStrip());
                if (_c.LastReport != null) page.Children.Add(U.Margin(U.Link("View last debrief", delegate { Show("debrief"); }), 0, 22, 0, 0));
                ((FrameworkElement)page.Children[page.Children.Count - 1]).HorizontalAlignment = HorizontalAlignment.Left;
                return page;
            }

            var s = _c.Season;
            var round = s.Rounds[s.RoundIndex];
            var teammate = s.Field.First(e => e.IsTeammate);
            var hero = new Grid { MinHeight = 536 };

            var stage = U.Stage(Installed.CarImage(_acRoot, s.CarId, s.Skin), 930, 523);
            stage.HorizontalAlignment = HorizontalAlignment.Right;
            stage.VerticalAlignment = VerticalAlignment.Top;
            stage.Margin = new Thickness(0, -8, -PagePad, 0);
            hero.Children.Add(stage);
            U.Reveal(stage, 60, 0);

            var caption = U.Stack(U.Display(s.CarName, 19), U.Margin(U.Eyebrow(s.Team), 0, 3, 0, 0));
            caption.HorizontalAlignment = HorizontalAlignment.Right;
            caption.VerticalAlignment = VerticalAlignment.Bottom;
            caption.Margin = new Thickness(0, 0, 0, 22);
            ((TextBlock)caption.Children[0]).TextAlignment = TextAlignment.Right;
            hero.Children.Add(caption);

            var timed = Engine.IsTimed(_c, round);
            var facts = U.Row(
                U.Margin(Fact(timed ? "Endurance" : "Laps", timed ? Engine.MinutesFor(_c, round) + " min" : Engine.LapsFor(_c, round).ToString(), 26), 0, 0, 34, 0),
                U.Margin(Fact("Grid", (s.Field.Count + 1).ToString(), 26), 0, 0, 34, 0),
                U.Margin(Fact("Start", Engine.StartingPosition(_c) > 0 ? "P" + Engine.StartingPosition(_c) : "Quali", 26), 0, 0, 34, 0),
                Fact("Air", round.Ambient + "°C", 26));

            var slots = _env.HasPure ? round.WeatherSlots : round.WeatherSlots.Take(1).ToList();
            var forecast = string.Join("  →  ", slots.Select(id => Engine.WeatherById(id).Name));

            var clauses = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            foreach (var clause in s.Clauses)
                clauses.Children.Add(U.Margin(U.Row(U.Margin(U.Icon(U.IconArrow, 10, U.Volt), 0, 1, 10, 0), U.Body(clause.Text, 14.5)), 0, 0, 0, 6));

            var text = new StackPanel { Width = 540, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 46, 0, 0) };
            text.Children.Add(U.Eyebrow(string.Format("Round {0} of {1}  /  {2}", s.RoundIndex + 1, s.Rounds.Count, s.SeriesName)));
            text.Children.Add(U.Margin(RoundName(round.TrackName, 58), 0, 8, 0, 22));
            text.Children.Add(facts);
            text.Children.Add(U.Margin(U.Eyebrow("Race weekend"), 0, 18, 0, 0));
            text.Children.Add(U.Margin(U.Body(WeekendLine(round), 14.5, U.Text, true), 0, 5, 0, 0));
            text.Children.Add(U.Margin(U.Eyebrow("Forecast"), 0, 18, 0, 0));
            text.Children.Add(U.Margin(U.Body(forecast, 14.5, U.Text, true), 0, 5, 0, 0));
            text.Children.Add(U.Margin(U.Eyebrow("Contract  /  Teammate " + teammate.Name), 0, 18, 0, 0));
            text.Children.Add(clauses);
            text.Children.Add(U.Margin(RaceActions(), 0, 18, 0, 0));
            hero.Children.Add(text);
            U.Reveal(text, 120);

            var view = U.Stack(hero, U.Margin(ProfileStrip(), 0, 16, 0, 0));
            if (_c.Mode == "ladder") view.Children.Add(U.Margin(PromotionPanel(), 0, 24, 0, 0));
            if (_c.LastReport != null)
            {
                var link = U.Link("View last debrief", delegate { Show("debrief"); });
                link.HorizontalAlignment = HorizontalAlignment.Left;
                view.Children.Add(U.Margin(link, 0, 22, 0, 0));
            }
            return view;
        }

        static readonly string PromotionRule = string.Format("Teams score your season: 55% championship position, 30% clean driving, 15% team trust. {0}% earns promotion, and under {1}% drops you a tier.", (int)Math.Round(Engine.PromoteAt * 100), (int)Math.Round(Engine.DropBelow * 100));

        static FrameworkElement ScorePart(string label, int value, int max, Brush color)
        {
            return U.Stack(U.Eyebrow(label), U.Margin(U.Display(value + " / " + max, 24), 0, 5, 0, 7), U.Meter((double)value / max, color));
        }

        static FrameworkElement ScoreStrip(string heading, int total, int position, int clean, int trust, string verdict, Brush color)
        {
            var strip = Strip(
                Fact(heading, total + "%", 24, color),
                ScorePart("Championship", position, 55, U.Muted),
                ScorePart("Clean driving", clean, 30, U.Muted),
                ScorePart("Team trust", trust, 15, U.Muted),
                U.Stack(U.Eyebrow("Outlook"), U.Margin(U.Body(verdict, 14.5, color, true), 0, 6, 0, 0)));
            return U.Stack(strip, U.Margin(U.Body(PromotionRule, 13, U.Muted, true), 0, 12, 0, 0));
        }

        FrameworkElement PromotionPanel()
        {
            var outlook = Engine.Outlook(_c);
            if (_c.Season.RoundIndex == 0)
            {
                var opening = outlook.Danger ? outlook.Verdict + ". " : "Your score starts after your first race. ";
                return U.Stack(U.Eyebrow("Promotion score"), U.Margin(U.Body(opening + PromotionRule, 13.5, U.Muted, true), 0, 8, 0, 0));
            }
            return ScoreStrip("Promotion score", outlook.Total, outlook.Position, outlook.Clean, outlook.Trust, outlook.Verdict, outlook.Danger ? U.Bad : outlook.Good ? U.Good : U.Text);
        }

        FrameworkElement RaceActions()
        {
            if (IsRaceRunning() && _c.Pending == null)
                return U.Stack(U.Display("Round complete", 24, U.Volt), U.Margin(U.Body("Click Next race in the game to carry on.", 13.5, U.Muted), 0, 4, 0, 0));
            if (IsRaceRunning())
                return U.Stack(U.Display("Race in progress", 24, U.Volt), U.Margin(U.Body("Your result appears here when you take the flag.", 13.5, U.Muted), 0, 4, 0, 0));

            if (_c.Season.Released && _c.Pending == null) return ReleasedPanel();
            var setup = U.Link("Race setup", delegate { Show("raceplan"); });
            setup.Margin = new Thickness(24, 0, 0, 0);
            var forfeit = U.Link("Forfeit round", ConfirmForfeit);
            forfeit.Margin = new Thickness(24, 0, 0, 0);
            var details = U.Link("Race details", delegate { Show("briefing"); });
            details.Margin = new Thickness(24, 0, 0, 0);
            var buttons = U.Row(U.Primary(_c.Pending != null ? "Race again" : "Start race", StartRace), details, setup, forfeit);
            var actions = new StackPanel();
            if (_c.Season.OnNotice) actions.Children.Add(U.Margin(U.Body("You're on notice. Team trust has to rise at this race, or your contract will be cancelled.", 13.5, U.Bad, true), 0, 0, 0, 8));
            if (_c.Pending != null) actions.Children.Add(U.Margin(U.Body("You left this round before the flag.", 13.5, U.Warn), 0, 0, 0, 10));
            var cost = Engine.RepairCost(_c);
            if (cost > 0)
            {
                var fix = U.Link("Repair car  /  " + Engine.Cr(cost), delegate { Show("garage"); });
                fix.HorizontalAlignment = HorizontalAlignment.Left;
                actions.Children.Add(U.Body(Engine.DamageSummary(_c) + " will carry into this race unless you repair it.", 13.5, U.Warn, true));
                actions.Children.Add(U.Margin(fix, 0, 6, 0, 12));
            }
            actions.Children.Add(buttons);
            return actions;
        }

        string WeekendLine(Round round) { return Engine.WeekendLine(_c, round); }

        // On or off, plus the length when it's on.
        static FrameworkElement SessionControl(bool on, int minutes, Action<bool> toggle, Action<int> setMinutes)
        {
            return SwitchWithStepper(on, minutes, Engine.MinSession, Engine.MaxSession, 1, "min", toggle, setMinutes);
        }

        static FrameworkElement SwitchWithStepper(bool on, int value, int min, int max, int step, string unit, Action<bool> toggle, Action<int> setValue)
        {
            var onOff = U.Segments(new[] { "On", "Off" }, new[] { true, false }, on, toggle);
            onOff.VerticalAlignment = VerticalAlignment.Center;
            if (!on) return onOff;
            var row = U.Row(U.Stepper(value, min, max, step, unit, setValue, 150), U.Margin(onOff, 16, 0, 0, 0));
            row.VerticalAlignment = VerticalAlignment.Center;
            return row;
        }

        static string ClockText(int seconds)
        {
            return string.Format("{0}:{1:00}", seconds / 3600 > 0 ? (seconds / 3600) + ":" + (seconds % 3600 / 60).ToString("00") : (seconds / 60).ToString(), seconds % 60);
        }

        FrameworkElement RacePlanView()
        {
            var s = _c.Season;
            if (s == null) return PaddockView();
            var round = s.Rounds[s.RoundIndex];
            var timed = Engine.IsTimed(_c, round);

            Action redraw = delegate { Save(); Show("raceplan"); };
            var format = U.Segments(FormatLabels, FormatValues, timed ? "endurance" : "laps", v =>
            {
                if (v == "endurance") { round.CustomMinutes = Engine.MinutesFor(_c, round); round.CustomLaps = 0; }
                else { round.CustomLaps = Engine.LapsFor(_c, round); round.CustomMinutes = 0; }
                redraw();
            });
            var length = timed
                ? U.Stepper(Engine.MinutesFor(_c, round), Engine.MinEndurance, Engine.MaxEndurance, 15, "min", v => { round.CustomMinutes = v; round.CustomLaps = 0; redraw(); })
                : U.Stepper(Engine.LapsFor(_c, round), Engine.MinLaps, Engine.MaxLaps, 1, "laps", v => { round.CustomLaps = v; round.CustomMinutes = 0; redraw(); });

            var page = U.Stack(
                U.Eyebrow(string.Format("Round {0} of {1}  /  {2}", s.RoundIndex + 1, s.Rounds.Count, s.SeriesName)),
                U.Margin(RoundName(round.TrackName, 46), 0, 6, 0, 16),
                U.Rule(),
                SettingRow("Race format", "Applies to this race only.", format),
                SettingRow(timed ? "Race time" : "Race laps", "Applies to this race only.", length),
                SettingRow("Practice", "Applies to this race only. Not scored.", SessionControl(Engine.PracticeMinutesFor(_c, round) > 0, round.PracticeOverride > 0 ? round.PracticeOverride : _c.PracticeMinutes,
                    on => { round.PracticeOverride = on ? _c.PracticeMinutes : -1; redraw(); }, v => { round.PracticeOverride = v; redraw(); })),
                SettingRow("Qualifying", Engine.QualifyingMinutesFor(_c, round) > 0 ? "Applies to this race only. Sets your grid position." : "Applies to this race only. You start from the back of the grid.", SessionControl(Engine.QualifyingMinutesFor(_c, round) > 0, round.QualifyingOverride > 0 ? round.QualifyingOverride : _c.QualifyingMinutes,
                    on => { round.QualifyingOverride = on ? _c.QualifyingMinutes : -1; redraw(); }, v => { round.QualifyingOverride = v; redraw(); })));

            var slotSeconds = Engine.WeatherSlotSeconds(_c, round);
            var slotCount = _env.HasPure ? Engine.WeatherSlotCount : 1;

            var randomise = U.Ghost("Randomise", delegate { round.WeatherSlots = Engine.RandomWeather(new Random(Environment.TickCount)); redraw(); });
            var weatherHint = _env.HasPure ? "Four stages through the race, set up in Pure for you. Practice and qualifying use Stage 1." : "Install Pure for four weather stages, rain and storms. Without it, one weather is used.";
            page.Children.Add(U.Margin(SettingRow("Weather", weatherHint, randomise), 0, 0, 0, 0));

            for (var i = 0; i < slotCount; i++)
            {
                var slot = i;
                var from = ClockText(slot * slotSeconds);
                var to = ClockText((slot + 1) * slotSeconds);
                var definition = Engine.WeatherById(round.WeatherSlots[slot]);
                page.Children.Add(SettingRow("Stage " + (slot + 1), string.Format("{0} to {1}  /  {2}", from, to, definition.Group), U.GroupedDropdown(Engine.WeatherOptions, round.WeatherSlots[slot], v => { round.WeatherSlots[slot] = v; redraw(); })));
            }

            var back = U.Primary("Done", delegate { Show("paddock"); });
            back.HorizontalAlignment = HorizontalAlignment.Left;
            page.Children.Add(U.Margin(back, 0, 22, 0, 0));
            return page;
        }

        void EnsureOffers()
        {
            if (_c.Mode == "ladder" && _c.Offers == null)
            {
                _c.Offers = Engine.MakeOffers(_c, _series, null);
                Save();
            }
        }

        void RepairCar() { RepairPart(null, false); }

        void RepairPart(string part, bool half)
        {
            if (Engine.TryRepair(_c, part, half)) Save();
            if (_view == "repairpart") GoBack();
            else Show(_view);
        }

        // The list of what's wrong. Each row opens that part on its own screen, where the money is spent.
        FrameworkElement GarageRow(DamagePart part)
        {
            var open = U.Ghost("Options", delegate { _repairPart = part.Id; Show("repairpart"); });
            open.VerticalAlignment = VerticalAlignment.Center;
            var price = U.Body("Full repair  " + Engine.Cr(part.Cost), 15, part.Cost <= _c.Credits ? U.Text : U.Bad);
            price.VerticalAlignment = VerticalAlignment.Center;
            var actions = U.Row(U.Margin(price, 0, 0, 22, 0), open);
            actions.VerticalAlignment = VerticalAlignment.Center;

            var row = new DockPanel { Margin = new Thickness(0, 14, 0, 14) };
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            row.Children.Add(U.Stack(U.Display(part.Name, 26), U.Margin(U.Row(U.Eyebrow(part.Severity, U.Warn), U.Margin(U.Body(part.Effect, 14, U.Muted), 14, -2, 0, 0)), 0, 5, 0, 0)));
            return U.Stack(row, U.Rule(0, 0));
        }

        FrameworkElement GarageRows()
        {
            var list = new StackPanel();
            var parts = Engine.DamageParts(_c);
            if (parts.Count == 0) list.Children.Add(U.Margin(U.Display("Your car is in perfect condition", 26), 0, 22, 0, 0));
            for (var i = 0; i < parts.Count; i++)
            {
                var row = GarageRow(parts[i]);
                U.Reveal(row, 60 + i * 80);
                list.Children.Add(row);
            }
            var total = Engine.RepairCost(_c);
            if (parts.Count > 1 && total <= _c.Credits)
            {
                var all = U.Ghost("Repair everything  /  " + Engine.Cr(total), RepairCar);
                all.HorizontalAlignment = HorizontalAlignment.Left;
                list.Children.Add(U.Margin(all, 0, 22, 0, 0));
            }
            return list;
        }

        string CarNameNow() { return _c.Season != null ? _c.Season.CarName : _c.OwnedCarName ?? "Your car"; }

        FrameworkElement GarageHeading(string eyebrow, string title, string hint)
        {
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
            var money = Fact("Your credits", Engine.Cr(_c.Credits), 32, U.Volt);
            DockPanel.SetDock(money, Dock.Right);
            heading.Children.Add(money);
            heading.Children.Add(U.Stack(U.Eyebrow(eyebrow), U.Margin(U.Display(title, 46, null, true), 0, 6, 0, 4), U.Body(hint, 14.5, U.Muted, true)));
            return heading;
        }

        FrameworkElement GarageView()
        {
            var page = U.Stack(GarageHeading("Garage", CarNameNow(), "Pick a part to see what it needs. Whatever you leave stays on the car."), U.Rule(0, 0));
            if (IsRaceRunning())
            {
                page.Children.Add(U.Margin(U.Body("The car is at the track. Repairs are made from the screen in the game.", 15, U.Muted, true), 0, 22, 0, 0));
                return page;
            }
            page.Children.Add(GarageRows());
            var done = U.Primary("Done", delegate { Show("paddock"); });
            done.HorizontalAlignment = HorizontalAlignment.Left;
            page.Children.Add(U.Margin(done, 0, 26, 0, 0));
            return page;
        }

        // One part, what leaving it costs you on track, and what you can do about it.
        FrameworkElement RepairPartView()
        {
            var part = Engine.DamageParts(_c).FirstOrDefault(x => x.Id == _repairPart);
            if (part == null || IsRaceRunning()) return GarageView();

            var options = U.Stack();
            if (part.Cost <= _c.Credits) options.Children.Add(U.Primary("Repair  /  " + Engine.Cr(part.Cost), delegate { RepairPart(part.Id, false); }));
            else options.Children.Add(U.Body("You don't have the " + Engine.Cr(part.Cost) + " for a full repair.", 15, U.Bad, true));
            if (part.HalfCost > 0 && part.HalfCost <= _c.Credits) options.Children.Add(U.Margin(U.Ghost("Repair half  /  " + Engine.Cr(part.HalfCost), delegate { RepairPart(part.Id, true); }), 0, 12, 0, 0));
            options.Children.Add(U.Margin(U.Ghost("Leave it", GoBack), 0, 12, 0, 0));
            foreach (FrameworkElement option in options.Children) option.HorizontalAlignment = HorizontalAlignment.Left;

            var columns = new Grid { Margin = new Thickness(0, 26, 0, 0) };
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
            var about = U.Stack(U.Eyebrow("Condition"), U.Margin(U.Display(part.Severity, 40, U.Warn), 0, 6, 0, 18), U.Eyebrow("On track"), U.Margin(U.Body(part.Effect, 16, U.Text, true), 0, 6, 0, 18), U.Eyebrow("If you leave it"), U.Margin(U.Body(part.IfLeft, 16, U.Text, true), 0, 6, 0, 0));
            Grid.SetColumn(options, 1);
            columns.Children.Add(about);
            columns.Children.Add(options);
            U.Reveal(about, 60);
            U.Reveal(options, 160);
            return U.Stack(GarageHeading(CarNameNow(), part.Name, "Half a repair costs half and takes this part halfway back to perfect."), U.Rule(0, 0), columns);
        }

        // What's wrong with the car and what can be done about it. Null when it's undamaged.
        FrameworkElement DamagePanel()
        {
            var cost = Engine.RepairCost(_c);
            if (cost <= 0) return null;
            var damage = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var actions = U.Row();
            actions.VerticalAlignment = VerticalAlignment.Center;
            actions.Children.Add(U.Margin(U.Link("Choose repairs", delegate { Show("garage"); }), 0, 0, 18, 0));
            if (cost <= _c.Credits) actions.Children.Add(U.Ghost("Repair  /  " + Engine.Cr(cost), RepairCar));
            DockPanel.SetDock(actions, Dock.Right);
            damage.Children.Add(actions);
            damage.Children.Add(U.Body(Engine.DamageSummary(_c) + (cost <= _c.Credits ? " carries over unless repaired." : " carries over. You can't afford the full " + Engine.Cr(cost) + " repair."), 14, U.Warn, true));
            return damage;
        }

        FrameworkElement CreditsPanel(RoundReport r)
        {
            var panel = new StackPanel();
            panel.Children.Add(U.Eyebrow("Credits"));
            panel.Children.Add(U.Rule(8, 6));
            foreach (var line in r.CreditLines)
            {
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var amount = U.Body((line.Amount > 0 ? "+" : "") + Engine.Cr(line.Amount), 14.5, line.Amount >= 0 ? U.Text : U.Bad);
                DockPanel.SetDock(amount, Dock.Right);
                row.Children.Add(amount);
                row.Children.Add(U.Body(line.Label, 14.5, U.Muted));
                panel.Children.Add(row);
            }
            panel.Children.Add(U.Rule(6, 8));
            var total = new DockPanel();
            var balance = U.Display(Engine.Cr(r.CreditsBalance), 22, U.Volt);
            DockPanel.SetDock(balance, Dock.Right);
            total.Children.Add(balance);
            total.Children.Add(U.Display((r.CreditsNet < 0 ? "Lost " + Engine.Cr(-r.CreditsNet) : "Earned " + Engine.Cr(r.CreditsNet)), 22));
            panel.Children.Add(total);
            if (!string.IsNullOrEmpty(r.DebtNote)) panel.Children.Add(U.Margin(U.Body(r.DebtNote, 14, U.Warn, true), 0, 12, 0, 0));
            return panel;
        }

        void ConfirmForfeit()
        {
            if (!Dialog.Confirm(this, "Forfeit this round?", "You'll score no points and your team will lose some trust.", "Forfeit")) return;
            Engine.Forfeit(_c);
            Store.ClearRaceFiles();
            Save();
            Show("debrief");
        }

        void StartRace()
        {
            CloseFinishedRaceWindows();
            _cleanUpUntil = DateTime.MinValue;
            try
            {
                RaceContext context;
                var ini = Engine.BuildRaceIni(_c, _env, out context);
                Store.ClearRaceFiles();
                _gameWasOpen = false;
                _debriefRaceId = null;
                _gameDebrief = null;
                _c.Pending = context;
                Save();
                Store.WriteContext(context);
                if (_env.HasPure)
                {
                    var round = _c.Season.Rounds[_c.Season.RoundIndex];
                    Store.PreparePureWeather(_acRoot, round.WeatherSlots, Engine.WeatherSlotSeconds(_c, round), _env, Engine.WeatherLeadSeconds(_c, round));
                }
                Store.StartRace(_acRoot, ini);
                _focusUntil = DateTime.UtcNow.AddMinutes(3);
                _startingUntil = DateTime.UtcNow.AddSeconds(90);
                _wasRacing = true;
                UpdateMusic();
            }
            catch (Exception ex)
            {
                _notice = "The race couldn't be started: " + ex.Message;
                _c.Pending = null;
                _startingUntil = DateTime.MinValue;
                _wasRacing = false;
                Save();
            }
            Show("paddock");
        }

        // Debrief ----------------------------------------------------------------------------

        static FrameworkElement ClauseLine(string line)
        {
            var met = line.StartsWith("Met: ", StringComparison.Ordinal);
            var missed = line.StartsWith("Missed: ", StringComparison.Ordinal);
            if (!met && !missed) return U.Margin(U.Body(line, 14, U.Muted, true), 0, 0, 0, 9);
            var text = line.Substring(line.IndexOf(": ", StringComparison.Ordinal) + 2);
            return U.Margin(U.Row(U.Margin(U.Icon(met ? U.IconCheck : U.IconCross, 12, met ? U.Good : U.Bad), 0, 2, 12, 0), U.Body(text, 15)), 0, 0, 0, 9);
        }

        static string Signed(int value) { return value > 0 ? "+" + value : value.ToString(); }

        static readonly Dictionary<string, string> StepNames = new Dictionary<string, string> { { "result", "Result" }, { "targets", "Targets" }, { "stewards", "Stewards" }, { "standings", "Standings" }, { "credits", "Credits" }, { "car", "Car" }, { "contract", "Contract" }, { "season", "Season" } };

        void ResetDebrief()
        {
            _debriefStep = 0;
            _debriefHadDamage = _c != null && Engine.RepairCost(_c) > 0;
        }

        List<string> DebriefSteps(RoundReport r)
        {
            var steps = new List<string> { "result", "targets", "standings", "credits" };
            if (r.Incidents != null && r.Incidents.Count > 0) steps.Insert(2, "stewards");
            if (_debriefHadDamage) steps.Add("car");
            if (!string.IsNullOrEmpty(r.ContractTitle)) steps.Add("contract");
            if (r.SeasonOutcome != null) steps.Add("season");
            return steps;
        }

        FrameworkElement StepTrail(List<string> steps)
        {
            var row = U.Row();
            for (var i = 0; i < steps.Count; i++)
                row.Children.Add(U.Margin(U.Eyebrow(StepNames[steps[i]], i == _debriefStep ? U.Volt : i < _debriefStep ? U.Text : U.Faint), 0, 0, 26, 0));
            return row;
        }

        static string LapText(int ms)
        {
            return ms <= 0 ? "--" : string.Format("{0}:{1:00}.{2:000}", ms / 60000, ms / 1000 % 60, ms % 1000);
        }

        FrameworkElement ResultStep(RoundReport r)
        {
            var left = U.Stack(
                U.Margin(RoundName(r.Title.Replace(" at ", " - "), 30), 0, 0, 0, 8),
                U.Row(U.Display(r.Retired ? "DNF" : "P" + r.Pos, 170, r.Retired ? U.Bad : r.Pos == 1 ? U.Volt : U.Text), U.Margin(U.Stack(U.Eyebrow(r.Retired ? "Retired" : "of " + r.Grid), U.Margin(U.Display("+" + r.Points, 40), 0, 6, 0, 0), U.Eyebrow("Points")), 18, 62, 0, 0)));
            var gained = r.StartPos - r.Pos;
            left.Children.Add(U.Margin(Fact("Started", r.Retired && r.StartPos > 0 ? "P" + r.StartPos : r.StartPos > 0 ? string.Format("P{0}  /  {1}", r.StartPos, gained == 0 ? "held" : Signed(gained)) : "-", 26, gained > 0 && r.StartPos > 0 && !r.Retired ? U.Good : U.Text), 0, 10, 0, 0));

            var right = new StackPanel { Margin = new Thickness(30, 4, 0, 0) };
            right.Children.Add(U.Margin(U.Eyebrow("Classification"), 0, 0, 0, 8));
            var rows = r.Classification ?? new List<DebriefRow>();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = TableRow(rows[i].Pos.ToString(), rows[i].Name, "", "", LapText(rows[i].BestMs), false, rows[i].IsPlayer ? U.Volt : U.Text, rows[i].Team, rows[i].IsPlayer, true);
                U.Reveal(row, 80 + i * 45);
                right.Children.Add(row);
            }
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            Grid.SetColumn(right, 1);
            columns.Children.Add(left);
            columns.Children.Add(right);
            U.Reveal(left, 40);
            return columns;
        }

        FrameworkElement TargetsStep(RoundReport r)
        {
            var list = new StackPanel();
            list.Children.Add(U.Margin(U.Eyebrow("Weekend targets"), 0, 0, 0, 12));
            var targets = r.Targets ?? new List<TargetLine>();
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                var money = U.Body(target.Met ? "+" + Engine.Cr(target.Credits) : "Missed", 15, target.Met ? U.Volt : U.Faint);
                DockPanel.SetDock(money, Dock.Right);
                row.Children.Add(money);
                row.Children.Add(U.Row(U.Margin(U.Icon(target.Met ? U.IconCheck : U.IconCross, 12, target.Met ? U.Good : U.Bad), 0, 3, 12, 0), U.Margin(U.Eyebrow(target.Session, U.Muted), 0, 3, 14, 0), U.Body(target.Text, 16)));
                U.Reveal(row, 60 + i * 120);
                list.Children.Add(row);
            }
            foreach (var line in r.Lines.Where(x => !x.StartsWith("Met: ", StringComparison.Ordinal) && !x.StartsWith("Missed: ", StringComparison.Ordinal)))
                list.Children.Add(ClauseLine(line));
            list.Children.Add(U.Margin(Strip(
                Fact("Track-limit cuts", r.Cuts.ToString(), 26, r.Cuts == 0 ? U.Good : U.Text),
                Fact("Contact", r.Contacts.ToString(), 26, r.Contacts == 0 ? U.Good : U.Text),
                Fact("Team trust", Signed(r.TrustDelta), 26, r.TrustDelta >= 0 ? U.Good : U.Bad),
                Fact("Licence points", Signed(r.LicenceDelta), 26, r.LicenceDelta <= 0 ? U.Good : U.Bad)), 0, 14, 0, 0));
            return list;
        }

        // Every contact gets a ruling, so being hit is never treated like doing the hitting.
        FrameworkElement StewardsStep(RoundReport r)
        {
            var page = U.Stack(U.Eyebrow("Stewards' room"), U.Margin(U.Display("Every contact, ruled on", 46, null, true), 0, 6, 0, 18), U.Rule(0, 0));
            for (var i = 0; i < r.Incidents.Count; i++)
            {
                var incident = r.Incidents[i];
                var color = incident.Who == "you" ? U.Bad : incident.Who == "them" ? U.Good : U.Muted;
                var row = new DockPanel { Margin = new Thickness(0, 12, 0, 12) };
                var verdict = U.Body(incident.Verdict, 15, color);
                verdict.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(verdict, Dock.Right);
                row.Children.Add(verdict);
                row.Children.Add(U.Row(U.Margin(U.Eyebrow("Lap " + incident.Lap), 0, 7, 16, 0), U.Display(incident.Text, 21)));
                var line = U.Stack(row, U.Rule(0, 0));
                U.Reveal(line, 60 + i * 110);
                page.Children.Add(line);
            }
            return page;
        }

        FrameworkElement DriverTable(List<Standing> rows)
        {
            var panel = new StackPanel();
            panel.Children.Add(TableRow("Pos", "Driver", "Wins", "Gap", "Pts", true, U.Muted, null, false));
            for (var i = 0; i < rows.Count; i++)
            {
                var row = TableRow((i + 1).ToString(), rows[i].Name, rows[i].Wins.ToString(), i == 0 ? "" : "-" + (rows[0].Points - rows[i].Points), rows[i].Points.ToString(), false, rows[i].IsPlayer ? U.Volt : U.Text, rows[i].Team, rows[i].IsPlayer);
                U.Reveal(row, 60 + i * 45);
                panel.Children.Add(row);
            }
            return panel;
        }

        FrameworkElement TeamTable(List<TeamStanding> rows)
        {
            var panel = new StackPanel();
            panel.Children.Add(TableRow("Pos", "Team", "Wins", "Gap", "Pts", true, U.Muted, null, false));
            for (var i = 0; i < rows.Count; i++)
            {
                var row = TableRow((i + 1).ToString(), rows[i].Name, rows[i].Wins.ToString(), i == 0 ? "" : "-" + (rows[0].Points - rows[i].Points), rows[i].Points.ToString(), false, rows[i].IsPlayer ? U.Volt : U.Text, null, rows[i].IsPlayer);
                U.Reveal(row, 120 + i * 60);
                panel.Children.Add(row);
            }
            return panel;
        }

        FrameworkElement StandingsStep(RoundReport r)
        {
            var left = U.Stack(U.Margin(U.Eyebrow("Drivers"), 0, 0, 0, 8), DriverTable(r.Table ?? new List<Standing>()));
            var right = U.Stack(U.Margin(U.Eyebrow("Teams"), 0, 0, 0, 8), TeamTable(r.TeamTable ?? new List<TeamStanding>()));
            right.Margin = new Thickness(30, 0, 0, 0);
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(right, 1);
            columns.Children.Add(left);
            columns.Children.Add(right);
            var page = U.Stack(columns);
            if (_c.Mode == "ladder" && _c.Season != null) page.Children.Add(U.Margin(PromotionPanel(), 0, 18, 0, 0));
            return page;
        }

        FrameworkElement ContractStep(RoundReport r)
        {
            var page = U.Stack(U.Eyebrow("Your contract"), U.Margin(U.Display(r.ContractTitle, 46, r.Released ? U.Bad : U.Warn, true), 0, 8, 0, 10), U.Body(r.ContractText, 16, U.Text, true));
            if (_c.Season != null) page.Children.Add(U.Margin(Readout("Team trust", _c.Season.Trust, _c.Season.Trust >= 40 ? U.Text : U.Bad), 0, 24, 0, 0));
            return page;
        }

        FrameworkElement SeasonStep(RoundReport r)
        {
            var table = r.Table ?? new List<Standing>();
            var teams = r.TeamTable ?? new List<TeamStanding>();
            var page = U.Stack(U.Eyebrow("Season complete"), U.Margin(U.Display(r.SeasonOutcome, 34, U.Volt, true), 0, 8, 0, 8));
            if (table.Count > 0)
                page.Children.Add(Strip(
                    Fact("Drivers' champion", table[0].Name, 22, table[0].IsPlayer ? U.Volt : U.Text),
                    Fact("Champion's team", table[0].Team ?? "-", 22),
                    Fact("Teams' champion", teams.Count > 0 ? teams[0].Name : "-", 22, teams.Count > 0 && teams[0].IsPlayer ? U.Volt : U.Text),
                    Fact("Your team", teams.Count > 0 ? "P" + (teams.FindIndex(t => t.IsPlayer) + 1) : "-", 22)));
            if (_c.Mode == "ladder") page.Children.Add(U.Margin(ScoreStrip("Final score", r.ScoreTotal, r.ScorePosition, r.ScoreClean, r.ScoreTrust, r.ScoreTotal >= 62 ? "You met the promotion mark" : "Below the promotion mark", r.ScoreTotal >= 62 ? U.Good : U.Text), 0, 22, 0, 0));
            return page;
        }

        // One idea per screen: what happened, what you were asked for, where that leaves you, what it paid, and the car.
        FrameworkElement DebriefView()
        {
            var r = _c == null ? null : _c.LastReport;
            if (r == null) return PaddockView();

            Action leave = delegate { Show("paddock"); };
            if (r.Forfeit)
            {
                var forfeited = U.Stack(U.Eyebrow("Race debrief"), U.Margin(U.Display(r.Title, 52, U.Bad, true), 0, 10, 0, 16));
                foreach (var line in r.Lines) forfeited.Children.Add(ClauseLine(line));
                var damaged = DamagePanel();
                if (damaged != null) forfeited.Children.Add(damaged);
                if (r.SeasonOutcome != null) forfeited.Children.Add(U.Margin(U.Display(r.SeasonOutcome, 26, U.Volt, true), 0, 20, 0, 0));
                forfeited.Children.Add(U.Margin(U.Row(U.Primary("Continue", leave)), 0, 28, 0, 0));
                forfeited.Margin = new Thickness(0, 40, 0, 0);
                return forfeited;
            }

            var steps = DebriefSteps(r);
            _debriefStep = Math.Max(0, Math.Min(_debriefStep, steps.Count - 1));
            var step = steps[_debriefStep];
            FrameworkElement body;
            switch (step)
            {
                case "targets": body = TargetsStep(r); break;
                case "stewards": body = StewardsStep(r); break;
                case "standings": body = StandingsStep(r); break;
                case "credits": body = CreditsPanel(r); break;
                case "car": body = U.Stack(GarageHeading(CarNameNow(), "Your car", "Pick a part to see what it needs. Whatever you leave stays on the car."), U.Rule(0, 0), GarageRows()); break;
                case "contract": body = ContractStep(r); break;
                case "season": body = SeasonStep(r); break;
                default: body = ResultStep(r); break;
            }

            var last = _debriefStep == steps.Count - 1;
            var buttons = U.Row(U.Primary(last ? "Continue" : "Next", delegate
            {
                if (last) leave();
                else { _debriefStep++; _freshStep = true; Show("debrief", false); }
            }));
            if (_debriefStep > 0) buttons.Children.Add(U.Margin(U.Link("Previous", delegate { _debriefStep--; _freshStep = true; Show("debrief", false); }), 24, 0, 0, 0));
            ((FrameworkElement)buttons.Children[buttons.Children.Count - 1]).VerticalAlignment = VerticalAlignment.Center;
            U.Reveal(body, 30);
            return U.Stack(U.Margin(StepTrail(steps), 0, 4, 0, 18), body, U.Margin(buttons, 0, 30, 0, 0));
        }

        // Season -----------------------------------------------------------------------------

        FrameworkElement RoundTile(int index)
        {
            var s = _c.Season;
            var r = s.Rounds[index];
            var current = index == s.RoundIndex;
            string line;
            Brush color = U.Faint;
            if (r.Forfeit) { line = "Forfeit"; color = U.Bad; }
            else if (r.Retired) { line = "Retired"; color = U.Bad; }
            else if (r.Done) { line = string.Format("P{0}  /  {1} pts", r.Pos, r.Points); color = U.Text; }
            else if (current) { line = "Next"; color = U.Volt; }
            else line = "Upcoming";

            var name = r.TrackName;
            var at = name.IndexOf(" - ", StringComparison.Ordinal);
            var title = at > 0 ? name.Substring(0, at) : name;
            return new Border
            {
                Margin = new Thickness(index == 0 ? 0 : 5, 0, index == s.Rounds.Count - 1 ? 0 : 5, 0),
                BorderBrush = current ? U.Volt : Brushes.Transparent,
                BorderThickness = new Thickness(0, 2, 0, 0),
                Child = U.Stack(
                    U.Photo(Installed.TrackImage(_acRoot, r.Track, r.Layout), 92, 330, false),
                    U.Margin(U.Eyebrow("Round " + (index + 1)), 0, 10, 0, 0),
                    U.Margin(U.Display(title, 17), 0, 3, 0, 3),
                    U.Eyebrow(line, color)),
            };
        }

        FrameworkElement StandingsTable()
        {
            var rows = Engine.Standings(_c);
            var leader = rows[0].Points;
            var panel = new StackPanel();
            var header = TableRow("Pos", "Driver", "Wins", "Gap", "Pts", true, U.Muted, null, false);
            panel.Children.Add(header);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var color = r.IsPlayer ? U.Volt : U.Text;
                var gap = i == 0 ? "" : "-" + (leader - r.Points);
                panel.Children.Add(TableRow((i + 1).ToString(), r.Name, r.Wins.ToString(), gap, r.Points.ToString(), false, color, (r.IsTeammate ? "Teammate  /  " : "") + r.Team, r.IsPlayer));
            }
            return panel;
        }

        static FrameworkElement TableRow(string pos, string name, string wins, string gap, string pts, bool header, Brush color, string note, bool highlight, bool wideLast = false)
        {
            var g = new Grid { Height = header ? 30 : 42 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wideLast ? 0 : 58) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wideLast ? 0 : 58) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wideLast ? 110 : 64) });

            Action<UIElement, int> place = (element, col) =>
            {
                ((FrameworkElement)element).VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(element, col);
                g.Children.Add(element);
            };

            if (header)
            {
                place(U.Eyebrow(pos), 0);
                place(U.Eyebrow(name), 1);
                place(U.Eyebrow(wins), 2);
                place(U.Eyebrow(gap), 3);
                place(U.Eyebrow(pts), 4);
            }
            else
            {
                place(U.Display(pos, 16, highlight ? U.Volt : U.Muted), 0);
                var who = U.Row(U.Body(name, 14.5, color));
                if (note != null) who.Children.Add(U.Margin(U.Body(note, 12.5, U.Faint), 12, 1, 0, 0));
                place(who, 1);
                place(U.Display(wins, 15, U.Muted), 2);
                place(U.Display(gap, 15, U.Muted), 3);
                place(U.Display(pts, wideLast ? 15 : 19, color, false, !wideLast), 4);
            }

            return new Border
            {
                Child = g,
                BorderBrush = U.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = highlight ? U.Surface : Brushes.Transparent,
                Padding = new Thickness(highlight ? 8 : 8, 0, 8, 0),
            };
        }

        FrameworkElement SeasonView()
        {
            var s = _c.Season;
            if (s == null) return PaddockView();

            var band = new UniformGrid { Rows = 1, Columns = s.Rounds.Count };
            for (var i = 0; i < s.Rounds.Count; i++) band.Children.Add(RoundTile(i));

            return U.Stack(
                U.Eyebrow(string.Format("{0}  /  {1}", s.Team, s.CarName)),
                U.Margin(U.Display(s.SeriesName, 46, null, true), 0, 6, 0, 22),
                band,
                _c.Mode == "ladder" ? U.Margin(PromotionPanel(), 0, 26, 0, 0) : new Border(),
                U.Margin(U.Eyebrow("Standings"), 0, 30, 0, 8),
                StandingsTable());
        }

        // Contracts and series ---------------------------------------------------------------

        FrameworkElement OfferPanel(Offer o, int index)
        {
            // Columns and gaps are set by the caller, so every photo comes out the same width.
            var photo = U.Photo(Installed.CarImage(_acRoot, o.CarId, o.Skin), 200, 620, true);
            var clauses = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            foreach (var clause in o.Clauses)
                clauses.Children.Add(U.Margin(U.Row(U.Margin(U.Icon(U.IconArrow, 9, U.Volt), 0, 1, 10, 0), U.Body(clause.Text, 14)), 0, 0, 0, 6));

            var sign = U.Primary("View car", delegate { _detailOffer = o; Show("offerdetail"); });
            sign.HorizontalAlignment = HorizontalAlignment.Left;

            var details = U.Stack(
                U.Eyebrow(o.Requested ? "Requested seat  /  " + o.Team : o.Team, U.Volt),
                U.Margin(U.Display(o.CarName, 28, null, true), 0, 6, 0, 16),
                U.Row(U.Margin(Fact("Series", o.SeriesName, 15), 0, 0, 26, 0), Fact("Power to weight", o.Pw > 0 ? string.Format("{0:0} bhp/t", o.Pw) : "Not listed", 15)),
                U.Margin(U.Row(U.Margin(Fact("Your cost", Engine.CostOf(_c, o).Text, 15, Engine.CostOf(_c, o).Loan ? U.Warn : U.Volt), 0, 0, 26, 0), Fact("Left after signing", Engine.Cr(Engine.CreditsAfterSigning(_c, o)), 15)), 0, 14, 0, 0),
                U.Margin(U.Eyebrow("The team wants"), 0, 20, 0, 0),
                clauses,
                U.Margin(sign, 0, 18, 0, 0));
            details.Margin = new Thickness(0, 6, 0, 0);

            var panel = new Border { Background = Brushes.Transparent, Child = U.Stack(photo, details) };
            U.AddHoverZoom(panel, photo);
            U.Reveal(panel, 60 + index * 90);
            return panel;
        }

        static FrameworkElement Spec(string label, string value)
        {
            return U.Margin(Fact(label, string.IsNullOrEmpty(value) ? "Not listed" : value, 19), 0, 0, 0, 16);
        }

        // Second look before any money moves: the car, the deal and what the team expects.
        FrameworkElement OfferDetailView()
        {
            var o = _detailOffer;
            if (o == null || (o != _c.MarketOffer && (_c.Offers == null || !_c.Offers.Contains(o)))) return ContractsView();
            var cost = Engine.CostOf(_c, o);
            var car = _cars.FirstOrDefault(x => x.Id == o.CarId);

            var specs = new UniformGrid { Columns = 3 };
            specs.Children.Add(Spec("Power", car != null && car.Bhp > 0 ? string.Format("{0:0} bhp", car.Bhp) : ""));
            specs.Children.Add(Spec("Weight", car != null && car.Kg > 0 ? string.Format("{0:0} kg", car.Kg) : ""));
            specs.Children.Add(Spec("Power to weight", o.Pw > 0 ? string.Format("{0:0} bhp/t", o.Pw) : ""));
            specs.Children.Add(Spec("Torque", car == null ? "" : car.Torque));
            specs.Children.Add(Spec("Top speed", car == null ? "" : car.TopSpeed));
            specs.Children.Add(Spec("Acceleration", car == null ? "" : car.Acceleration));

            var left = U.Stack(U.Photo(Installed.CarImage(_acRoot, o.CarId, o.Skin), 300, 900, true), U.Margin(specs, 0, 12, 0, 0));
            if (car != null && car.Description != "") left.Children.Add(U.Body(car.Description, 13.5, U.Muted, true));

            Func<string, string, Brush, FrameworkElement> money = (label, value, color) =>
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var amount = U.Body(value, 15, color);
                DockPanel.SetDock(amount, Dock.Right);
                row.Children.Add(amount);
                row.Children.Add(U.Body(label, 15, U.Muted));
                return row;
            };
            var right = new StackPanel { Margin = new Thickness(40, 0, 0, 0) };
            right.Children.Add(U.Eyebrow("The deal"));
            right.Children.Add(U.Rule(8, 10));
            right.Children.Add(money("Your credits", Engine.Cr(_c.Credits), U.Volt));
            if (cost.Owned) right.Children.Add(money("You already own this car", Engine.Cr(0), U.Text));
            else
            {
                right.Children.Add(money("Car price", Engine.Cr(cost.Price), U.Text));
                if (cost.TradeIn > 0) right.Children.Add(money("Trade-in for your current car", "-" + Engine.Cr(cost.TradeIn), U.Good));
                if (cost.SeatFee > 0) right.Children.Add(money("Seat fee", Engine.Cr(cost.SeatFee), U.Text));
                right.Children.Add(money(cost.Loan ? "Team loan: winnings halved" : "You pay", cost.Loan ? Engine.Cr(0) : Engine.Cr(cost.Pay), cost.Loan ? U.Warn : U.Text));
            }
            if (cost.Owned && cost.SeatFee > 0) right.Children.Add(money("Seat fee", Engine.Cr(cost.SeatFee), U.Text));
            if (cost.Buyout > 0) right.Children.Add(money("Buying out your contract", Engine.Cr(cost.Buyout), U.Warn));
            right.Children.Add(money("Left after signing", Engine.Cr(Engine.CreditsAfterSigning(_c, o)), U.Text));
            right.Children.Add(U.Margin(U.Eyebrow("The team wants"), 0, 18, 0, 0));
            right.Children.Add(U.Rule(8, 10));
            foreach (var clause in o.Clauses)
                right.Children.Add(U.Margin(U.Row(U.Margin(U.Icon(U.IconArrow, 10, U.Volt), 0, 1, 10, 0), U.Body(clause.Text, 15)), 0, 0, 0, 7));
            right.Children.Add(U.Margin(U.Eyebrow(string.Format("{0}  /  {1} tier  /  {2} rounds", o.SeriesName, Engine.TierName(o.Tier), Engine.RoundsPerSeason)), 0, 14, 0, 0));
            var sign = U.Primary("Sign contract", delegate { Sign(o); });
            sign.HorizontalAlignment = HorizontalAlignment.Left;
            var back = U.Link("Back to offers", GoBack);
            back.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(U.Margin(U.Row(sign, U.Margin(back, 24, 0, 0, 0)), 0, 22, 0, 0));

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(right, 1);
            columns.Children.Add(left);
            columns.Children.Add(right);
            U.Reveal(left, 40);
            U.Reveal(right, 140);
            return U.Stack(U.Eyebrow(o.Team, U.Volt), U.Margin(U.Display(o.CarName, 46, null, true), 0, 6, 0, 18), columns);
        }

        // The whole weekend on one page, including what it can pay.
        FrameworkElement BriefingView()
        {
            if (_c.Season == null) return PaddockView();
            var b = Engine.BuildBriefing(_c, (track, layout) => Installed.TrackImage(_acRoot, track, layout), _env.HasPure);
            var left = U.Stack(U.Photo(b.Image, 260, 800, true));
            var facts = new UniformGrid { Columns = 3 };
            facts.Children.Add(Spec("Length", b.LengthText));
            facts.Children.Add(Spec("Grid", b.Grid.ToString()));
            facts.Children.Add(Spec("Start", b.StartText));
            left.Children.Add(U.Margin(facts, 0, 12, 0, 0));
            left.Children.Add(U.Eyebrow("Race weekend"));
            left.Children.Add(U.Margin(U.Body(b.Weekend, 15, U.Text, true), 0, 5, 0, 12));
            left.Children.Add(U.Eyebrow("Forecast"));
            left.Children.Add(U.Margin(U.Body(b.Forecast + "  /  " + b.Ambient + "°C", 15, U.Text, true), 0, 5, 0, 0));

            var right = new StackPanel { Margin = new Thickness(40, 0, 0, 0) };
            right.Children.Add(U.Eyebrow("What you can earn"));
            right.Children.Add(U.Rule(8, 10));
            foreach (var target in b.Targets)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
                var amount = U.Body("+" + Engine.Cr(target.Credits), 15, U.Volt);
                DockPanel.SetDock(amount, Dock.Right);
                row.Children.Add(amount);
                row.Children.Add(U.Row(U.Margin(U.Eyebrow(target.Session, U.Muted), 0, 3, 14, 0), U.Body(target.Text, 15)));
                right.Children.Add(row);
            }
            right.Children.Add(U.Rule(4, 10));
            var total = new DockPanel();
            var most = U.Display(Engine.Cr(b.MaxCredits), 24, U.Volt);
            DockPanel.SetDock(most, Dock.Right);
            total.Children.Add(most);
            total.Children.Add(U.Display(b.OnLoan ? "The most you can keep" : "The most you can earn", 20));
            right.Children.Add(total);
            right.Children.Add(U.Margin(U.Body(b.PrizeText, 13.5, U.Muted, true), 0, 12, 0, 4));
            right.Children.Add(U.Body(b.FinesText, 13.5, U.Muted, true));

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(right, 1);
            columns.Children.Add(left);
            columns.Children.Add(right);
            U.Reveal(left, 40);
            U.Reveal(right, 140);

            var page = U.Stack(U.Eyebrow(b.RoundText + "  /  " + b.SeriesName), U.Margin(RoundName(b.TrackName, 46), 0, 6, 0, 18), columns);
            var cost = Engine.RepairCost(_c);
            var actions = U.Row(U.Primary(_c.Pending != null ? "Race again" : "Start race", StartRace));
            if (cost > 0)
            {
                var fix = U.Ghost("Repair car  /  " + Engine.Cr(cost), delegate { Show("garage"); });
                actions.Children.Add(U.Margin(fix, 16, 0, 0, 0));
                page.Children.Add(U.Margin(U.Body(Engine.DamageSummary(_c) + " will carry into this race unless you repair it.", 14, U.Warn, true), 0, 22, 0, 0));
            }
            page.Children.Add(U.Margin(actions, 0, 14, 0, 0));
            return page;
        }

        // Team, teammate and rivals ---------------------------------------------------------------

        FrameworkElement ReleasedPanel()
        {
            var find = U.Primary("Find a new team", delegate { Show("market"); });
            var sit = U.Link("Sit out the season", ConfirmSitOut);
            sit.Margin = new Thickness(24, 0, 0, 0);
            sit.VerticalAlignment = VerticalAlignment.Center;
            return U.Stack(U.Margin(U.Display("Your contract was cancelled", 24, U.Bad, true), 0, 0, 0, 4), U.Margin(U.Body("You need a new seat to race the rest of this season.", 13.5, U.Muted, true), 0, 0, 0, 12), U.Row(find, sit));
        }

        void ConfirmSitOut()
        {
            if (!Dialog.Confirm(this, "Sit out the rest of the season?", "You'll score nothing in the remaining rounds, and you'll look for a new drive next season.", "Sit out")) return;
            Engine.SitOutSeason(_c);
            Save();
            Show("debrief");
        }

        static FrameworkElement CompareLine(string label, string mine, string theirs, Brush mineColor, Brush theirColor, bool header)
        {
            var g = new Grid { Height = header ? 24 : 30 };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            Func<string, Brush, int, UIElement> cell = (text, color, column) =>
            {
                FrameworkElement item = header ? (FrameworkElement)U.Eyebrow(text, color) : U.Body(text, 14.5, color);
                item.VerticalAlignment = VerticalAlignment.Center;
                if (column > 0) item.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(item, column);
                return item;
            };
            g.Children.Add(cell(label, U.Muted, 0));
            g.Children.Add(cell(mine, mineColor, 1));
            g.Children.Add(cell(theirs, theirColor, 2));
            return new Border { Child = g, BorderBrush = U.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        }

        FrameworkElement MatchupCard(Matchup match, int index)
        {
            var tagColor = match.Tag == "Teammate" ? U.Good : match.Tag == "Holds a grudge" || match.Tag == "Your rival" ? U.Bad : U.Volt;
            var card = U.Stack(U.Eyebrow(match.Tag, tagColor), U.Margin(U.Display(match.Name, 26), 0, 4, 0, 2), U.Margin(U.Body(match.Team ?? "", 13.5, U.Muted), 0, 0, 0, 2), U.Margin(U.Body(match.Summary, 14, U.Text, true), 0, 0, 0, 8));
            card.Children.Add(CompareLine("", "You", "Them", U.Text, U.Text, true));
            foreach (var row in match.Rows)
                card.Children.Add(CompareLine(row.Label, row.Mine, row.Theirs, row.Edge > 0 ? U.Volt : U.Text, row.Edge < 0 ? U.Volt : U.Text, false));
            U.Reveal(card, 60 + index * 90);
            return card;
        }

        FrameworkElement TeamView()
        {
            var s = _c.Season;
            if (s == null) return PaddockView();
            var status = s.Released ? "Cancelled" : s.OnNotice ? "On notice" : "Secure";
            var statusColor = s.Released ? U.Bad : s.OnNotice ? U.Warn : U.Good;
            var page = U.Stack(U.Eyebrow("Your team"), U.Margin(U.Display(s.Team, 46, null, true), 0, 6, 0, 2), U.Body(string.Format("{0}  /  {1}", s.CarName, s.SeriesName), 15, U.Muted));
            page.Children.Add(Strip(
                Readout("Team trust", s.Trust, s.Trust >= 40 ? U.Text : U.Bad),
                Fact("Contract", status, 24, statusColor),
                Fact("Reputation", Engine.ReputationOf(_c), 24, Engine.ReputationOf(_c) == "Dirty" ? U.Bad : Engine.ReputationOf(_c) == "Respected" ? U.Good : U.Text),
                Fact("Cost to leave", Engine.Cr(Engine.BuyoutCost(_c)), 24),
                Fact("Your credits", Engine.Cr(_c.Credits), 24, _c.Credits < 0 ? U.Bad : U.Volt)));

            var hint = Engine.ReputationText(_c) + " " + (s.OnNotice ? "Your team is close to letting you go, so leaving costs half." : "Teams are harder to join mid-season, and each can only be asked once.");
            var move = U.Ghost(s.Released ? "Find a new team" : "Look for another team", delegate { Show("market"); });
            move.IsEnabled = _c.Pending == null;
            move.HorizontalAlignment = HorizontalAlignment.Left;
            page.Children.Add(U.Margin(U.Body(hint, 14, U.Muted, true), 0, 18, 0, 8));
            page.Children.Add(move);

            var choices = s.Field.Where(e => !e.IsTeammate).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
            var names = new[] { "No one" }.Concat(choices.Select(e => e.Name + "  /  " + e.Team)).ToArray();
            var picker = U.PlainDropdown(names, choices.FindIndex(e => e.Name == s.ChosenRival) + 1, chosen =>
            {
                Engine.SetRival(_c, chosen == 0 ? null : choices[chosen - 1].Name);
                Save();
                Show("team");
            }, 340);
            var rivalRow = new DockPanel { Margin = new Thickness(0, 22, 0, 0) };
            DockPanel.SetDock(picker, Dock.Right);
            rivalRow.Children.Add(picker);
            rivalRow.Children.Add(U.Stack(U.Display("Your rival", 22), U.Margin(U.Body("They fight you harder when you're close, and beating them in a race pays extra.", 14, U.Muted, true), 0, 3, 20, 0)));
            page.Children.Add(rivalRow);

            var matches = Engine.Matchups(_c);
            var mate = matches.FirstOrDefault(m => m.Tag == "Teammate");
            var rivals = matches.Where(m => m.Tag != "Teammate").ToList();
            var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0, 26, 0, 0) };
            var index = 0;
            if (mate != null) cards.Children.Add(U.Margin(MatchupCard(mate, index++), 0, 0, 30, 28));
            foreach (var rival in rivals) cards.Children.Add(U.Margin(MatchupCard(rival, index++), 0, 0, 30, 28));
            page.Children.Add(U.Margin(U.Eyebrow("Teammate and rivals"), 0, 26, 0, 0));
            page.Children.Add(U.Rule(8, 0));
            page.Children.Add(cards);
            return page;
        }

        void Approach(MarketSeat seat)
        {
            var accepted = Engine.ResolveApproach(_c, _series, seat, Engine.MarketRoll(_c, seat));
            Save();
            Dialog.Tell(this, accepted ? "A team said yes" : "No seat this time", _c.SeatNote);
            if (accepted)
            {
                _detailOffer = _c.MarketOffer;
                Show("offerdetail");
            }
            else Show("market");
        }

        FrameworkElement MarketView()
        {
            var s = _c.Season;
            if (s == null) return PaddockView();
            var buyout = Engine.BuyoutCost(_c);
            var canLeave = _c.Credits >= buyout;
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var money = Fact("Your credits", Engine.Cr(_c.Credits), 32, _c.Credits < 0 ? U.Bad : U.Volt);
            DockPanel.SetDock(money, Dock.Right);
            heading.Children.Add(money);
            var line = s.Released ? "Your old team has let you go, so there's nothing to buy out." : canLeave ? "Leaving " + s.Team + " costs " + Engine.Cr(buyout) + " if you sign elsewhere." : "You can't afford the " + Engine.Cr(buyout) + " to leave " + s.Team + ".";
            heading.Children.Add(U.Stack(U.Eyebrow("Transfer market"), U.Margin(U.Display(s.Released ? "Find a new team" : "Look for another team", 46, null, true), 0, 6, 0, 4), U.Body(line + " Staying in " + s.SeriesName + " keeps your points. Changing series starts a new table for the rounds left.", 14.5, canLeave ? U.Muted : U.Bad, true)));
            var page = U.Stack(heading, U.Rule(0, 0));

            foreach (var seat in Engine.Market(_c, _series))
            {
                var captured = seat;
                var row = new DockPanel { Margin = new Thickness(0, 12, 0, 12) };
                var approach = U.Ghost("Approach", delegate { Approach(captured); });
                approach.IsEnabled = captured.Allowed && canLeave && _c.Pending == null;
                approach.Opacity = approach.IsEnabled ? 1 : 0.35;
                var odds = U.Stack(U.Eyebrow("Odds"), U.Display(captured.Allowed ? captured.Chance + "%" : captured.Asked ? "Asked" : "Closed", 20, captured.Allowed ? U.Text : U.Faint));
                odds.Width = 90;
                var right = U.Row(odds, approach);
                right.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(right, Dock.Right);
                row.Children.Add(right);
                row.Children.Add(U.Stack(U.Display(captured.Name, 21, captured.Allowed ? U.Text : U.Faint), U.Margin(U.Body(string.Format("{0}  /  {1} tier.  {2}", captured.SeriesName, captured.TierName, captured.Reason), 13.5, U.Muted, true), 0, 3, 0, 0)));
                page.Children.Add(U.Stack(row, U.Rule(0, 0)));
            }
            if (s.Released)
            {
                var sit = U.Link("Sit out the season", ConfirmSitOut);
                sit.HorizontalAlignment = HorizontalAlignment.Left;
                page.Children.Add(U.Margin(sit, 0, 22, 0, 0));
            }
            return page;
        }

        FrameworkElement ContractsView()
        {
            EnsureOffers();
            var heading = new DockPanel();
            var money = Fact("Your credits", Engine.Cr(_c.Credits), 32, U.Volt);
            money.Margin = new Thickness(40, 0, 0, 0);
            DockPanel.SetDock(money, Dock.Right);
            heading.Children.Add(money);
            heading.Children.Add(U.Stack(U.Eyebrow("Season " + _c.SeasonNo), U.Margin(U.Display(Engine.OffersTitle(_c), 46, null, true), 0, 6, 0, 4), U.Margin(U.Body(Engine.OffersHint(_c), 14.5, U.Muted), 0, 0, 0, 22)));
            var page = U.Stack(heading);

            if (_c.Offers == null || _c.Offers.Count == 0)
            {
                page.Children.Add(U.Row(U.Primary("Choose a series", delegate { Show("series"); })));
                return page;
            }

            var grid = new Grid();
            for (var i = 0; i < 3; i++)
            {
                if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            for (var i = 0; i < _c.Offers.Count; i++)
            {
                var row = i / 3;
                var column = i % 3;
                if (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var panel = OfferPanel(_c.Offers[i], column);
                panel.Margin = new Thickness(0, 0, 0, 34);
                Grid.SetRow(panel, row);
                Grid.SetColumn(panel, column * 2);
                grid.Children.Add(panel);
                if (column == 0) continue;
                var divider = new Rectangle { Width = 1, Fill = U.Line, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 34) };
                Grid.SetRow(divider, row);
                Grid.SetColumn(divider, column * 2 - 1);
                grid.Children.Add(divider);
            }
            page.Children.Add(grid);

            if (_c.Mode == "ladder") page.Children.Add(SeatRequestPrompt());

            if (_c.Mode == "open")
            {
                var seriesId = _c.Offers[0].SeriesId;
                var addCar = U.Ghost("Add car to this series", delegate { OpenAddCar(seriesId); });
                addCar.HorizontalAlignment = HorizontalAlignment.Left;
                page.Children.Add(U.Margin(addCar, 0, 0, 0, 18));

                var back = U.Link("Back to all series", delegate { _c.Offers = null; Save(); Show("series"); });
                back.HorizontalAlignment = HorizontalAlignment.Left;
                page.Children.Add(U.Margin(back, 0, 24, 0, 0));
            }
            return page;
        }

        FrameworkElement SeatRequestPrompt()
        {
            var text = U.Stack(
                U.Display("Want a different seat?", 22),
                U.Margin(U.Body("Your manager can approach teams in any series. Moving up is earned, so your odds depend on your record, and each team that turns you down makes the next one harder.", 14, U.Muted, true), 0, 4, 0, 0));

            var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            if (_c.SeatNote != null) text.Children.Add(U.Margin(U.Body(_c.SeatNote, 14, U.Volt, true), 0, 8, 0, 0));

            var request = U.Ghost("Request a seat", delegate { Show("seatrequest"); });
            request.VerticalAlignment = VerticalAlignment.Center;
            request.Margin = new Thickness(24, 0, 0, 0);
            DockPanel.SetDock(request, Dock.Right);
            panel.Children.Add(request);
            panel.Children.Add(text);
            return U.Stack(U.Rule(0, 18), panel);
        }

        FrameworkElement SeatRequestView()
        {
            var rating = Engine.DriverRating(_c);
            var licence = Engine.LicenceStatus(_c.LicencePoints);
            var page = U.Stack(
                U.Eyebrow("Season " + _c.SeasonNo),
                U.Margin(U.Display("Request a seat", 46, null, true), 0, 6, 0, 18),
                Strip(Fact("Your tier", Engine.TierName(_c.Tier)), Fact("Driver rating", rating.ToString()), Fact("Licence", licence, 24, licence == "Clear" ? U.Good : U.Bad), Fact("Titles", _c.Titles.ToString())),
                U.Margin(U.Body("Driver rating combines your pace, clean driving, last season's score and titles. One tier up needs 70. Two tiers up needs 80 and last season's title. The bigger the jump, the fewer seats on offer. You can ask each series once an off-season, and every team that turns down a move up takes " + Engine.SeatRefusalPenalty + " points off your odds for the next.", 13.5, U.Muted, true), 0, 14, 0, 12));

            foreach (var group in _series.Where(s => s.Group != Engine.CustomGroup).GroupBy(s => s.Group))
            {
                page.Children.Add(U.Margin(U.Eyebrow(group.Key, U.Text, 12), 0, 14, 0, 0));
                page.Children.Add(U.Rule(8, 0));
                foreach (var series in group.OrderBy(s => s.Tier))
                {
                    var chosen = series;
                    var odds = Engine.SeatRequestOdds(_c, series);
                    FrameworkElement action;
                    if (odds.Allowed) action = U.Ghost("Request", delegate { MakeSeatRequest(chosen, odds); });
                    else action = new Border();
                    action.VerticalAlignment = VerticalAlignment.Center;

                    var chance = U.Stack(U.Eyebrow("Odds"), U.Margin(U.Display(odds.Allowed ? odds.Chance + "%" : odds.Asked ? "Asked" : "Closed", 18, odds.Allowed ? U.Text : U.Faint), 0, 3, 0, 0));
                    chance.Width = 90;
                    chance.VerticalAlignment = VerticalAlignment.Center;

                    var row = new DockPanel { Margin = new Thickness(0, 11, 0, 11) };
                    DockPanel.SetDock(action, Dock.Right);
                    DockPanel.SetDock(chance, Dock.Right);
                    row.Children.Add(action);
                    row.Children.Add(chance);
                    row.Children.Add(U.Stack(U.Display(series.Name, 19), U.Margin(U.Body(Engine.TierName(series.Tier) + " tier.  " + odds.Reason, 13, U.Muted, true), 0, 2, 16, 0)));
                    page.Children.Add(row);
                    page.Children.Add(U.Rule());
                }
            }
            return page;
        }

        void MakeSeatRequest(SeriesDef series, SeatOdds odds)
        {
            if (!Dialog.Confirm(this, "Request a seat in " + series.Name + "?", string.Format("Your odds are {0}%. Your current offers stay whatever the answer, but this team won't be asked twice, and a no makes other moves up harder.", odds.Chance), "Make request")) return;

            var accepted = Engine.ResolveSeatRequest(_c, _series, series, Engine.SeatRoll(_c, series));
            Save();
            Dialog.Tell(this, accepted ? "A team said yes" : "No seat this time", _c.SeatNote);
            Show("contracts");
        }

        void Sign(Offer offer)
        {
            try
            {
                if (offer.MidSeason) Engine.Transfer(_c, offer, _series, _tracks);
                else Engine.StartSeason(_c, offer, _series, _tracks);
                Save();
                Show("paddock");
            }
            catch (InvalidOperationException ex)
            {
                _notice = ex.Message;
                Show(_view);
            }
        }

        FrameworkElement SeriesTile(SeriesDef series)
        {
            var chosen = series;
            var hero = series.Cars[series.Cars.Count / 2];
            var photo = U.Photo(Installed.CarImage(_acRoot, hero.Id, hero.Skins[0]), 152, 480, false);
            var countLabel = series.Cars.Count == 1 ? "One-make" : series.Cars.Count + " cars";
            var text = U.Margin(U.Stack(U.Display(series.Name, 19), U.Margin(U.Eyebrow(Engine.TierName(series.Tier) + "  /  " + countLabel), 0, 4, 0, 0)), 0, 10, 0, 0);

            var tile = new StackPanel { Width = 270, Margin = new Thickness(0, 0, 14, 22), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            tile.Children.Add(photo);
            tile.Children.Add(text);
            tile.MouseLeftButtonUp += delegate { _c.Offers = Engine.MakeOffers(_c, _series, chosen); Save(); Show("contracts"); };
            U.AddHoverZoom(tile, photo);
            return tile;
        }

        FrameworkElement SeriesView()
        {
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
            var create = U.Ghost("Custom series", delegate { Show("customseries"); });
            create.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(create, Dock.Right);
            heading.Children.Add(create);
            heading.Children.Add(U.Stack(U.Eyebrow("Season " + _c.SeasonNo), U.Margin(U.Display("Pick a series", 46, null, true), 0, 6, 0, 0)));
            var page = U.Stack(heading);
            foreach (var group in _series.GroupBy(s => s.Group))
            {
                page.Children.Add(U.Margin(U.Eyebrow(group.Key, U.Text, 12), 0, 6, 0, 0));
                page.Children.Add(U.Rule(8, 12));
                var wrap = new WrapPanel();
                foreach (var series in group) wrap.Children.Add(SeriesTile(series));
                page.Children.Add(U.Margin(wrap, 0, 0, 0, 10));
            }
            return page;
        }

        // Career and settings ----------------------------------------------------------------

        FrameworkElement CareerView()
        {
            var seasons = new StackPanel();
            seasons.Children.Add(U.Eyebrow("Seasons"));
            seasons.Children.Add(U.Rule(8, 0));
            if (_c.History.Count == 0) seasons.Children.Add(U.Margin(U.Body("Finished seasons appear here.", 14, U.Muted), 0, 12, 0, 0));
            foreach (var h in Enumerable.Reverse(_c.History))
            {
                seasons.Children.Add(U.Margin(U.Stack(
                    U.Display(string.Format("P{0}  {1}", h.Rank, h.SeriesName), 20),
                    U.Margin(U.Body(string.Format("Season {0}. {1} with {2}. {3}", h.SeasonNo, h.CarName, h.Team, h.Outcome), 13, U.Muted, true), 0, 4, 0, 0)), 0, 12, 0, 12));
                seasons.Children.Add(U.Rule());
            }

            var rivals = new StackPanel();
            rivals.Children.Add(U.Eyebrow("Rivals"));
            rivals.Children.Add(U.Rule(8, 0));
            foreach (var r in _c.Rivals.OrderByDescending(x => x.Grudge).ThenByDescending(x => x.Titles).ThenByDescending(x => x.Wins).Take(8))
            {
                var angry = r.Grudge > 0 && r.Note != null;
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
                var record = U.Body(string.Format("{0} {1}{2}", r.Wins, r.Wins == 1 ? "win" : "wins", r.Titles > 0 ? ", " + r.Titles + (r.Titles == 1 ? " title" : " titles") : ""), 13, U.Muted);
                DockPanel.SetDock(record, Dock.Right);
                row.Children.Add(record);
                row.Children.Add(U.Stack(U.Body(r.Name, 14.5, angry ? U.Bad : U.Text), angry ? (UIElement)U.Body(r.Note + ". They'll race you harder.", 12.5, U.Muted, true) : new Border()));
                rivals.Children.Add(row);
                rivals.Children.Add(U.Rule());
            }

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(rivals, 2);
            columns.Children.Add(seasons);
            columns.Children.Add(rivals);

            return U.Stack(
                U.Eyebrow(_c.Mode == "ladder" ? "Career ladder" : "Open series"),
                U.Margin(U.Display(_c.Driver, 46, null, true), 0, 6, 0, 22),
                Strip(Fact("Races", _c.Stats.Races.ToString()), Fact("Wins", _c.Stats.Wins.ToString()), Fact("Podiums", _c.Stats.Podiums.ToString()), Fact("Clean races", _c.Stats.CleanRaces.ToString()), Fact("Poles", _c.Stats.Poles.ToString()), Fact("Titles", _c.Titles.ToString()), Fact("Licence points", _c.LicencePoints.ToString())),
                U.Margin(columns, 0, 34, 0, 0));
        }

        static FrameworkElement SettingRow(string label, string hint, UIElement control)
        {
            var dock = new DockPanel { Margin = new Thickness(0, 16, 0, 16) };
            DockPanel.SetDock(control, Dock.Right);
            dock.Children.Add(control);
            dock.Children.Add(U.Stack(U.Display(label, 19), U.Margin(U.Body(hint, 13, U.Muted, true), 0, 3, 24, 0)));
            return U.Stack(dock, U.Rule());
        }

        Action SaveAudioSettings()
        {
            Action save = delegate
            {
                var settings = Store.LoadSettings();
                settings.MusicOn = _musicOn;
                settings.MusicVolume = _musicVolume;
                settings.SoundsOn = _soundsOn;
                settings.SoundsVolume = _soundsVolume;
                Sfx.Configure(_soundsOn, _soundsVolume);
                try { Store.SaveSettings(settings); }
                catch (Exception ex) { _notice = "Your music setting couldn't be saved: " + ex.Message; }
                UpdateMusic();
                Show("settings");
            };
            return save;
        }

        FrameworkElement SettingsView()
        {
            var delete = U.Link("Delete this career", delegate { ConfirmDeleteCareer(_c.Id, _c.Driver); });
            delete.HorizontalAlignment = HorizontalAlignment.Left;

            var tier = Engine.Tiers[(_c.Season != null ? _c.Season.Tier : _c.Tier) - 1];
            var tierNumber = _c.Season != null ? _c.Season.Tier : _c.Tier;
            var ai = ClampAi(Engine.AiBase(_c, tierNumber));
            var reference = ai - _c.BaseDifficulty;
            var open = _c.Mode == "open";
            Action<int> setOffset = v => { _c.BaseDifficulty = v; Save(); Show("settings"); };

            var aiHint = open ? "How quick your rivals are in every series. Individual drivers vary by up to 3%." : string.Format("Your rivals' strength in the {0} tier. It rises 3% with each tier.", tier.Name);
            if (_c.AutoOffset != 0) aiHint += string.Format(" Your results currently make it {0}%.", ClampAi(ai + _c.AutoOffset));

            var endurance = _c.Format == "endurance";
            var lengthHint = endurance ? "How long each race runs. Applies from your next race." : "Laps per race. Circuits over 10 km use fewer. Applies from your next race.";
            if (_c.Season != null && !endurance)
            {
                var round = _c.Season.Rounds[_c.Season.RoundIndex];
                if (round.CustomLaps == 0 && round.CustomMinutes == 0) lengthHint = string.Format("Next race: {0} laps at {1}. Circuits over 10 km use fewer.", Engine.LapsFor(_c, round), round.TrackName);
            }
            var lengthControl = endurance
                ? U.Stepper(_c.EnduranceMinutes, Engine.MinEndurance, Engine.MaxEndurance, 15, "min", v => { _c.EnduranceMinutes = v; Save(); Show("settings"); })
                : U.Stepper(_c.RaceLaps, Engine.MinLaps, Engine.MaxLaps, 1, "laps", v => { _c.RaceLaps = v; Save(); Show("settings"); });

            var adaptHint = "Nudges AI strength up after strong finishes and down after weak ones.";
            if (_c.AutoDifficulty) adaptHint += string.Format(" Current adjustment: {0}%.", Signed(_c.AutoOffset));

            return U.Stack(
                U.Margin(U.Display("Settings", 46, null, true), 0, 6, 0, 14),
                U.Rule(),
                SettingRow("AI strength", aiHint, U.Stepper(ai, 70, 100, 1, "%", v => setOffset(v - reference))),
                SettingRow("Adapt to my results", adaptHint, U.Segments(new[] { "On", "Off" }, new[] { true, false }, _c.AutoDifficulty, v => { _c.AutoDifficulty = v; if (!v) _c.AutoOffset = 0; Save(); Show("settings"); })),
                SettingRow("Race format", "The default for every race. You can change any single race under Race setup.", U.Segments(FormatLabels, FormatValues, _c.Format, v => { _c.Format = v; Save(); Show("settings"); })),
                SettingRow(endurance ? "Race time" : "Race laps", lengthHint, lengthControl),
                SettingRow("Practice", "A free session before qualifying to learn the track. Not scored.", SessionControl(_c.PracticeOn, _c.PracticeMinutes, v => { _c.PracticeOn = v; Save(); Show("settings"); }, v => { _c.PracticeMinutes = v; Save(); Show("settings"); })),
                SettingRow("Qualifying", _c.QualifyingOn ? "Sets your grid position for the race. Not scored." : "Off: you start every race from the back of the grid.", SessionControl(_c.QualifyingOn, _c.QualifyingMinutes, v => { _c.QualifyingOn = v; Save(); Show("settings"); }, v => { _c.QualifyingMinutes = v; Save(); Show("settings"); })),
                SettingRow("Car damage", _c.DamageOn ? "Repairs cost credits, and unrepaired damage carries into the next race." : "No repair bills. Damage is cleared during races and never carries over.", U.Segments(new[] { "On", "Off" }, new[] { true, false }, _c.DamageOn, v => { _c.DamageOn = v; Save(); Show("settings"); })),
                SettingRow("Menu music", "An original lounge track for the launcher and the in-game results screen. It stops when you go racing.", SwitchWithStepper(_musicOn, _musicVolume, 5, 100, 5, "%", v => { _musicOn = v; SaveAudioSettings()(); }, v => { _musicVolume = v; SaveAudioSettings()(); })),
                SettingRow("Interface sounds", "Sounds for hovering, clicking and scrolling, in the launcher and on the in-game results screen.", SwitchWithStepper(_soundsOn, _soundsVolume, 5, 100, 5, "%", v => { _soundsOn = v; SaveAudioSettings()(); }, v => { _soundsVolume = v; SaveAudioSettings()(); })),
                SettingRow("Custom series", "Build your own series from any cars you have installed.", U.Ghost("Manage", delegate { Show("customseries"); })),
                SettingRow("Car classes", "Move any car to a different series if it's been placed wrongly.", U.Ghost("Manage", delegate { Show("cars"); })),
                SettingRow("Assetto Corsa folder", _acRoot, new Border()),
                U.Margin(delete, 0, 24, 0, 0));
        }

        // Custom series -----------------------------------------------------------------------

        void OpenAddCar(string seriesId)
        {
            _addTarget = seriesId;
            _addFilter = "";
            Show("addcar");
        }

        FrameworkElement CustomSeriesView()
        {
            var nameBox = new TextBox { Text = _newSeriesName, Width = 360, FontSize = 20, FontFamily = U.DisplayFont, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Left };
            nameBox.TextChanged += delegate { _newSeriesName = nameBox.Text; };

            var create = U.Primary("Create and add cars", delegate
            {
                var name = string.IsNullOrWhiteSpace(_newSeriesName) ? "My Series" : _newSeriesName.Trim();
                var custom = new CustomSeries { Id = Guid.NewGuid().ToString("N"), Name = name, Tier = _newSeriesTier };
                _edits.Custom.Add(custom);
                Store.SaveSeriesEdits(_edits);
                _newSeriesName = "";
                OpenAddCar("custom:" + custom.Id);
            });

            var page = U.Stack(
                U.Margin(U.Display("Custom series", 46, null, true), 0, 6, 0, 4),
                U.Margin(U.Body("Build a series from any cars you have. Custom series appear when you pick any series, in every career.", 14.5, U.Muted, true), 0, 0, 0, 22),
                U.Eyebrow("Series name"),
                U.Margin(nameBox, 0, 4, 0, 18),
                U.Eyebrow("Grid size"),
                U.Margin(U.Segments(new[] { "10 cars", "12 cars", "14 cars", "16 cars" }, new[] { 1, 2, 3, 4 }, _newSeriesTier, v => { _newSeriesTier = v; Show("customseries"); }), 0, 8, 0, 22),
                U.Row(create));

            if (_edits.Custom.Count > 0)
            {
                page.Children.Add(U.Margin(U.Eyebrow("Your series"), 0, 34, 0, 0));
                page.Children.Add(U.Rule(8, 0));
            }
            foreach (var custom in _edits.Custom)
            {
                var item = custom;
                var count = item.CarIds.Count(id => _cars.Any(x => x.Id == id));
                var actions = U.Row(U.Margin(U.Ghost("Add cars", delegate { OpenAddCar("custom:" + item.Id); }), 0, 0, 22, 0), U.Link("Delete", delegate { ConfirmDeleteSeries(item); }));
                actions.VerticalAlignment = VerticalAlignment.Center;
                var row = new DockPanel { Margin = new Thickness(0, 12, 0, 12) };
                DockPanel.SetDock(actions, Dock.Right);
                row.Children.Add(actions);
                row.Children.Add(U.Stack(U.Display(item.Name, 20), U.Margin(U.Body(count == 0 ? "No cars yet" : count + (count == 1 ? " car" : " cars"), 13, U.Muted), 0, 2, 0, 0)));
                page.Children.Add(row);
                page.Children.Add(U.Rule());
            }
            return page;
        }

        void ConfirmDeleteSeries(CustomSeries series)
        {
            if (!Dialog.Confirm(this, "Delete this series?", "\"" + series.Name + "\" will be removed. Seasons already under way aren't affected.", "Delete")) return;
            _edits.Custom.Remove(series);
            Store.SaveSeriesEdits(_edits);
            RebuildSeries();
            DropStaleOffers();
            Show("customseries");
        }

        CustomSeries TargetCustom()
        {
            if (_addTarget == null || !_addTarget.StartsWith("custom:")) return null;
            var id = _addTarget.Substring("custom:".Length);
            return _edits.Custom.FirstOrDefault(x => x.Id == id);
        }

        string TargetName()
        {
            var custom = TargetCustom();
            if (custom != null) return custom.Name;
            var series = _series.FirstOrDefault(s => s.Id == _addTarget);
            return series != null ? series.Name : "this series";
        }

        FrameworkElement AddCarView()
        {
            var filter = new TextBox { Text = _addFilter, Width = 360, FontSize = 18, FontFamily = U.DisplayFont, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Left };
            var list = new StackPanel();
            filter.TextChanged += delegate { _addFilter = filter.Text; DrawAddRows(list); };
            DrawAddRows(list);

            return U.Stack(
                U.Eyebrow("Add cars"),
                U.Margin(U.Display(TargetName(), 46, null, true), 0, 6, 0, 18),
                U.Eyebrow("Search"),
                U.Margin(filter, 0, 4, 0, 14),
                U.Rule(),
                list);
        }

        bool IsAddedByPlayer(string carId)
        {
            var custom = TargetCustom();
            if (custom != null) return custom.CarIds.Contains(carId);
            List<string> extra;
            return _addTarget != null && _edits.Additions.TryGetValue(_addTarget, out extra) && extra.Contains(carId);
        }

        void SetCarInTarget(string carId, bool add)
        {
            var custom = TargetCustom();
            if (custom != null)
            {
                if (add && !custom.CarIds.Contains(carId)) custom.CarIds.Add(carId);
                if (!add) custom.CarIds.Remove(carId);
            }
            else
            {
                List<string> extra;
                if (!_edits.Additions.TryGetValue(_addTarget, out extra))
                {
                    extra = new List<string>();
                    _edits.Additions[_addTarget] = extra;
                }
                if (add && !extra.Contains(carId)) extra.Add(carId);
                if (!add) extra.Remove(carId);
            }
            Store.SaveSeriesEdits(_edits);
            RebuildSeries();

            // Keeps the car list for this series up to date when going back to it.
            if (_c != null && _c.Mode == "open" && _c.Offers != null && _c.Offers.Count > 0 && _c.Offers[0].SeriesId == _addTarget)
            {
                var series = _series.FirstOrDefault(s => s.Id == _addTarget);
                _c.Offers = series != null ? Engine.MakeOffers(_c, _series, series) : null;
                Save();
            }
        }

        void DrawAddRows(Panel list)
        {
            list.Children.Clear();
            var target = _series.FirstOrDefault(s => s.Id == _addTarget);
            var matches = _cars.Where(x => _addFilter == "" || x.Name.IndexOf(_addFilter, StringComparison.OrdinalIgnoreCase) >= 0 || x.Brand.IndexOf(_addFilter, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var car in matches.Take(MaxCarRows))
            {
                var current = car;
                var inSeries = target != null && target.Cars.Contains(car);
                var added = IsAddedByPlayer(car.Id);
                FrameworkElement action;
                if (added) action = U.Link("Remove", delegate { SetCarInTarget(current.Id, false); DrawAddRows(list); });
                else if (inSeries) action = U.Eyebrow("In this series", U.Faint);
                else action = U.Ghost("Add", delegate { SetCarInTarget(current.Id, true); DrawAddRows(list); });
                action.VerticalAlignment = VerticalAlignment.Center;

                var home = _series.FirstOrDefault(s => s.Group != Engine.CustomGroup && s.Cars.Contains(car));
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
                DockPanel.SetDock(action, Dock.Right);
                row.Children.Add(action);
                row.Children.Add(U.Stack(U.Display(car.Name, 18, inSeries || added ? U.Volt : null), U.Margin(U.Body(home != null ? home.Name : "No series", 13, U.Muted), 0, 2, 0, 0)));
                list.Children.Add(row);
                list.Children.Add(U.Rule());
            }

            if (matches.Count > MaxCarRows)
                list.Children.Add(U.Margin(U.Body(string.Format("Showing {0} of {1} cars. Search to narrow it down.", MaxCarRows, matches.Count), 13.5, U.Muted), 0, 12, 0, 0));
            if (matches.Count == 0)
                list.Children.Add(U.Margin(U.Body("No cars match that search.", 13.5, U.Muted), 0, 12, 0, 0));
        }

        // Car classes -------------------------------------------------------------------------

        const int MaxCarRows = 40;

        FrameworkElement CarClassesView()
        {
            var filter = new TextBox { Text = _carFilter, Width = 360, FontSize = 18, FontFamily = U.DisplayFont, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Left };
            var list = new StackPanel();
            filter.TextChanged += delegate { _carFilter = filter.Text; DrawCarRows(list); };
            DrawCarRows(list);

            return U.Stack(
                U.Eyebrow(_cars.Count + " cars"),
                U.Margin(U.Display("Car classes", 46, null, true), 0, 6, 0, 4),
                U.Margin(U.Body("Changes apply to new seasons. Races already under way keep their grid.", 14.5, U.Muted), 0, 0, 0, 18),
                U.Eyebrow("Search"),
                U.Margin(filter, 0, 4, 0, 14),
                U.Rule(),
                list);
        }

        void DrawCarRows(Panel list)
        {
            list.Children.Clear();
            var options = new List<string> { "Automatic" };
            var keys = new List<string> { null };
            foreach (var definition in Engine.Classes)
            {
                options.Add(definition.Name);
                keys.Add(definition.Key);
            }
            options.Add("Road car, by speed");
            keys.Add("road");
            options.Add("Race car, by speed");
            keys.Add("race");

            var matches = _cars.Where(x => _carFilter == "" || x.Name.IndexOf(_carFilter, StringComparison.OrdinalIgnoreCase) >= 0 || x.Brand.IndexOf(_carFilter, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var car in matches.Take(MaxCarRows))
            {
                var current = car;
                string chosen;
                var index = _classOverrides.TryGetValue(car.Id, out chosen) ? Math.Max(0, keys.IndexOf(chosen)) : 0;
                var series = _series.FirstOrDefault(s => s.Cars.Contains(car));
                var dropdown = U.PlainDropdown(options.ToArray(), index, v =>
                {
                    if (keys[v] == null) _classOverrides.Remove(current.Id);
                    else _classOverrides[current.Id] = keys[v];
                    Store.SaveClassOverrides(_classOverrides);
                    RebuildSeries();
                    DropStaleOffers();
                    DrawCarRows(list);
                });
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
                DockPanel.SetDock(dropdown, Dock.Right);
                row.Children.Add(dropdown);
                row.Children.Add(U.Stack(U.Display(car.Name, 18), U.Margin(U.Body(series != null ? series.Name : "No series", 13, U.Muted), 0, 2, 0, 0)));
                list.Children.Add(row);
                list.Children.Add(U.Rule());
            }

            if (matches.Count > MaxCarRows)
                list.Children.Add(U.Margin(U.Body(string.Format("Showing {0} of {1} cars. Search to narrow it down.", MaxCarRows, matches.Count), 13.5, U.Muted), 0, 12, 0, 0));
            if (matches.Count == 0)
                list.Children.Add(U.Margin(U.Body("No cars match that search.", 13.5, U.Muted), 0, 12, 0, 0));
        }

#if SELFTEST
        public void UseCareerForTest(Career career) { _c = career; }
        public string CurrentView { get { return _view; } }
        public void BackForTest() { GoBack(); }
        public void PollForTest() { Poll(); }
        public bool MusicWantedForTest { get { return MusicWanted(); } }
        public void StartRaceForTest() { StartRace(); }
        public Career CareerForTest { get { return _c; } }
        public string NoticeForTest { get { return _notice; } }
        public void AddTargetForTest(string id) { _addTarget = id; }
        public void AddEditsForTest(SeriesEdits edits) { _edits = edits; RebuildSeries(); }
#endif
    }
}
