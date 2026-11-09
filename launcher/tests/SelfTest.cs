#if SELFTEST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaddockLadder
{
    // Logic checks and offscreen screenshots. Uses a temporary documents folder, so real saves are never touched.
    public static partial class SelfTest
    {
        static int _passed, _failed;

        static void Check(string name, bool ok, object detail = null)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL  " + name + (detail != null ? "  (" + detail + ")" : ""));
        }

        static CarInfo Car(string id, string brand, double pw, string classKey = "road")
        {
            return new CarInfo { Id = id, Name = id, Brand = brand, Pw = pw, Bhp = pw, Kg = 1000, ClassKey = classKey, AutoClassKey = classKey, Skins = new List<string> { "a", "b" } };
        }

        static List<CarInfo> FakeCars()
        {
            var cars = new List<CarInfo>();
            for (var i = 1; i <= 8; i++) cars.Add(Car("rookie" + i, "brand" + i, 100 + i * 5));
            for (var i = 1; i <= 8; i++) cars.Add(Car("club" + i, "brand" + i, 170 + i * 5));
            cars.Add(Car("lonely1", "brand1", 300, "fturbo"));
            for (var i = 1; i <= 4; i++) cars.Add(Car("fr" + i, "brand" + i, 700, "fmodern"));
            return cars;
        }

        static List<TrackInfo> FakeTracks()
        {
            var tracks = new List<TrackInfo>();
            for (var i = 1; i <= 8; i++) tracks.Add(new TrackInfo { Id = "t" + i, Layout = "", Name = "Track " + i, Km = 2 + i, Pits = 24 });
            return tracks;
        }

        static RaceResult Result(Career c, int pos, int cuts, int contacts, int laps = 10)
        {
            var result = new RaceResult { RaceId = "x", Finished = true, Cuts = cuts, Laps = laps };
            var slot = 1;
            foreach (var e in c.Season.Field)
            {
                if (slot == pos) slot++;
                result.Rows.Add(new ResultRow { Name = e.Name, Pos = slot, BestMs = 90000 + slot });
                slot++;
            }
            result.Rows.Add(new ResultRow { Name = c.Driver, Pos = pos, BestMs = 89000, IsPlayer = true });
            if (contacts > 0) result.Contacts.Add(new ContactRecord { Driver = c.Season.Field[1].Name, Count = contacts });
            return result;
        }

        static Career SeasonCareer(int seed, string mode = "ladder")
        {
            var c = Engine.NewCareer("Tester", mode, 0, 1, seed);
            var series = Engine.BuildSeries(FakeCars());
            c.Offers = Engine.MakeOffers(c, series, null);
            Engine.StartSeason(c, c.Offers[0], series, FakeTracks());
            return c;
        }

        static void TestRules()
        {
            Check("tier boundaries", Engine.TierOf(159) == 1 && Engine.TierOf(160) == 2 && Engine.TierOf(599) == 4 && Engine.TierOf(5000) == 5);

            var series = Engine.BuildSeries(FakeCars());
            Check("road tiers exist", series.Any(s => s.Id == "road-1") && series.Any(s => s.Id == "road-2"));
            Check("a class stays together", series.First(s => s.Id == "fmodern").Cars.Count == 4);
            Check("one-make series is allowed", series.First(s => s.Id == "fturbo").Cars.Count == 1);
            Check("class sets the tier", series.First(s => s.Id == "fmodern").Tier == 5);
            var thin = Engine.BuildSeries(new List<CarInfo> { Car("a", "b", 100), Car("c", "d", 110), Car("e", "f", 180), Car("g", "h", 190), Car("i", "j", 200) });
            Check("small speed groups merge", thin.Count == 1 && thin[0].Cars.Count == 5, thin.Count);

            var open = Engine.NewCareer("Tester", "open", 0, 1, 43);
            var every = Engine.MakeOffers(open, series, series.First(s => s.Id == "road-1"));
            Check("picking a series shows every car", every.Count == 8, every.Count);
            Engine.StartSeason(open, every[5], series, FakeTracks());
            Check("the chosen car is used", open.Season.CarId == every[5].CarId);
            Check("grid stays in the class", open.Season.Field.All(e => e.Car.StartsWith("rookie")));

            var formula = Engine.NewCareer("Tester", "open", 0, 1, 44);
            Engine.StartSeason(formula, Engine.MakeOffers(formula, series, series.First(s => s.Id == "fturbo"))[0], series, FakeTracks());
            Check("one-make grid uses that car", formula.Season.Field.All(e => e.Car == "lonely1"));

            var vintageCars = new List<CarInfo>();
            for (var i = 0; i < 4; i++) vintageCars.Add(Car("slow" + i, "b" + i, 300 + i, "vintagegt"));
            for (var i = 0; i < 4; i++) vintageCars.Add(Car("fast" + i, "c" + i, 800 + i, "vintagegt"));
            var vintageSeries = Engine.BuildSeries(vintageCars);
            var vintage = Engine.NewCareer("Tester", "open", 0, 1, 47);
            Engine.StartSeason(vintage, Engine.MakeOffers(vintage, vintageSeries, vintageSeries[0]).First(o => o.CarId == "slow0"), vintageSeries, FakeTracks());
            Check("broad classes keep similar cars together", vintage.Season.Field.All(e => e.Car.StartsWith("slow")));

            var gtCars = new List<CarInfo>();
            for (var i = 0; i < 4; i++) gtCars.Add(Car("a" + i, "b" + i, 300 + i, "gt3"));
            for (var i = 0; i < 4; i++) gtCars.Add(Car("z" + i, "c" + i, 800 + i, "gt3"));
            var gtSeries = Engine.BuildSeries(gtCars);
            var gt = Engine.NewCareer("Tester", "open", 0, 1, 48);
            Engine.StartSeason(gt, Engine.MakeOffers(gt, gtSeries, gtSeries[0]).First(o => o.CarId == "a0"), gtSeries, FakeTracks());
            Check("racing classes keep the whole field", gt.Season.Field.Any(e => e.Car.StartsWith("z")));

            var ladder = Engine.NewCareer("Tester", "ladder", 2, 1, 45);
            var openAi = Engine.NewCareer("Tester", "open", 2, 1, 46);
            Check("ladder strength rises with tier", Engine.AiBase(ladder, 5) - Engine.AiBase(ladder, 1) == 12);
            Check("open strength is what was set", Engine.AiBase(openAi, 5) == 86 && Engine.AiBase(openAi, 1) == 86, Engine.AiBase(openAi, 5));

            var forCustom = FakeCars();
            var customEdits = new SeriesEdits();
            customEdits.Custom.Add(new CustomSeries { Id = "abc", Name = "My Cup", Tier = 2, CarIds = new List<string> { "fr1", "rookie1", "gone" } });
            customEdits.Custom.Add(new CustomSeries { Id = "empty", Name = "Empty" });
            customEdits.Additions["fturbo"] = new List<string> { "fr2" };
            var edited = Engine.ApplySeriesEdits(Engine.BuildSeries(forCustom), forCustom, customEdits);
            var mine = edited.First(s => s.Id == "custom:abc");
            Check("custom series uses chosen cars", mine.Cars.Count == 2 && mine.Group == Engine.CustomGroup && edited[0] == mine);
            Check("empty custom series is hidden", !edited.Any(s => s.Id == "custom:empty"));
            Check("cars can be added to a series", edited.First(s => s.Id == "fturbo").Cars.Count == 2);
            Check("an added car stays in its own series", edited.First(s => s.Id == "fmodern").Cars.Any(x => x.Id == "fr2"));
            var customCareer = Engine.NewCareer("Tester", "open", 0, 1, 49);
            Engine.StartSeason(customCareer, Engine.MakeOffers(customCareer, edited, mine)[0], edited, FakeTracks());
            Check("custom grid uses only chosen cars", customCareer.Season.Field.All(e => e.Car == "fr1" || e.Car == "rookie1"));
            var ladderCustom = Engine.NewCareer("Tester", "ladder", 0, 1, 50);
            ladderCustom.Tier = 2;
            Check("ladder offers skip custom series", Engine.MakeOffers(ladderCustom, edited, null).All(o => !o.SeriesId.StartsWith("custom:")));

            var pathCars = new List<CarInfo>();
            foreach (var key in new[] { "fjunior", "f5060", "f6070", "ttcup", "porschecup", "gt4", "gt3", "sports", "supercars", "groupc" })
                for (var i = 0; i < 4; i++) pathCars.Add(Car(key + i, key + "b" + i, 300, key));
            var pathSeries = Engine.BuildSeries(pathCars);
            var formulaRun = Engine.NewCareer("Tester", "ladder", 0, 1, 52);
            formulaRun.Tier = 3;
            formulaRun.LastSeriesId = "fjunior";
            Check("formula leads to formula", Engine.MakeOffers(formulaRun, pathSeries, null)[0].SeriesId == "f5060", Engine.MakeOffers(formulaRun, pathSeries, null)[0].SeriesId);
            Check("offers come from different series", Engine.MakeOffers(formulaRun, pathSeries, null).Select(o => o.SeriesId).Distinct().Count() == 3);
            formulaRun.Tier = 4;
            formulaRun.LastSeriesId = "f5060";
            Check("formula keeps climbing", Engine.MakeOffers(formulaRun, pathSeries, null)[0].SeriesId == "f6070");
            var cupRun = Engine.NewCareer("Tester", "ladder", 0, 1, 53);
            cupRun.Tier = 3;
            cupRun.LastSeriesId = "ttcup";
            Check("tt cup leads to cup and gt4", new[] { "porschecup", "gt4" }.All(id => Engine.MakeOffers(cupRun, pathSeries, null).Take(2).Any(o => o.SeriesId == id)));
            cupRun.Tier = 4;
            cupRun.LastSeriesId = "porschecup";
            Check("cup leads to gt3", Engine.MakeOffers(cupRun, pathSeries, null)[0].SeriesId == "gt3");
            var roadRun = Engine.NewCareer("Tester", "ladder", 0, 1, 54);
            roadRun.Tier = 3;
            roadRun.LastSeriesId = "sports";
            Check("road leads to road", Engine.MakeOffers(roadRun, pathSeries, null)[0].SeriesId == "supercars");
            var fresh = Engine.NewCareer("Tester", "ladder", 0, 1, 55);
            fresh.Tier = 4;
            Check("a first season has no path", Engine.MakeOffers(fresh, pathSeries, null).Select(o => o.SeriesId).Distinct().Count() > 1);

            var gt3Series = pathSeries.First(s => s.Id == "gt3");
            var rookieRequest = Engine.NewCareer("Tester", "ladder", 0, 1, 56);
            rookieRequest.Tier = 1;
            rookieRequest.Pace = 95;
            rookieRequest.Clean = 95;
            rookieRequest.LastSeasonScore = 1;
            Check("three tiers up is closed", !Engine.SeatRequestOdds(rookieRequest, gt3Series).Allowed);

            var weak = Engine.NewCareer("Tester", "ladder", 0, 1, 57);
            weak.Tier = 3;
            weak.Pace = 30;
            weak.Clean = 40;
            Check("a weak record can't move up", !Engine.SeatRequestOdds(weak, gt3Series).Allowed);

            var strong = Engine.NewCareer("Tester", "ladder", 0, 1, 58);
            strong.Tier = 3;
            strong.Pace = 90;
            strong.Clean = 90;
            strong.LastSeasonScore = 0.9;
            var strongOdds = Engine.SeatRequestOdds(strong, gt3Series);
            Check("a strong record has a chance", strongOdds.Allowed && strongOdds.Chance > 15 && strongOdds.Chance <= 75, strongOdds.Chance);

            var noTitle = Engine.NewCareer("Tester", "ladder", 0, 1, 59);
            noTitle.Tier = 2;
            noTitle.Pace = 100;
            noTitle.Clean = 100;
            noTitle.LastSeasonScore = 1;
            noTitle.History.Add(new HistoryEntry { Rank = 2 });
            Check("two tiers needs a title", !Engine.SeatRequestOdds(noTitle, gt3Series).Allowed);
            noTitle.History[0].Rank = 1;
            var longShot = Engine.SeatRequestOdds(noTitle, gt3Series);
            Check("a champion gets a long shot", longShot.Allowed && longShot.Chance <= 40, longShot.Chance);

            strong.LicencePoints = Engine.Probation;
            Check("probation closes moves up", !Engine.SeatRequestOdds(strong, gt3Series).Allowed);
            Check("stepping down stays open", Engine.SeatRequestOdds(strong, pathSeries.First(s => s.Id == "fjunior")).Chance == 100);
            strong.LicencePoints = 0;

            strong.Offers = new List<Offer>();
            Check("a high roll is turned down", !Engine.ResolveSeatRequest(strong, pathSeries, gt3Series, 99) && strong.SeatRefusals == 1 && strong.Offers.Count == 0);
            Check("a series can't be asked twice", !Engine.SeatRequestOdds(strong, gt3Series).Allowed && Engine.SeatRequestOdds(strong, gt3Series).Asked);
            var otherMoveUp = new SeriesDef { Id = "another", Name = "Another", Tier = gt3Series.Tier, Group = gt3Series.Group };
            var beforeRefusal = SeatChanceWithRefusals(strong, otherMoveUp, 0);
            Check("a refusal makes the next move up harder", SeatChanceWithRefusals(strong, otherMoveUp, 1) == Math.Max(Engine.MinSeatChance, beforeRefusal - Engine.SeatRefusalPenalty), beforeRefusal);
            Check("the odds never reach zero", SeatChanceWithRefusals(strong, otherMoveUp, 50) == Engine.MinSeatChance);
            Check("a refusal doesn't affect stepping down", SeatChanceWithRefusals(strong, pathSeries.First(s => s.Id == "fjunior"), 3) == 100);
            strong.SeatRequests.Clear();
            strong.SeatRefusals = 0;
            Check("a low roll gets the seat", Engine.ResolveSeatRequest(strong, pathSeries, gt3Series, 0) && strong.Offers.All(o => o.SeriesId == "gt3" && o.Requested));
            Check("one tier up means fewer seats", strong.Offers.Count == 2, strong.Offers.Count);
            noTitle.Offers = new List<Offer>();
            noTitle.SeatRequests.Clear();
            Engine.ResolveSeatRequest(noTitle, pathSeries, gt3Series, 0);
            Check("two tiers up means one seat", noTitle.Offers.Count == 1, noTitle.Offers.Count);

            var oldSave = Engine.NewCareer("Tester", "ladder", 0, 1, 60);
            oldSave.Tier = 3;
            oldSave.Offers = new List<Offer>();
            oldSave.History.Add(new HistoryEntry { Rank = 1, SeriesName = pathSeries.First(s => s.Id == "fjunior").Name });
            Check("an older save picks up its discipline", Engine.AdoptPathFromHistory(oldSave, pathSeries) && oldSave.LastSeriesId == "fjunior" && oldSave.Offers == null && oldSave.LastSeasonScore > 0);
            Check("that only happens once", !Engine.AdoptPathFromHistory(oldSave, pathSeries));

            var unraced = SeasonCareer(72);
            Check("no races means no verdict yet", Engine.Outlook(unraced).Verdict == "No races yet", Engine.Outlook(unraced).Verdict);

            var outlookCareer = SeasonCareer(70);
            var outlook = Engine.Outlook(outlookCareer);
            Check("outlook parts add up", Math.Abs(outlook.Position + outlook.Clean + outlook.Trust - outlook.Total) <= 2, outlook.Total);
            outlookCareer.LicencePoints = Engine.Probation;
            Check("probation shows in outlook", Engine.Outlook(outlookCareer).Danger);

            var scoreRun = SeasonCareer(71);
            scoreRun.SeatRequests.Add("gt3");
            scoreRun.SeatRefusals = 2;
            RoundReport finalReport = null;
            for (var i = 0; i < 6; i++) finalReport = Engine.ApplyResult(scoreRun, Result(scoreRun, 1, 0, 0));
            Check("final score is reported", finalReport.ScoreTotal > 62 && finalReport.ScorePosition == 55, finalReport.ScoreTotal);
            Check("season remembers its series", scoreRun.LastSeriesId != null && scoreRun.LastSeasonScore > 0.6);
            Check("a new off-season starts the requests again", scoreRun.SeatRequests.Count == 0 && scoreRun.SeatRefusals == 0);

            var lapCheck = Engine.NewCareer("Tester", "ladder", 0, 1, 51);
            lapCheck.RaceLaps = 1;
            Check("one-lap races are allowed", Engine.MinLaps == 1);

            var moved = FakeCars();
            Engine.ApplyClassOverrides(moved, new Dictionary<string, string> { { "rookie1", "gt3" } });
            Check("a car can be moved", Engine.BuildSeries(moved).First(s => s.Id == "gt3").Cars[0].Id == "rookie1");
            Engine.ApplyClassOverrides(moved, new Dictionary<string, string>());
            Check("a moved car can go back", !Engine.BuildSeries(moved).Any(s => s.Id == "gt3"));

            var c = Engine.NewCareer("Tester", "ladder", 0, 1, 42);
            Check("24 unique rivals", c.Rivals.Select(r => r.Name).Distinct().Count() == 24);

            var offers = Engine.MakeOffers(c, series, null);
            Check("three offers", offers.Count == 3, offers.Count);
            Check("offers match tier", offers.All(o => o.Tier == 1));
            Check("different brands", offers.Select(o => o.Brand).Distinct().Count() == 3);
            Check("offers differ by team style", offers.Select(o => o.Clauses[0].Kind + o.Clauses[1].Kind).Distinct().Count() == 3);

            c.LicencePoints = Engine.Probation;
            Check("probation shrinks offers", Engine.MakeOffers(c, series, null).Count == 2);
            c.LicencePoints = 0;

            var picked = series.First(s => s.Id == "road-2");
            Check("series pick is honoured", Engine.MakeOffers(c, series, picked).All(o => o.SeriesId == "road-2") && Engine.MakeOffers(c, series, picked).Count == 8);

            Engine.StartSeason(c, offers[0], series, FakeTracks());
            var season = c.Season;
            Check("six distinct rounds", season.Rounds.Select(r => r.Track).Distinct().Count() == 6);
            Check("grid size", season.Field.Count + 1 == 20, season.Field.Count);
            Check("teammate shares the car", season.Field[0].IsTeammate && season.Field[0].Car == season.CarId);
            Check("teammate has own skin", season.Field[0].Skin != season.Skin);
            Check("qualifying decides the first grid", Engine.StartingPosition(c) == 0, Engine.StartingPosition(c));
            Check("offers cleared", c.Offers == null);

            RaceContext context;
            var ini = Engine.BuildRaceIni(c, out context);
            Check("ini track", ini.Contains("TRACK=" + season.Rounds[0].Track));
            Check("ini last car", ini.Contains("[CAR_" + season.Field.Count + "]"));
            Check("ini no extra car", !ini.Contains("[CAR_" + (season.Field.Count + 1) + "]"));
            Check("ini race session", ini.Contains("TYPE=3"));
            Check("ini offline", ini.Contains("[REMOTE]\nACTIVE=0"));
            Check("context car count", context.Cars == season.Field.Count + 1);
            Check("context carries clauses", context.Clauses.Count == 2);
            Check("context teammate", context.Teammate == season.Field[0].Name);

            var saved = Store.FromJson<Career>(Store.ToJson(c));
            Check("save round trip", saved.Season.Field[3].Name == season.Field[3].Name && saved.Season.Points[season.Field[0].Name] == 0 && saved.Season.Clauses[1].Text == season.Clauses[1].Text);

            var incoming = Store.FromJson<RaceResult>("{\"RaceId\":\"r1\",\"Finished\":true,\"Cuts\":2,\"Laps\":7,\"Rows\":[{\"Name\":\"A\",\"Pos\":1,\"BestMs\":90000,\"IsPlayer\":true}]}");
            Check("result without contacts parses", incoming.Rows.Count == 1 && incoming.Rows[0].IsPlayer && incoming.Contacts.Count == 0);
        }

        static void TestJudging()
        {
            var c = SeasonCareer(11);
            c.Season.Clauses = new List<Clause>
            {
                new Clause { Kind = "maxCuts", Target = 2, Text = "cuts" },
                new Clause { Kind = "beatTeammate", Text = "mate" },
            };
            var report = Engine.ApplyResult(c, Result(c, 1, 0, 0));
            Check("win scores 25", report.Points == 25);
            Check("clauses met raise trust", report.TrustDelta == 12 && c.Season.Trust == 72, c.Season.Trust);
            Check("clean race relieves nothing below zero", c.LicencePoints == 0);
            Check("clean rating rises", c.Clean > 70, c.Clean);
            Check("round advanced", c.Season.RoundIndex == 1);
            Check("finishing doesn't set the next grid", Engine.StartingPosition(c) == 0);
            var round = c.Season.Rounds[c.Season.RoundIndex];
            var savedLaps = c.RaceLaps;
            c.RaceLaps = 33;
            Check("laps are used as set", Engine.LapsFor(c, new Round { Km = 3 }) == 33, Engine.LapsFor(c, new Round { Km = 3 }));
            c.RaceLaps = 100;
            Check("laps stay within limits", Engine.LapsFor(c, new Round { Km = 5 }) == 60);
            c.RaceLaps = 6;
            Check("very long circuits use fewer laps", Engine.LapsFor(c, new Round { Km = 20.8 }) == 2, Engine.LapsFor(c, new Round { Km = 20.8 }));
            Check("a single race can override laps", Engine.LapsFor(c, new Round { Km = 20.8, CustomLaps = 9 }) == 9);
            c.RaceLaps = savedLaps;
            Check("difficulty rises after a win", c.AutoOffset == 1);

            var messy = Engine.ApplyResult(c, Result(c, 5, 8, 3));
            Check("missed cuts clause", messy.Lines.Any(l => l.StartsWith("Missed") && l.Contains("cuts")));
            Check("licence points added", c.LicencePoints == 3 + 2, c.LicencePoints);
            Check("clean rating falls", c.Clean < 70, c.Clean);

            var hit = c.Season.Field[1].Name;
            var rival = c.Rivals.First(r => r.Name == hit);
            Check("grudge recorded", rival.Grudge == 3 && rival.Note != null, rival.Grudge);
            Check("grudge raises aggression", c.Season.Field[1].Aggression == Math.Min(100, rival.Aggression + 45), c.Season.Field[1].Aggression);

            RaceContext context;
            Engine.BuildRaceIni(c, out context);
            Check("note reaches the game", context.Notes.ContainsKey(hit));

            Engine.ApplyResult(c, Result(c, 5, 0, 0));
            Check("grudge fades with a clean race", rival.Grudge == 2, rival.Grudge);
            Check("clean race lowers licence points", c.LicencePoints == 3, c.LicencePoints);

            Check("clean P5 beats messy P3", Engine.PromotionScore(5, 12, 90, 70) > Engine.PromotionScore(3, 12, 40, 30));
            Check("clean P5 is promotable", Engine.PromotionScore(5, 12, 90, 70) >= 0.62);
            Check("messy P3 is not", Engine.PromotionScore(3, 12, 40, 30) < 0.62);
        }

        static void TestSeasons()
        {
            var good = SeasonCareer(21);
            RoundReport last = null;
            for (var i = 0; i < 6; i++) last = Engine.ApplyResult(good, Result(good, 1, 0, 0));
            Check("season ends", good.Season == null && good.SeasonNo == 2);
            Check("promotion", good.Tier == 2 && last.SeasonOutcome.Contains("Promoted"), last.SeasonOutcome);
            Check("title counted", good.Titles == 1);
            Check("history kept", good.History.Count == 1 && good.History[0].Rank == 1);

            var bad = SeasonCareer(22);
            bad.Tier = 2;
            bad.Season.Tier = 2;
            for (var i = 0; i < 6; i++) last = Engine.ApplyResult(bad, Result(bad, bad.Season.Field.Count + 1, 10, 3));
            Check("relegation", bad.Tier == 1, bad.Tier);

            var probation = SeasonCareer(23);
            probation.LicencePoints = 12;
            for (var i = 0; i < 6; i++)
            {
                last = Engine.ApplyResult(probation, Result(probation, 1, 12, 2));
                probation.LicencePoints = 12;
                if (probation.Season != null) { probation.Season.Trust = 60; probation.Season.Released = false; probation.Season.OnNotice = false; }
            }
            Check("probation blocks promotion", probation.Tier == 1 && last.SeasonOutcome.Contains("probation"), last.SeasonOutcome);

            var open = SeasonCareer(24, "open");
            for (var i = 0; i < 6; i++) last = Engine.ApplyResult(open, Result(open, 1, 0, 0));
            Check("open mode keeps tier", open.Tier == 1 && last.SeasonOutcome.Contains("any series"));

            var forfeit = SeasonCareer(25);
            forfeit.Pending = new RaceContext { RaceId = "a" };
            var report = Engine.Forfeit(forfeit);
            Check("forfeit scores nothing", report.Forfeit && forfeit.Season.Points[Engine.PlayerKey] == 0);
            Check("forfeit costs trust", forfeit.Season.Trust == 50);
            Check("forfeit clears pending", forfeit.Pending == null);
            Check("forfeit advances", forfeit.Season.RoundIndex == 1 && forfeit.Season.Rounds[0].Forfeit);
        }

        static void TestParsing()
        {
            var raw = "{ \"name\": \"Test GT\", \"brand\": \"Acme\", \"description\": \"line one\nline two\",\n\"class\": \"race\", \"tags\": [\"gt3\", \"rwd\"], \"specs\": { \"bhp\": \"500bhp\", \"weight\": \"1,250 kg\" } }";
            var car = Content.ParseCar("acme_gt", raw);
            Check("parses messy car", car != null && car.Bhp == 500 && car.Kg == 1250);
            Check("car class", car.ClassKey == "gt3", car.ClassKey);
            var blankSpecs = Content.ParseCar("ks_f", "{ \"name\": \"Test F1\", \"class\": \"race\", \"tags\": [\"gp\", \"singleseater\"], \"specs\": { \"bhp\": \"--bhp\", \"weight\": \"--kg\" } }");
            Check("known class races without specs", blankSpecs != null && blankSpecs.Pw == 0 && blankSpecs.ClassKey == "fcar:test f1", blankSpecs == null ? null : blankSpecs.ClassKey);
            Check("rejects drift car", Content.ParseCar("acme_drift", raw) == null);
            Check("rejects no specs", Content.ParseCar("x", "{ \"name\": \"Nope\" }") == null);

            Func<string, string[], string, string> cat = (name, tags, cls) => Content.Classify(name, tags.ToList(), cls);
            Check("junior formula", cat("Tatuus FA01", new[] { "singleseater", "race" }, "race") == "fjunior");
            Check("formula by era", cat("Lotus Type 25", new[] { "gp", "singleseater", "vintage" }, "race") == "f5060");
            Check("formula late era", cat("Ferrari 312T", new[] { "gp", "singleseater", "vintage" }, "race") == "f6070");
            Check("formula turbo", cat("Lotus 98T", new[] { "gp", "singleseater", "vintage", "turbo" }, "race") == "fturbo");
            Check("modern formula by year", cat("Ferrari F138", new[] { "gp", "singleseater" }, "race") == "fy:2013");
            Check("year in a mod's name", cat("Mod Racer 1975", new[] { "gp" }, "race") == "f6070");
            Check("modern year in a mod's name", cat("Mod Racer 2021", new[] { "gp" }, "race") == "fy:2021");
            Check("no year means its own series", cat("Mod Racer", new[] { "gp" }, "race") == "fcar:mod racer");
            Check("variants share a series", cat("Mod Racer Stage 2", new[] { "gp" }, "race") == "fcar:mod racer");
            Check("lmp3 stays out of formula", cat("Praga R1", new[] { "#lmp3", "singleseater" }, "race") == "lmp3");
            Check("group c", cat("Mazda 787B", new[] { "#proto c", "prototype c" }, "race") == "groupc");
            Check("lmp1", cat("Audi R18", new[] { "#lmp1", "lmp1", "gt" }, "race") == "lmp1");
            Check("gt3", cat("Audi R8", new[] { "#gte-gt3", "gt3", "gt" }, "race") == "gt3");
            Check("gt2 beats its shared tag", cat("BMW M3 GT2", new[] { "#gte-gt3", "gt2", "gt" }, "race") == "gt2");
            Check("gt4", cat("BMW M235i", new[] { "#gt4" }, "race") == "gt4");
            Check("cup car", cat("Porsche Cup", new[] { "#porsche cup" }, "race") == "porschecup");
            Check("vintage gt", cat("GT40", new[] { "#vintage gt", "vintage" }, "race") == "vintagegt");
            Check("road gt stays road", cat("Road Car", new[] { "gt" }, "street") == "road");
            Check("supercar", cat("Super", new[] { "#supercars" }, "street") == "supercars");
            Check("90s touring", cat("M3", new[] { "#90s touring" }, "race") == "touring90");
            Check("unknown race car", cat("Mystery", new[] { "race" }, "race") == "race");

            var circuit = "{ \"name\": \"Spa\", \"tags\": [\"circuit\"], \"length\": \"7004\", \"pitboxes\": \"24\", \"run\": \"clockwise\" }";
            Check("parses circuit", Content.ParseTrack("spa", "", circuit).Pits == 24);
            Check("rejects p2p", Content.ParseTrack("x", "", circuit.Replace("clockwise", "point-to-point")) == null);
            Check("rejects a2b", Content.ParseTrack("x", "", circuit.Replace("circuit", "A2B")) == null);
            Check("rejects drift layout", Content.ParseTrack("x", "layout_drift", circuit) == null);
            Check("handles km", Math.Abs(Content.ParseTrack("x", "", circuit.Replace("7004", "5.8 km")).Km - 5.8) < 0.001);
        }

        static void TestRaceSetup(string acRoot)
        {
            var c = SeasonCareer(61);
            Engine.EnsureRaceSetup(c);
            var round = c.Season.Rounds[0];
            Check("every round has four weather stages", c.Season.Rounds.All(r => r.WeatherSlots != null && r.WeatherSlots.Count == 4));
            Check("stages are known weathers", c.Season.Rounds.SelectMany(r => r.WeatherSlots).All(id => Engine.WeatherOptions.Any(w => w.Id == id)));

            var old = SeasonCareer(62);
            old.RaceLaps = 0;
            old.LengthMult = 1;
            foreach (var r in old.Season.Rounds) r.WeatherSlots = null;
            Engine.EnsureRaceSetup(old);
            Check("older saves get laps", old.RaceLaps == 5, old.RaceLaps);
            Check("older saves get weather", old.Season.Rounds.All(r => r.WeatherSlots.Count == 4));

            RaceContext context;
            var plainIni = Engine.BuildRaceIni(c, out context);
            Check("lap race has no duration", plainIni.Contains("DURATION_MINUTES=0"));

            c.Format = "endurance";
            c.EnduranceMinutes = 90;
            Check("endurance is timed", Engine.IsTimed(c, round));
            Check("endurance slot length", Engine.WeatherSlotSeconds(c, round) == 90 * 60 / 4 / 10 * 10, Engine.WeatherSlotSeconds(c, round));
            var timedIni = Engine.BuildRaceIni(c, out context);
            Check("endurance ini has duration", timedIni.Contains("DURATION_MINUTES=90"));
            Check("endurance ini has no laps", timedIni.Contains("\nLAPS=0") && timedIni.Contains("RACE_LAPS=0"));

            round.CustomLaps = 7;
            Check("a single race can go back to laps", !Engine.IsTimed(c, round) && Engine.LapsFor(c, round) == 7);
            round.CustomLaps = 0;
            c.Format = "laps";
            round.CustomMinutes = 45;
            Check("a single race can be endurance", Engine.IsTimed(c, round) && Engine.MinutesFor(c, round) == 45);
            round.CustomMinutes = 0;

            var env = new RaceEnv { HasPure = true };
            round.WeatherSlots = new List<int> { 7, 8, 6, 15 };
            var pureIni = Engine.BuildRaceIni(c, env, out context);
            Check("pure controller requested", pureIni.Contains("__CM_WEATHER_CONTROLLER=pureCtrl"));
            Check("first stage sets weather type", pureIni.Contains("__CM_WEATHER_TYPE=7"));
            Check("pure uses a safe base weather", pureIni.Contains("[WEATHER]\nNAME=3_clear"));

            var noPure = new RaceEnv();
            noPure.InstalledWeathers.Add("sol_35_rain");
            var basicIni = Engine.BuildRaceIni(c, noPure, out context);
            Check("no pure controller without Pure", !basicIni.Contains("__CM_WEATHER_CONTROLLER"));
            Check("first stage maps to an installed weather", basicIni.Contains("NAME=sol_35_rain"));

            var presets = Store.ReadPurePresets(acRoot);
            Check("reads Pure's presets", presets.Count >= 30 && Math.Abs(presets[7][0] - 0.125) < 0.0001, presets.Count);
            Check("ignores commented presets", !presets.ContainsKey(29));

            var plan = Store.FromJson<Dictionary<string, object>>(Store.BuildPurePlan(new List<int> { 7, 8, 6, 15 }, 300, presets));
            var control = (Dictionary<string, object>)plan["control"];
            var containers = (System.Collections.ArrayList)plan["container"];
            Check("plan is timed", (int)control["type"] == 2);
            Check("plan has four stages", containers.Count == 4);
            var second = (Dictionary<string, object>)((Dictionary<string, object>)containers[1])["data"];
            var weather = (Dictionary<string, object>)second["weather"];
            Check("plan stage weather", (int)weather["index"] == 8 && (int)second["duration"] == 300);
            Check("plan carries rain values", Convert.ToDouble(weather["rain_amount"]) > 0.5);

            var fakeRoot = Path.Combine(Store.DocsOverride, "acroot");
            Directory.CreateDirectory(fakeRoot);
            var settings = Path.Combine(Store.DocsOverride, "cfg", "extension", "state", "lua", "wfx_controller", "pureCtrl__settings.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(settings));
            File.WriteAllText(settings, "[SETTINGS]\nLAST_USED=1\n");
            Store.PreparePureWeather(fakeRoot, new List<int> { 7, 8, 6, 15 }, 300, new RaceEnv { HasPure = true, Presets = presets });
            Check("plan file written", File.Exists(Path.Combine(fakeRoot, "extension", "config-ext", "PurePlanner", "Plans", "Timed", "PaddockLadder.json")));
            Check("pure pointed at plan", File.ReadAllText(settings).Contains("PLAN=Timed/PaddockLadder") && File.ReadAllText(settings).Contains("LAST_USED=0"));
            Store.PreparePureWeather(fakeRoot, new List<int> { 7, 8, 6, 15 }, 300, new RaceEnv { HasPure = true, Presets = presets });
            Check("second race keeps the original backup", File.ReadAllText(Path.Combine(Store.DataDir, "pure_settings.backup")).Contains("LAST_USED=1"));
            if (Process.GetProcessesByName("acs").Length == 0)
            {
                Store.RestorePureSettings();
                Check("original Pure settings restored", File.ReadAllText(settings) == "[SETTINGS]\nLAST_USED=1\n", File.ReadAllText(settings));
                Check("backup removed", !File.Exists(Path.Combine(Store.DataDir, "pure_settings.backup")));
            }
            Directory.Delete(Store.DocsOverride, true);
        }

        static void TestCareerSlots()
        {
            var data = Store.DataDir;
            Directory.CreateDirectory(data);
            string problem;
            Check("no careers at first", Store.ListCareers().Count == 0 && Store.LoadActiveCareer(out problem) == null);

            var legacy = SeasonCareer(31);
            legacy.Id = null;
            legacy.Driver = "Original";
            var legacyPath = Path.Combine(data, "career.json");
            File.WriteAllText(legacyPath, Store.ToJson(legacy));
            Check("migration reports no problem", Store.MigrateLegacyCareer() == null);
            Check("old save retired, not erased", !File.Exists(legacyPath) && File.Exists(legacyPath + ".migrated"));
            var moved = Store.LoadActiveCareer(out problem);
            Check("migrated career is active", moved != null && moved.Driver == "Original" && moved.Season != null);
            Check("migration keeps season", moved != null && moved.Season.Field.Count == legacy.Season.Field.Count);

            System.Threading.Thread.Sleep(30);
            legacy.Titles = 9;
            File.WriteAllText(legacyPath, Store.ToJson(legacy));
            File.SetLastWriteTimeUtc(legacyPath, DateTime.UtcNow.AddMinutes(1));
            Store.MigrateLegacyCareer();
            Check("later old save updates the slot", Store.ListCareers().Count == 1, Store.ListCareers().Count);
            Check("later old save content", Store.LoadActiveCareer(out problem).Titles == 9);

            var second = SeasonCareer(32);
            second.Driver = "Second";
            Store.SaveCareer(second);
            var third = SeasonCareer(33);
            third.Driver = "Third";
            Store.SaveCareer(third);
            Check("three careers listed", Store.ListCareers().Count == 3);
            Check("summary fields", Store.ListCareers().First(s => s.Driver == "Second").Status.Contains("round 1 of 6"));

            Store.SetActiveCareer(second.Id);
            Check("switching loads the right one", Store.LoadActiveCareer(out problem).Driver == "Second");
            Check("other careers untouched", Store.LoadCareer(third.Id, out problem).Driver == "Third");

            var waiting = new RaceContext { RaceId = "race-for-third" };
            third.Pending = waiting;
            Store.SaveCareer(third);
            Check("finds the career awaiting a result", Store.FindCareerAwaiting("race-for-third").Driver == "Third");
            Check("no owner for unknown race", Store.FindCareerAwaiting("nope") == null);

            Store.DeleteCareer(second.Id);
            Check("delete removes only that one", Store.ListCareers().Count == 2 && !File.Exists(Store.PathFor(second.Id)) && File.Exists(Store.PathFor(third.Id)));
            Check("a deleted career leaves nothing behind", !Directory.Exists(Path.Combine(data, "careers", "deleted")));
            Check("active falls back to newest", Store.LoadActiveCareer(out problem) != null);

            File.WriteAllText(Store.PathFor("broken"), "not valid ((");
            Check("broken save is skipped in the list", Store.ListCareers().Count == 2);
            Check("broken save is preserved", Store.LoadCareer("broken", out problem) == null && problem != null && File.Exists(Store.PathFor("broken") + ".unreadable"));

            Directory.Delete(data, true);
        }

        static void TestInstalled(string acRoot)
        {
            var cars = Content.ScanCars(acRoot);
            var tracks = Content.ScanTracks(acRoot);
            var series = Engine.BuildSeries(cars);
            Console.WriteLine("Installed: {0} cars, {1} layouts, {2} series", cars.Count, tracks.Count, series.Count);
            foreach (var s in series) Console.WriteLine("  {0}: {1} cars", s.Name, s.Cars.Count);
            Check("every tier has a series", Enumerable.Range(1, 5).All(t => series.Any(s => s.Tier == t)));
            Check("every car is in a series", series.Sum(s => s.Cars.Count) == cars.Count, series.Sum(s => s.Cars.Count));

            Func<string, string> classOf = name =>
            {
                var match = cars.FirstOrDefault(x => x.Name == name);
                return match == null ? "missing" : match.ClassKey;
            };
            var exos = series.Where(s => s.Cars.Any(x => x.Name.StartsWith("Lotus Exos 125"))).ToList();
            Check("exos variants share a series", exos.Count == 1 && exos[0].Cars.Count == 2 && exos[0].Name == "Lotus Exos 125", exos.Count);
            Check("modern formula cars are separate", series.Count(s => s.Id.StartsWith("fy:") || s.Id.StartsWith("fcar:")) >= 5);
            Check("single-year series named after the car", series.First(s => s.Id == "fy:2004").Name == "Ferrari F2004", series.First(s => s.Id == "fy:2004").Name);
            var known = new[] {
                "Maserati 250F 6 cylinder|f5060", "Lotus Type 25|f5060", "Ferrari 312/67|f6070", "Lotus Type 49|f6070", "Lotus 72D|f6070", "Ferrari 312T|f6070",
                "Lotus 98T|fturbo", "Ferrari F138|fy:2013", "Ferrari F2004|fy:2004", "Ferrari SF70H|fy:2017", "Lotus Exos 125 Stage 1|fcar:lotus exos 125", "Tatuus FA01|fjunior",
                "Praga R1|lmp3", "Mazda 787B|groupc", "Porsche 919 Hybrid 2016|lmp1", "Ferrari 488 GT3|gt3", "Nissan GT-R GT3|gt3", "Ferrari 458 GT2|gt2",
                "BMW M235i Racing|gt4", "Porsche 911 GT3 Cup 2017|porschecup", "Mazda MX5 Cup|mx5cup", "BMW M3 E30 Group A|touring90", "Ford GT40|vintagegt" };
            foreach (var pair in known)
            {
                var parts = pair.Split('|');
                Check("real class: " + parts[0], classOf(parts[0]) == parts[1], classOf(parts[0]));
            }

            for (var tier = 1; tier <= 5; tier++)
            {
                var c = Engine.NewCareer("Tester", "ladder", 0, 1, 500 + tier);
                c.Tier = tier;
                var offers = Engine.MakeOffers(c, series, null);
                Check("real offers tier " + tier, offers.Count == 3, offers.Count);
                Engine.StartSeason(c, offers[0], series, tracks);
                RaceContext context;
                Check("real ini tier " + tier, Engine.BuildRaceIni(c, out context).Length > 500);
                Check("real images tier " + tier, File.Exists(Content.CarImage(acRoot, offers[0].CarId, offers[0].Skin)));
            }
        }

        static string _shotsDir;

        static void Snap(MainWindow window, string view, string dir, string file)
        {
            window.Show(view);
            var root = window.RootElement;
            var size = new Size(1280, 800);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var stream = File.Create(Path.Combine(dir, file))) encoder.Save(stream);
        }

        static void TestScreens(string acRoot, string outDir)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            U.ApplyTheme(app);
            U.Animate = false;
            Directory.CreateDirectory(outDir);

            var fresh = new MainWindow(acRoot, null);
            Snap(fresh, "new", outDir, "1_new.png");

            var series = Engine.BuildSeries(Content.ScanCars(acRoot));
            var tracks = Content.ScanTracks(acRoot);
            var c = Engine.NewCareer("Arjun", "ladder", 0, 1, 77);
            c.Offers = Engine.MakeOffers(c, series, null);

            var window = new MainWindow(acRoot, null);
            window.UseCareerForTest(c);
            Snap(window, "contracts", outDir, "2_contracts.png");

            Engine.StartSeason(c, c.Offers[0], series, tracks);
            Engine.EnsureRaceSetup(c);
            Snap(window, "paddock", outDir, "3_paddock.png");
            Snap(window, "raceplan", outDir, "11_raceplan.png");

            Engine.ApplyResult(c, Result(c, 3, 3, 1));
            Engine.ApplyResult(c, Result(c, 2, 0, 0));
            Snap(window, "debrief", outDir, "9_debrief.png");
            Snap(window, "paddock", outDir, "4_after_race.png");
            Snap(window, "season", outDir, "5_season.png");

            var open = Engine.NewCareer("Arjun", "open", 4, 1, 88);
            window.UseCareerForTest(open);
            Snap(window, "series", outDir, "6_series.png");
            open.Offers = Engine.MakeOffers(open, series, series.First(s => s.Id == "gt3"));
            Snap(window, "contracts", outDir, "13_gt3_all.png");
            open.Offers = Engine.MakeOffers(open, series, series.First(s => s.Id == "fturbo"));
            Snap(window, "contracts", outDir, "14_onemake.png");
            open.Offers = null;
            Snap(window, "cars", outDir, "12_cars.png");

            var demoEdits = new SeriesEdits();
            demoEdits.Custom.Add(new CustomSeries { Id = "demo", Name = "Sunday Club", Tier = 2, CarIds = series[0].Cars.Take(3).Select(x => x.Id).ToList() });
            window.AddEditsForTest(demoEdits);
            Snap(window, "customseries", outDir, "15_custom.png");
            window.AddTargetForTest("custom:demo");
            Snap(window, "addcar", outDir, "16_addcar.png");

            var dialogBody = Dialog.BuildBody("Delete this series?", "\"Sunday Club\" will be removed. Seasons already under way aren't affected.", "Delete", "Cancel", delegate { }, delegate { });
            var dialogSize = new Size(492, 240);
            dialogBody.Measure(dialogSize);
            dialogBody.Arrange(new Rect(dialogBody.DesiredSize));
            dialogBody.UpdateLayout();
            var dialogBmp = new RenderTargetBitmap((int)dialogBody.DesiredSize.Width, (int)dialogBody.DesiredSize.Height, 96, 96, PixelFormats.Pbgra32);
            dialogBmp.Render(dialogBody);
            var dialogEncoder = new PngBitmapEncoder();
            dialogEncoder.Frames.Add(BitmapFrame.Create(dialogBmp));
            using (var stream = File.Create(Path.Combine(outDir, "17_dialog.png"))) dialogEncoder.Save(stream);

            System.Windows.Size shownDialog = new Size();
            var dialogTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            dialogTimer.Tick += delegate
            {
                dialogTimer.Stop();
                foreach (Window open2 in Application.Current.Windows)
                    if (open2.Title == "Dialog check")
                    {
                        shownDialog = new Size(open2.ActualWidth, open2.ActualHeight);
                        open2.Close();
                    }
            };
            dialogTimer.Start();
            Check("closing a dialog answers no", !Dialog.Confirm(null, "Dialog check", "A real window with the app's styling.", "Delete"));
            Check("the dialog opens at its full size", shownDialog.Width >= 480 && shownDialog.Height >= 150, shownDialog);

            var requester = Engine.NewCareer("Arjun", "ladder", 0, 1, 321);
            requester.Tier = 2;
            requester.Pace = 78;
            requester.Clean = 82;
            requester.LastSeasonScore = 0.8;
            requester.Offers = Engine.MakeOffers(requester, series, null);
            window.UseCareerForTest(requester);
            Snap(window, "seatrequest", outDir, "18_seat.png");
            Snap(window, "contracts", outDir, "19_ladder_offers.png");

            var askedFor = series.First(s => s.Tier == 3 && s.Group == "Road");
            var firstOffers = requester.Offers.ToList();
            Check("a granted seat is allowed in this test", Engine.ResolveSeatRequest(requester, series, askedFor, 0));
            Check("a granted seat keeps your other offers", firstOffers.All(o => requester.Offers.Contains(o)) && requester.Offers.Count > firstOffers.Count, requester.Offers.Count);
            Check("the requested seat is listed first", requester.Offers[0].Requested && requester.Offers[0].SeriesId == askedFor.Id);
            Check("your tier waits until you sign", requester.Tier == 2, requester.Tier);
            Snap(window, "contracts", outDir, "20_offers_with_seat.png");

            var staysPut = Engine.NewCareer("Arjun", "ladder", 0, 1, 322);
            staysPut.Tier = 2;
            staysPut.Pace = 78;
            staysPut.Clean = 82;
            staysPut.LastSeasonScore = 0.8;
            staysPut.Offers = Engine.MakeOffers(staysPut, series, null);
            Engine.ResolveSeatRequest(staysPut, series, askedFor, 0);
            Engine.StartSeason(staysPut, staysPut.Offers.First(o => !o.Requested), series, tracks);
            Check("signing an original offer keeps your tier", staysPut.Tier == 2, staysPut.Tier);
            Engine.StartSeason(requester, requester.Offers[0], series, tracks);
            Check("signing the requested seat moves your tier", requester.Tier == askedFor.Tier, requester.Tier);
            window.UseCareerForTest(c);

            window.Show("settings");
            window.Show("history");
            window.BackForTest();
            Check("back returns to the previous screen", window.CurrentView == "settings", window.CurrentView);
            window.BackForTest();
            Check("back goes further", window.CurrentView == "contracts", window.CurrentView);
            window.UseCareerForTest(c);
            var other = Engine.NewCareer("Second Driver", "open", 4, 1, 99);
            Store.SaveCareer(c);
            Store.SaveCareer(other);
            Snap(window, "careers", outDir, "10_careers.png");
            Snap(window, "history", outDir, "7_career.png");
            Snap(window, "settings", outDir, "8_settings.png");
        }

        // Runs the save migration on a copy of a real career in a temporary folder.
        public static int MigrateCopy(string path)
        {
            Store.DocsOverride = Path.Combine(Path.GetTempPath(), "PaddockLadderMigrate_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Store.DataDir);
                var copy = Path.Combine(Store.DataDir, "career.json");
                File.Copy(path, copy);
                var original = Store.FromJson<Career>(File.ReadAllText(copy));
                Console.WriteLine("problem: " + (Store.MigrateLegacyCareer() ?? "none"));
                string problem;
                var moved = Store.LoadActiveCareer(out problem);
                Console.WriteLine("driver {0} -> {1}, season {2} -> {3}, round {4} -> {5}, races {6} -> {7}, trust {8} -> {9}, pending {10} -> {11}, listed {12}",
                    original.Driver, moved.Driver, original.SeasonNo, moved.SeasonNo,
                    original.Season == null ? -1 : original.Season.RoundIndex, moved.Season == null ? -1 : moved.Season.RoundIndex,
                    original.Stats.Races, moved.Stats.Races, original.Season == null ? -1 : original.Season.Trust, moved.Season == null ? -1 : moved.Season.Trust,
                    original.Pending != null, moved.Pending != null, Store.ListCareers().Count);
                var same = Store.ToJson(new[] { original.Rivals, moved.Rivals }.First()) == Store.ToJson(moved.Rivals);
                Console.WriteLine("rivals identical: " + same);
                return 0;
            }
            finally
            {
                if (Directory.Exists(Store.DocsOverride)) Directory.Delete(Store.DocsOverride, true);
            }
        }

        // Starts one timed race through Content Manager with rain changing to clear. The career file isn't saved.
        public static int LiveWeather(string acRoot)
        {
            string problem;
            var c = Store.LoadActiveCareer(out problem);
            var round = c.Season.Rounds[c.Season.RoundIndex];
            round.WeatherSlots = new List<int> { 8, 15, 7, 16 };
            round.CustomMinutes = 30;
            var env = Store.ReadEnvironment(acRoot);
            RaceContext context;
            var ini = Engine.BuildRaceIni(c, env, out context);
            Console.WriteLine("Pure: {0}, presets {1}, track {2}, cars {3}", env.HasPure, env.Presets.Count, context.TrackName, context.Cars);
            Store.WriteContext(context);
            Store.PreparePureWeather(acRoot, round.WeatherSlots, 50, env);
            Store.StartRace(acRoot, ini);
            return 0;
        }

        // Opens the race setup screen with a dropdown open, on a temporary career, for a few seconds.
        public static int ShowDropdown(string acRoot)
        {
            Store.DocsOverride = Path.Combine(Path.GetTempPath(), "PaddockLadderShow_" + Guid.NewGuid().ToString("N"));
            var app = new Application();
            U.ApplyTheme(app);
            U.Animate = false;
            var series = Engine.BuildSeries(Content.ScanCars(acRoot));
            var tracks = Content.ScanTracks(acRoot);
            var c = Engine.NewCareer("Test", "ladder", 0, 1, 77);
            c.Offers = Engine.MakeOffers(c, series, null);
            Engine.StartSeason(c, c.Offers[0], series, tracks);
            Engine.EnsureRaceSetup(c);

            var window = new MainWindow(acRoot, null) { Topmost = true };
            window.UseCareerForTest(c);
            window.Show("raceplan");
            window.Loaded += delegate
            {
                var open = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                open.Tick += delegate
                {
                    open.Stop();
                    var box = FindFirst<System.Windows.Controls.ComboBox>(window);
                    box.IsDropDownOpen = true;
                };
                open.Start();
                var close = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                close.Tick += delegate { close.Stop(); app.Shutdown(); };
                close.Start();
            };
            var result = app.Run(window);
            if (Directory.Exists(Store.DocsOverride)) Directory.Delete(Store.DocsOverride, true);
            return result;
        }

        static T FindFirst<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var match = child as T ?? FindFirst<T>(child);
                if (match != null) return match;
            }
            return null;
        }

        // A real career near the end of a Rookie season, leading the championship, with only short races left.
        public static int MakeTestCareer(string acRoot, int roundsLeft = 1)
        {
            var cars = Content.ScanCars(acRoot);
            var series = Engine.BuildSeries(cars);
            var tracks = Content.ScanTracks(acRoot);

            var c = Engine.NewCareer(roundsLeft > 1 ? "Next Race Test" : "Promotion Test", "ladder", -14, 1, roundsLeft > 1 ? 707070 : 909090);
            c.AutoDifficulty = false;
            c.RaceLaps = 1;
            c.Format = "laps";
            c.PracticeMinutes = 3;
            c.QualifyingMinutes = 3;
            var pick = series.First(s => s.Id == "small");
            var car = pick.Cars.FirstOrDefault(x => x.Name.Contains("Abarth 500 EsseEsse")) ?? pick.Cars[0];
            var offer = new Offer { SeriesId = pick.Id, SeriesName = pick.Name, Tier = pick.Tier, CarId = car.Id, CarName = car.Name, Brand = car.Brand, Skin = car.Skins[0], Pw = car.Pw, Team = "Halcyon Racing" };
            offer.Clauses.Add(new Clause { Kind = "finishTop", Target = 5, Text = "Finish in the top 5" });
            offer.Clauses.Add(new Clause { Kind = "maxContacts", Target = 1, Text = "No more than 1 contact" });
            Engine.StartSeason(c, offer, series, tracks);

            for (var i = 0; i < Engine.RoundsPerSeason - roundsLeft; i++) Engine.ApplyResult(c, Result(c, 1, 0, 0));
            c.LastReport = null;

            // A different circuit each round, so it's obvious when the next one loads.
            var shortTracks = tracks.Where(t => t.Pits >= c.Season.Field.Count + 1).OrderBy(t => t.Km).Take(roundsLeft).ToList();
            var last = c.Season.Rounds[c.Season.RoundIndex];
            var shortest = shortTracks[0];
            for (var i = 0; i < roundsLeft; i++)
            {
                var round = c.Season.Rounds[c.Season.RoundIndex + i];
                var track = shortTracks[i % shortTracks.Count];
                round.Track = track.Id;
                round.Layout = track.Layout;
                round.TrackName = track.Name;
                round.Km = track.Km;
                round.CustomLaps = 1;
                round.WeatherSlots = new List<int> { 15, 15, 15, 15 };
                round.Weather = "3_clear";
            }

            Store.SaveCareer(c);
            Store.SetActiveCareer(c.Id);
            var table = Engine.Standings(c);
            Console.WriteLine("Saved {0}. Car {1}. Round {2} of 6 at {3}, {4} km, {5} lap. AI {6}%. You: P{7} {8} pts, next: {9} pts", c.Id, c.Season.CarName, c.Season.RoundIndex + 1, last.TrackName, last.Km, Engine.LapsFor(c, last), Engine.AiBase(c, c.Season.Tier), table.FindIndex(r => r.IsPlayer) + 1, table.First(r => r.IsPlayer).Points, table.First(r => !r.IsPlayer).Points);
            Console.WriteLine("Promotion score if you win: {0:0.00}", Engine.PromotionScore(1, table.Count, c.Clean, c.Season.Trust));
            return 0;
        }

        // Prepares a one-lap race at the shortest circuit, with one-minute sessions, for run-game-tests.ps1.
        public static int GameTestSetup(string acRoot, string layout)
        {
            var series = Engine.BuildSeries(Content.ScanCars(acRoot));
            var tracks = Content.ScanTracks(acRoot);
            var c = Engine.NewCareer("Game Test", "ladder", -14, 1, 424242);
            c.AutoDifficulty = false;
            c.RaceLaps = 1;
            var pick = series.First(s => s.Id == "small");
            var car = pick.Cars[0];
            var offer = new Offer { SeriesId = pick.Id, SeriesName = pick.Name, Tier = pick.Tier, CarId = car.Id, CarName = car.Name, Brand = car.Brand, Skin = car.Skins[0], Pw = car.Pw, Team = "Test Team" };
            offer.Clauses.Add(new Clause { Kind = "finishTop", Target = 5, Text = "Finish in the top 5" });
            Engine.StartSeason(c, offer, series, tracks);

            var round = c.Season.Rounds[0];
            var shortest = tracks.OrderBy(t => t.Km).First(t => t.Pits >= c.Season.Field.Count + 1);
            round.Track = shortest.Id;
            round.Layout = shortest.Layout;
            round.TrackName = shortest.Name;
            round.Km = shortest.Km;
            round.Weather = "3_clear";
            round.CustomLaps = 1;
            round.PracticeOverride = layout.Contains("p") ? 1 : -1;
            round.QualifyingOverride = layout.Contains("q") ? 1 : -1;

            RaceContext context;
            var ini = Engine.BuildRaceIni(c, null, out context);
            Store.WriteContext(context);
            Store.InstallGameApp(acRoot);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Store.DataDir), "cfg", "race.ini"), ini, new System.Text.UTF8Encoding(false));
            Console.WriteLine("Prepared {0} at {1}, {2} cars", car.Name, shortest.Name, context.Cars);
            return 0;
        }

        // Plays the real track through the real player, almost silently, and reports whether it ran.
        public static int MusicCheck(string acRoot)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Store.InstallGameApp(acRoot);
            var music = new MenuMusic(Path.Combine(acRoot, "apps", "lua", "PaddockLadder", "music", "menu.mp3"));
            music.Apply(true, 1);
            Sfx.Configure(true, 1);
            foreach (var name in new[] { "hover", "click", "scroll", "confirm" })
            {
                Console.WriteLine("sound {0}: {1}", name, Sfx.PlayForCheck(name));
                System.Threading.Thread.Sleep(600);
            }
            var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            stop.Tick += delegate { stop.Stop(); app.Shutdown(); };
            stop.Start();
            app.Run();
            Console.WriteLine("position {0:0.0}s, length {1:0.0}s, broken {2}", music.PositionForTest.TotalSeconds, music.DurationForTest.TotalSeconds, music.BrokenForTest);
            return music.PositionForTest.TotalSeconds > 2 && music.DurationForTest.TotalSeconds > 150 && !music.BrokenForTest ? 0 : 1;
        }

        // A driver two poor results away from the sack, racing GT3 on a short circuit.
        public static int TrailerSetup(string acRoot, bool alreadyOnNotice)
        {
            var series = Engine.BuildSeries(Content.ScanCars(acRoot));
            var tracks = Content.ScanTracks(acRoot);
            var c = Engine.NewCareer("Alex Mercer", "ladder", -14, 1, 717171);
            c.AutoDifficulty = false;
            var pick = series.FirstOrDefault(s => s.Id == "gt3") ?? series.OrderByDescending(s => s.Cars.Count).First();
            var car = pick.Cars[pick.Cars.Count / 2];
            c.Tier = pick.Tier;
            var offer = new Offer { SeriesId = pick.Id, SeriesName = pick.Name, Tier = pick.Tier, CarId = car.Id, CarName = car.Name, Brand = car.Brand, Skin = car.Skins[0], Pw = car.Pw, Team = "Ironbridge Racing" };
            offer.Clauses.Add(new Clause { Kind = "finishTop", Target = 3, Text = "Finish in the top 3" });
            offer.Clauses.Add(new Clause { Kind = "maxContacts", Target = 0, Text = "Cause no contact" });
            c.Credits = 400000;
            Engine.StartSeason(c, offer, series, tracks);
            for (var i = 0; i < 2; i++) Engine.ApplyResult(c, Result(c, 14 - i * 3, 0, 0));
            var venue = tracks.Where(t => t.Pits >= 20 && t.Name.Contains("Brands Hatch") && t.Name.Contains("Indy")).FirstOrDefault() ?? tracks.Where(t => t.Pits >= 20).OrderBy(t => t.Km).First();
            foreach (var round in c.Season.Rounds.Skip(c.Season.RoundIndex))
            {
                round.Track = venue.Id;
                round.Layout = venue.Layout;
                round.TrackName = venue.Name;
                round.Km = venue.Km;
                round.Weather = "3_clear";
                round.WeatherSlots = new List<int> { 15, 15, 15, 15 };
                round.CustomLaps = 1;
                round.PracticeOverride = -1;
                round.QualifyingOverride = -1;
            }
            c.Season.Trust = alreadyOnNotice ? 20 : 35;
            c.Season.OnNotice = alreadyOnNotice;
            c.Season.NoticeTrust = 20;
            c.Season.Released = false;
            c.Pace = 92;
            c.Clean = 88;
            c.LastSeasonScore = 0.9;
            c.LicencePoints = 0;
            c.Credits = 240000;
            Store.SaveCareer(c);
            Store.SetActiveCareer(c.Id);
            Console.WriteLine("Saved {0}: {1} at {2}", c.Driver, car.Name, venue.Name);
            return 0;
        }

        // A full weekend on the shortest track with one-minute sessions, for run-weekend-test.ps1.
        public static int WeekendFlowSetup(string acRoot)
        {
            var series = Engine.BuildSeries(Content.ScanCars(acRoot));
            var tracks = Content.ScanTracks(acRoot);
            var c = Engine.NewCareer("Weekend Test", "ladder", -14, 1, 616161);
            c.AutoDifficulty = false;
            var pick = series.First(s => s.Id == "small");
            var car = pick.Cars[0];
            var offer = new Offer { SeriesId = pick.Id, SeriesName = pick.Name, Tier = pick.Tier, CarId = car.Id, CarName = car.Name, Brand = car.Brand, Skin = car.Skins[0], Pw = car.Pw, Team = "Test Team" };
            offer.Clauses.Add(new Clause { Kind = "finishTop", Target = 8, Text = "Finish in the top 8" });
            Engine.StartSeason(c, offer, series, tracks);
            var shortest = tracks.OrderBy(t => t.Km).First(t => t.Pits >= c.Season.Field.Count + 1);
            var round = c.Season.Rounds[0];
            round.Track = shortest.Id;
            round.Layout = shortest.Layout;
            round.TrackName = shortest.Name;
            round.Km = shortest.Km;
            round.Weather = "3_clear";
            round.WeatherSlots = new List<int> { 15, 15, 15, 15 };
            round.CustomLaps = 5;
            round.PracticeOverride = 1;
            round.QualifyingOverride = 1;
            c.Credits = 500000;
            Store.SaveCareer(c);
            Store.SetActiveCareer(c.Id);
            Console.WriteLine("Saved {0}: {1}", c.Driver, shortest.Name);
            return 0;
        }

        // Two one-lap rounds left in a season, for run-finish-test.ps1.
        public static int FinishFlowSetup(string acRoot)
        {
            var series = Engine.BuildSeries(Content.ScanCars(acRoot));
            var tracks = Content.ScanTracks(acRoot);
            var c = Engine.NewCareer("Finish Test", "ladder", -14, 1, 515151);
            c.AutoDifficulty = false;
            c.RaceLaps = 1;
            var pick = series.First(s => s.Id == "small");
            var car = pick.Cars[0];
            var offer = new Offer { SeriesId = pick.Id, SeriesName = pick.Name, Tier = pick.Tier, CarId = car.Id, CarName = car.Name, Brand = car.Brand, Skin = car.Skins[0], Pw = car.Pw, Team = "Test Team" };
            offer.Clauses.Add(new Clause { Kind = "finishTop", Target = 8, Text = "Finish in the top 8" });
            offer.Clauses.Add(new Clause { Kind = "maxContacts", Target = 3, Text = "No more than 3 contacts" });
            Engine.StartSeason(c, offer, series, tracks);

            for (var i = 0; i < Engine.RoundsPerSeason - 2; i++) Engine.ApplyResult(c, Result(c, 2, 0, 0));
            var shortest = tracks.OrderBy(t => t.Km).First(t => t.Pits >= c.Season.Field.Count + 1);
            foreach (var round in c.Season.Rounds.Skip(c.Season.RoundIndex))
            {
                round.Track = shortest.Id;
                round.Layout = shortest.Layout;
                round.TrackName = shortest.Name;
                round.Km = shortest.Km;
                round.Weather = "3_clear";
                round.WeatherSlots = new List<int> { 15, 15, 15, 15 };
                round.CustomLaps = 1;
                round.PracticeOverride = -1;
                round.QualifyingOverride = -1;
            }
            // Damage from "earlier in the season", to prove it reaches the next race and can be repaired.
            c.CarBody = new List<double> { 30, 0, 0, 0 };
            c.CarEngine = 800;
            c.CarGearbox = 0.5;
            c.CarSuspension = new List<double> { 0.6, 0, 0, 0 };
            c.Credits = 500000;

            // Every rival holds a grudge, so at least the cars nearby should turn fierce on the grid.
            foreach (var entry in c.Season.Field)
            {
                var holder = c.Rivals.First(r => r.Name == entry.Name);
                holder.Aggression = 10;
                holder.Grudge = 2;
                entry.Aggression = 60;
            }
            Store.SaveCareer(c);
            Store.SetActiveCareer(c.Id);
            Console.WriteLine("Saved {0}: round {1} of 6 at {2}", c.Driver, c.Season.RoundIndex + 1, shortest.Name);
            return 0;
        }

        static int SeatChanceWithRefusals(Career c, SeriesDef series, int refusals)
        {
            var saved = c.SeatRefusals;
            c.SeatRefusals = refusals;
            var chance = Engine.SeatRequestOdds(c, series).Chance;
            c.SeatRefusals = saved;
            return chance;
        }

        public static int ParseResult(string path)
        {
            var result = Store.FromJson<RaceResult>(File.ReadAllText(path));
            Console.WriteLine("RaceId={0} Finished={1} Cuts={2} Laps={3} Rows={4} Contacts={5} PlayerPos={6}", result.RaceId, result.Finished, result.Cuts,
                result.Laps, result.Rows.Count, result.Contacts.Count, result.Rows.First(r => r.IsPlayer).Pos);
            return 0;
        }

        public static int Run(string[] args)
        {
            var acRoot = args[1];
            var outDir = args[2];
            _shotsDir = outDir;
            Store.DocsOverride = Path.Combine(Path.GetTempPath(), "PaddockLadderTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Action<string, Action> guarded = (name, test) =>
                {
                    try { test(); }
                    catch (Exception ex) { _failed++; Console.WriteLine("CRASH " + name + ": " + ex); }
                };
                guarded("rules", TestRules);
                guarded("judging", TestJudging);
                guarded("seasons", TestSeasons);
                guarded("parsing", TestParsing);
                guarded("race setup", delegate { TestRaceSetup(acRoot); });
                guarded("slots", TestCareerSlots);
                guarded("installed", delegate { TestInstalled(acRoot); });
                guarded("screens", delegate { TestScreens(acRoot, outDir); });
                guarded("flows", TestFlows);
                guarded("performance", delegate { TestPerformance(acRoot); });
            }
            finally
            {
                if (Directory.Exists(Store.DocsOverride)) Directory.Delete(Store.DocsOverride, true);
            }
            Console.WriteLine("passed {0}, failed {1}", _passed, _failed);
            return _failed == 0 ? 0 : 1;
        }
    }
}
#endif
