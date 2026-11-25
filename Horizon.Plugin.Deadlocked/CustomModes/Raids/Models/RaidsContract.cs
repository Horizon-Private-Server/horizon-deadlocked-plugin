using Server.Common.Stream;
using System;
using System.IO;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsContract
    {
        public const int SERIALIZED_SIZE = 96;

        public uint Uid { get; set; }
        public uint OwnerAccountId { get; set; }
        public uint TargetCount { get; set; }
        public uint Count { get; set; }
        public bool TaskStarted { get; set; }
        public int RuleIndex { get; set; } = -1;
        public uint CompletedTimeMs { get; set; }
        public RaidsContractType Type { get; set; }
        public ushort MobDefMask { get; set; }
        public string Name { get; set; }
        public string MapFilename { get; set; }
        public string MapName { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public int CompletedDifficulty { get; set; } = -1;
        public int CompletedPartySize { get; set; } = 1;
        public RaidsContractReward Reward { get; set; } = new();
        public RaidsContractDifficulty[] Difficulties { get; set; }

        public bool Completed => ((Type == RaidsContractType.Kill || Type == RaidsContractType.Task && TaskStarted) && TargetCount > 0 && Count >= TargetCount)
            || (Type == RaidsContractType.Clear && CompletedTimeMs > 0);

        public bool HasSpeedBonus => Type == RaidsContractType.Clear
            && CompletedDifficulty >= 0
            && Difficulties[CompletedDifficulty].ForPartySize(CompletedPartySize).TargetTimeMs > 0
            && Difficulties[CompletedDifficulty].BonusRewardFactor > 0
            && CompletedTimeMs <= Difficulties[CompletedDifficulty].ForPartySize(CompletedPartySize).TargetTimeMs;

        public RaidsContractReward Payout
        {
            get
            {
                if (Type == RaidsContractType.Kill || Type == RaidsContractType.Task)
                    return Reward;

                if (Type == RaidsContractType.Clear && CompletedDifficulty >= 0)
                {
                    var difficulty = Difficulties[CompletedDifficulty].ForPartySize(CompletedPartySize);
                    return HasSpeedBonus || difficulty.TargetTimeMs == 0 || difficulty.BonusRewardFactor == 0
                        ? difficulty.Reward.Add(difficulty.SpeedBonus) : difficulty.Reward;
                }

                return new RaidsContractReward();
            }
        }

        public bool IsExpired(DateTime now)
        {
            return !Completed && now >= ExpiresUtc;
        }

        public RaidsContractFlags Flags(DateTime now)
        {
            var flags = TaskStarted && Type == RaidsContractType.Task ? RaidsContractFlags.TaskStarted : RaidsContractFlags.None;
            if (Completed)
                flags |= RaidsContractFlags.Complete;
            if (IsExpired(now))
                flags |= RaidsContractFlags.Expired;
            if (HasSpeedBonus)
                flags |= RaidsContractFlags.SpeedBonusEarned;

            return flags;
        }

        public void UpdateCount(uint count, bool taskStarted)
        {
            if (Completed) return;
            if (Type == RaidsContractType.Task)
            {
                TaskStarted |= taskStarted;
                if (TaskStarted) Count = Math.Min(TargetCount, count);
            }
            else if (Type == RaidsContractType.Kill)
                Count = Math.Min(TargetCount, count);
        }

        public void Write(BinaryWriter writer, int previewDifficulty, DateTime now, RaidsContractReceipt receipt = null, bool claimed = false, string label = null, int previewPartySize = 1)
        {
            var difficulty = CompletedDifficulty >= 0 ? CompletedDifficulty : previewDifficulty;
            var completion = Type == RaidsContractType.Clear
                ? Difficulties[difficulty].ForPartySize(Completed ? CompletedPartySize : previewPartySize) : null;
            var flags = Flags(now);
            if (receipt != null)
                flags |= receipt.Claimed ? RaidsContractFlags.Claimed : RaidsContractFlags.Claimable;
            else if (claimed)
                flags |= RaidsContractFlags.Claimed;

            // identity and rewards
            writer.Write(Uid);
            writer.Write(OwnerAccountId);
            writer.Write(RuleIndex);

            var reward = new RaidsContractReward();
            if (Type == RaidsContractType.Kill || Type == RaidsContractType.Task)
                reward = Reward;
            else if (Type == RaidsContractType.Clear)
                reward = Completed ? Payout : completion.Reward;

            reward.Write(writer);

            // count and clear contracts share the same payload space
            if (Type == RaidsContractType.Kill || Type == RaidsContractType.Task)
            {
                writer.Write(TargetCount);
                writer.Write(Count);
                writer.Write(new byte[20]); // unused union space
            }
            else if (Type == RaidsContractType.Clear)
            {
                writer.Write(CompletedTimeMs);
                writer.Write(completion.TargetTimeMs);
                (!Completed ? completion.SpeedBonus : new RaidsContractReward()).Write(writer);
                writer.Write(completion.TargetTimeMs > 0 ? completion.BonusRewardFactor : 0);
            }
            else
            {
                // reserve the payload space for types without a serializer
                writer.Write(new byte[28]);
            }

            // status and display name
            writer.Write(MobDefMask);
            writer.Write((ushort)Math.Clamp(Math.Ceiling((ExpiresUtc - now).TotalSeconds), 0, 21600));
            writer.Write((byte)Type);
            writer.Write((byte)flags);
            writer.Write((sbyte)CompletedDifficulty);
            writer.Write((sbyte)(receipt?.ClassType ?? -1));
            writer.Write(label ?? Name, 32);
        }
    }
}
