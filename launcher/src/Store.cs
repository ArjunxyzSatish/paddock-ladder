using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace PaddockLadder
{
    public class LauncherSettings
    {
        public string AcRoot, ActiveCareer;
        public bool MusicOn = true;
        public int MusicVolume = 35;
        public bool SoundsOn = true;
        public int SoundsVolume = 60;
    }

    public static class Store
    {
        public static string DocsOverride;
#if SELFTEST
        public static Action LaunchForTest;
#endif

        static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        }

        public static string DataDir
        {
            get
            {
                var docs = DocsOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa");
                return Path.Combine(docs, "paddock_ladder");
            }
        }

        const string ImportedCareerId = "imported";

        static string LegacyCareerPath { get { return Path.Combine(DataDir, "career.json"); } }
        static string CareersDir { get { return Path.Combine(DataDir, "careers"); } }
        static string DeletedDir { get { return Path.Combine(CareersDir, "deleted"); } }
        public static string PathFor(string id) { return Path.Combine(CareersDir, id + ".json"); }
        public static string ContextPath { get { return Path.Combine(DataDir, "race_context.json"); } }
        public static string ResultPath { get { return Path.Combine(DataDir, "race_result.json"); } }
        public static string ProgressPath { get { return Path.Combine(DataDir, "race_progress.json"); } }
        public static string DebriefPath { get { return Path.Combine(DataDir, "race_debrief.json"); } }
        public static string CommandPath { get { return Path.Combine(DataDir, "race_command.json"); } }
        public static string RaceIniPath { get { return Path.Combine(DataDir, "next_race.ini"); } }
        static string SettingsPath { get { return Path.Combine(DataDir, "launcher.json"); } }

        static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static string ToJson(object value) { return Json().Serialize(value); }
        public static T FromJson<T>(string text) { return Json().Deserialize<T>(text); }

        // Saves from before career slots existed move into a slot. The fixed id means a save written
        // later by an older copy of the launcher updates that slot instead of creating a duplicate.
        public static string MigrateLegacyCareer()
        {
            if (!File.Exists(LegacyCareerPath)) return null;
            try
            {
                var target = PathFor(ImportedCareerId);
                if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(LegacyCareerPath))
                {
                    RetireLegacyCareer();
                    return null;
                }

                var career = FromJson<Career>(File.ReadAllText(LegacyCareerPath));
                career.Id = ImportedCareerId;
                SaveCareer(career);
                RetireLegacyCareer();

                var settings = LoadSettings();
                settings.ActiveCareer = ImportedCareerId;
                SaveSettings(settings);
                return null;
            }
            catch (Exception ex)
            {
                return "Your earlier career couldn't be moved into a career slot: " + ex.Message;
            }
        }

        static void RetireLegacyCareer()
        {
            var keep = LegacyCareerPath + ".migrated";
            if (File.Exists(keep)) File.Delete(keep);
            File.Move(LegacyCareerPath, keep);
        }

        // A damaged save is kept to one side instead of being overwritten.
        public static Career LoadCareer(string id, out string problem)
        {
            problem = null;
            var path = PathFor(id);
            if (!File.Exists(path)) return null;
            try
            {
                var career = FromJson<Career>(File.ReadAllText(path));
                career.Id = id;
                Engine.EnsureRaceSetup(career);
                Engine.AdoptOwnedCar(career);
                Engine.AdoptTeams(career);
                return career;
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException)
                {
                    problem = "That career couldn't be opened: " + ex.Message;
                    return null;
                }
                File.Copy(path, path + ".unreadable", true);
                problem = "That career save was damaged. A copy was kept next to it as .unreadable.";
                return null;
            }
        }

        public static void SaveCareer(Career career) { WriteAtomic(PathFor(career.Id), ToJson(career)); }

        static string ClassOverridesPath { get { return Path.Combine(DataDir, "car_classes.json"); } }

        public static Dictionary<string, string> LoadClassOverrides()
        {
            try
            {
                if (File.Exists(ClassOverridesPath)) return FromJson<Dictionary<string, string>>(File.ReadAllText(ClassOverridesPath));
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
            }
            return new Dictionary<string, string>();
        }

        public static void SaveClassOverrides(Dictionary<string, string> overrides) { WriteAtomic(ClassOverridesPath, ToJson(overrides)); }

        static string SeriesEditsPath { get { return Path.Combine(DataDir, "custom_series.json"); } }

        public static SeriesEdits LoadSeriesEdits()
        {
            try
            {
                if (File.Exists(SeriesEditsPath)) return FromJson<SeriesEdits>(File.ReadAllText(SeriesEditsPath)) ?? new SeriesEdits();
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
            }
            return new SeriesEdits();
        }

        public static void SaveSeriesEdits(SeriesEdits edits) { WriteAtomic(SeriesEditsPath, ToJson(edits)); }

        public static void SetActiveCareer(string id)
        {
            var settings = LoadSettings();
            settings.ActiveCareer = id;
            SaveSettings(settings);
        }

        public static List<CareerSummary> ListCareers()
        {
            var result = new List<CareerSummary>();
            if (!Directory.Exists(CareersDir)) return result;
            foreach (var path in Directory.GetFiles(CareersDir, "*.json"))
            {
                try
                {
                    var c = FromJson<Career>(File.ReadAllText(path));
                    var offer = c.Offers != null && c.Offers.Count > 0 ? c.Offers[0] : null;
                    result.Add(new CareerSummary
                    {
                        Id = Path.GetFileNameWithoutExtension(path),
                        Driver = c.Driver,
                        Mode = c.Mode,
                        Tier = c.Season != null ? c.Season.Tier : c.Tier,
                        SeasonNo = c.SeasonNo,
                        CarId = c.Season != null ? c.Season.CarId : offer != null ? offer.CarId : null,
                        Skin = c.Season != null ? c.Season.Skin : offer != null ? offer.Skin : null,
                        Status = c.Season == null ? "Between seasons"
                            : c.Season.SeriesName + ", round " + (c.Season.RoundIndex + 1) + " of " + c.Season.Rounds.Count + (c.Pending != null ? " (unfinished)" : ""),
                        LastPlayed = File.GetLastWriteTime(path),
                    });
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException) throw;
                }
            }
            return result.OrderByDescending(s => s.LastPlayed).ToList();
        }

        // The last career used, or the most recent one if that's gone.
        public static Career LoadActiveCareer(out string problem)
        {
            problem = null;
            var active = LoadSettings().ActiveCareer;
            if (!string.IsNullOrEmpty(active) && File.Exists(PathFor(active))) return LoadCareer(active, out problem);
            var newest = ListCareers().FirstOrDefault();
            return newest == null ? null : LoadCareer(newest.Id, out problem);
        }

        // Permanent: once confirmed, nothing belonging to the career stays on disk.
        public static void DeleteCareer(string id)
        {
            var path = PathFor(id);
            foreach (var file in new[] { path, path + ".unreadable" })
                if (File.Exists(file)) File.Delete(file);
        }

        // Earlier versions kept deleted careers in a folder so they could be restored.
        public static void RemoveOldDeletedCareers()
        {
            if (Directory.Exists(DeletedDir)) Directory.Delete(DeletedDir, true);
        }

        public static Career FindCareerAwaiting(string raceId)
        {
            foreach (var summary in ListCareers())
            {
                string problem;
                var career = LoadCareer(summary.Id, out problem);
                if (career != null && career.Pending != null && career.Pending.RaceId == raceId) return career;
            }
            return null;
        }

        public static void ClearResult()
        {
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
        }

        public static void WriteContext(RaceContext context) { WriteAtomic(ContextPath, ToJson(context)); }
        public static void WriteRaceIni(string ini) { WriteAtomic(RaceIniPath, ini); }
        public static void WriteDebrief(GameDebrief debrief) { WriteAtomic(DebriefPath, ToJson(debrief)); }

        public static RaceCommand ReadCommand()
        {
            if (!File.Exists(CommandPath)) return null;
            return FromJson<RaceCommand>(File.ReadAllText(CommandPath));
        }

        public static void ClearCommand()
        {
            if (File.Exists(CommandPath)) File.Delete(CommandPath);
        }

