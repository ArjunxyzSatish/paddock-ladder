using System;
using System.IO;
using System.Windows;

namespace PaddockLadder
{
    public static class Program
    {
        static string AskForFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select your Assetto Corsa folder (the one that contains acs.exe).";
                while (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (Store.IsAcRoot(dialog.SelectedPath)) return dialog.SelectedPath;
                    Dialog.Tell(null, "That isn't the game folder", "That folder doesn't contain Assetto Corsa. Choose the one with acs.exe in it.");
                }
            }
            return null;
        }

        [STAThread]
        public static int Main(string[] args)
        {
#if SELFTEST
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run(args);
            if (args.Length > 0 && args[0] == "--parse-result") return SelfTest.ParseResult(args[1]);
            if (args.Length > 0 && args[0] == "--live-weather") return SelfTest.LiveWeather(args[1]);
            if (args.Length > 0 && args[0] == "--show-dropdown") return SelfTest.ShowDropdown(args[1]);
            if (args.Length > 0 && args[0] == "--make-test-career") return SelfTest.MakeTestCareer(args[1], args.Length > 2 ? int.Parse(args[2]) : 1);
            if (args.Length > 0 && args[0] == "--migrate-copy") return SelfTest.MigrateCopy(args[1]);
            if (args.Length > 0 && args[0] == "--game-test-setup") return SelfTest.GameTestSetup(args[1], args[2]);
            if (args.Length > 0 && args[0] == "--music-check") return SelfTest.MusicCheck(args[1]);
            if (args.Length > 0 && args[0] == "--finish-flow-setup") return SelfTest.FinishFlowSetup(args[1]);
            if (args.Length > 0 && args[0] == "--weekend-flow-setup") return SelfTest.WeekendFlowSetup(args[1]);
            if (args.Length > 0 && args[0] == "--trailer-setup") return SelfTest.TrailerSetup(args[1], args.Length > 2);
            if (args.Length > 0 && args[0].StartsWith("--") && args[0] != "--auto-start-race")
            {
                Console.WriteLine("Unknown test command: " + args[0]);
                return 2;
            }
#endif
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            U.ApplyTheme(app);

            var acRoot = Store.FindAcRoot() ?? AskForFolder();
            if (acRoot == null) return 1;

            string notice = null;
            try
            {
                var settings = Store.LoadSettings();
                settings.AcRoot = acRoot;
                Store.SaveSettings(settings);
                notice = Store.MigrateLegacyCareer();
                Store.InstallGameApp(acRoot);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                notice = "The in-game app couldn't be installed, so races won't be scored: " + ex.Message;
            }

            try { Store.RemoveOldDeletedCareers(); }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
            }

            if (!Directory.Exists(Path.Combine(acRoot, "extension")))
                notice = "Custom Shaders Patch wasn't found. Install it in Content Manager, or races won't be scored.";

            var main = new MainWindow(acRoot, notice);
            app.MainWindow = main;
#if SELFTEST
            if (args.Length > 0 && args[0] == "--auto-start-race") main.Loaded += delegate { main.StartRaceForTest(); };
#endif
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            return app.Run(main);
        }
    }
}
