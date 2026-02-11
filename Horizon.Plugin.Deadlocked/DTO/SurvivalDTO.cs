using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.DTO
{
    public class SurvivalMapDTO
    {
        public string Name { get; set; }
        public string Filename { get; set; }
    }

    public class SurvivalAccountStatDTO
    {
        public int AccountId { get; set; }
        public decimal TotalPercentCompleted { get; set; }
        public long? Ranking { get; set; }

        public long GamesPlayed { get; set; }
        public long TimePlayedMs { get; set; }
        public long Kills { get; set; }
        public long Deaths { get; set; }
        public long Revives { get; set; }
        public long TimesRevived { get; set; }

        public long WrenchKills { get; set; }
        public long DualViperKills { get; set; }
        public long MagmaCannonKills { get; set; }
        public long ArbiterKills { get; set; }
        public long FusionRifleKills { get; set; }
        public long MineLauncherKills { get; set; }
        public long B6Kills { get; set; }
        public long HoloshieldKills { get; set; }
        public long ScorpionFlailKills { get; set; }
    }

    public class SurvivalAccountMapStatDTO
    {
        public int AccountId { get; set; }
        public string MapFilename { get; set; }
        public int Xp { get; set; }
        public int Rank { get; set; }
        public int Prestige { get; set; }
        public decimal PercentCompleted { get; set; }

        public int? SoloRound { get; set; }
        public long? Solo50 { get; set; }
        public int? CoopRound { get; set; }
        public long? Coop50 { get; set; }

        public long? SoloRoundRanking { get; set; }
        public long? Solo50Ranking { get; set; }
        public long? OverallRanking { get; set; }

        public List<SurvivalAccountMapGambitStatDTO> Gambits { get; set; } = [];
    }

    public class SurvivalAccountMapGambitStatDTO
    {
        public string Gambit { get; set; }
        public bool Completed { get; set; }
        public int BestRound { get; set; }
    }

    public class SurvivalAccountMapGambitStatPostDTO
    {
        public int AccountId { get; set; }
        public string MapFilename { get; set; }
        public string Gambit { get; set; }
        public bool? Completed { get; set; }
        public int? BestRound { get; set; }
    }

    public class SurvivalRunDTO
    {
        public int Id { get; set; }
        public int GameHistoryId { get; set; }
        public int PlayerCountAtStart { get; set; }
        public string MapFilename { get; set; }
        public string? Gambit { get; set; }
        public int RoundsCompleted { get; set; }
        public long TimeMs { get; set; }
        public long? Time50Ms { get; set; }
        public int[] AccountIds { get; set; } = [];
    }

}