#if SELFTEST
        public static Func<bool> GameOpenForTest;
#endif

        public static bool GameOpenForTestIsOff
        {
            get
            {
#if SELFTEST
                return GameOpenForTest == null;
#else
                return true;
#endif
            }
        }

        public static bool IsGameOpen()
        {
#if SELFTEST
            if (GameOpenForTest != null) return GameOpenForTest();
#endif
            return Process.GetProcessesByName("acs").Length > 0;
        }

        public static RaceResult ReadResult()
        {
            if (!File.Exists(ResultPath)) return null;
            return FromJson<RaceResult>(File.ReadAllText(ResultPath));
        }

        delegate bool WindowVisitor(IntPtr window, IntPtr data);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visit, IntPtr data);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        static readonly HashSet<string> FinishedRaceTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Race", "Cancelled", "Practice", "Qualifying", "Qualification" };

        // Content Manager leaves a results window behind after every race. Only call this with no race starting,
        // because it can't tell a finished window from one that's still waiting on a game by title alone.
        public static int CloseFinishedRaceWindows()
        {
            var manager = Process.GetProcessesByName("Content Manager").Concat(Process.GetProcessesByName("AcManager")).Select(p => (uint)p.Id).ToList();
            if (manager.Count == 0) return 0;

            var owners = new Dictionary<IntPtr, IntPtr>();
            EnumWindows((window, data) =>
            {
                uint process;
                GetWindowThreadProcessId(window, out process);
                if (!manager.Contains(process) || !IsWindowVisible(window)) return true;
                var owner = GetWindow(window, 4);
                if (owner == IntPtr.Zero) return true;
                var title = new System.Text.StringBuilder(64);
                GetWindowText(window, title, 64);
                if (FinishedRaceTitles.Contains(title.ToString().Trim())) owners[window] = owner;
                return true;
            }, IntPtr.Zero);

            // Each window is owned by the one before, so the newest has to close first.
            Func<IntPtr, int> depth = window =>
            {
                var levels = 0;
                IntPtr owner;
                while (owners.TryGetValue(window, out owner)) { window = owner; levels++; }
                return levels;
            };
            foreach (var window in owners.Keys.OrderByDescending(depth).ToList())
            {
                IntPtr result;
                SendMessageTimeout(window, 0x10, IntPtr.Zero, IntPtr.Zero, 2, 1500, out result);
            }
            return owners.Count;
        }

        // After the quick shutdown between races, Content Manager can ask whether the game crashed.
        // That question sits in front of the next race and stops it loading, so it's answered with no.
        public static void DismissCrashPrompt()
        {
            var manager = Process.GetProcessesByName("Content Manager").Concat(Process.GetProcessesByName("AcManager")).Select(p => (uint)p.Id).ToList();
            if (manager.Count == 0) return;
            var prompts = new List<IntPtr>();
            EnumWindows((window, data) =>
            {
                uint process;
                GetWindowThreadProcessId(window, out process);
                if (!manager.Contains(process) || !IsWindowVisible(window)) return true;
                var title = new System.Text.StringBuilder(64);
                GetWindowText(window, title, 64);
                if (title.ToString().IndexOf("just crash", StringComparison.OrdinalIgnoreCase) >= 0) prompts.Add(window);
                return true;
            }, IntPtr.Zero);
            foreach (var window in prompts)
            {
                IntPtr result;
                SendMessageTimeout(window, 0x10, IntPtr.Zero, IntPtr.Zero, 2, 1500, out result);
            }
        }

        public static RaceResult ReadProgress()
        {
            if (!File.Exists(ProgressPath)) return null;
            return FromJson<RaceResult>(File.ReadAllText(ProgressPath));
        }

        public static void ClearProgress()
        {
            if (File.Exists(ProgressPath)) File.Delete(ProgressPath);
        }

        public static void ClearRaceFiles()
        {
            foreach (var path in new[] { ContextPath, ResultPath, RaceIniPath, DebriefPath, CommandPath, ProgressPath })
                if (File.Exists(path)) File.Delete(path);
        }

        public static LauncherSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath)) return FromJson<LauncherSettings>(File.ReadAllText(SettingsPath));
            }
            catch (Exception) { }
            return new LauncherSettings();
        }

        public static void SaveSettings(LauncherSettings settings) { WriteAtomic(SettingsPath, ToJson(settings)); }

        public static bool IsAcRoot(string path)
        {
            return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "acs.exe")) && Directory.Exists(Path.Combine(path, "content", "cars"));
        }

        static string ContentManagerExe()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\acmanager\shell\open\command"))
            {
                var command = key == null ? null : key.GetValue(null) as string;
                if (string.IsNullOrEmpty(command)) return null;
                var trimmed = command.Trim();
                if (trimmed.StartsWith("\"")) return trimmed.Substring(1, trimmed.IndexOf('"', 1) - 1);
                return trimmed.Split(' ')[0];
            }
        }

        public static bool HasContentManager() { return ContentManagerExe() != null; }

        static IEnumerable<string> Parents(string start)
        {
            var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
            while (dir != null)
            {
                yield return dir.FullName;
                dir = dir.Parent;
            }
        }

        static IEnumerable<string> SteamCandidates()
        {
            string steam = null;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (key != null) steam = key.GetValue("SteamPath") as string;
            if (steam == null) yield break;
            yield return Path.Combine(steam, "steamapps", "common", "assettocorsa");

            var library = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(library)) yield break;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(library), "\"path\"\\s+\"([^\"]+)\""))
                yield return Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps", "common", "assettocorsa");
        }

        public static string FindAcRoot()
        {
            var saved = LoadSettings().AcRoot;
            if (IsAcRoot(saved)) return saved;

            var candidates = new List<string>();
            candidates.AddRange(Parents(AppDomain.CurrentDomain.BaseDirectory));
            var cm = ContentManagerExe();
            if (cm != null) candidates.AddRange(Parents(Path.GetDirectoryName(cm)));
            try { candidates.AddRange(SteamCandidates()); }
            catch (Exception) { }
            return candidates.FirstOrDefault(IsAcRoot);
        }

        // Pure -----------------------------------------------------------------------------------

        const string PurePlanName = "Timed/PaddockLadder";

        static string DocsDir { get { return Path.GetDirectoryName(DataDir); } }
        static string PureSettingsPath { get { return Path.Combine(DocsDir, "cfg", "extension", "state", "lua", "wfx_controller", "pureCtrl__settings.ini"); } }
        static string PureBackupPath { get { return Path.Combine(DataDir, "pure_settings.backup"); } }

        // Pure's values are read from the player's own install, so nothing from Pure ships with this mod.
        public static Dictionary<int, double[]> ReadPurePresets(string acRoot)
        {
            var presets = new Dictionary<int, double[]>();
            var file = Path.Combine(acRoot, "apps", "lua", "PurePlanner", "settings", "settings_defaults.lua");
            if (!File.Exists(file)) return presets;
            var pattern = new System.Text.RegularExpressions.Regex("^\\s*\\{\\s*\"\\w+\"\\s*,\\s*(\\d+)\\s*,\\s*([-\\d.]+)\\s*,\\s*([-\\d.]+)\\s*,\\s*([-\\d.]+)\\s*,\\s*([-\\d.]+)\\s*,\\s*([-\\d.]+)\\s*,\\s*([-\\d.]+)\\s*,\\s*([-\\d.]+)\\s*,\\s*([-\\d.]+)");
            foreach (var line in File.ReadAllLines(file))
            {
                var m = pattern.Match(line);
                if (!m.Success) continue;
                var values = new double[8];
                for (var i = 0; i < 8; i++) values[i] = double.Parse(m.Groups[i + 2].Value, System.Globalization.CultureInfo.InvariantCulture);
                presets[int.Parse(m.Groups[1].Value)] = values;
            }
            return presets;
        }

        public static RaceEnv ReadEnvironment(string acRoot)
        {
            var env = new RaceEnv();
            var weatherDir = Path.Combine(acRoot, "content", "weather");
            if (Directory.Exists(weatherDir))
                foreach (var dir in Directory.GetDirectories(weatherDir)) env.InstalledWeathers.Add(Path.GetFileName(dir));
            env.Presets = ReadPurePresets(acRoot);
            env.HasPure = env.Presets.Count > 0 && File.Exists(Path.Combine(acRoot, "extension", "weather-controllers", "pureCtrl", "controller.lua"));
            return env;
        }

        // A timed plan: each slot runs in real time and Pure blends from one to the next.
        public static string BuildPurePlan(IList<int> slots, int slotSeconds, Dictionary<int, double[]> presets, int leadSeconds = 0)
        {
            var containers = new List<object>();
            long timestamp = 1794096000;
            foreach (var id in slots)
            {
                double[] p;
                if (!presets.TryGetValue(id, out p)) p = new double[8];
                var weather = new Dictionary<string, object>
                {
                    { "index", id }, { "rain_amount", p[0] }, { "rain_probability", p[1] }, { "rain_variance", p[2] }, { "rain_wetness", p[3] }, { "rain_water", p[4] },
                    { "wind_strength", p[5] }, { "wind_direction", 0 }, { "humidity", p[6] }, { "mist", p[7] }, { "temp_air", 0 }, { "temp_road", 0 },
                    { "trans_auto", true }, { "trans_look_A", 0.5 }, { "trans_look_B", 1 }, { "trans_data_A", 0.5 }, { "trans_data_B", 1 },
                    { "temp_air_dyn", true }, { "temp_road_dyn", true }, { "mist_dyn", false }, { "humidity_dyn", false },
                    { "rain_water_dyn", false }, { "rain_wetness_dyn", false }, { "rain_amount_dyn", false },
                };
                foreach (var key in new[] { "rain_amount", "rain_probability", "rain_variance", "rain_wetness", "rain_water", "wind_strength", "wind_direction", "humidity", "mist", "temp_air", "temp_road" })
                    weather[key + "_range"] = 0;
                // The first stage also covers practice and qualifying.
                var duration = containers.Count == 0 ? slotSeconds + leadSeconds : slotSeconds;
                containers.Add(new Dictionary<string, object> { { "data", new Dictionary<string, object> { { "timestamp", timestamp }, { "duration", duration }, { "weather", weather } } } });
                timestamp += duration;
            }
            return ToJson(new Dictionary<string, object>
            {
                { "control", new Dictionary<string, object> { { "type", 2 }, { "timemulti", 1 }, { "loop", false } } },
                { "container", containers },
            });
        }

        // Writes this race's plan and points Pure at it. The player's own Pure settings are kept and put back.
        public static void PreparePureWeather(string acRoot, IList<int> slots, int slotSeconds, RaceEnv env, int leadSeconds = 0)
        {
            var plan = Path.Combine(acRoot, "extension", "config-ext", "PurePlanner", "Plans", "Timed", "PaddockLadder.json");
            WriteAtomic(plan, BuildPurePlan(slots, slotSeconds, env.Presets, leadSeconds));

            Directory.CreateDirectory(DataDir);
            if (!File.Exists(PureBackupPath))
                File.WriteAllText(PureBackupPath, File.Exists(PureSettingsPath) ? File.ReadAllText(PureSettingsPath) : "", new UTF8Encoding(false));

            WriteAtomic(PureSettingsPath, "[SETTINGS]\nLIVE=0\nLAST_USED=0\nPLAN=" + PurePlanName + "\nAUTOSTART=1\nCM_WEATHER=36\nSTART_WETNESS=1\nSTART_PUDDLES=1\n");
        }

        public static void RestorePureSettings()
        {
            if (!File.Exists(PureBackupPath)) return;
            if (IsGameOpen()) return;
            WriteAtomic(PureSettingsPath, File.ReadAllText(PureBackupPath));
            File.Delete(PureBackupPath);
        }

        // Content Manager starts the race when it's installed, so players keep their usual settings.
        public static void StartRace(string acRoot, string ini)
        {
            WriteRaceIni(ini);
#if SELFTEST
            if (LaunchForTest != null)
            {
                LaunchForTest();
                return;
            }
#endif
            if (HasContentManager())
            {
                Process.Start("acmanager://race/config?configFile=" + Uri.EscapeDataString(RaceIniPath));
                return;
            }

            var docs = Path.GetDirectoryName(DataDir);
            File.WriteAllText(Path.Combine(docs, "cfg", "race.ini"), ini, new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(Path.Combine(acRoot, "acs.exe")) { WorkingDirectory = acRoot, UseShellExecute = true });
        }

        // The launcher carries the in-game app, so there's only one thing to download and install.
        public static void InstallGameApp(string acRoot)
        {
            var target = Path.Combine(acRoot, "apps", "lua", "PaddockLadder");
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("game/")))
            {
                var path = Path.Combine(target, name.Substring("game/".Length).Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = assembly.GetManifestResourceStream(name))
                using (var memory = new MemoryStream())
                {
                    input.CopyTo(memory);
                    var bytes = memory.ToArray();
                    if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
                }
            }
            foreach (var old in new[] { "career.lua", "content.lua", "raceini.lua" })
            {
                var path = Path.Combine(target, "src", old);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
