using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PaddockLadder
{
    // Kunos and mod JSON often has raw line breaks or missing commas, so fields are read with patterns.
    public static class Content
    {
        static string Field(string text, string key)
        {
            var m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static List<string> Tags(string text)
        {
            var block = Regex.Match(text, "\"tags\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            var tags = new List<string>();
            if (block.Success)
                foreach (Match m in Regex.Matches(block.Groups[1].Value, "\"([^\"]*)\""))
                    tags.Add(m.Groups[1].Value.ToLowerInvariant());
            return tags;
        }

        public static double? FirstNumber(string s)
        {
            if (s == null) return null;
            var m = Regex.Match(s.Replace(",", ""), @"\d+(\.\d+)?");
            if (!m.Success) return null;
            return double.Parse(m.Value, CultureInfo.InvariantCulture);
        }

        // AC cars have no year field, so eras come from names we know, a year in the name, or the vintage tag.
        static string FormulaEra(string name, bool vintage)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("250f") || lower.Contains("type 25")) return "f5060";
            if (lower.Contains("312/67") || lower.Contains("type 49") || lower.Contains("72d") || lower.Contains("312t")) return "f6070";
            if (lower.Contains("98t")) return "fturbo";

            var y = FormulaYear(lower, name);
            if (y > 0) return y < 1966 ? "f5060" : y < 1980 ? "f6070" : y < 1990 ? "fturbo" : "fy:" + y;
            return vintage ? "f6070" : "fcar:" + StripVariant(name).ToLowerInvariant();
        }

        static int FormulaYear(string lower, string name)
        {
            if (lower.Contains("f2004")) return 2004;
            if (lower.Contains("f138")) return 2013;
            if (lower.Contains("sf15")) return 2015;
            if (lower.Contains("sf70")) return 2017;
            var year = Regex.Match(name, @"\b(19[5-9]\d|20[0-3]\d)\b");
            return year.Success ? int.Parse(year.Value) : 0;
        }

        // "Lotus Exos 125 Stage 1" and "Lotus Exos 125" are the same car, so they share a series.
        public static string StripVariant(string name)
        {
            return Regex.Replace(name, @"\s+(stage|step)\s*\d+\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        public static string Classify(string name, List<string> tags, string carClass)
        {
            Func<string[], bool> any = names => names.Any(tags.Contains);

            if (any(new[] { "singleseater", "gp", "formula", "open wheel", "f1", "f2", "f3", "formula 1", "formula 2", "formula 3" }))
            {
                if (any(new[] { "#lmp3", "lmp3" })) return "lmp3";
                if (any(new[] { "f2", "f3", "formula 2", "formula 3", "junior" })) return "fjunior";
                if (tags.Contains("singleseater") && !any(new[] { "gp", "f1", "formula 1", "formula", "open wheel" })) return "fjunior";
                return FormulaEra(name, tags.Contains("vintage"));
            }

            if (any(new[] { "#lmp1", "lmp1", "lmp2", "lmp", "hypercar lmh" })) return "lmp1";
            if (any(new[] { "#lmp3", "lmp3" })) return "lmp3";
            if (any(new[] { "#proto c", "prototype c", "group c" })) return "groupc";

            if (tags.Contains("#porsche cup")) return "porschecup";
            if (tags.Contains("#tt cup")) return "ttcup";
            if (tags.Contains("#global cup")) return "mx5cup";
            if (tags.Contains("#500abarthac")) return "abarthcup";

            if (any(new[] { "#rally", "group b" }) && carClass == "race") return "groupb";

            if (tags.Contains("gt2") || tags.Contains("gte")) return "gt2";
            if (tags.Contains("gt3") || tags.Contains("#gte-gt3")) return "gt3";
            if (tags.Contains("gt4") || tags.Contains("#gt4")) return "gt4";
            if (tags.Contains("dtm")) return "dtm";
            if (tags.Contains("tcr")) return "tcr";

            if (tags.Contains("#90s touring")) return "touring90";
            if (tags.Contains("#vintage touring")) return "vintagetouring";
            if (tags.Contains("#vintage gt")) return "vintagegt";
            if (tags.Contains("#vintage supercars")) return "vintagesuper";
            if (tags.Contains("#hypercars r")) return "trackhyper";
            if (any(new[] { "#hypercars", "hypercar" })) return "hypercars";
            if (any(new[] { "#supercars", "supercar" })) return "supercars";
            if (tags.Contains("#sportscars")) return "sports";
            if (tags.Contains("#small sports")) return "small";
            if (tags.Contains("hot hatchback")) return "hatch";

            return carClass == "race" ? "race" : "road";
        }

        public static CarInfo ParseCar(string id, string text)
        {
            var lowerId = id.ToLowerInvariant();
            if (lowerId.Contains("drift") || lowerId.Contains("drag")) return null;
            var tags = Tags(text);
            if (tags.Contains("#drift") || tags.Contains("drift")) return null;

            var name = Field(text, "name") ?? id;
            var classKey = Classify(name, tags, (Field(text, "class") ?? "").ToLowerInvariant());

            var bhp = FirstNumber(Field(text, "bhp"));
            var kg = FirstNumber(Field(text, "weight"));
            var hasSpecs = bhp != null && kg != null && bhp >= 30 && kg >= 300 && kg <= 3000;

            // A car in a known class can race without specs. Without a class, specs are all there is to go on.
            if (!hasSpecs && (classKey == "road" || classKey == "race")) return null;

            return new CarInfo
            {
                Id = id,
                Name = name,
                Brand = Field(text, "brand") ?? name,
                Bhp = hasSpecs ? bhp.Value : 0,
                Kg = hasSpecs ? kg.Value : 0,
                Pw = hasSpecs ? bhp.Value / (kg.Value / 1000) : 0,
                ClassKey = classKey,
                AutoClassKey = classKey,
                Torque = Field(text, "torque") ?? "",
                TopSpeed = Field(text, "topspeed") ?? "",
                Acceleration = Field(text, "acceleration") ?? "",
                Country = Field(text, "country") ?? "",
                Description = CleanDescription(Field(text, "description")),
            };
        }

        // Descriptions are often pasted from websites, line breaks and all.
        static string CleanDescription(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var clean = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", " ").Replace("\\n", " ").Replace("\\r", " ").Replace("\\t", " ").Replace("\\\"", "\"");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, "\\s+", " ").Trim();
            if (clean.Length <= 340) return clean;
            var cut = clean.LastIndexOf(' ', 340);
            return clean.Substring(0, cut > 0 ? cut : 340) + "...";
        }

        public static TrackInfo ParseTrack(string id, string layout, string text)
        {
            var label = (id + " " + layout).ToLowerInvariant();
            if (label.Contains("drift") || label.Contains("drag")) return null;

            var run = (Field(text, "run") ?? "").ToLowerInvariant();
            if (run.Contains("point") || run.Contains("drift") || run.Contains("drag")) return null;

            var tags = Tags(text);
            if (new[] { "#drift", "hillclimb", "a2b", "touge", "rally", "drag", "point to point" }.Any(tags.Contains)) return null;

            var pits = FirstNumber(Field(text, "pitboxes"));
            var length = FirstNumber(Field(text, "length"));
            if (pits == null || length == null || pits < 6) return null;

            var km = length > 100 ? length.Value / 1000 : length.Value;
            if (km < 1.5) return null;
            return new TrackInfo { Id = id, Layout = layout, Name = Field(text, "name") ?? id, Km = km, Pits = (int)pits.Value };
        }

        static IEnumerable<string> SubDirs(string path)
        {
            if (!Directory.Exists(path)) return new string[0];
            return Directory.GetDirectories(path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        }

        public static List<CarInfo> ScanCars(string acRoot)
        {
            var root = Path.Combine(acRoot, "content", "cars");
            var cars = new List<CarInfo>();
            foreach (var id in SubDirs(root))
            {
                var dir = Path.Combine(root, id);
                var ui = Path.Combine(dir, "ui", "ui_car.json");
                if (!File.Exists(ui)) continue;
                if (!File.Exists(Path.Combine(dir, "data.acd")) && !Directory.Exists(Path.Combine(dir, "data"))) continue;
                try
                {
                    var car = ParseCar(id, File.ReadAllText(ui));
                    if (car == null) continue;
                    car.Skins = SubDirs(Path.Combine(dir, "skins")).ToList();
                    if (car.Skins.Count > 0) cars.Add(car);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return cars;
        }

        static void AddTrack(List<TrackInfo> list, string root, string id, string layout)
        {
            var dir = Path.Combine(root, id);
            var ui = layout == "" ? Path.Combine(dir, "ui", "ui_track.json") : Path.Combine(dir, "ui", layout, "ui_track.json");
            var ai = layout == "" ? Path.Combine(dir, "ai", "fast_lane.ai") : Path.Combine(dir, layout, "ai", "fast_lane.ai");
            if (!File.Exists(ui) || !File.Exists(ai)) return;
            try
            {
                var track = ParseTrack(id, layout, File.ReadAllText(ui));
                if (track != null) list.Add(track);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static List<TrackInfo> ScanTracks(string acRoot)
        {
            var root = Path.Combine(acRoot, "content", "tracks");
            var tracks = new List<TrackInfo>();
            foreach (var id in SubDirs(root))
            {
                AddTrack(tracks, root, id, "");
                foreach (var layout in SubDirs(Path.Combine(root, id, "ui"))) AddTrack(tracks, root, id, layout);
            }
            return tracks;
        }

        public static string CarImage(string acRoot, string carId, string skin)
        {
            return Path.Combine(acRoot, "content", "cars", carId, "skins", skin ?? "", "preview.jpg");
        }

        public static string TrackImage(string acRoot, string track, string layout)
        {
            return string.IsNullOrEmpty(layout)
                ? Path.Combine(acRoot, "content", "tracks", track, "ui", "preview.png")
                : Path.Combine(acRoot, "content", "tracks", track, "ui", layout, "preview.png");
        }
    }
}
