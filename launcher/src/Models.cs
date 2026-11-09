using System;
using System.Collections.Generic;

namespace PaddockLadder
{
    public class CarInfo
    {
        public string Id, Name, Brand, ClassKey, AutoClassKey, Torque, TopSpeed, Acceleration, Country, Description;
        public double Bhp, Kg, Pw;
        public List<string> Skins = new List<string>();
    }

    public class TrackInfo
    {
        public string Id, Layout, Name;
        public double Km;
        public int Pits;
    }

    public class ClassDef
    {
        public string Key, Name, Group;
        public int Tier;
    }

    public class CustomSeries
    {
        public string Id, Name;
        public int Tier = 4;
        public List<string> CarIds = new List<string>();
    }

    // The player's own series, and cars they've added to existing ones. Shared by every career.
    public class SeriesEdits
    {
        public List<CustomSeries> Custom = new List<CustomSeries>();
        public Dictionary<string, List<string>> Additions = new Dictionary<string, List<string>>();
    }

    public class SeriesDef
    {
        public string Id, Name, Group;
        public int Tier;
        public List<CarInfo> Cars = new List<CarInfo>();
    }

    public class Rival
    {
        public string Name, Nation, Note;
        public int Skill, Aggression, Wins, Titles, Grudge;
    }

    public class Clause
    {
        public string Kind, Text;
        public int Target;
    }

    public class Offer
    {
        public string SeriesId, SeriesName, CarId, CarName, Brand, Skin, Team;
        public int Tier;
        public double Pw;
        public bool Requested;
        public List<Clause> Clauses = new List<Clause>();

        // A move during the season: the seat it replaces, and how much of the season is left to race.
        public bool MidSeason, BuyoutPaid;
        public string ReplacesTeam;
        public int RoundsLeft;
    }

    // How one driver's season has gone, used to compare them with the player.
    public class DriverForm
    {
        public int Starts, TotalPos, Best, Podiums, BestMs, AheadOfYou, BehindYou;
    }

    public class GridEntry
    {
        public string Name, Nation, Car, CarName, Skin, Team;
        public int Skill, Aggression;
        public bool IsTeammate;
    }

    public class Round
    {
        public string Track, Layout, TrackName, Weather, Winner;
        public double Km;
        public int Ambient, Road, Sun, Pos, StartPos, Points, Cuts, Contacts, ClausesMet, ClausesTotal, CustomLaps, CustomMinutes;

        // 0 follows the career setting, -1 turns the session off, and anything else is its length in minutes.
        public int PracticeOverride, QualifyingOverride;
        public bool Done, Forfeit, Retired;
        public List<int> WeatherSlots;
    }

    public class WeatherDef
    {
        public int Id;
        public string Name, Group, BaseName;
    }

    // What this player's install can do with weather.
    public class RaceEnv
    {
        public bool HasPure;
        public Dictionary<int, double[]> Presets = new Dictionary<int, double[]>();
        public HashSet<string> InstalledWeathers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public class Season
    {
        public string SeriesId, SeriesName, CarId, CarName, Skin, Team;
        public int Tier, RoundIndex, Trust, LastFinish;
        public bool OnLoan;

        // A team can warn you and then cancel your contract. Each team can only be approached once a season.
        public bool OnNotice, Released;
        public string ChosenRival;
        public int NoticeTrust, MarketRefusals;
        public List<string> MarketAsked = new List<string>();
        public Dictionary<string, DriverForm> Form = new Dictionary<string, DriverForm>();

        public List<Clause> Clauses = new List<Clause>();
        public List<Round> Rounds = new List<Round>();
        public List<GridEntry> Field = new List<GridEntry>();
        public Dictionary<string, int> Points = new Dictionary<string, int>();
        public Dictionary<string, int> Wins = new Dictionary<string, int>();
    }

    // Written by the launcher and read by the in-game app.
    public class RaceContext
    {
        public string RaceId, Track, Layout, Car, FirstRival, Teammate, SeriesName, TrackName, Team;
        public int Cars, Laps, RoundNumber, Credits, RepairRate, RespectShift;
        public List<string> MainRivals = new List<string>();
        public string ChosenRival;
        public bool SkipQualifying;

        // Goals for the sessions before the race, and the damage this car brings with it.
        public int PracticeGoalLaps, PracticeReward, QualifyTop, QualifyReward;
        public bool DamageOff;
        public double CarEngine = 1000, CarGearbox;
        public List<double> CarBody, CarSuspension;
        public List<Rivalry> Rivalries = new List<Rivalry>();
        public List<Clause> Clauses = new List<Clause>();
        public Dictionary<string, string> Notes = new Dictionary<string, string>();
    }

    // A driver who holds a grudge races harder, but only while close to the player.
    public class Rivalry
    {
        public string Name;
        public int Normal, Angry;
    }

