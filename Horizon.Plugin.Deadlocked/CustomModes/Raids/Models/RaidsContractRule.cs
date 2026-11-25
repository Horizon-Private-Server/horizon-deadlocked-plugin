using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsContractRule
    {
        public RaidsContractType Type { get; set; }
        public ushort MobDefMask { get; set; }
        public ushort MinCount { get; set; }
        public ushort MaxCount { get; set; }
        public uint MinHeroLevel { get; set; } = 1;
        public uint CountIncrement { get; set; } = 1;
        public float RewardRamp { get; set; } = 1;
        public float PartyHeroLevelRamp { get; set; } = 1;
        public float XpMult { get; set; }
        public float BoltMult { get; set; }
        public float ClassXpMult { get; set; }
        public string Name { get; set; }
        public RaidsContractDifficulty[] Difficulties { get; set; }

        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return false;

            // names must fit in the 32-byte field with a null terminator
            if (Encoding.UTF8.GetByteCount(Name) > 31 || Name.Any(char.IsControl))
                return false;

            if (Type == RaidsContractType.Kill || Type == RaidsContractType.Task)
            {
                if ((Type == RaidsContractType.Kill && MobDefMask == 0) || (Type == RaidsContractType.Task && MobDefMask != 0) || MinCount == 0 || MaxCount < MinCount || MinHeroLevel < 1 || MinHeroLevel > 99)
                    return false;

                if (CountIncrement < 1 || CountIncrement > ushort.MaxValue
                    || !float.IsFinite(RewardRamp) || RewardRamp < 0
                    || !float.IsFinite(PartyHeroLevelRamp) || PartyHeroLevelRamp < -4 || PartyHeroLevelRamp > 4)
                    return false;

                if (!float.IsFinite(XpMult) || XpMult < 0)
                    return false;
                if (!float.IsFinite(BoltMult) || BoltMult < 0)
                    return false;
                if (!float.IsFinite(ClassXpMult) || ClassXpMult < 0)
                    return false;

                return true;
            }
            else if (Type == RaidsContractType.Clear)
            {
                if (MobDefMask != 0 || Difficulties?.Length != 5)
                    return false;

                foreach (var difficulty in Difficulties)
                {
                    if (difficulty == null || difficulty.Reward == null || difficulty.SpeedBonus == null
                        || (difficulty.TargetTimeSeconds != null && (difficulty.TargetTimeSeconds.Length != 5 || difficulty.TotalReward == null))
                        || !float.IsFinite(difficulty.BonusRewardFactor) || difficulty.BonusRewardFactor < 0 || difficulty.BonusRewardFactor > 1)
                        return false;
                }

                return true;
            }

            return false;
        }

        public double GetRewardCount(uint count)
        {
            // clamp to the count range and preserve endpoints for fixed counts and a zero reward ramp
            if (count <= MinCount || MaxCount <= MinCount) return MinCount;
            if (count >= MaxCount) return MaxCount;
            if (RewardRamp == 1) return count;

            // apply the reward ramp within the count range to get the count used for reward multipliers
            var normalized = (double)(count - MinCount) / (MaxCount - MinCount);
            return MinCount + (MaxCount - MinCount) * Math.Pow(normalized, RewardRamp);
        }

        public RaidsContractReward GetReward(uint count)
        {
            var rewardCount = GetRewardCount(count);
            var heroXp = RaidsContractReward.Saturate(rewardCount * XpMult);
            return new()
            {
                Bolts = RaidsContractReward.Saturate(rewardCount * BoltMult),
                PlayerXp = heroXp,
                WeaponXp = (uint)((ulong)heroXp * 3 / 10),
                ClassXp = RaidsContractReward.Saturate(rewardCount * ClassXpMult)
            };
        }

        public void Deserialize(BinaryReader reader)
        {
            Type = (RaidsContractType)reader.ReadUInt16();
            MobDefMask = reader.ReadUInt16();
            Name = Encoding.UTF8.GetString(reader.ReadBytes(32)).TrimEnd('\0');

            if (Type == RaidsContractType.Kill || Type == RaidsContractType.Task)
            {
                XpMult = reader.ReadSingle();
                BoltMult = reader.ReadSingle();
                ClassXpMult = reader.ReadSingle();
                MinCount = reader.ReadUInt16();
                MaxCount = reader.ReadUInt16();
                MinHeroLevel = reader.ReadUInt32();
                CountIncrement = reader.ReadUInt32();
                RewardRamp = reader.ReadSingle();
                PartyHeroLevelRamp = reader.ReadSingle();
            }
            else if (Type == RaidsContractType.Clear)
            {
                Difficulties = new RaidsContractDifficulty[5];
                for (int i = 0; i < 5; ++i)
                    Difficulties[i] = RaidsContractDifficulty.Read(reader);
            }
            else
            {
                throw new InvalidDataException("Unknown contract rule type.");
            }
        }
    }
}
