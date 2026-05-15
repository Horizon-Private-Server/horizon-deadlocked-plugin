using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.DTO
{
    public class ForgeCgmMapDTO
    {
        public string Name { get; set; }
        public string MapFilename { get; set; }
        public string? SharedRankCode { get; set; }
        public string Metadata { get; set; }
    }

    public class ForgeCgmMapAccountStatDTO
    {
        public int AccountId { get; set; }
        public string MapFilename { get; set; }

        public int Rank { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int GamesPlayed { get; set; }
        public long TimePlayedMs { get; set; }

        public long[] TrackedStats { get; set; } = new long[8];
    }

    public class ForgeCgmMapAggregatedAccountStatDTO
    {
        public int AccountId { get; set; }
        public long? Ranking { get; set; }

        public int Rank { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int GamesPlayed { get; set; }
        public long TimePlayedMs { get; set; }

        public long[] TrackedStats { get; set; } = new long[8];
    }

}
