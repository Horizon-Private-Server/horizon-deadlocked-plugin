using System;
using System.IO;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsContractDifficulty
    {
        // Resolved fields remain for saved contracts and receipts from the previous format.
        public uint TargetTimeMs { get; set; }
        public ushort[] TargetTimeSeconds { get; set; }
        public RaidsContractReward TotalReward { get; set; }
        public RaidsContractReward Reward { get; set; } = new();
        public RaidsContractReward SpeedBonus { get; set; } = new();
        public float BonusRewardFactor { get; set; } = 0.75f;

        public static RaidsContractDifficulty Read(BinaryReader reader)
        {
            var times = new ushort[5];
            for (var i = 0; i < times.Length; ++i) times[i] = reader.ReadUInt16();
            reader.ReadUInt16(); // align rewards to four bytes
            var total = RaidsContractReward.Read(reader);
            var factor = reader.ReadSingle();
            if (!float.IsFinite(factor) || factor < 0 || factor > 1)
                throw new InvalidDataException("Bonus reward factor must be finite and between 0 and 1.");
            return new RaidsContractDifficulty
            {
                TargetTimeSeconds = times,
                TotalReward = total,
                BonusRewardFactor = factor
            }.ForPartySize(1);
        }

        public RaidsContractDifficulty ForPartySize(int partySize)
        {
            if (partySize < 1 || partySize > 5)
                throw new ArgumentOutOfRangeException(nameof(partySize));

            // older saved contracts already have their target time and rewards resolved
            if (TargetTimeSeconds == null) return this;

            if (TargetTimeSeconds.Length != 5 || TotalReward == null)
                throw new InvalidDataException("Completion targets require five party sizes and total rewards.");

            // select the party's time target and reserve a share of the reward for the speed bonus
            // without a time target, completing the contract pays the full reward
            var targetTimeMs = (uint)TargetTimeSeconds[partySize - 1] * 1000;
            var total = TotalReward;
            var factor = BonusRewardFactor;
            var completionFactor = targetTimeMs > 0 ? 1 - (double)factor : 1;

            uint Completion(uint amount, int step = 1)
            {
                return (uint)Math.Min(amount,
                    Math.Round(amount * completionFactor / step, MidpointRounding.AwayFromZero) * step);
            }

            // round the completion reward, with class xp rounded to multiples of five when split
            var completion = new RaidsContractReward
            {
                Bolts = Completion(total.Bolts),
                PlayerXp = Completion(total.PlayerXp),
                WeaponXp = Completion(total.WeaponXp),
                ClassXp = completionFactor == 1 ? total.ClassXp : Completion(total.ClassXp, 5)
            };

            // use the remainder for the speed bonus so both payouts add up to the total reward
            return new()
            {
                TargetTimeMs = targetTimeMs,
                TargetTimeSeconds = TargetTimeSeconds,
                TotalReward = TotalReward,
                BonusRewardFactor = factor,
                Reward = completion,
                SpeedBonus = new()
                {
                    Bolts = total.Bolts - completion.Bolts,
                    PlayerXp = total.PlayerXp - completion.PlayerXp,
                    WeaponXp = total.WeaponXp - completion.WeaponXp,
                    ClassXp = total.ClassXp - completion.ClassXp
                }
            };
        }
    }
}
