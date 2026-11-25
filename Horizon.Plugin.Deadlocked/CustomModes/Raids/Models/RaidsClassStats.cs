using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsClassStats
    {
        public const int MAX_SKILLS = 16;

        public ulong Experience { get; set; }

        // numeric keys keep persisted allocations tied to stable skill IDs
        public Dictionary<int, int> SkillRanks { get; set; } = [];

        public static void Serialize(BinaryWriter writer, RaidsClassType classType, RaidsClassStats stats)
        {
            writer.Write((int)classType);

            var ranks = stats?.SkillRanks.OrderBy(x => x.Key).ToArray() ?? [];
            for (int i = 0; i < MAX_SKILLS; ++i)
            {
                writer.Write(i < ranks.Length ? (byte)ranks[i].Key : (byte)0);
                writer.Write(i < ranks.Length ? (byte)ranks[i].Value : (byte)0);
            }
        }
    }
}
