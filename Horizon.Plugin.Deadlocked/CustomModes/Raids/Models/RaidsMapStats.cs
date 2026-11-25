using Server.Common.Stream;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsMapStats
    {
        public string Name { get; set; }
        public int CollectiblesCount { get; set; }
        public uint CollectiblesMask { get; set; }

        public int ChallengesCount { get; set; }
        public uint ChallengesMask { get; set; }

        public uint[] CompletionsPerDifficulty { get; set; } = new uint[5];

        public uint[] BestTimeMsPerDifficulty { get; set; } = new uint[5];

        public RaidsMissionType MissionType { get; set; } = RaidsMissionType.Hub;

        public List<RaidsContractRule> ContractRules { get; set; } = new List<RaidsContractRule>();

        public static uint CountCompleted(uint mask, int count)
        {
            uint validMask = count <= 0 ? 0u : count >= 32 ? uint.MaxValue : (1u << count) - 1;
            return (uint)BitOperations.PopCount(mask & validMask);
        }

        public float GetPercentComplete(bool ignoreTimes = false)
        {
            float max = 0;
            float sum = 0;

            if (CollectiblesCount > 0)
            {
                max += CollectiblesCount;
                sum += CountCompleted(CollectiblesMask, CollectiblesCount);
            }

            if (ChallengesCount > 0)
            {
                max += ChallengesCount;
                sum += CountCompleted(ChallengesMask, ChallengesCount);
            }

            // limit to 5 (50% of progress)
            if (max > 5)
            {
                var ratio = 5 / max;
                max *= ratio;
                sum *= ratio;
            }

            if (!ignoreTimes)
            {
                max += 5;
                sum += BestTimeMsPerDifficulty.Take(5).Count(x => x > 0);
            }

            if (max == 0) return 0;

            return sum / max;
        }

        public void Serialize(BinaryWriter writer, string mapFilename, RaidsMissionType missionType)
        {
            writer.Write(0); // is valid
            writer.Write(CollectiblesCount);
            writer.Write(CollectiblesMask);
            writer.Write(ChallengesCount);
            writer.Write(ChallengesMask);
            writer.Write(GetPercentComplete(missionType != RaidsMissionType.Raid));
            for (int i = 0; i < 5; ++i)
                writer.Write(BestTimeMsPerDifficulty[i]);
            writer.Write(mapFilename, 64);
        }
    }
}
