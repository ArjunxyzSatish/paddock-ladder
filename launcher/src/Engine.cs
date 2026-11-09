using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PaddockLadder
{
    public class TierDef
    {
        public string Name;
        public double MinPw, MaxPw;
        public int Ai, Grid, Km;
    }

    public class Standing
    {
        public string Name, CarName, Team;
        public int Points, Wins;
        public bool IsPlayer, IsTeammate;
    }

    // Career rules. No file or game access, so everything here can be tested on its own.
    public static class Engine
    {
        public const string PlayerKey = "__player";
        public const int RoundsPerSeason = 6;
        public const int StartCredits = 30000;
        public const int RetireTrustCost = 10;
        const int MinimumHalfRepair = 100;
        const double LowestEngineLife = 100;
        static readonly int[] TierPrice = { 20000, 45000, 90000, 180000, 350000 };
        static readonly int[] TierPay = { 1, 2, 3, 4, 6 };

        // What a team charges for a seat. The rookie season is on the house.
        static readonly int[] TierSeatFee = { 10000, 30000, 70000, 130000, 260000 };

        // What it costs to walk out on a full season's contract. It shrinks as the season goes on.
        static readonly int[] TierBuyout = { 6000, 18000, 45000, 90000, 180000 };
        const int NoticeBelow = 30, SackBelow = 15, MidSeasonOddsPenalty = 25, ReleasedOddsPenalty = 10, MarketRefusalPenalty = 5, NewTeamTrust = 50, RetirementCost = 2000, MinReleasedChance = 25, ChosenRivalAggression = 30, RespectedAt = 75;
        static readonly int[] RacePayout = { 8000, 6000, 5000, 4000, 3200, 2600, 2100, 1700, 1400, 1100, 950, 850, 800, 750, 700, 650, 600, 600, 600, 600 };
        const int LastPlacePayout = 600, ObjectiveBonus = 1000, PracticeBonus = 1000, QualifyBonus = 1500;
        const int FreeCuts = 2, CutFine = 400, FreeContacts = 1, ContactFine = 1000;
        public const int SeatRefusalPenalty = 10;
        public const int MinSeatChance = 5;
        public const int Probation = 8;

        public static readonly int[] PointsTable = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };

        public static readonly TierDef[] Tiers =
        {
            new TierDef { Name = "Rookie",        MinPw = 0,   MaxPw = 160,    Ai = 84, Grid = 20, Km = 25 },
            new TierDef { Name = "Club",          MinPw = 160, MaxPw = 260,    Ai = 87, Grid = 20, Km = 30 },
            new TierDef { Name = "National",      MinPw = 260, MaxPw = 380,    Ai = 90, Grid = 20, Km = 35 },
            new TierDef { Name = "International", MinPw = 380, MaxPw = 600,    Ai = 93, Grid = 20, Km = 40 },
            new TierDef { Name = "Legend",        MinPw = 600, MaxPw = 100000, Ai = 96, Grid = 20, Km = 45 },
        };

        public static readonly string[] GroupOrder = { "Road", "Cup", "Touring", "GT", "Vintage", "Rally", "Formula", "Prototype" };

        static ClassDef Class(string key, string name, string group, int tier)
        {
            return new ClassDef { Key = key, Name = name, Group = group, Tier = tier };
        }

        // Real racing classes, so a grid only contains cars that would have raced each other.
        public static readonly ClassDef[] Classes =
        {
            Class("small", "Small Sports Cup", "Road", 1), Class("hatch", "Hot Hatch Cup", "Road", 1), Class("sports", "Sports Car Series", "Road", 2),
            Class("supercars", "Supercar Series", "Road", 3), Class("hypercars", "Hypercar Series", "Road", 4), Class("trackhyper", "Track Hypercars", "Road", 5),
            Class("abarthcup", "Abarth Trofeo", "Cup", 1), Class("mx5cup", "MX-5 Cup", "Cup", 1), Class("ttcup", "TT Cup", "Cup", 2), Class("porschecup", "Porsche Cup", "Cup", 3),
            Class("tcr", "TCR Touring", "Touring", 2), Class("touring90", "1990s Touring", "Touring", 2), Class("dtm", "DTM", "Touring", 4),
            Class("gt4", "GT4", "GT", 3), Class("gt3", "GT3", "GT", 4), Class("gt2", "GT2", "GT", 4),
            Class("vintagetouring", "Vintage Touring", "Vintage", 2), Class("vintagesuper", "Vintage Supercars", "Vintage", 3), Class("vintagegt", "Vintage GT", "Vintage", 4),
            Class("groupb", "Group B", "Rally", 4),
            Class("fjunior", "Junior Formula", "Formula", 2), Class("f5060", "Formula 1950s-60s", "Formula", 3), Class("f6070", "Formula Late 60s-70s", "Formula", 4),
            Class("fturbo", "Formula Turbo Era", "Formula", 5), Class("fmodern", "Formula Modern", "Formula", 5),
            Class("lmp3", "LMP3", "Prototype", 3), Class("groupc", "Group C", "Prototype", 5), Class("lmp1", "LMP1", "Prototype", 5),
        };

        static readonly string[] FirstNames =
        {
            "Marco", "Elias", "Tomasz", "Hugo", "Kenji", "Rafael", "Lukas", "Owen", "Mateo", "Nils", "Andrei", "Callum", "Yuto", "Bruno",
            "Felix", "Dario", "Emil", "Tariq", "Jonas", "Silas", "Ana", "Freya", "Chiara", "Noor", "Ines", "Maja", "Sofia", "Hana", "Elena", "Tessa",
        };

        static readonly string[] LastNames =
        {
            "Ferrandi", "Holtz", "Wrona", "Delacroix", "Sakamoto", "Teixeira", "Brandl", "Hartley", "Quiroga", "Lindqvist", "Popescu", "Ashworth",
            "Morita", "Cavalcanti", "Reinholt", "Santoro", "Nyberg", "Haddad", "Vogel", "Marchetti", "Okafor", "Duval", "Ibarra", "Kowalik",
        };

        static readonly string[] Nations = { "Italy", "Germany", "UK", "France", "Japan", "Spain", "USA", "Brazil", "Sweden", "Poland" };

        static readonly string[] TeamNames =
        {
            "Northgate", "Halcyon", "Kestrel", "Ironbridge", "Meridian", "Vanta", "Blackwater", "Solstice", "Tarn Valley", "Copperline",
        };

        static readonly string[] TeamSuffixes = { "Racing", "Motorsport", "Autosport", "Competition" };

        static readonly object[][] Weather =
        {
            new object[] { "3_clear", 24, 31 }, new object[] { "4_mid_clear", 21, 27 },
            new object[] { "5_light_clouds", 18, 23 }, new object[] { "6_mid_clouds", 15, 19 },
        };

        public const int WeatherSlotCount = 4;
        public const int MinLaps = 1, MaxLaps = 60, MinEndurance = 30, MaxEndurance = 720, MinSession = 1, MaxSession = 60;
        public const double PromoteAt = 0.62, DropBelow = 0.30;

        static WeatherDef W(int id, string name, string group, string baseName)
        {
            return new WeatherDef { Id = id, Name = name, Group = group, BaseName = baseName };
        }

        public static readonly WeatherDef[] WeatherOptions =
        {
            W(100, "No Clouds", "Clear", "3_clear"), W(15, "Clear", "Clear", "3_clear"), W(16, "Few Clouds", "Clear", "4_mid_clear"),
            W(17, "Scattered Clouds", "Cloudy", "5_light_clouds"), W(31, "Windy", "Cloudy", "5_light_clouds"), W(18, "Broken Clouds", "Cloudy", "6_mid_clouds"),
            W(19, "Overcast", "Cloudy", "7_heavy_clouds"), W(23, "Haze", "Hazy", "sol_21_haze"), W(21, "Mist", "Hazy", "sol_11_mist"),
            W(20, "Fog", "Hazy", "sol_12_fog"), W(25, "Dust", "Hazy", "sol_22_dust"), W(24, "Sand", "Hazy", "sol_23_sand"), W(22, "Smoke", "Hazy", "sol_24_smoke"),
            W(3, "Light Drizzle", "Rain", "sol_31_light_drizzle"), W(4, "Drizzle", "Rain", "sol_32_drizzle"), W(5, "Heavy Drizzle", "Rain", "sol_33_heavy_drizzle"),
            W(6, "Light Rain", "Rain", "sol_34_light_rain"), W(7, "Rain", "Rain", "sol_35_rain"), W(8, "Heavy Rain", "Rain", "sol_36_heavy_rain"),
            W(0, "Light Thunderstorm", "Storm", "sol_41_light_thunderstorm"), W(1, "Thunderstorm", "Storm", "sol_42_thunderstorm"), W(2, "Heavy Thunderstorm", "Storm", "sol_43_heavy_thunderstorm"),
            W(26, "Squalls", "Storm", "sol_44_squalls"), W(27, "Tornado", "Storm", "sol_44_squalls"), W(28, "Hurricane", "Storm", "sol_44_squalls"),
            W(9, "Light Snow", "Winter", "3_clear"), W(10, "Snow", "Winter", "3_clear"), W(11, "Heavy Snow", "Winter", "3_clear"),
            W(12, "Light Sleet", "Winter", "3_clear"), W(13, "Sleet", "Winter", "3_clear"), W(14, "Heavy Sleet", "Winter", "3_clear"),
        };

        public static WeatherDef WeatherById(int id)
        {
            return WeatherOptions.FirstOrDefault(w => w.Id == id) ?? WeatherOptions[1];
        }

        static int Clamp(int x, int lo, int hi) { return x < lo ? lo : x > hi ? hi : x; }

        // string.GetHashCode can change between .NET versions, so anything saved or repeatable uses this.
        public static int StableHash(string text)
        {
            unchecked
            {
                var hash = 17;
                foreach (var ch in text) hash = hash * 31 + ch;
                return hash;
            }
        }

        // Most races stay dry, some turn wet part-way through, and a few start wet.
        public static List<int> RandomWeather(Random rng)
        {
            int[] dry = { 15, 16, 17, 18, 19, 31, 100 }, damp = { 3, 4, 5, 6 }, wet = { 6, 7, 8, 1 };
            Func<int[], int> pick = pool => pool[rng.Next(pool.Length)];
            var roll = rng.NextDouble();
            if (roll < 0.55) return new List<int> { pick(dry), pick(dry), pick(dry), pick(dry) };
            if (roll < 0.8) return new List<int> { pick(dry), pick(damp), pick(wet), pick(damp) };
            if (roll < 0.92) return new List<int> { pick(wet), pick(damp), pick(dry), pick(dry) };
            return new List<int> { pick(damp), pick(wet), pick(wet), pick(damp) };
        }

        // Fills in anything an older save doesn't have yet.
        public static void EnsureRaceSetup(Career c)
        {
            if (c.RaceLaps <= 0) c.RaceLaps = Clamp((int)Math.Round(Tiers[Clamp(c.Tier, 1, Tiers.Length) - 1].Km * c.LengthMult / 5), MinLaps, MaxLaps);
            if (c.EnduranceMinutes <= 0) c.EnduranceMinutes = 60;
            if (c.Format == null) c.Format = "laps";
            if (c.Season == null) return;
            var rng = new Random(c.Seed + c.SeasonNo * 3571);
            foreach (var round in c.Season.Rounds)
                if (round.WeatherSlots == null || round.WeatherSlots.Count != WeatherSlotCount) round.WeatherSlots = RandomWeather(rng);
        }

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                var t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }

        public static int TierOf(double pw)
        {
            for (var i = 0; i < Tiers.Length; i++)
                if (pw >= Tiers[i].MinPw && pw < Tiers[i].MaxPw) return i + 1;
            return Tiers.Length;
        }

        public static string TierName(int tier) { return Tiers[Clamp(tier, 1, Tiers.Length) - 1].Name; }

        public static string LicenceStatus(int points) { return points >= Probation ? "Probation" : points >= 4 ? "Caution" : "Clear"; }

        static bool IsBroad(string key) { return key == "road" || key == "race"; }

        public static bool IsBroadSeries(SeriesDef series) { return series.Id.StartsWith("road-") || series.Id.StartsWith("race-"); }

        // Cars with a real class stay together. Everything else is grouped by speed, and small
        // speed groups join the nearest tier so they don't produce a thin series.
        public static List<SeriesDef> BuildSeries(List<CarInfo> cars)
        {
            var groups = new Dictionary<string, List<CarInfo>>();
            foreach (var car in cars)
            {
                var key = IsBroad(car.ClassKey) ? car.ClassKey + "-" + TierOf(car.Pw) : car.ClassKey;
                if (!groups.ContainsKey(key)) groups[key] = new List<CarInfo>();
                groups[key].Add(car);
            }

            foreach (var key in groups.Keys.Where(k => k.StartsWith("road-") || k.StartsWith("race-")).ToList())
            {
                if (groups[key].Count >= 3) continue;
                var prefix = key.Substring(0, 5);
                var tier = int.Parse(key.Substring(5));
                var target = groups.Keys
                    .Where(k => k != key && k.StartsWith(prefix) && groups[k].Count >= 3)
                    .OrderBy(k => Math.Abs(int.Parse(k.Substring(5)) - tier))
                    .FirstOrDefault();
                if (target == null) continue;
                groups[target].AddRange(groups[key]);
                groups.Remove(key);
            }

            var result = new List<SeriesDef>();
            foreach (var pair in groups)
            {
                var series = new SeriesDef { Id = pair.Key, Cars = pair.Value.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList() };
                if (pair.Key.StartsWith("road-") || pair.Key.StartsWith("race-"))
                {
                    series.Tier = int.Parse(pair.Key.Substring(5));
                    series.Name = TierName(series.Tier) + (pair.Key.StartsWith("road-") ? " Road Cup" : " Race Cup");
                    series.Group = pair.Key.StartsWith("road-") ? "Road" : "Touring";
                }
                else if (pair.Key.StartsWith("fy:") || pair.Key.StartsWith("fcar:"))
                {
                    series.Tier = 5;
                    series.Group = "Formula";
                    series.Name = pair.Key.StartsWith("fy:") && series.Cars.Count > 1
                        ? "Formula " + pair.Key.Substring(3)
                        : Content.StripVariant(series.Cars[0].Name);
                }
                else
                {
                    var definition = Classes.FirstOrDefault(c => c.Key == pair.Key);
                    if (definition == null) continue;
                    series.Tier = definition.Tier;
                    series.Name = definition.Name;
                    series.Group = definition.Group;
                }
                result.Add(series);
            }
            return result.OrderBy(s => Array.IndexOf(GroupOrder, s.Group)).ThenBy(s => s.Tier).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        }

        public const string CustomGroup = "Custom";

        // Adds the player's own series at the front, and any cars they've added to existing series.
        public static List<SeriesDef> ApplySeriesEdits(List<SeriesDef> built, List<CarInfo> cars, SeriesEdits edits)
        {
            var result = new List<SeriesDef>();
            foreach (var custom in edits.Custom)
            {
                var chosen = custom.CarIds.Select(id => cars.FirstOrDefault(x => x.Id == id)).Where(x => x != null).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (chosen.Count == 0) continue;
                result.Add(new SeriesDef { Id = "custom:" + custom.Id, Name = custom.Name, Group = CustomGroup, Tier = Clamp(custom.Tier, 1, Tiers.Length), Cars = chosen });
            }
            foreach (var series in built)
            {
                List<string> extra;
                if (edits.Additions.TryGetValue(series.Id, out extra))
                {
                    foreach (var id in extra)
                    {
                        var car = cars.FirstOrDefault(x => x.Id == id);
                        if (car != null && !series.Cars.Contains(car)) series.Cars.Add(car);
                    }
                    series.Cars = series.Cars.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }
                result.Add(series);
            }
            return result;
        }

        public static void ApplyClassOverrides(List<CarInfo> cars, Dictionary<string, string> overrides)
        {
            foreach (var car in cars)
            {
                string key;
                car.ClassKey = overrides.TryGetValue(car.Id, out key) ? key : car.AutoClassKey;
            }
        }

        // Ladder careers start at each tier's strength. In open careers the number set is the number used.
        public static int AiBase(Career c, int tier)
        {
            var reference = c.Mode == "open" ? Tiers[0].Ai : Tiers[Clamp(tier, 1, Tiers.Length) - 1].Ai;
            return reference + c.BaseDifficulty;
        }

        public static Career NewCareer(string driver, string mode, int baseDifficulty, double lengthMult, int seed)
        {
            var rng = new Random(seed);
            var career = new Career
            {
                Id = Guid.NewGuid().ToString("N"),
                Driver = string.IsNullOrWhiteSpace(driver) ? "Driver" : driver.Trim(),
                Mode = mode,
                Seed = seed,
                BaseDifficulty = baseDifficulty,
                LengthMult = lengthMult,
            };
            var used = new HashSet<string>();
            while (career.Rivals.Count < 24)
            {
                var name = FirstNames[rng.Next(FirstNames.Length)] + " " + LastNames[rng.Next(LastNames.Length)];
                if (!used.Add(name)) continue;
                career.Rivals.Add(new Rival
                {
                    Name = name,
                    Nation = Nations[rng.Next(Nations.Length)],
                    Skill = rng.Next(-3, 4),
                    Aggression = rng.Next(10, 61),
                });
            }
            return career;
        }

        static string TeamName(Random rng) { return TeamNames[rng.Next(TeamNames.Length)] + " " + TeamSuffixes[rng.Next(TeamSuffixes.Length)]; }

        static List<Clause> ClausesFor(int style, int grid)
        {
            var clauses = new List<Clause>();
            if (style == 0)
            {
                clauses.Add(new Clause { Kind = "maxCuts", Target = 2, Text = "No more than 2 track-limit cuts" });
                clauses.Add(new Clause { Kind = "maxContacts", Target = 0, Text = "Cause no contact" });
            }
            else if (style == 1)
            {
                var target = Math.Max(3, grid / 3);
                clauses.Add(new Clause { Kind = "finishTop", Target = target, Text = "Finish in the top " + target });
                clauses.Add(new Clause { Kind = "beatTeammate", Target = 0, Text = "Beat your teammate" });
            }
            else
            {
                var target = Math.Max(4, grid / 2);
                clauses.Add(new Clause { Kind = "finishTop", Target = target, Text = "Finish in the top " + target });
                clauses.Add(new Clause { Kind = "maxContacts", Target = 1, Text = "Cause no more than 1 contact" });
            }
            return clauses;
        }

        static Offer MakeOffer(SeriesDef series, CarInfo car, int style, Random rng)
        {
            return new Offer
            {
                SeriesId = series.Id,
                SeriesName = series.Name,
                Tier = series.Tier,
                CarId = car.Id,
                CarName = car.Name,
                Brand = car.Brand,
                Skin = car.Skins[0],
                Pw = car.Pw,
                Team = TeamName(rng),
                Clauses = ClausesFor(style, Tiers[series.Tier - 1].Grid),
            };
        }

        // Takes one car from each series in turn, so offers aren't all from the same class.
        static List<KeyValuePair<SeriesDef, CarInfo>> Interleave(List<SeriesDef> series, Random rng)
        {
            var perSeries = series.Select(s =>
            {
                var cars = s.Cars.ToList();
                Shuffle(cars, rng);
                return new KeyValuePair<SeriesDef, List<CarInfo>>(s, cars);
            }).ToList();
            var result = new List<KeyValuePair<SeriesDef, CarInfo>>();
            for (var depth = 0; perSeries.Any(p => p.Value.Count > depth); depth++)
                foreach (var entry in perSeries.Where(p => p.Value.Count > depth))
                    result.Add(new KeyValuePair<SeriesDef, CarInfo>(entry.Key, entry.Value[depth]));
            return result;
        }

        // Disciplines a driver naturally moves up through. Cup, touring and GT racing feed into prototypes.
        public static string PathOf(string group)
        {
            switch (group)
            {
                case "Road": return "road";
                case "Formula": return "formula";
                case "Vintage": return "vintage";
                case "Rally": return "rally";
                case CustomGroup: return null;
                default: return "circuit";
            }
        }

        static string LastPath(Career c, List<SeriesDef> allSeries)
        {
            if (c.LastSeriesId == null) return null;
            var last = allSeries.FirstOrDefault(s => s.Id == c.LastSeriesId);
            if (last != null) return PathOf(last.Group);
            if (c.LastSeriesId.StartsWith("fy:") || c.LastSeriesId.StartsWith("fcar:")) return "formula";
            return null;
        }

        // Series on this path at the tier, or the closest tier it has, preferring a step up.
        static List<SeriesDef> PathSeriesNear(List<SeriesDef> allSeries, string path, int tier)
        {
            var onPath = allSeries.Where(s => s.Group != CustomGroup && PathOf(s.Group) == path).ToList();
            foreach (var candidate in new[] { tier, tier + 1, tier - 1, tier + 2, tier - 2 })
            {
                var match = onPath.Where(s => s.Tier == candidate).ToList();
                if (match.Count > 0) return match;
            }
            return new List<SeriesDef>();
        }

        // Saves from before disciplines existed work theirs out from the last season, and get fresh offers.
        public static bool AdoptPathFromHistory(Career c, List<SeriesDef> allSeries)
        {
            if (c.Mode != "ladder" || c.LastSeriesId != null || c.Season != null || c.History.Count == 0) return false;
            var entry = c.History[c.History.Count - 1];
            var last = allSeries.FirstOrDefault(s => s.Name == entry.SeriesName);
            if (last == null) return false;
            c.LastSeriesId = last.Id;
            c.LastSeasonScore = PromotionScore(entry.Rank, Tiers[last.Tier - 1].Grid, c.Clean, 50);
            c.Offers = null;
            return true;
        }

        // Combines recent form, clean driving and last season's score into one number teams look at.
        public static int DriverRating(Career c)
        {
            var rating = 0.45 * c.Pace + 0.30 * c.Clean + 25 * c.LastSeasonScore + Math.Min(10, c.Titles * 5);
            return Clamp((int)Math.Round(rating), 0, 100);
        }

        // Moving across or down is simple. Moving up is earned, and two tiers at once needs a title.
        public static SeatOdds SeatRequestOdds(Career c, SeriesDef series)
        {
            var skip = series.Tier - c.Tier;
            var rating = DriverRating(c);
            var odds = new SeatOdds { Skip = skip, Allowed = true };

            if (c.SeatRequests.Contains(series.Id))
            {
                odds.Allowed = false;
                odds.Asked = true;
                odds.Reason = "You've already asked this off-season, and their answer stands.";
                return odds;
            }
            if (skip < 0)
            {
                odds.Chance = 100;
                odds.Reason = "A step down. Teams will gladly take you.";
                return odds;
            }
            if (c.LicencePoints >= Probation)
            {
                odds.Allowed = false;
                odds.Reason = "No team will sign you while your licence is on probation.";
                return odds;
            }
            if (skip >= 3)
            {
                odds.Allowed = false;
                odds.Reason = "Too big a step. Teams want to see you race at a higher level first.";
                return odds;
            }

            if (skip == 2)
            {
                var champion = c.History.Count > 0 && c.History[c.History.Count - 1].Rank == 1;
                if (!champion || rating < 80)
                {
                    odds.Allowed = false;
                    odds.Reason = "Two tiers up needs last season's title and a driver rating of 80.";
                    return odds;
                }
                odds.Chance = Math.Min(30, 5 + (rating - 80) * 2);
                odds.Reason = "A long shot. Only a champion gets this conversation.";
            }
            else if (skip == 1)
            {
                if (rating < 70)
                {
                    odds.Allowed = false;
                    odds.Reason = "One tier up needs a driver rating of 70.";
                    return odds;
                }
                odds.Chance = Math.Min(50, 10 + (rating - 70) * 2);
                odds.Reason = "A step up. Teams will want convincing.";
            }
            else
            {
                odds.Chance = Clamp(40 + (rating - 50) * 6 / 5, 30, 95);
                odds.Reason = "A move at your current level.";
            }

            if (c.LicencePoints >= 4)
            {
                odds.Chance = Math.Max(5, odds.Chance - 15);
                odds.Reason += " Your licence record hurts your chances.";
            }
            if (skip > 0 && c.SeatRefusals > 0)
            {
                odds.Chance = Math.Max(MinSeatChance, odds.Chance - c.SeatRefusals * SeatRefusalPenalty);
                odds.Reason += " Word has spread that other teams turned you down.";
            }
            return odds;
        }

        public static int SeatRoll(Career c, SeriesDef series)
        {
            return new Random(c.Seed + c.SeasonNo * 5639 + StableHash(series.Id) % 1000).Next(100);
        }

        // Each series can be asked once an off-season, and being turned down makes the next move up harder.
        // A successful request adds seats in that series to the other offers.
        public static bool ResolveSeatRequest(Career c, List<SeriesDef> allSeries, SeriesDef series, int roll)
        {
            var odds = SeatRequestOdds(c, series);
            c.SeatRequests.Add(series.Id);
            if (!odds.Allowed || roll >= odds.Chance)
            {
                if (odds.Skip > 0) c.SeatRefusals++;
                c.SeatNote = "No seat in " + series.Name + " this time. Your manager says teams went another way, so your other offers still stand.";
                return false;
            }

            var rng = new Random(c.Seed + c.SeasonNo * 2741 + StableHash(series.Id) % 1000);
            var existing = c.Offers ?? new List<Offer>();
            var cars = series.Cars.Where(car => !existing.Any(o => o.SeriesId == series.Id && o.CarId == car.Id)).ToList();
            Shuffle(cars, rng);
            var seats = odds.Skip >= 2 ? 1 : odds.Skip == 1 ? 2 : 3;

            // The new seats join your other offers. Your tier only changes if you sign one of them.
            var granted = new List<Offer>();
            foreach (var car in cars.Take(seats))
            {
                var seat = MakeOffer(series, car, granted.Count, rng);
                seat.Requested = true;
                granted.Add(seat);
            }
            c.Offers = granted.Concat(existing).ToList();
            c.SeatNote = "A team in " + series.Name + " has agreed to take you on. The seat is with your other offers.";
            return true;
        }

        public static PromotionOutlook Outlook(Career c)
        {
            var s = c.Season;
            var table = Standings(c);
            var rank = table.FindIndex(r => r.IsPlayer) + 1;
            var outlook = ScoreParts(rank, table.Count, c.Clean, s.Trust);
            var score = PromotionScore(rank, table.Count, c.Clean, s.Trust);

            if (c.LicencePoints >= Probation)
            {
                outlook.Danger = true;
                outlook.Verdict = "Licence on probation: no promotion";
            }
            else if (s.RoundIndex == 0)
            {
                outlook.Verdict = "No races yet";
            }
            else if (score >= PromoteAt)
            {
                outlook.Good = c.Tier < Tiers.Length;
                outlook.Verdict = c.Tier < Tiers.Length ? "On course for promotion" : "Top tier: nowhere higher to go";
            }
            else if (score < DropBelow && c.Tier > 1)
            {
                outlook.Danger = true;
                outlook.Verdict = "At risk of being dropped";
            }
            else
            {
                outlook.Verdict = "On course to stay in this tier";
            }
            return outlook;
        }

        // Ladder offers come from the career's tier. A licence on probation gets one fewer team interested.
        public static List<Offer> MakeOffers(Career c, List<SeriesDef> allSeries, SeriesDef only)
        {
            var rng = new Random(c.Seed + c.SeasonNo * 7919 + c.Tier * 1009 + (only == null ? 0 : StableHash(only.Id) % 1000));
            var pool = new List<SeriesDef>();
            if (only != null)
            {
                pool.Add(only);
            }
            else
            {
                var radius = 0;
                while (pool.Count == 0 && radius <= Tiers.Length)
                {
                    pool = allSeries.Where(s => s.Group != CustomGroup && Math.Abs(s.Tier - c.Tier) <= radius).ToList();
                    radius++;
                }
            }
            if (pool.Count == 0) return new List<Offer>();

            // Picking a series yourself means choosing any car in it.
            if (only != null)
            {
                var everyCar = new List<Offer>();
                foreach (var car in only.Cars) everyCar.Add(MakeOffer(only, car, everyCar.Count % 3, rng));
                return everyCar;
            }

            var wanted = c.LicencePoints >= Probation ? 2 : 3;
            Shuffle(pool, rng);

            // Teams in the discipline you've been racing come first. Others only fill any gaps.
            var path = LastPath(c, allSeries);
            var preferred = path == null ? new List<SeriesDef>() : PathSeriesNear(allSeries, path, c.Tier);
            Shuffle(preferred, rng);
            var others = pool.Where(s => !preferred.Contains(s)).ToList();

            var candidates = Interleave(preferred, rng);
            candidates.AddRange(Interleave(others, rng));

            // Three different series wherever the installed cars allow it, then different brands.
            var offers = new List<Offer>();
            var brands = new HashSet<string>();
            var seriesUsed = new HashSet<string>();
            foreach (var pair in candidates)
            {
                if (offers.Count >= wanted) break;
                if (seriesUsed.Contains(pair.Key.Id) || brands.Contains(pair.Value.Brand)) continue;
                seriesUsed.Add(pair.Key.Id);
                brands.Add(pair.Value.Brand);
                offers.Add(MakeOffer(pair.Key, pair.Value, offers.Count, rng));
            }
            foreach (var pair in candidates)
            {
                if (offers.Count >= wanted) break;
                if (!seriesUsed.Add(pair.Key.Id)) continue;
                brands.Add(pair.Value.Brand);
                offers.Add(MakeOffer(pair.Key, pair.Value, offers.Count, rng));
            }
            foreach (var pair in candidates)
            {
                if (offers.Count >= wanted) break;
                if (offers.Any(o => o.CarId == pair.Value.Id) || !brands.Add(pair.Value.Brand)) continue;
                offers.Add(MakeOffer(pair.Key, pair.Value, offers.Count, rng));
            }
            foreach (var pair in candidates)
            {
                if (offers.Count >= wanted) break;
                if (offers.Any(o => o.CarId == pair.Value.Id)) continue;
                offers.Add(MakeOffer(pair.Key, pair.Value, offers.Count, rng));
            }
            return offers;
        }

        public static void StartSeason(Career c, Offer offer, List<SeriesDef> allSeries, List<TrackInfo> tracks)
        {
            if (tracks.Count == 0) throw new InvalidOperationException("No usable circuits were found.");
            var series = allSeries.FirstOrDefault(s => s.Id == offer.SeriesId);
            if (series == null) throw new InvalidOperationException("That series is no longer available.");
            var playerCar = series.Cars.FirstOrDefault(x => x.Id == offer.CarId);
            if (playerCar == null) throw new InvalidOperationException("That car is no longer installed.");
            if (offer.Requested) c.Tier = series.Tier;
            var cost = CostOf(c, offer);

            var rng = new Random(c.Seed + c.SeasonNo * 104729 + StableHash(offer.CarId) % 10000);
            // Tracks with room for the whole grid come first, so one small circuit can't shrink the season.
            var fullGrid = Math.Min(Tiers[series.Tier - 1].Grid, c.Rivals.Count + 1);
            var roomy = tracks.Where(t => t.Pits >= fullGrid).ToList();
            var venues = roomy.Select(t => t.Id).Distinct().Count() >= 3 ? roomy : tracks.ToList();
            Shuffle(venues, rng);
            var picked = new List<TrackInfo>();
            var seen = new HashSet<string>();
            foreach (var t in venues)
                if (picked.Count < RoundsPerSeason && seen.Add(t.Id)) picked.Add(t);
            var unique = picked.Count;
            while (picked.Count < RoundsPerSeason) picked.Add(picked[picked.Count % unique]);

            var gridSize = Math.Min(Tiers[series.Tier - 1].Grid, c.Rivals.Count + 1);
            foreach (var v in picked) gridSize = Math.Min(gridSize, v.Pits);
            gridSize = Math.Max(gridSize, 2);

            // Racing classes are fair grids already. Road and vintage labels are broad, so those narrow around your car.
            var rivalCars = series.Cars.ToList();
            if (IsBroadSeries(series) || series.Group == "Road" || series.Group == "Vintage")
            {
                var close = series.Cars.Where(x => x.Pw >= playerCar.Pw * 0.8 && x.Pw <= playerCar.Pw * 1.25).ToList();
                if (close.Count >= 3) rivalCars = close;
            }
            Shuffle(rivalCars, rng);

            var rivals = c.Rivals.ToList();
            Shuffle(rivals, rng);

            var season = new Season
            {
                SeriesId = series.Id,
                SeriesName = series.Name,
                Tier = series.Tier,
                CarId = playerCar.Id,
                CarName = playerCar.Name,
                Skin = offer.Skin,
                Team = offer.Team,
                Clauses = offer.Clauses,
                Trust = 60,
            };
            season.Points[PlayerKey] = 0;
            season.Wins[PlayerKey] = 0;

            for (var i = 0; i < gridSize - 1; i++)
            {
                var rival = rivals[i];
                var teammate = i == 0;
                var car = teammate ? playerCar : rivalCars[i % rivalCars.Count];
                var skins = car.Skins.Where(s => !(car.Id == playerCar.Id && s == offer.Skin)).ToList();
                if (skins.Count == 0) skins = car.Skins;
                season.Field.Add(new GridEntry
                {
                    Name = rival.Name,
                    Nation = rival.Nation,
                    Skill = rival.Skill,
                    Aggression = Clamp(rival.Aggression + rival.Grudge * 15, 0, 100),
                    Car = car.Id,
                    CarName = car.Name,
                    Skin = skins[rng.Next(skins.Count)],
                    IsTeammate = teammate,
                });
                season.Points[rival.Name] = 0;
                season.Wins[rival.Name] = 0;
            }

            AssignTeams(season, c.Seed + c.SeasonNo * 31);

            foreach (var v in picked)
            {
                var w = Weather[rng.Next(Weather.Length)];
                season.Rounds.Add(new Round
                {
                    Track = v.Id,
                    Layout = v.Layout,
                    TrackName = v.Name,
                    Km = v.Km,
                    Weather = (string)w[0],
                    Ambient = (int)w[1],
                    Road = (int)w[2],
                    Sun = rng.Next(-40, 41),
                    WeatherSlots = RandomWeather(rng),
                });
            }

            season.OnLoan = cost.Loan;
            if (!cost.Loan)
            {
                c.Credits -= cost.Pay;
                if (!cost.Owned)
                {
                    c.OwnedCarId = playerCar.Id;
                    c.OwnedCarName = playerCar.Name;
                    c.OwnedCarPaid = cost.Price;
                    ClearDamage(c);
                }
            }

            c.Season = season;
            c.Offers = null;
            c.LastReport = null;
            c.Pending = null;
            c.SeatNote = null;
        }

        public static int LapsFor(Career c, Round round)
        {
            if (round.CustomLaps > 0) return Clamp(round.CustomLaps, MinLaps, MaxLaps);
            var laps = c.RaceLaps;
            // Very long circuits take fewer laps, so one setting doesn't produce a three-hour Nordschleife race.
            if (round.Km > 10) laps = (int)Math.Round(laps * 7 / round.Km);
            return Clamp(laps, MinLaps, MaxLaps);
        }

        public static bool IsTimed(Career c, Round round)
        {
            if (round.CustomMinutes > 0) return true;
            if (round.CustomLaps > 0) return false;
            return c.Format == "endurance";
        }

        public static int MinutesFor(Career c, Round round)
        {
            return Clamp(round.CustomMinutes > 0 ? round.CustomMinutes : c.EnduranceMinutes, MinEndurance, MaxEndurance);
        }

        // Only used to divide the race into weather slots, so a rough figure is fine.
        public static int EstimatedSeconds(Career c, Round round)
        {
            if (IsTimed(c, round)) return MinutesFor(c, round) * 60;
            var averageKmh = new[] { 120, 135, 150, 165, 185 }[c.Season.Tier - 1];
            return (int)(LapsFor(c, round) * round.Km / averageKmh * 3600) + 60;
        }

        public static int WeatherSlotSeconds(Career c, Round round)
        {
            return Math.Max(60, EstimatedSeconds(c, round) / WeatherSlotCount / 10 * 10);
        }

        static int SessionMinutes(int roundOverride, bool on, int minutes)
        {
            if (roundOverride < 0) return 0;
            if (roundOverride > 0) return Clamp(roundOverride, MinSession, MaxSession);
            return on ? Clamp(minutes, MinSession, MaxSession) : 0;
        }

        // Zero means the session doesn't run at this round.
        public static int PracticeMinutesFor(Career c, Round round) { return SessionMinutes(round.PracticeOverride, c.PracticeOn, c.PracticeMinutes); }
        public static int QualifyingMinutesFor(Career c, Round round) { return SessionMinutes(round.QualifyingOverride, c.QualifyingOn, c.QualifyingMinutes); }

        // Practice and qualifying run in the first forecast stage, so the changes happen during the race.
        public static int WeatherLeadSeconds(Career c, Round round)
        {
            return (PracticeMinutesFor(c, round) + QualifyingMinutesFor(c, round)) * 60;
        }

        public static int AiLevelFor(Career c, GridEntry entry)
        {
            return Clamp(AiBase(c, c.Season.Tier) + c.AutoOffset + entry.Skill, 70, 100);
        }

        public static int StartingPosition(Career c)
        {
            // Zero means qualifying decides it. Without qualifying you start from the back.
            return QualifyingMinutesFor(c, c.Season.Rounds[c.Season.RoundIndex]) > 0 ? 0 : c.Season.Field.Count + 1;
        }

        public static List<Standing> Standings(Career c)
        {
            var s = c.Season;
            var rows = new List<Standing>
            {
                new Standing { Name = c.Driver, CarName = s.CarName, Team = s.Team, Points = s.Points[PlayerKey], Wins = s.Wins[PlayerKey], IsPlayer = true },
            };
            foreach (var e in s.Field)
                rows.Add(new Standing { Name = e.Name, CarName = e.CarName, Team = e.Team, Points = s.Points[e.Name], Wins = s.Wins[e.Name], IsTeammate = e.IsTeammate });
            return rows.OrderByDescending(r => r.Points).ThenByDescending(r => r.Wins).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        }

        // Rivals race in pairs, and drivers of the same car are grouped so a team looks like a team.
        static void AssignTeams(Season s, int seed)
        {
            var rng = new Random(seed);
            var taken = new HashSet<string> { s.Team };
            Func<string> freshName = () =>
            {
                for (var tries = 0; tries < 200; tries++)
                {
                    var name = TeamName(rng);
                    if (taken.Add(name)) return name;
                }
                return "Privateer " + (taken.Count + 1);
            };
            foreach (var mate in s.Field.Where(e => e.IsTeammate)) mate.Team = s.Team;
            var others = s.Field.Where(e => !e.IsTeammate).OrderBy(e => e.Car, StringComparer.Ordinal).ToList();
            for (var i = 0; i < others.Count; i += 2)
            {
                var name = freshName();
                others[i].Team = name;
                if (i + 1 < others.Count) others[i + 1].Team = name;
            }
        }

        // Seasons from before teams existed get theirs when the career loads.
        public static void AdoptTeams(Career c)
        {
            if (c.Season != null && c.Season.Field.Any(e => e.Team == null)) AssignTeams(c.Season, c.Seed + c.SeasonNo * 31);
        }

        public static List<TeamStanding> TeamStandings(List<Standing> drivers)
        {
            return drivers.Where(d => d.Team != null).GroupBy(d => d.Team)
                .Select(g => new TeamStanding { Name = g.Key, Points = g.Sum(x => x.Points), Wins = g.Sum(x => x.Wins), IsPlayer = g.Any(x => x.IsPlayer) })
                .OrderByDescending(t => t.Points).ThenByDescending(t => t.Wins).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        }

        static List<DebriefRow> Classify(Career c, List<ResultRow> rows, List<Standing> table)
        {
            Func<string, string> teamOf = name => table.Where(x => x.Name == name).Select(x => x.Team).FirstOrDefault() ?? "";
            return rows.OrderBy(r => r.Pos).Select(r =>
            {
                var name = r.IsPlayer ? c.Driver : r.Name;
                return new DebriefRow { Pos = r.Pos, Name = name, Team = teamOf(name), BestMs = r.BestMs, IsPlayer = r.IsPlayer };
            }).ToList();
        }

        static void Section(StringBuilder sb, string name, params object[] pairs)
        {
            sb.Append('[').Append(name).Append("]\n");
            for (var i = 0; i < pairs.Length; i += 2)
            {
                var value = Convert.ToString(pairs[i + 1], CultureInfo.InvariantCulture) ?? "";
                value = value.Replace("\r", "").Replace("\n", "").Replace("[", "").Replace("]", "");
                sb.Append(pairs[i]).Append('=').Append(value).Append('\n');
            }
            sb.Append('\n');
        }

        // Economy --------------------------------------------------------------------------------

        public static string Cr(int amount)
        {
            return amount.ToString("N0", CultureInfo.InvariantCulture) + " cr";
        }

        // AC cars have no prices, so a car costs what its tier and power say it should.
        public static int CarPrice(int tier, double pw)
        {
            var def = Tiers[Clamp(tier, 1, Tiers.Length) - 1];
            var top = Math.Min(def.MaxPw, def.MinPw + 400);
            var position = Math.Max(0, Math.Min(1, (pw - def.MinPw) / (top - def.MinPw)));
            var price = TierPrice[Clamp(tier, 1, Tiers.Length) - 1] * (0.8 + 0.4 * position);
            return (int)Math.Round(price / 500) * 500;
        }

        // Your current car is traded in for half of what you paid. If you can't pay, the team lends you one.
        public static CarCost CostOf(Career c, Offer offer)
        {
            var cost = new CarCost { Price = CarPrice(offer.Tier, offer.Pw), SeatFee = SeatFeeFor(c, offer) };
            if (offer.MidSeason && !offer.BuyoutPaid) cost.Buyout = BuyoutCost(c);
            var spendable = c.Credits - cost.Buyout;
            if (c.OwnedCarId == offer.CarId)
            {
                cost.Owned = true;
                cost.Pay = cost.SeatFee;
                cost.Loan = cost.Pay > 0 && cost.Pay > spendable;
                cost.Text = cost.Loan ? "Team loan: winnings halved" : cost.Pay > 0 ? "Your car, " + Cr(cost.Pay) + " seat fee" : "You own this car";
                return cost;
            }
            cost.TradeIn = c.OwnedCarPaid / 2;
            cost.Pay = Math.Max(0, cost.Price - cost.TradeIn) + cost.SeatFee;
            cost.Loan = cost.Pay > spendable;
            cost.Text = cost.Loan ? "Team loan: winnings halved" : Cr(cost.Pay) + (cost.SeatFee > 0 ? " with seat fee" : cost.TradeIn > 0 ? " after trade-in" : "");
            return cost;
        }

        public static int SeatFeeFor(Career c, Offer offer)
        {
            var fee = TierSeatFee[Clamp(offer.Tier, 1, Tiers.Length) - 1];
            if (offer.MidSeason) return fee * offer.RoundsLeft / RoundsPerSeason;
            return c.SeasonNo <= 1 ? 0 : fee;
        }

        // A team that has already warned you is glad to let you go for half. One that has let you go charges nothing.
        public static int BuyoutCost(Career c)
        {
            var s = c.Season;
            if (s == null || s.Released) return 0;
            var left = s.Rounds.Count - s.RoundIndex;
            var cost = TierBuyout[Clamp(s.Tier, 1, Tiers.Length) - 1] * left / RoundsPerSeason;
            return s.OnNotice ? cost / 2 : cost;
        }

        // Careers from before credits existed already own the car they're racing.
        public static void AdoptOwnedCar(Career c)
        {
            if (c.OwnedCarId != null || c.Season == null || c.Season.OnLoan) return;
            c.OwnedCarId = c.Season.CarId;
            c.OwnedCarName = c.Season.CarName;
            c.OwnedCarPaid = TierPrice[Clamp(c.Season.Tier, 1, Tiers.Length) - 1];
        }

        static int PayOf(int tier) { return TierPay[Clamp(tier, 1, Tiers.Length) - 1]; }

        public static int PracticeGoalLaps(Career c, Round round)
        {
            var minutes = PracticeMinutesFor(c, round);
            return minutes <= 0 ? 0 : minutes >= 5 ? 3 : 1;
        }

        public static int QualifyGoalTop(Career c, Round round)
        {
            return QualifyingMinutesFor(c, round) <= 0 ? 0 : (c.Season.Field.Count + 2) / 2;
        }

        static void AddSessionTargets(Career c, Round round, RaceResult result, RoundReport report)
        {
            var pay = PayOf(c.Season.Tier);
            var practice = PracticeGoalLaps(c, round);
            if (practice > 0) report.Targets.Add(new TargetLine { Session = "Practice", Text = LapsGoalText(practice), Met = result.PracticeLaps >= practice, Credits = PracticeBonus * pay });
            var qualify = QualifyGoalTop(c, round);
            if (qualify > 0) report.Targets.Add(new TargetLine { Session = "Qualifying", Text = "Qualify in the top " + qualify, Met = result.StartPos >= 1 && result.StartPos <= qualify, Credits = QualifyBonus * pay });
        }

        static bool BeatRival(Season s, RaceResult result, int position)
        {
            if (s.ChosenRival == null) return false;
            var row = (result.Rows ?? new List<ResultRow>()).FirstOrDefault(r => r.Name == s.ChosenRival);
            return row != null && position < row.Pos;
        }

        // Picking someone is a choice with consequences: they fight harder near you, and beating them pays.
        public static void SetRival(Career c, string name)
        {
            var s = c.Season;
            if (s == null) return;
            s.ChosenRival = s.Field.Any(e => e.Name == name && !e.IsTeammate) ? name : null;
        }

        static string LapsGoalText(int laps) { return string.Format("Set {0} clean {1}", laps, laps == 1 ? "lap" : "laps"); }

        static void AwardCredits(Career c, Round round, RaceResult result, RoundReport report, int met, int contacts, int position, bool retired)
        {
            var s = c.Season;
            var pay = PayOf(s.Tier);
            Action<string, int> line = (label, amount) => report.CreditLines.Add(new CreditLine { Label = label, Amount = amount });

            if (retired) line("Retired: no prize money, team costs", -RetirementCost * pay);
            else line("Finished P" + position, (position >= 1 && position <= RacePayout.Length ? RacePayout[position - 1] : LastPlacePayout) * pay);
            if (met > 0) line(string.Format("Contract objectives {0} of {1}", met, s.Clauses.Count), met * ObjectiveBonus * pay);

            var practiceGoal = PracticeGoalLaps(c, round);
            if (practiceGoal > 0 && result.PracticeLaps >= practiceGoal) line(string.Format("Practice goal: {0} clean {1}", practiceGoal, practiceGoal == 1 ? "lap" : "laps"), PracticeBonus * pay);
            var qualifyGoal = QualifyGoalTop(c, round);
            if (qualifyGoal > 0 && result.StartPos >= 1 && result.StartPos <= qualifyGoal) line("Qualifying goal: top " + qualifyGoal, QualifyBonus * pay);

            if (!retired && BeatRival(s, result, position)) line("Beat your rival, " + s.ChosenRival, ObjectiveBonus * pay);

            var extraCuts = Math.Max(0, result.Cuts - FreeCuts);
            if (extraCuts > 0) line(string.Format("Track limits: {0} over the limit", extraCuts), -extraCuts * CutFine * pay);
            var extraContacts = Math.Max(0, contacts - FreeContacts);
            if (extraContacts > 0) line(string.Format("Contact: {0} over the limit", extraContacts), -extraContacts * ContactFine * pay);

            // Nothing floors this: fines can outweigh winnings, and a balance below zero is paid off first.
            var total = report.CreditLines.Sum(x => x.Amount);
            if (s.OnLoan && total > 0)
            {
                var kept = total / 2;
                line("Team's share of a loan car", -kept);
                total -= kept;
            }
            report.CreditsNet = total;
            if (c.Credits < 0 && total > 0) report.DebtNote = string.Format("{0} of this went straight to what you owed.", Cr(Math.Min(total, -c.Credits)));
            c.Credits += total;
            if (c.Credits < 0) report.DebtNote = "You owe " + Cr(-c.Credits) + ". Future earnings pay this off first, and you can't buy repairs or cars until it's cleared.";
            report.CreditsBalance = c.Credits;
        }

        static void ClearDamage(Career c)
        {
            c.CarBody = new List<double> { 0, 0, 0, 0 };
            c.CarSuspension = new List<double> { 0, 0, 0, 0 };
            c.CarEngine = 1000;
            c.CarGearbox = 0;
        }

        // The game sends the car's running totals, so the same reading can arrive twice without counting twice.
        // An engine run to nothing is kept barely alive, or the car couldn't start the next session.
        public static void RecordDamage(Career c, RaceResult result)
        {
            if (!c.DamageOn || c.Season == null || c.Season.OnLoan || result.Body == null || result.Body.Count < 4) return;
            c.CarBody = result.Body.Take(4).ToList();
            c.CarEngine = Math.Max(LowestEngineLife, result.EngineLife);
            c.CarGearbox = Math.Min(1, Math.Max(0, result.Gearbox));
            if (result.Suspension != null && result.Suspension.Count >= 4)
                c.CarSuspension = result.Suspension.Take(4).Select(v => Math.Min(1, Math.Max(0, v))).ToList();
        }

        static int RepairPrice(double amount) { return (int)Math.Ceiling(amount / 50) * 50; }

        static string IfLeftAlone(string part)
        {
            if (part == "body") return "The car stays slower on the straights until this is fixed, and more contact makes it worse.";
            if (part == "engine") return "You keep racing with less power, and more abuse could finish the engine off.";
            if (part == "gearbox") return "Every gear change takes longer, which costs time out of each corner.";
            return "The car keeps pulling to one side and that corner has less grip.";
        }

        static string SeverityOf(double amount) { return amount < 0.25 ? "Light" : amount < 0.6 ? "Moderate" : "Heavy"; }

        // Each part is priced on its own, so you can fix what matters and leave the rest.
        public static List<DamagePart> DamageParts(Career c)
        {
            var parts = new List<DamagePart>();
            if (!c.DamageOn || c.Season != null && c.Season.OnLoan) return parts;
            var pay = PayOf(c.Season != null ? c.Season.Tier : c.Tier);
            Action<string, string, string, double, double> add = (id, name, effect, amount, severity) =>
            {
                var cost = RepairPrice(amount * pay);
                parts.Add(new DamagePart { Id = id, Name = name, Effect = effect, IfLeft = IfLeftAlone(id), Severity = SeverityOf(severity), Cost = cost, HalfCost = cost > MinimumHalfRepair ? RepairPrice(amount * pay / 2) : 0 });
            };
            var body = c.CarBody.Sum();
            var suspension = c.CarSuspension.Sum();
            if (body > 0.5) add("body", "Bodywork", "Loses downforce and top speed", body * 20, body / 200);
            if (c.CarEngine < 999) add("engine", "Engine", "Less power, and it could fail", (1000 - c.CarEngine) * 8, (1000 - c.CarEngine) / 1000);
            if (c.CarGearbox > 0.001) add("gearbox", "Gearbox", "Slower gear changes", c.CarGearbox * 6000, c.CarGearbox);
            if (suspension > 0.001) add("suspension", "Suspension", "Pulls to one side and loses grip", suspension * 3000, c.CarSuspension.Max());
            return parts;
        }

        public static int RepairCost(Career c) { return DamageParts(c).Sum(p => p.Cost); }

        public static string DamageSummary(Career c)
        {
            var parts = new List<string>();
            if (c.CarBody.Sum() > 0.5) parts.Add("body");
            if (c.CarEngine < 999) parts.Add("engine");
            if (c.CarGearbox > 0.001) parts.Add("gearbox");
            if (c.CarSuspension.Sum() > 0.001) parts.Add("suspension");
            if (parts.Count == 0) return "";
            var text = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
            return char.ToUpperInvariant(text[0]) + text.Substring(1) + " damage";
        }

        public static bool TryRepair(Career c) { return TryRepair(c, null, false); }

        // No part named means everything. Half takes each chosen part halfway back to perfect.
        public static bool TryRepair(Career c, string partId, bool half)
        {
            var parts = DamageParts(c).Where(p => string.IsNullOrEmpty(partId) || p.Id == partId).ToList();
            if (parts.Count == 0 || half && parts.Any(p => p.HalfCost <= 0)) return false;
            var cost = parts.Sum(p => half ? p.HalfCost : p.Cost);
            if (cost <= 0 || cost > c.Credits) return false;
            c.Credits -= cost;
            if (string.IsNullOrEmpty(partId) && !half)
            {
                ClearDamage(c);
                return true;
            }
            var left = half ? 0.5 : 0.0;
            foreach (var part in parts)
            {
                if (part.Id == "body") c.CarBody = c.CarBody.Select(v => v * left).ToList();
                else if (part.Id == "engine") c.CarEngine = 1000 - (1000 - c.CarEngine) * left;
                else if (part.Id == "gearbox") c.CarGearbox *= left;
                else c.CarSuspension = c.CarSuspension.Select(v => v * left).ToList();
            }
            return true;
        }

        // What's left in the bank once this car is paid for.
        public static int CreditsAfterSigning(Career c, Offer offer)
        {
            var cost = CostOf(c, offer);
            return c.Credits - cost.Buyout - (cost.Loan ? 0 : cost.Pay);
        }

        static void FillCarDamage(RaceContext context, Career c)
        {
            context.Credits = c.Credits;
            context.RepairRate = PayOf(c.Season.Tier);
            context.MainRivals = Matchups(c).Where(m => m.Tag != "Teammate").Select(m => m.Name).ToList();
            context.ChosenRival = c.Season.ChosenRival;
            context.RespectShift = RespectShift(c);
            context.DamageOff = !c.DamageOn;
            if (!c.DamageOn || c.Season.OnLoan) return;
            context.CarBody = c.CarBody;
            context.CarEngine = c.CarEngine;
            context.CarGearbox = c.CarGearbox;
            context.CarSuspension = c.CarSuspension;
        }

        public static void FillCredits(GameDebrief d, Career c, bool isActiveCareer)
        {
            d.CreditsBalance = c.Credits;
            d.RepairCost = isActiveCareer ? RepairCost(c) : 0;
            d.CanAffordRepair = d.RepairCost > 0 && d.RepairCost <= c.Credits;
            d.DamageText = d.RepairCost > 0 ? DamageSummary(c) : "";
            d.Parts = isActiveCareer ? DamageParts(c) : new List<DamagePart>();
            d.CarBody = c.CarBody;
            d.CarEngine = c.CarEngine;
            d.CarGearbox = c.CarGearbox;
            d.CarSuspension = c.CarSuspension;
        }

        public static string BuildRaceIni(Career c, out RaceContext context)
        {
            return BuildRaceIni(c, null, out context);
        }

        public static string BuildRaceIni(Career c, RaceEnv env, out RaceContext context)
        {
            var s = c.Season;
            var round = s.Rounds[s.RoundIndex];
            var timed = IsTimed(c, round);
            var laps = timed ? 0 : LapsFor(c, round);
            var minutes = timed ? MinutesFor(c, round) : 0;
            var cars = s.Field.Count + 1;
            var sb = new StringBuilder();

            var firstSlot = WeatherById(round.WeatherSlots != null && round.WeatherSlots.Count > 0 ? round.WeatherSlots[0] : 15);
            var pure = env != null && env.HasPure;
            var weatherName = round.Weather;
            if (pure) weatherName = "3_clear";
            else if (env != null && env.InstalledWeathers.Contains(firstSlot.BaseName)) weatherName = firstSlot.BaseName;

            Section(sb, "RACE", "TRACK", round.Track, "CONFIG_TRACK", round.Layout, "MODEL", s.CarId, "MODEL_CONFIG", "", "CARS", cars,
                "AI_LEVEL", Clamp(AiBase(c, s.Tier) + c.AutoOffset, 70, 100), "FIXED_SETUP", 0, "PENALTIES", 1,
                "SKIN", s.Skin, "DRIFT_MODE", 0, "JUMP_START_PENALTY", 0, "RACE_LAPS", laps);
            Section(sb, "GHOST_CAR", "RECORDING", 0, "PLAYING", 0, "SECONDS_ADVANTAGE", 0, "LOAD", 0, "FILE", "", "ENABLED", 0);
            Section(sb, "REPLAY", "FILENAME", "", "ACTIVE", 0);
            if (pure)
                Section(sb, "LIGHTING", "SUN_ANGLE", round.Sun, "TIME_MULT", 1, "CLOUD_SPEED", 0.2, "__CM_WEATHER_CONTROLLER", "pureCtrl", "__CM_WEATHER_TYPE", firstSlot.Id);
            else
                Section(sb, "LIGHTING", "SUN_ANGLE", round.Sun, "TIME_MULT", 1, "CLOUD_SPEED", 0.2);
            Section(sb, "GROOVE", "VIRTUAL_LAPS", 10, "MAX_LAPS", 30, "STARTING_LAPS", 0);
            Section(sb, "DYNAMIC_TRACK", "SESSION_START", 95, "SESSION_TRANSFER", 80, "RANDOMNESS", 2, "LAP_GAIN", 10);
            Section(sb, "REMOTE", "ACTIVE", 0);
            Section(sb, "LAP_INVALIDATOR", "ALLOWED_TYRES_OUT", -1);
            Section(sb, "TEMPERATURE", "AMBIENT", round.Ambient, "ROAD", round.Road);
            Section(sb, "WEATHER", "NAME", weatherName);
            Section(sb, "WIND", "SPEED_KMH_MIN", 0, "SPEED_KMH_MAX", 0, "DIRECTION_DEG", 0);
            Section(sb, "BENCHMARK", "ACTIVE", 0);
            Section(sb, "__PREVIEW_GENERATION", "ACTIVE", 0);
            Section(sb, "HEADER", "VERSION", 2);
            var sessionIndex = 0;
            var practice = PracticeMinutesFor(c, round);
            var qualifying = QualifyingMinutesFor(c, round);
            if (practice > 0) Section(sb, "SESSION_" + sessionIndex++, "NAME", "Practice", "TYPE", 1, "DURATION_MINUTES", practice, "SPAWN_SET", "PIT");
            if (qualifying > 0) Section(sb, "SESSION_" + sessionIndex++, "NAME", "Qualifying", "TYPE", 2, "DURATION_MINUTES", qualifying, "SPAWN_SET", "PIT");

            // After practice the game ignores STARTING_POSITION and puts the player on pole. A qualifying
            // session with no lap set does put them last, so the in-game app runs one and skips it at once.
            var hiddenQualifying = practice > 0 && qualifying == 0;
            if (hiddenQualifying) Section(sb, "SESSION_" + sessionIndex++, "NAME", "Grid", "TYPE", 2, "DURATION_MINUTES", MinSession, "SPAWN_SET", "PIT");
            if (qualifying > 0)
                Section(sb, "SESSION_" + sessionIndex, "NAME", "Race", "TYPE", 3, "LAPS", laps, "DURATION_MINUTES", minutes, "SPAWN_SET", "START");
            else
                Section(sb, "SESSION_" + sessionIndex, "NAME", "Race", "TYPE", 3, "LAPS", laps, "DURATION_MINUTES", minutes, "SPAWN_SET", "START", "STARTING_POSITION", StartingPosition(c));
            Section(sb, "CAR_0", "MODEL", "-", "MODEL_CONFIG", "", "SKIN", s.Skin, "SETUP", "", "BALLAST", 0, "RESTRICTOR", 0,
                "DRIVER_NAME", c.Driver, "NATIONALITY", "");

            for (var i = 0; i < s.Field.Count; i++)
            {
                var e = s.Field[i];
                Section(sb, "CAR_" + (i + 1), "MODEL", e.Car, "MODEL_CONFIG", "", "SKIN", e.Skin, "SETUP", "", "BALLAST", 0, "RESTRICTOR", 0,
                    "AI_LEVEL", AiLevelFor(c, e), "AI_AGGRESSION", CalmAggression(c, e), "DRIVER_NAME", e.Name, "NATIONALITY", e.Nation);
            }

            context = new RaceContext
            {
                RaceId = c.Seed + "-" + c.SeasonNo + "-" + (s.RoundIndex + 1) + "-" + DateTime.UtcNow.Ticks,
                Track = round.Track,
                Layout = round.Layout,
                TrackName = round.TrackName,
                Car = s.CarId,
                Cars = cars,
                Laps = laps,
                FirstRival = s.Field[0].Name,
                Teammate = s.Field.First(e => e.IsTeammate).Name,
                SeriesName = s.SeriesName,
                Team = s.Team,
                RoundNumber = s.RoundIndex + 1,
                Clauses = s.Clauses,
                SkipQualifying = hiddenQualifying,
            };
            foreach (var e in s.Field)
            {
                var rival = c.Rivals.FirstOrDefault(r => r.Name == e.Name);
                if (rival != null && rival.Grudge > 0 && !string.IsNullOrEmpty(rival.Note)) context.Notes[e.Name] = rival.Note;
                if (rival != null && e.Name == s.ChosenRival) context.Rivalries.Add(new Rivalry { Name = e.Name, Normal = rival.Aggression, Angry = Math.Min(100, Math.Max(e.Aggression, rival.Aggression + ChosenRivalAggression)) });
                else if (rival != null && rival.Grudge > 0) context.Rivalries.Add(new Rivalry { Name = e.Name, Normal = rival.Aggression, Angry = e.Aggression });
            }
            context.PracticeGoalLaps = PracticeGoalLaps(c, round);
            context.PracticeReward = PracticeBonus * PayOf(s.Tier);
            context.QualifyTop = QualifyGoalTop(c, round);
            context.QualifyReward = QualifyBonus * PayOf(s.Tier);
            FillCarDamage(context, c);
            return sb.ToString();
        }

        // Grudges are applied in the race itself, so a rival is only fiercer when you're near them.
        static int CalmAggression(Career c, GridEntry e)
        {
            var rival = c.Rivals.FirstOrDefault(r => r.Name == e.Name);
            return rival != null ? rival.Aggression : e.Aggression;
        }

        static bool ClauseMet(Clause clause, RaceResult result, int pos, int teammatePos, int contacts)
        {
            switch (clause.Kind)
            {
                case "maxCuts": return result.Cuts <= clause.Target;
                case "maxContacts": return contacts <= clause.Target;
                case "finishTop": return pos <= clause.Target;
                case "beatTeammate": return teammatePos == 0 || pos < teammatePos;
                default: return false;
            }
        }

        // How you race decides how the grid races you.
        public static string ReputationOf(Career c) { return c.Clean >= RespectedAt ? "Respected" : c.Clean >= 50 ? "Neutral" : "Dirty"; }

        public static string ReputationText(Career c)
        {
            var name = ReputationOf(c);
            if (name == "Respected") return "Rivals give you more room when you're close.";
            if (name == "Dirty") return "Rivals fight you harder when you're close.";
            return "Rivals race you as they would anyone.";
        }

        static int RespectShift(Career c)
        {
            var name = ReputationOf(c);
            return name == "Respected" ? -15 : name == "Dirty" ? 20 : 0;
        }

        static List<IncidentLine> RuleIncidents(RaceResult result)
        {
            var lines = new List<IncidentLine>();
            foreach (var incident in result.Incidents ?? new List<IncidentRecord>())
            {
                var rear = incident.Kind == "rear";
                var text = incident.Who == "you" ? (rear ? "You ran into the back of " + incident.Driver : "You moved across " + incident.Driver)
                    : incident.Who == "them" ? (rear ? incident.Driver + " ran into the back of you" : incident.Driver + " moved across you")
                    : "Contact with " + incident.Driver;
                var verdict = incident.Who == "you" ? "Your fault. It counts against you." : incident.Who == "them" ? "Not your fault. No penalty." : "Racing incident. No penalty.";
                lines.Add(new IncidentLine { Lap = incident.Lap, Text = text, Verdict = verdict, Who = incident.Who });
            }
            return lines;
        }

        static void RecordForm(Career c, List<ResultRow> rows)
        {
            var s = c.Season;
            var mine = rows.FirstOrDefault(r => r.IsPlayer);
            foreach (var row in rows)
            {
                var key = row.IsPlayer ? PlayerKey : row.Name;
                DriverForm form;
                if (!s.Form.TryGetValue(key, out form)) s.Form[key] = form = new DriverForm();
                form.Starts++;
                form.TotalPos += row.Pos;
                if (form.Best == 0 || row.Pos < form.Best) form.Best = row.Pos;
                if (row.Pos <= 3) form.Podiums++;
                if (row.BestMs > 0 && (form.BestMs == 0 || row.BestMs < form.BestMs)) form.BestMs = row.BestMs;
                if (!row.IsPlayer && mine != null)
                {
                    if (row.Pos < mine.Pos) form.AheadOfYou++;
                    else form.BehindYou++;
                }
            }
        }

        static CompareRow CompareNumbers(string label, double mine, double theirs, bool lowerWins, string format, bool hasData)
        {
            var row = new CompareRow { Label = label, Mine = hasData ? mine.ToString(format, CultureInfo.InvariantCulture) : "-", Theirs = hasData ? theirs.ToString(format, CultureInfo.InvariantCulture) : "-" };
            if (hasData && mine != theirs) row.Edge = (mine < theirs) == lowerWins ? 1 : -1;
            return row;
        }

        static string LapClock(int ms) { return ms <= 0 ? "-" : string.Format("{0}:{1:00}.{2:000}", ms / 60000, ms / 1000 % 60, ms % 1000); }

        static Matchup CompareWith(Career c, GridEntry other, string tag)
        {
            var s = c.Season;
            DriverForm mine, theirs;
            s.Form.TryGetValue(PlayerKey, out mine);
            s.Form.TryGetValue(other.Name, out theirs);
            mine = mine ?? new DriverForm();
            theirs = theirs ?? new DriverForm();
            var raced = mine.Starts > 0 && theirs.Starts > 0;
            var match = new Matchup { Name = other.Name, Team = other.Team, Tag = tag };
            match.Rows.Add(CompareNumbers("Points", s.Points[PlayerKey], s.Points[other.Name], false, "0", true));
            match.Rows.Add(CompareNumbers("Wins", s.Wins[PlayerKey], s.Wins[other.Name], false, "0", true));
            match.Rows.Add(CompareNumbers("Podiums", mine.Podiums, theirs.Podiums, false, "0", raced));
            match.Rows.Add(CompareNumbers("Average finish", raced ? (double)mine.TotalPos / mine.Starts : 0, raced ? (double)theirs.TotalPos / theirs.Starts : 0, true, "0.0", raced));
            match.Rows.Add(CompareNumbers("Best finish", mine.Best, theirs.Best, true, "0", raced));
            match.Rows.Add(CompareNumbers("Races ahead", theirs.BehindYou, theirs.AheadOfYou, false, "0", raced));
            var laps = new CompareRow { Label = "Best lap", Mine = LapClock(mine.BestMs), Theirs = LapClock(theirs.BestMs) };
            if (mine.BestMs > 0 && theirs.BestMs > 0 && mine.BestMs != theirs.BestMs) laps.Edge = mine.BestMs < theirs.BestMs ? 1 : -1;
            match.Rows.Add(laps);

            if (!raced) match.Summary = "You haven't raced each other yet.";
            else if (theirs.BehindYou == theirs.AheadOfYou) match.Summary = string.Format("Level at {0}-{0} in races.", theirs.BehindYou);
            else if (theirs.BehindYou > theirs.AheadOfYou) match.Summary = string.Format("You lead {0}-{1} in races.", theirs.BehindYou, theirs.AheadOfYou);
            else match.Summary = string.Format("They lead {0}-{1} in races.", theirs.AheadOfYou, theirs.BehindYou);
            return match;
        }

        // Your teammate first, then whoever matters most: grudges, the drivers either side of you, and the leader.
        public static List<Matchup> Matchups(Career c)
        {
            var s = c.Season;
            var result = new List<Matchup>();
            if (s == null) return result;
            var mate = s.Field.FirstOrDefault(e => e.IsTeammate);
            if (mate != null) result.Add(CompareWith(c, mate, "Teammate"));

            Action<string, string> add = (name, tag) =>
            {
                if (result.Count >= 4 || result.Any(x => x.Name == name)) return;
                var entry = s.Field.FirstOrDefault(e => e.Name == name && !e.IsTeammate);
                if (entry != null) result.Add(CompareWith(c, entry, tag));
            };
            if (s.ChosenRival != null) add(s.ChosenRival, "Your rival");
            foreach (var rival in c.Rivals.Where(r => r.Grudge > 0).OrderByDescending(r => r.Grudge)) add(rival.Name, "Holds a grudge");
            var table = Standings(c);
            var me = table.FindIndex(r => r.IsPlayer);
            if (me > 0) add(table[me - 1].Name, "Just ahead of you");
            if (me + 1 < table.Count) add(table[me + 1].Name, "Just behind you");
            if (me != 0) add(table[0].Name, "Championship leader");
            foreach (var row in table.Where(r => !r.IsPlayer)) add(row.Name, "Rival");
            return result;
        }

        public static string TagOf(Career c, string name)
        {
            if (c.Season == null) return "";
            if (c.Season.Field.Any(e => e.Name == name && e.IsTeammate)) return "TEAMMATE";
            return Matchups(c).Any(m => m.Name == name && m.Tag != "Teammate") ? "RIVAL" : "";
        }

        // The driver's position with their current team, after each result.
        static void ReviewContract(Career c, RoundReport report)
        {
            var s = c.Season;
            if (s == null || s.RoundIndex >= s.Rounds.Count || s.RoundIndex < 2 || s.Released) return;
            var wasOnNotice = s.OnNotice;
            if (s.Trust < SackBelow || (wasOnNotice && s.Trust <= s.NoticeTrust && s.Trust < NoticeBelow))
            {
                s.Released = true;
                s.OnNotice = false;
                report.Released = true;
                report.ContractTitle = s.Team + " has cancelled your contract";
                report.ContractText = "Your results haven't turned around, so the team has let you go. You owe them nothing, but you need a new seat to keep racing this season.";
            }
            else if (s.Trust < NoticeBelow)
            {
                s.OnNotice = true;
                s.NoticeTrust = s.Trust;
                report.ContractTitle = wasOnNotice ? "You're still on notice" : "You're on notice";
                report.ContractText = s.Team + " is losing patience. Show them team trust moving the right way at the next race, or your contract will be cancelled.";
            }
            else if (wasOnNotice)
            {
                s.OnNotice = false;
                report.ContractTitle = "You're off notice";
                report.ContractText = s.Team + " has seen enough improvement to keep you on.";
            }
        }

        // Moving mid-season ------------------------------------------------------------------------

        static SeatOdds TransferOdds(Career c, int tier, string key)
        {
            var s = c.Season;
            var skip = tier - s.Tier;
            var rating = DriverRating(c);
            var odds = new SeatOdds { Skip = skip, Allowed = true };
            if (s.MarketAsked.Contains(key))
            {
                odds.Allowed = false;
                odds.Asked = true;
                odds.Reason = "You've already asked this season, and their answer stands.";
                return odds;
            }
            if (skip < 0)
            {
                odds.Chance = 85;
                odds.Reason = "A step down. There's usually room for you.";
            }
            else if (c.LicencePoints >= Probation)
            {
                odds.Allowed = false;
                odds.Reason = "No team at this level will sign you while your licence is on probation.";
                return odds;
            }
            else if (skip >= 2)
            {
                odds.Allowed = false;
                odds.Reason = "Nobody will take that big a gamble in the middle of a season.";
                return odds;
            }
            else if (skip == 1)
            {
                if (rating < 70)
                {
                    odds.Allowed = false;
                    odds.Reason = "A step up mid-season needs a driver rating of 70.";
                    return odds;
                }
                odds.Chance = Math.Min(50, 10 + (rating - 70) * 2);
                odds.Reason = "A step up. Teams rarely change drivers for this.";
            }
            else
            {
                odds.Chance = Clamp(40 + (rating - 50) * 6 / 5, 30, 95);
                odds.Reason = "A seat at your current level.";
            }

            odds.Chance -= skip < 0 ? 10 : MidSeasonOddsPenalty;
            if (s.Released)
            {
                odds.Chance -= ReleasedOddsPenalty;
                odds.Reason += " Being let go counts against you.";
            }
            if (c.LicencePoints >= 4) odds.Chance -= 15;
            odds.Chance -= s.MarketRefusals * MarketRefusalPenalty;
            odds.Chance = Math.Max(MinSeatChance, odds.Chance);

            // Somebody at your level will always give a released driver a look.
            if (s.Released && skip <= 0) odds.Chance = Math.Max(MinReleasedChance, odds.Chance);
            return odds;
        }

        static MarketSeat SeatFrom(Career c, string seriesId, string seriesName, string group, int tier, string team)
        {
            var key = team ?? seriesId;
            var odds = TransferOdds(c, tier, key);
            return new MarketSeat { SeriesId = seriesId, SeriesName = seriesName, Group = group, Team = team, Tier = tier, TierName = TierName(tier), Name = team ?? "A team in " + seriesName, Chance = odds.Chance, Allowed = odds.Allowed, Asked = odds.Asked, Reason = odds.Reason, SameSeries = team != null };
        }

        // Rival teams in your series come first, since the championship carries over with those.
        public static List<MarketSeat> Market(Career c, List<SeriesDef> allSeries)
        {
            var s = c.Season;
            var seats = new List<MarketSeat>();
            if (s == null) return seats;
            var mine = allSeries.FirstOrDefault(x => x.Id == s.SeriesId);
            foreach (var team in s.Field.Where(e => !e.IsTeammate && e.Team != null).GroupBy(e => e.Team).Where(g => g.Count() == 2))
                seats.Add(SeatFrom(c, s.SeriesId, s.SeriesName, mine != null ? mine.Group : "", s.Tier, team.Key));
            foreach (var other in allSeries.Where(x => x.Group != CustomGroup && x.Id != s.SeriesId).OrderByDescending(x => x.Tier == s.Tier).ThenBy(x => Math.Abs(x.Tier - s.Tier)).ThenBy(x => x.Name, StringComparer.Ordinal))
                seats.Add(SeatFrom(c, other.Id, other.Name, other.Group, other.Tier, null));
            return seats.OrderBy(x => x.Allowed ? 0 : x.Asked ? 1 : 2).ToList();
        }

        public static int MarketRoll(Career c, MarketSeat seat)
        {
            return new Random(c.Seed + c.SeasonNo * 4441 + c.Season.RoundIndex * 613 + StableHash(seat.Team ?? seat.SeriesId) % 1000).Next(100);
        }

        // A yes puts one seat on the table. Nothing is paid until you sign it.
        public static bool ResolveApproach(Career c, List<SeriesDef> allSeries, MarketSeat seat, int roll)
        {
            var s = c.Season;
            var key = seat.Team ?? seat.SeriesId;
            var odds = TransferOdds(c, seat.Tier, key);
            s.MarketAsked.Add(key);
            var series = allSeries.FirstOrDefault(x => x.Id == seat.SeriesId);
            if (!odds.Allowed || roll >= odds.Chance || series == null)
            {
                s.MarketRefusals++;
                c.SeatNote = seat.Name + " turned you down. Each refusal makes the next team a little warier.";
                return false;
            }

            var rng = new Random(c.Seed + c.SeasonNo * 2081 + s.RoundIndex * 37 + StableHash(key) % 1000);
            CarInfo car = null;
            if (seat.Team != null)
            {
                var entry = s.Field.First(e => e.Team == seat.Team);
                car = series.Cars.FirstOrDefault(x => x.Id == entry.Car);
            }
            if (car == null)
            {
                var cars = series.Cars.ToList();
                Shuffle(cars, rng);
                car = cars[0];
            }

            var offer = MakeOffer(series, car, rng.Next(3), rng);
            if (seat.Team != null) offer.Team = seat.Team;
            offer.ReplacesTeam = seat.Team;
            offer.MidSeason = true;
            offer.Requested = true;
            offer.RoundsLeft = s.Rounds.Count - s.RoundIndex;
            c.MarketOffer = offer;
            c.SeatNote = seat.Name + " will take you on. Look over the deal before you sign.";
            return true;
        }

        public static void Transfer(Career c, Offer offer, List<SeriesDef> allSeries, List<TrackInfo> tracks)
        {
            var s = c.Season;
            if (s == null) throw new InvalidOperationException("You aren't under contract this season.");
            var first = CostOf(c, offer);
            if (first.Buyout > c.Credits) throw new InvalidOperationException("You can't afford the " + Cr(first.Buyout) + " to buy out your contract.");
            c.Credits -= first.Buyout;
            offer.BuyoutPaid = true;

            if (offer.ReplacesTeam != null && offer.SeriesId == s.SeriesId) SwapSeat(c, offer, allSeries);
            else
            {
                var table = Standings(c);
                c.History.Add(new HistoryEntry { SeasonNo = c.SeasonNo, SeriesName = s.SeriesName, CarName = s.CarName, Team = s.Team, Rank = table.FindIndex(r => r.IsPlayer) + 1, Points = s.Points[PlayerKey], Champion = "", Outcome = s.Released ? "Released mid-season." : "Left mid-season for " + offer.Team + "." });
                c.Season = null;
                c.Offers = null;
                StartSeason(c, offer, allSeries, tracks);
                c.Season.Rounds = c.Season.Rounds.Take(Math.Max(1, offer.RoundsLeft)).ToList();
                c.Season.Trust = NewTeamTrust;
            }
            c.MarketOffer = null;
            c.Pending = null;
            c.SeatNote = null;
        }

        // Same championship, different garage: you take a seat at the new team and that driver takes yours.
        static void SwapSeat(Career c, Offer offer, List<SeriesDef> allSeries)
        {
            var s = c.Season;
            var series = allSeries.First(x => x.Id == offer.SeriesId);
            var car = series.Cars.First(x => x.Id == offer.CarId);
            var cost = CostOf(c, offer);
            var incoming = s.Field.Where(e => e.Team == offer.ReplacesTeam).OrderBy(e => e.Skill).ToList();
            var leaving = incoming[0];
            var newMate = incoming[1];
            foreach (var entry in s.Field) entry.IsTeammate = false;
            newMate.IsTeammate = true;
            if (s.ChosenRival == newMate.Name) s.ChosenRival = null;

            leaving.Team = s.Team;
            leaving.Car = s.CarId;
            leaving.CarName = s.CarName;
            leaving.Skin = s.Skin;

            s.Team = offer.Team;
            s.CarId = car.Id;
            s.CarName = car.Name;
            s.Skin = car.Skins.FirstOrDefault(x => !s.Field.Any(e => e.Car == car.Id && e.Skin == x)) ?? car.Skins[0];
            s.Clauses = offer.Clauses;
            s.Trust = NewTeamTrust;
            s.OnNotice = false;
            s.Released = false;
            s.OnLoan = cost.Loan;
            if (!cost.Loan)
            {
                c.Credits -= cost.Pay;
                if (!cost.Owned)
                {
                    c.OwnedCarId = car.Id;
                    c.OwnedCarName = car.Name;
                    c.OwnedCarPaid = cost.Price;
                    ClearDamage(c);
                }
            }
        }

        // No seat to be had: the rest of the season runs without you.
        public static RoundReport SitOutSeason(Career c)
        {
            var s = c.Season;
            if (s == null) return null;
            var title = s.SeriesName;
            while (s.RoundIndex < s.Rounds.Count)
            {
                var rng = new Random(c.Seed + s.RoundIndex * 131 + c.SeasonNo * 17);
                var rows = s.Field.Select(e => new { e.Name, Score = e.Skill + rng.NextDouble() * 6 }).OrderByDescending(x => x.Score).Select((x, i) => new ResultRow { Name = x.Name, Pos = i + 1 }).ToList();
                rows.Add(new ResultRow { Name = c.Driver, Pos = rows.Count + 1, IsPlayer = true });
                ScoreRows(c, rows, true);
                var round = s.Rounds[s.RoundIndex];
                round.Done = true;
                round.Forfeit = true;
                round.Winner = rows[0].Name;
                s.RoundIndex++;
            }
            s.Released = true;
            var report = new RoundReport { Title = "You sat out the rest of " + title, Forfeit = true, Grid = s.Field.Count + 1, SeriesName = title };
            report.Lines.Add("Without a seat, you scored nothing for the remaining rounds.");
            c.Pending = null;
            c.MarketOffer = null;
            FinishSeason(c, report);
            c.LastReport = report;
            return report;
        }

        static int PointsFor(int position)
        {
            return position >= 1 && position <= PointsTable.Length ? PointsTable[position - 1] : 0;
        }

        static void ScoreRows(Career c, List<ResultRow> rows, bool forfeit)
        {
            var s = c.Season;
            RecordForm(c, rows);
            foreach (var row in rows)
            {
                var key = row.IsPlayer ? PlayerKey : row.Name;
                if (!s.Points.ContainsKey(key)) continue;
                var earned = PointsFor(row.Pos);
                if (row.IsPlayer && forfeit) earned = 0;
                s.Points[key] += earned;
                if (row.Pos == 1 && !(row.IsPlayer && forfeit)) s.Wins[key]++;
            }
            var winner = rows.OrderBy(r => r.Pos).First();
            if (!winner.IsPlayer)
            {
                var rival = c.Rivals.FirstOrDefault(r => r.Name == winner.Name);
                if (rival != null) rival.Wins++;
            }
        }

        // Finishing matters most, but clean driving and keeping the team happy can outweigh a better result.
        public static double PromotionScore(int rank, int grid, double clean, int trust)
        {
            var finish = grid > 1 ? 1.0 - (rank - 1.0) / (grid - 1.0) : 1.0;
            return 0.55 * finish + 0.30 * (clean / 100) + 0.15 * (trust / 100.0);
        }

        public static PromotionOutlook ScoreParts(int rank, int grid, double clean, int trust)
        {
            var finish = grid > 1 ? 1.0 - (rank - 1.0) / (grid - 1.0) : 1.0;
            return new PromotionOutlook
            {
                Total = (int)Math.Round(100 * PromotionScore(rank, grid, clean, trust)),
                Position = (int)Math.Round(55 * finish),
                Clean = (int)Math.Round(30 * clean / 100),
                Trust = (int)Math.Round(15 * trust / 100.0),
            };
        }

        static void FinishSeason(Career c, RoundReport report)
        {
            var s = c.Season;
            var table = Standings(c);
            var rank = table.FindIndex(r => r.IsPlayer) + 1;
            var champion = table[0];

            if (champion.IsPlayer)
            {
                c.Titles++;
            }
            else
            {
                var rival = c.Rivals.FirstOrDefault(r => r.Name == champion.Name);
                if (rival != null) rival.Titles++;
            }

            var score = PromotionScore(rank, table.Count, c.Clean, s.Trust);
            var parts = ScoreParts(rank, table.Count, c.Clean, s.Trust);
            report.ScoreTotal = parts.Total;
            report.ScorePosition = parts.Position;
            report.ScoreClean = parts.Clean;
            report.ScoreTrust = parts.Trust;
            c.LastSeasonScore = score;
            c.LastSeriesId = s.SeriesId;
            c.SeatRequests.Clear();
            c.SeatRefusals = 0;
            c.SeatNote = null;
            string outcome;
            if (c.Mode != "ladder")
            {
                outcome = "Pick any series for next season.";
            }
            else if (score < DropBelow && c.Tier > 1)
            {
                c.Tier--;
                outcome = "Dropped to the " + TierName(c.Tier) + " tier.";
            }
            else if (s.Released)
            {
                outcome = "After being released, you stay in the " + TierName(c.Tier) + " tier.";
            }
            else if (c.LicencePoints >= Probation)
            {
                outcome = "Your licence is on probation, so no team will move you up yet.";
            }
            else if (score >= PromoteAt && c.Tier < Tiers.Length)
            {
                c.Tier++;
                outcome = "Promoted to the " + TierName(c.Tier) + " tier.";
            }
            else
            {
                outcome = "You stay in the " + TierName(c.Tier) + " tier for another season.";
            }

            report.SeasonOutcome = "Season over: P" + rank + " of " + table.Count + ". " + outcome;
            c.History.Add(new HistoryEntry
            {
                SeasonNo = c.SeasonNo,
                SeriesName = s.SeriesName,
                CarName = s.CarName,
                Team = s.Team,
                Rank = rank,
                Points = s.Points[PlayerKey],
                Champion = champion.Name,
                Outcome = outcome,
            });
            c.Season = null;
            c.Offers = null;
            c.SeasonNo++;
        }

        // Drivers you hit hold a grudge, and it fades when a race goes by without contact.
        static void NoteContacts(Career c, Round round, List<ContactRecord> contactList, RoundReport report)
        {
            var s = c.Season;
            foreach (var rival in c.Rivals)
            {
                var hit = contactList.FirstOrDefault(x => x.Driver == rival.Name);
                if (hit != null)
                {
                    rival.Grudge = Math.Min(3, rival.Grudge + hit.Count);
                    rival.Note = "You made contact at " + round.TrackName;
                    report.Lines.Add(rival.Name + " will remember the contact at " + round.TrackName + ".");
                }
                else if (rival.Grudge > 0 && s.Field.Any(e => e.Name == rival.Name))
                {
                    rival.Grudge--;
                    if (rival.Grudge == 0) rival.Note = null;
                }
            }
            foreach (var entry in s.Field)
            {
                var rival = c.Rivals.FirstOrDefault(r => r.Name == entry.Name);
                if (rival != null) entry.Aggression = Clamp(rival.Aggression + rival.Grudge * 15, 0, 100);
            }
        }

        // Closing the game once the race is under way. Everything that happened still counts, but there's no prize.
        public static RoundReport Retire(Career c, RaceResult result)
        {
            var s = c.Season;
            if (s == null || s.RoundIndex >= s.Rounds.Count) return null;
            var round = s.Rounds[s.RoundIndex];
            var contactList = result.Contacts ?? new List<ContactRecord>();
            var contacts = contactList.Sum(x => x.Count);

            // Everyone else keeps the place they held when you stopped, and you're classified last.
            var rows = (result.Rows ?? new List<ResultRow>()).Where(r => !r.IsPlayer).OrderBy(r => r.Pos).ToList();
            if (rows.Count == 0) rows = s.Field.OrderByDescending(e => e.Skill).Select(e => new ResultRow { Name = e.Name }).ToList();
            for (var i = 0; i < rows.Count; i++) rows[i].Pos = i + 1;
            rows.Add(new ResultRow { Name = c.Driver, Pos = rows.Count + 1, IsPlayer = true });
            var last = rows.Count;
            ScoreRows(c, rows, true);

            var report = new RoundReport
            {
                Title = "Round " + (s.RoundIndex + 1) + " at " + round.TrackName,
                SeriesName = s.SeriesName,
                Incidents = RuleIncidents(result),
                Retired = true,
                Pos = last,
                Grid = last,
                StartPos = result.StartPos,
                Cuts = result.Cuts,
                Contacts = contacts,
                TrustDelta = -RetireTrustCost,
            };
            report.Lines.Add(string.Format("You retired on lap {0}, so the team's objectives weren't met.", result.Laps + 1));
            AddSessionTargets(c, round, result, report);
            foreach (var clause in s.Clauses)
                report.Targets.Add(new TargetLine { Session = "Race", Text = clause.Text, Credits = ObjectiveBonus * PayOf(s.Tier) });
            s.Trust = Clamp(s.Trust - RetireTrustCost, 0, 100);

            var difficultyWeight = (c.BaseDifficulty + c.AutoOffset) * 0.02;
            c.Pace = Math.Max(0, Math.Min(100, c.Pace * 0.8 + 20 * Math.Max(0, Math.Min(1, difficultyWeight))));

            // Stopping early can't earn a better record than the laps you actually drove.
            var driveScore = 100 - Math.Min(100, (double)result.Cuts / Math.Max(1, result.Laps) * 200 + contacts * 15);
            c.Clean = Math.Min(c.Clean, c.Clean * 0.8 + driveScore * 0.2);
            var licenceBefore = c.LicencePoints;
            c.LicencePoints = Clamp(c.LicencePoints + Math.Max(0, result.Cuts - 2) / 2 + Math.Max(0, contacts - 1), 0, 12);
            report.LicenceDelta = c.LicencePoints - licenceBefore;

            NoteContacts(c, round, contactList, report);
            AwardCredits(c, round, result, report, 0, contacts, last, true);
            RecordDamage(c, result);
            c.Stats.Races++;

            round.Done = true;
            round.Retired = true;
            round.Pos = last;
            round.StartPos = result.StartPos;
            round.Cuts = result.Cuts;
            round.Contacts = contacts;
            round.ClausesTotal = s.Clauses.Count;
            round.Winner = rows[0].Name;
            s.LastFinish = last;
            s.RoundIndex++;
            c.Pending = null;
            report.Table = Standings(c);
            report.TeamTable = TeamStandings(report.Table);
            report.Classification = Classify(c, rows, report.Table);

            if (s.RoundIndex >= s.Rounds.Count) FinishSeason(c, report);
            else ReviewContract(c, report);
            c.LastReport = report;
            return report;
        }

        public static RoundReport ApplyResult(Career c, RaceResult result)
        {
            var s = c.Season;
            if (s == null || s.RoundIndex >= s.Rounds.Count) return null;
            var player = result.Rows.FirstOrDefault(r => r.IsPlayer);
            if (player == null) return null;

            var round = s.Rounds[s.RoundIndex];
            var grid = result.Rows.Count;
            var contactList = result.Contacts ?? new List<ContactRecord>();
            var contacts = contactList.Sum(x => x.Count);
            var teammateName = s.Field.Where(e => e.IsTeammate).Select(e => e.Name).FirstOrDefault();
            var teammateRow = result.Rows.FirstOrDefault(r => r.Name == teammateName);
            var teammatePos = teammateRow == null ? 0 : teammateRow.Pos;

            ScoreRows(c, result.Rows, false);

            var report = new RoundReport
            {
                Title = "Round " + (s.RoundIndex + 1) + " at " + round.TrackName,
                SeriesName = s.SeriesName,
                Incidents = RuleIncidents(result),
                Pos = player.Pos,
                Grid = grid,
                Points = PointsFor(player.Pos),
                StartPos = result.StartPos,
                Cuts = result.Cuts,
                Contacts = contacts,
            };

            AddSessionTargets(c, round, result, report);
            var met = 0;
            foreach (var clause in s.Clauses)
            {
                var ok = ClauseMet(clause, result, player.Pos, teammatePos, contacts);
                if (ok) met++;
                report.Targets.Add(new TargetLine { Session = "Race", Text = clause.Text, Met = ok, Credits = ObjectiveBonus * PayOf(s.Tier) });
                report.TrustDelta += ok ? 6 : -8;
                report.Lines.Add((ok ? "Met: " : "Missed: ") + clause.Text);
            }
            if (s.ChosenRival != null) report.Targets.Add(new TargetLine { Session = "Race", Text = "Beat your rival, " + s.ChosenRival, Met = BeatRival(s, result, player.Pos), Credits = ObjectiveBonus * PayOf(s.Tier) });
            s.Trust = Clamp(s.Trust + report.TrustDelta, 0, 100);

            var finishPct = grid > 1 ? 1.0 - (player.Pos - 1.0) / (grid - 1.0) : 1.0;
            var difficultyWeight = (c.BaseDifficulty + c.AutoOffset) * 0.02;
            c.Pace = Math.Max(0, Math.Min(100, c.Pace * 0.8 + 20 * Math.Max(0, Math.Min(1, finishPct + difficultyWeight))));

            var laps = Math.Max(1, result.Laps);
            var driveScore = 100 - Math.Min(100, (double)result.Cuts / laps * 200 + contacts * 15);
            c.Clean = c.Clean * 0.8 + driveScore * 0.2;

            var licenceBefore = c.LicencePoints;
            var added = Math.Max(0, result.Cuts - 2) / 2 + Math.Max(0, contacts - 1);
            var relief = result.Cuts == 0 && contacts == 0 ? 2 : (result.Cuts <= 2 && contacts <= 1 ? 1 : 0);
            c.LicencePoints = Clamp(c.LicencePoints + added - relief, 0, 12);
            report.LicenceDelta = c.LicencePoints - licenceBefore;

            NoteContacts(c, round, contactList, report);

            AwardCredits(c, round, result, report, met, contacts, player.Pos, false);
            RecordDamage(c, result);

            c.Stats.Races++;
            if (player.Pos == 1) c.Stats.Wins++;
            if (player.Pos <= 3) c.Stats.Podiums++;
            if (result.StartPos == 1) c.Stats.Poles++;
            if (result.Cuts == 0 && contacts == 0) c.Stats.CleanRaces++;

            if (c.AutoDifficulty)
            {
                if (player.Pos <= 2) c.AutoOffset = Clamp(c.AutoOffset + 1, -4, 4);
                else if (player.Pos > Math.Ceiling(grid * 0.7)) c.AutoOffset = Clamp(c.AutoOffset - 1, -4, 4);
            }

            round.Done = true;
            round.Pos = player.Pos;
            round.StartPos = result.StartPos;
            round.Points = report.Points;
            round.Cuts = result.Cuts;
            round.Contacts = contacts;
            round.ClausesMet = met;
            round.ClausesTotal = s.Clauses.Count;
            round.Winner = result.Rows.OrderBy(r => r.Pos).First().Name;
            s.LastFinish = player.Pos;
            s.RoundIndex++;
            c.Pending = null;
            report.Table = Standings(c);
            report.TeamTable = TeamStandings(report.Table);
            report.Classification = Classify(c, result.Rows, report.Table);

            if (s.RoundIndex >= s.Rounds.Count) FinishSeason(c, report);
            else ReviewContract(c, report);
            c.LastReport = report;
            return report;
        }

        public static string WeekendLine(Career c, Round round)
        {
            var parts = new List<string>();
            var practice = PracticeMinutesFor(c, round);
            var qualifying = QualifyingMinutesFor(c, round);
            if (practice > 0) parts.Add("Practice " + practice + " min");
            if (qualifying > 0) parts.Add("Qualifying " + qualifying + " min");
            parts.Add(qualifying > 0 ? "Race" : "Race from the back");
            return string.Join("  \u2192  ", parts);
        }

        // Everything the in-game results screen shows. Built after scoring, so it matches the launcher's debrief.
        public static GameDebrief BuildDebrief(Career c, RoundReport report, RaceResult result, bool isActiveCareer)
        {
            var ladder = c.Mode == "ladder";
            var d = new GameDebrief
            {
                RaceId = result.RaceId,
                Title = report.Title,
                SeriesName = report.SeriesName ?? "",
                SeasonOutcome = report.SeasonOutcome ?? "",
                Pos = report.Pos,
                Grid = report.Grid,
                StartPos = report.StartPos,
                Points = report.Points,
                Cuts = report.Cuts,
                Contacts = report.Contacts,
                TrustDelta = report.TrustDelta,
                LicenceDelta = report.LicenceDelta,
                PromotionMark = (int)Math.Round(PromoteAt * 100),
                ShowScore = ladder,
                Lines = report.Lines,
                CreditLines = report.CreditLines,
                CreditsNet = report.CreditsNet,
                Targets = report.Targets,
                Incidents = report.Incidents,
                Classification = report.Classification,
                DebtNote = report.DebtNote ?? "",
                ContractTitle = report.ContractTitle ?? "",
                ContractText = report.ContractText ?? "",
            };
            FillCredits(d, c, isActiveCareer);
            var table = report.Table ?? new List<Standing>();
            for (var i = 0; i < table.Count; i++)
                d.Standings.Add(new DebriefRow { Pos = i + 1, Name = table[i].Name, Team = table[i].Team ?? "", Tag = table[i].IsPlayer ? "" : TagOf(c, table[i].Name), Points = table[i].Points, Wins = table[i].Wins, IsPlayer = table[i].IsPlayer });
            var teams = report.TeamTable ?? new List<TeamStanding>();
            for (var i = 0; i < teams.Count; i++)
                d.TeamStandings.Add(new DebriefRow { Pos = i + 1, Name = teams[i].Name, Points = teams[i].Points, Wins = teams[i].Wins, IsPlayer = teams[i].IsPlayer });
            var me = table.FirstOrDefault(x => x.IsPlayer);
            if (me != null)
            {
                d.TeamName = me.Team ?? "";
                d.FinalRank = table.IndexOf(me) + 1;
                d.ChampionName = table[0].Name;
                d.ChampionTeam = table[0].Team ?? "";
                d.TeamChampion = teams.Count > 0 ? teams[0].Name : "";
                d.TeamRank = teams.FindIndex(t => t.IsPlayer) + 1;
            }

            var s = c.Season;
            if (s != null)
            {
                d.SeriesName = s.SeriesName;
                if (ladder)
                {
                    var outlook = Outlook(c);
                    d.ScoreTitle = "Promotion score";
                    d.ScoreTotal = outlook.Total;
                    d.ScorePosition = outlook.Position;
                    d.ScoreClean = outlook.Clean;
                    d.ScoreTrust = outlook.Trust;
                    d.ScoreVerdict = outlook.Verdict;
                    d.ScoreGood = outlook.Good;
                    d.ScoreDanger = outlook.Danger;
                }

                var round = s.Rounds[s.RoundIndex];
                var laps = LapsFor(c, round);
                var length = IsTimed(c, round) ? MinutesFor(c, round) + " min race" : laps + (laps == 1 ? " lap" : " laps");
                var weather = WeatherById(round.WeatherSlots != null && round.WeatherSlots.Count > 0 ? round.WeatherSlots[0] : 15).Name;
                d.NextTitle = string.Format("Round {0} of {1}  /  {2}", s.RoundIndex + 1, s.Rounds.Count, round.TrackName);
                d.NextDetail = WeekendLine(c, round) + "  /  " + length + "  /  " + weather;
                d.HasNextRace = isActiveCareer && !s.Released;
                d.Released = s.Released;
            }
            else if (ladder)
            {
                d.ScoreTitle = "Final score";
                d.ScoreTotal = report.ScoreTotal;
                d.ScorePosition = report.ScorePosition;
                d.ScoreClean = report.ScoreClean;
                d.ScoreTrust = report.ScoreTrust;
                d.ScoreGood = report.ScoreTotal >= d.PromotionMark;
                d.ScoreVerdict = d.ScoreGood ? "You met the promotion mark" : "Below the promotion mark";
            }

            if (!isActiveCareer) d.ContinueLabel = "OPEN PADDOCK LADDER";
            else if (s != null && s.Released) d.ContinueLabel = "FIND A NEW TEAM";
            else if (s != null) d.ContinueLabel = "NEXT RACE";
            else d.ContinueLabel = ladder ? "SEE YOUR OFFERS" : "CHOOSE YOUR NEXT SERIES";
            return d;
        }

        // Everything the player should know before committing to loading the next race.
        public static RaceBriefing BuildBriefing(Career c, Func<string, string, string> trackImage, bool allWeather)
        {
            var s = c.Season;
            var round = s.Rounds[s.RoundIndex];
            var pay = PayOf(s.Tier);
            var laps = LapsFor(c, round);
            var slots = round.WeatherSlots != null && round.WeatherSlots.Count > 0 ? round.WeatherSlots : new List<int> { 15 };
            var b = new RaceBriefing
            {
                RoundText = string.Format("Round {0} of {1}", s.RoundIndex + 1, s.Rounds.Count),
                SeriesName = s.SeriesName,
                TrackName = round.TrackName,
                Image = trackImage(round.Track, round.Layout),
                LengthText = (IsTimed(c, round) ? MinutesFor(c, round) + " min" : laps + (laps == 1 ? " lap" : " laps")) + string.Format("  /  {0:0.0} km a lap", round.Km),
                Weekend = WeekendLine(c, round),
                Forecast = string.Join("  \u2192  ", (allWeather ? slots : slots.Take(1)).Select(id => WeatherById(id).Name)),
                Team = s.Team,
                Teammate = s.Field.Where(e => e.IsTeammate).Select(e => e.Name).FirstOrDefault() ?? "",
                CarName = s.CarName,
                Grid = s.Field.Count + 1,
                Ambient = round.Ambient,
                StartText = StartingPosition(c) > 0 ? "P" + StartingPosition(c) : "From qualifying",
                OnLoan = s.OnLoan,
                PrizeText = string.Format("P1 {0}  /  P2 {1}  /  P3 {2}", Cr(RacePayout[0] * pay), Cr(RacePayout[1] * pay), Cr(RacePayout[2] * pay)),
                FinesText = string.Format("Fines: {0} for each track-limit cut after the first {1}, and {2} for each contact after the first {3}.", Cr(CutFine * pay), FreeCuts, Cr(ContactFine * pay), FreeContacts),
            };

            var practice = PracticeGoalLaps(c, round);
            if (practice > 0) b.Targets.Add(new TargetLine { Session = "Practice", Text = LapsGoalText(practice), Credits = PracticeBonus * pay });
            var qualify = QualifyGoalTop(c, round);
            if (qualify > 0) b.Targets.Add(new TargetLine { Session = "Qualifying", Text = "Qualify in the top " + qualify, Credits = QualifyBonus * pay });
            b.Targets.Add(new TargetLine { Session = "Race", Text = "Win the race", Credits = RacePayout[0] * pay });
            foreach (var clause in s.Clauses) b.Targets.Add(new TargetLine { Session = "Race", Text = clause.Text, Credits = ObjectiveBonus * pay });
            if (s.ChosenRival != null) b.Targets.Add(new TargetLine { Session = "Race", Text = "Beat your rival, " + s.ChosenRival, Credits = ObjectiveBonus * pay });
            b.MaxCredits = b.Targets.Sum(t => t.Credits);
            if (s.OnLoan) b.MaxCredits -= b.MaxCredits / 2;
            b.Matchups.Add(string.Format("Your reputation: {0}. {1}", ReputationOf(c), ReputationText(c)));
            foreach (var match in Matchups(c).Take(3)) b.Matchups.Add(string.Format("{0}: {1}. {2}", match.Tag, match.Name, match.Summary));
            return b;
        }

        public static string OffersTitle(Career c)
        {
            return c.Mode == "open" && c.Offers != null && c.Offers.Count > 0 ? c.Offers[0].SeriesName : "Contract offers";
        }

        public static string OffersHint(Career c)
        {
            if (c.Mode == "open")
                return c.Offers != null && c.Offers.Count == 1
                    ? "One-make grid: everyone races this car. Add more cars of this class for a mixed field."
                    : "Pick your car. Your rivals will all be from this class, and it's yours for all six rounds.";
            var hint = c.LicencePoints >= Probation ? "Your licence is on probation, so fewer teams are interested." : "Each team cares about different things. The car is yours for all six rounds.";
            return c.LastSeriesId != null ? string.Format("Last season's promotion score: {0}%. ", (int)Math.Round(c.LastSeasonScore * 100)) + hint : hint;
        }

        // The in-game version of the transfer market.
        public static void FillMarket(GameDebrief d, Career c, List<SeriesDef> allSeries, Func<string, string, string> imageOf)
        {
            d.Market.Clear();
            d.MarketOffer = null;
            var s = c.Season;
            d.Buyout = BuyoutCost(c);
            d.CanLeave = s != null && c.Credits >= d.Buyout;
            d.TeamTrust = s != null ? s.Trust : 0;
            d.Released = s != null && s.Released;
            d.MarketNote = c.SeatNote ?? "";
            d.RivalChoices.Clear();
            d.ChosenRival = "";
            if (s == null) return;
            d.ChosenRival = s.ChosenRival ?? "";
            var table = Standings(c);
            for (var i = 0; i < table.Count; i++)
                if (!table[i].IsPlayer && !table[i].IsTeammate) d.RivalChoices.Add(new DebriefRow { Pos = i + 1, Name = table[i].Name, Team = table[i].Team ?? "", Points = table[i].Points });
            foreach (var seat in Market(c, allSeries).Take(40))
                d.Market.Add(new DebriefSeat { SeriesId = seat.SeriesId, Team = seat.Team ?? "", Name = seat.Name, Group = seat.SeriesName, TierName = seat.TierName, Reason = seat.Reason, Chance = seat.Chance, Allowed = seat.Allowed && d.CanLeave, Asked = seat.Asked, Market = true });
            var o = c.MarketOffer;
            if (o == null) return;
            var cost = CostOf(c, o);
            d.MarketOffer = new DebriefOffer { Index = -1, Team = o.Team, CarName = o.CarName, SeriesName = o.SeriesName, Image = imageOf(o.CarId, o.Skin), Pw = o.Pw, Requested = true, Clauses = o.Clauses.Select(x => x.Text).ToList(), Cost = cost.Text, Loan = cost.Loan, CreditsAfter = CreditsAfterSigning(c, o), MidSeason = true };
            FillOfferDetails(d.MarketOffer, c, o, allSeries);
        }

        static void FillOfferDetails(DebriefOffer target, Career c, Offer o, List<SeriesDef> allSeries)
        {
            var cost = CostOf(c, o);
            target.SeatFee = cost.SeatFee;
            target.Buyout = cost.Buyout;
            target.Price = cost.Price;
            target.TradeIn = cost.Owned ? 0 : cost.TradeIn;
            target.Pay = cost.Pay;
            target.Owned = cost.Owned;
            target.TierName = TierName(o.Tier);
            target.Brand = o.Brand ?? "";
            var car = allSeries.SelectMany(s => s.Cars).FirstOrDefault(x => x.Id == o.CarId);
            if (car == null) return;
            target.Bhp = (int)Math.Round(car.Bhp);
            target.Kg = (int)Math.Round(car.Kg);
            target.Torque = car.Torque ?? "";
            target.TopSpeed = car.TopSpeed ?? "";
            target.Acceleration = car.Acceleration ?? "";
            target.Country = car.Country ?? "";
            target.Description = car.Description ?? "";
        }

        // Adds what the in-game screen needs between seasons: the offers, the seats to ask for and the series to pick from.
        public static void FillOffSeason(GameDebrief d, Career c, List<SeriesDef> allSeries, Func<string, string, string> imageOf)
        {
            d.CanChoose = true;
            d.Ladder = c.Mode == "ladder";
            d.SeasonNo = c.SeasonNo;
            d.OffersTitle = OffersTitle(c);
            d.OffersHint = OffersHint(c);
            d.SeatNote = c.SeatNote ?? "";
            d.TierName = TierName(c.Tier);
            d.DriverRating = DriverRating(c);
            d.Licence = LicenceStatus(c.LicencePoints);
            d.Titles = c.Titles;

            d.Offers.Clear();
            var offers = c.Offers ?? new List<Offer>();
            for (var i = 0; i < offers.Count; i++)
            {
                var o = offers[i];
                d.Offers.Add(new DebriefOffer { Index = i, Team = o.Team, CarName = o.CarName, SeriesName = o.SeriesName, Image = imageOf(o.CarId, o.Skin), Pw = o.Pw, Requested = o.Requested, Clauses = o.Clauses.Select(x => x.Text).ToList(), Cost = CostOf(c, o).Text, Loan = CostOf(c, o).Loan, CreditsAfter = CreditsAfterSigning(c, o) });
                FillOfferDetails(d.Offers[d.Offers.Count - 1], c, o, allSeries);
            }

            d.Seats.Clear();
            d.SeriesChoices.Clear();
            if (d.Ladder)
            {
                // The ones you can still ask come first, so they aren't lost among the closed ones.
                var seats = allSeries.Where(s => s.Group != CustomGroup).Select(s => new { Series = s, Odds = SeatRequestOdds(c, s) })
                    .OrderBy(x => x.Odds.Allowed ? 0 : x.Odds.Asked ? 1 : 2).ThenBy(x => x.Series.Tier);
                foreach (var seat in seats)
                    d.Seats.Add(new DebriefSeat { SeriesId = seat.Series.Id, Name = seat.Series.Name, Group = seat.Series.Group, TierName = TierName(seat.Series.Tier), Reason = seat.Odds.Reason, Chance = seat.Odds.Chance, Allowed = seat.Odds.Allowed, Asked = seat.Odds.Asked });
            }
            else
            {
                foreach (var s in allSeries)
                {
                    var hero = s.Cars[s.Cars.Count / 2];
                    d.SeriesChoices.Add(new DebriefSeries { SeriesId = s.Id, Name = s.Name, Group = s.Group, Detail = TierName(s.Tier) + "  /  " + (s.Cars.Count == 1 ? "One-make" : s.Cars.Count + " cars"), Image = imageOf(hero.Id, hero.Skins[0]) });
                }
            }
        }

        public static RoundReport Forfeit(Career c)
        {
            var s = c.Season;
            if (s == null) return null;
            var rng = new Random(c.Seed + s.RoundIndex * 131 + c.SeasonNo * 17);
            var scored = s.Field.Select(e => new { e.Name, Score = e.Skill + rng.NextDouble() * 6 }).OrderByDescending(x => x.Score).ToList();
            var rows = new List<ResultRow>();
            for (var i = 0; i < scored.Count; i++) rows.Add(new ResultRow { Name = scored[i].Name, Pos = i + 1 });
            rows.Add(new ResultRow { Name = c.Driver, Pos = rows.Count + 1, IsPlayer = true });
            ScoreRows(c, rows, true);

            var round = s.Rounds[s.RoundIndex];
            round.Done = true;
            round.Forfeit = true;
            round.Winner = rows[0].Name;
            s.Trust = Clamp(s.Trust - 10, 0, 100);
            s.RoundIndex++;
            c.Pending = null;

            var report = new RoundReport { Title = "Round " + s.RoundIndex + " forfeited", Forfeit = true, Grid = rows.Count, TrustDelta = -10 };
            report.Lines.Add("The team isn't happy about missing a race.");
            if (s.RoundIndex >= s.Rounds.Count) FinishSeason(c, report);
            else
            {
                ReviewContract(c, report);
                if (report.ContractTitle != "") report.Lines.Add(report.ContractTitle + ". " + report.ContractText);
            }
            c.LastReport = report;
            return report;
        }
    }
}