    public class CreditLine
    {
        public string Label;
        public int Amount;
    }

    public class CarCost
    {
        public int Price, TradeIn, Pay, SeatFee, Buyout;
        public bool Owned, Loan;
        public string Text;
    }

    // One part of the car that can be repaired on its own.
    public class DamagePart
    {
        public string Id, Name, Severity, Effect, IfLeft;
        public int Cost, HalfCost;
    }

    // One thing the player was asked to do this weekend, and whether it was done.
    public class TargetLine
    {
        public string Session, Text;
        public bool Met;
        public int Credits;
    }

    // A place the player could move to during the season.
    public class MarketSeat
    {
        public string SeriesId, SeriesName, Group, Team, Name, TierName, Reason;
        public int Tier, Chance;
        public bool Allowed, Asked, SameSeries;
    }

    public class CompareRow
    {
        public string Label, Mine, Theirs;
        public int Edge; // 1 means you're ahead, -1 means they are
    }

    public class Matchup
    {
        public string Name, Team, Tag, Summary;
        public List<CompareRow> Rows = new List<CompareRow>();
    }

    public class TeamStanding
    {
        public string Name;
        public int Points, Wins;
        public bool IsPlayer;
    }

    // Everything to know about the next race before loading it.
    public class RaceBriefing
    {
        public string RoundText = "", SeriesName = "", TrackName = "", Image = "", LengthText = "", Weekend = "", Forecast = "", Team = "", Teammate = "", CarName = "", StartText = "", FinesText = "", PrizeText = "";
        public int Grid, Ambient, MaxCredits;
        public bool OnLoan;
        public List<TargetLine> Targets = new List<TargetLine>();
        public List<string> Matchups = new List<string>();
    }

    public class ResultRow
    {
        public string Name;
        public int Pos, BestMs;
        public bool IsPlayer;
    }

    public class ContactRecord
    {
        public string Driver;
        public int Count;
    }

    // One contact as the game saw it. Only those ruled against the player count as Contacts.
    public class IncidentRecord
    {
        public string Driver, Who, Kind;
        public int Lap;
    }

    // The stewards' ruling, worded for the screen.
    public class IncidentLine
    {
        public string Text, Verdict, Who;
        public int Lap;
    }

    // Written by the in-game app when the player takes the flag, and every few seconds before that
    // so what happened still counts if the game is closed. Damage figures are the car's running totals.
    public class RaceResult
    {
        public string RaceId, Session;
        public bool Finished, RaceStarted;
        public int Cuts, Laps, StartPos, PracticeLaps;
        public double EngineLife, Gearbox;
        public List<double> Body, Suspension;
        public List<ResultRow> Rows = new List<ResultRow>();
        public List<ContactRecord> Contacts = new List<ContactRecord>();
        public List<IncidentRecord> Incidents = new List<IncidentRecord>();
    }

    public class RoundReport
    {
        public string Title, SeasonOutcome, SeriesName = "", DebtNote = "", ContractTitle = "", ContractText = "";
        public int Pos, StartPos, Grid, Points, Cuts, Contacts, TrustDelta, LicenceDelta, ScoreTotal, ScorePosition, ScoreClean, ScoreTrust;
        public bool Forfeit, Retired, Released;
        public List<string> Lines = new List<string>();

        public int CreditsNet, CreditsBalance;
        public List<CreditLine> CreditLines = new List<CreditLine>();
        public List<TargetLine> Targets = new List<TargetLine>();
        public List<IncidentLine> Incidents = new List<IncidentLine>();
        public List<DebriefRow> Classification = new List<DebriefRow>();

        // The championship as it stood after this race, kept because the season is cleared when it ends.
        public List<Standing> Table;
        public List<TeamStanding> TeamTable;
    }

    public class DebriefRow
    {
        public string Name, Team = "", Tag = "";
        public int Pos, BestMs, Points, Wins;
        public bool IsPlayer;
    }

    public class DebriefOffer
    {
        public int Index;
        public string Team, CarName, SeriesName, Image = "", Cost = "", Brand = "", TierName = "", Torque = "", TopSpeed = "", Acceleration = "", Country = "", Description = "";
        public bool Loan, Owned;
        public int CreditsAfter, Price, TradeIn, Pay, Bhp, Kg, SeatFee, Buyout;
        public bool MidSeason;
        public double Pw;
        public bool Requested;
        public List<string> Clauses = new List<string>();
    }

    public class DebriefSeat
    {
        public string SeriesId, Name, Group, TierName, Reason;
        public int Chance;
        public bool Allowed, Asked, Market;
        public string Team = "";
    }

    public class DebriefSeries
    {
        public string SeriesId, Name, Group, Detail, Image = "";
    }

    // Written by the launcher once a race is scored, and shown by the in-game results screen.
    public class GameDebrief
    {
        // Between seasons the game can do everything the launcher's offer pages can.
        public bool CanChoose, Ladder, Signed, MusicOn, SoundsOn;
        public int Revision, DriverRating, Titles, SeasonNo, MusicVolume, SoundsVolume;
        public int CreditsNet, CreditsBalance, RepairCost;
        public bool CanAffordRepair, SessionReview;
        public List<DamagePart> Parts = new List<DamagePart>();
        public List<TargetLine> Targets = new List<TargetLine>();
        public List<IncidentLine> Incidents = new List<IncidentLine>();
        public List<DebriefRow> TeamStandings = new List<DebriefRow>();
        public RaceBriefing Briefing;
        public string TeamName = "", ChampionName = "", ChampionTeam = "", TeamChampion = "", DebtNote = "", ContractTitle = "", ContractText = "", MarketNote = "";
        public bool Released, CanLeave;
        public int Buyout, TeamTrust;
        public List<DebriefSeat> Market = new List<DebriefSeat>();
        public List<DebriefRow> RivalChoices = new List<DebriefRow>();
        public string ChosenRival = "";
        public DebriefOffer MarketOffer;
        public int FinalRank, TeamRank;
        public double CarEngine = 1000, CarGearbox;
        public List<double> CarBody, CarSuspension;
        public string DamageText = "";
        public List<CreditLine> CreditLines = new List<CreditLine>();
        public string OffersTitle = "", OffersHint = "", SeatNote = "", TierName = "", Licence = "", ReplyTitle = "", ReplyText = "";
        public List<DebriefOffer> Offers = new List<DebriefOffer>();
        public List<DebriefSeat> Seats = new List<DebriefSeat>();
        public List<DebriefSeries> SeriesChoices = new List<DebriefSeries>();

        public string RaceId, Title = "", SeriesName = "", SeasonOutcome = "", NextTitle = "", NextDetail = "", ContinueLabel = "", ScoreTitle = "", ScoreVerdict = "";
        public int Pos, Grid, StartPos, Points, Cuts, Contacts, TrustDelta, LicenceDelta, ScoreTotal, ScorePosition, ScoreClean, ScoreTrust, PromotionMark;
        public bool HasNextRace, ShowScore, ScoreGood, ScoreDanger;
        public List<string> Lines = new List<string>();
        public List<DebriefRow> Classification = new List<DebriefRow>();
        public List<DebriefRow> Standings = new List<DebriefRow>();
    }

    // Written by the in-game results screen when the player chooses what to do next.
    public class RaceCommand
    {
        public string RaceId, Action, SeriesId, Part, Team;
        public int Seq, OfferIndex;
        public bool Half;
    }

    public class HistoryEntry
    {
        public int SeasonNo, Rank, Points;
        public string SeriesName, CarName, Team, Champion, Outcome;
    }

    public class PromotionOutlook
    {
        public int Total, Position, Clean, Trust;
        public string Verdict;
        public bool Good, Danger;
    }

    public class SeatOdds
    {
        public bool Allowed, Asked;
        public int Chance, Skip;
        public string Reason;
    }

    public class CareerStats
    {
        public int Races, Wins, Podiums, CleanRaces, Poles;
    }

    public class CareerSummary
    {
        public string Id, Driver, Mode, Status, CarId, Skin;
        public int Tier, SeasonNo;
        public DateTime LastPlayed;
    }

    public class Career
    {
        public int Version = 1;
        public string Id, Driver, Mode;
        public int Seed, Tier = 1, SeasonNo = 1, BaseDifficulty, AutoOffset, LicencePoints, Titles, RaceLaps, EnduranceMinutes = 60;
        public string Format = "laps", LastSeriesId, SeatNote;
        public double LastSeasonScore;

        // Reset each off-season: the series already approached, and how many moves up were turned down.
        public List<string> SeatRequests = new List<string>();

        // Money, and the one car you own. Damage stays with the car until it's repaired.
        public int Credits = Engine.StartCredits, OwnedCarPaid;
        public string OwnedCarId, OwnedCarName;
        public bool DamageOn = true;
        public double CarEngine = 1000, CarGearbox;
        public List<double> CarBody = new List<double> { 0, 0, 0, 0 };
        public List<double> CarSuspension = new List<double> { 0, 0, 0, 0 };
        public int SeatRefusals;
        public bool AutoDifficulty = true, PracticeOn = true, QualifyingOn = true;
        public int PracticeMinutes = 10, QualifyingMinutes = 10;
        public double LengthMult = 1, Pace = 50, Clean = 70;
        public CareerStats Stats = new CareerStats();
        public List<Rival> Rivals = new List<Rival>();
        public List<HistoryEntry> History = new List<HistoryEntry>();
        public List<Offer> Offers;
        public Offer MarketOffer;
        public Season Season;
        public RaceContext Pending;
        public RoundReport LastReport;
    }
}
