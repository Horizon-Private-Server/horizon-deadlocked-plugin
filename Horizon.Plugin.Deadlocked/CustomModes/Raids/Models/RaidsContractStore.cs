using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsContractStore
    {
        public const int MAX_CONTRACTS = 3;

        public Dictionary<string, RaidsContractBoard> Boards { get; set; } = new();
        public Dictionary<string, RaidsContractReceipt> Receipts { get; set; } = new();
        public List<string> ClaimedReceipts { get; set; } = new();
        public uint NextUid { get; set; }
        public uint PartyClaimUid { get; set; }

        public uint CompletedCount { get; set; }
        // Retired expired offers; GetExpiredCount also includes offers awaiting dismissal.
        public uint ExpiredCount { get; set; }
        public Dictionary<string, Dictionary<string, uint>> CompletedByMapAndRule { get; set; } = new();

        public uint GetCompletedCount(string mapFilename = null, string ruleName = null)
        {
            if (mapFilename == null) return CompletedCount;
            if (!CompletedByMapAndRule.TryGetValue(mapFilename, out var rules)) return 0;
            return ruleName == null
                ? (uint)Math.Min(uint.MaxValue, rules.Values.Aggregate(0UL, (sum, count) => sum + count))
                : rules.GetValueOrDefault(ruleName);
        }

        // Saved deadlines include offers that expired offline, before they are replaced.
        public uint GetExpiredCount(DateTime? now = null)
        {
            var utc = now ?? DateTime.UtcNow;
            return (uint)Math.Min(uint.MaxValue, (ulong)ExpiredCount + (ulong)Boards.Values
                .Sum(board => board.Contracts.Count(contract => contract.IsExpired(utc))));
        }

        private static uint Increment(uint count) => count == uint.MaxValue ? count : count + 1;

        public static uint GetTargetCount(RaidsContractRule rule, IEnumerable<int> heroLevels, Random random = null)
        {
            var averageLevel = heroLevels.Select(level => Math.Clamp(level, 1, 99)).DefaultIfEmpty(1).Average();

            // bias the roll by the party's average hero level
            // positive ramps favor higher counts for higher-level parties; negative ramps reverse this
            var exponent = Math.Pow(2, -rule.PartyHeroLevelRamp * (2 * (averageLevel - 1) / 98.0 - 1));
            var progress = Math.Pow((random ?? Random.Shared).NextDouble(), exponent);

            // select a count from MinCount in CountIncrement steps without exceeding MaxCount
            var steps = (uint)(rule.MaxCount - rule.MinCount) / rule.CountIncrement;
            var step = Math.Min(steps, (uint)Math.Floor(progress * (steps + 1)));
            return rule.MinCount + step * rule.CountIncrement;
        }

        public uint AllocateUid()
        {
            if (NextUid == uint.MaxValue)
                throw new InvalidOperationException("Contract ID space exhausted.");

            return ++NextUid;
        }

        public RaidsContractBoard GetBoard(string filename, string name)
        {
            if (!Boards.TryGetValue(filename, out var board))
                Boards[filename] = board = new() { BoardId = AllocateUid(), MapFilename = filename, MapName = name };

            return board;
        }

        public void Refresh(RaidsContractBoard board, uint owner, IReadOnlyList<RaidsContractRule> rules,
            IEnumerable<int> heroLevels, DateTime now, int hostHeroLevel, RaidsContract previous = null)
        {
            var usedNames = board.Contracts.Where(c => (c.Type == RaidsContractType.Kill || c.Type == RaidsContractType.Task)).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var usedMasks = board.Contracts.Where(c => c.Type == RaidsContractType.Kill).Select(c => c.MobDefMask).ToHashSet();

            // add the map's clear contract if there is room
            var completion = rules.Where(r => r.Type == RaidsContractType.Clear && r.IsValid())
                .OrderBy(r => previous != null && r.Name == previous.Name ? 1 : 0)
                .FirstOrDefault();
            if (completion != null && !board.Contracts.Any(c => c.Type == RaidsContractType.Clear) && board.Contracts.Count < MAX_CONTRACTS)
            {
                board.Contracts.Add(new()
                {
                    Uid = AllocateUid(),
                    OwnerAccountId = owner,
                    Type = RaidsContractType.Clear,
                    RuleIndex = rules.ToList().IndexOf(completion),
                    Name = completion.Name,
                    MapFilename = board.MapFilename,
                    MapName = board.MapName,
                    Difficulties = completion.Difficulties,
                    ExpiresUtc = now.AddHours(6)
                });
            }

            // use the highest unlocked tier for each contract name
            var countRules = rules.Where(r => (r.Type == RaidsContractType.Kill || r.Type == RaidsContractType.Task) && r.IsValid() && hostHeroLevel >= r.MinHeroLevel)
                .GroupBy(r => r.Name, StringComparer.Ordinal)
                .SelectMany(group => group.Where(r => r.MinHeroLevel == group.Max(candidate => candidate.MinHeroLevel)));

            // prefer a different objective when replacing an offer
            foreach (var rule in countRules
                .OrderBy(r => previous != null && (r.Name == previous.Name || (r.Type == RaidsContractType.Kill && previous.Type == RaidsContractType.Kill && r.MobDefMask == previous.MobDefMask)) ? 1 : 0)
                .ThenBy(_ => Random.Shared.Next()))
            {
                if (board.Contracts.Count >= MAX_CONTRACTS)
                    break;
                if (usedNames.Contains(rule.Name) || (rule.Type == RaidsContractType.Kill && usedMasks.Contains(rule.MobDefMask)))
                    continue;

                var target = GetTargetCount(rule, heroLevels);
                board.Contracts.Add(new()
                {
                    Uid = AllocateUid(),
                    OwnerAccountId = owner,
                    Type = rule.Type,
                    RuleIndex = rules.ToList().IndexOf(rule),
                    Name = rule.Name,
                    MobDefMask = rule.MobDefMask,
                    MapFilename = board.MapFilename,
                    MapName = board.MapName,
                    TargetCount = target,
                    ExpiresUtc = now.AddHours(6),
                    Reward = rule.GetReward(target)
                });
                usedNames.Add(rule.Name);
                if (rule.Type == RaidsContractType.Kill) usedMasks.Add(rule.MobDefMask);
            }
        }

        public bool Replace(RaidsContractBoard board, uint uid, uint owner, IReadOnlyList<RaidsContractRule> rules,
            IEnumerable<int> heroLevels, DateTime now, int hostHeroLevel)
        {
            var index = board.Contracts.FindIndex(c => c.Uid == uid);
            if (index < 0) return false;

            var previous = board.Contracts[index];
            board.Contracts.RemoveAt(index);
            var remaining = board.Contracts.Count;
            Refresh(board, owner, rules, heroLevels, now, hostHeroLevel, previous);

            // move the first new offer into the vacated slot without moving other offers
            var replacement = board.Contracts.Count > remaining ? board.Contracts[remaining] : previous;
            if (board.Contracts.Count > remaining)
                board.Contracts.RemoveAt(remaining);
            board.Contracts.Insert(index, replacement);
            ++board.Sequence;

            var replaced = replacement.Uid != previous.Uid;
            if (replaced && previous.IsExpired(now)) ExpiredCount = Increment(ExpiredCount);
            return replaced;
        }

        // retained for developer use; gameplay does not expose rejection
        public void Reroll(RaidsContractBoard board, uint uid)
        {
            var contract = board.Contracts.FirstOrDefault(c => c.Uid == uid);
            if (contract == null) return;
            if (contract.IsExpired(DateTime.UtcNow)) ExpiredCount = Increment(ExpiredCount);
            board.Contracts.Remove(contract);
        }

        public void AddReceipt(RaidsContract contract, int classType)
        {
            if (!contract.Completed) return;
            var receipt = new RaidsContractReceipt { Contract = contract, ClassType = classType };
            if (ClaimedReceipts.Contains(receipt.Key) || !Receipts.TryAdd(receipt.Key, receipt)) return;

            // Credit each eligible player at completion, independently of reward claims.
            CompletedCount = Increment(CompletedCount);
            if (!CompletedByMapAndRule.TryGetValue(contract.MapFilename, out var rules))
                CompletedByMapAndRule[contract.MapFilename] = rules = new();
            rules[contract.Name] = Increment(rules.GetValueOrDefault(contract.Name));
        }

        public RaidsContractReceipt FindReceipt(uint owner, uint uid) => Receipts.GetValueOrDefault($"{owner}:{uid}");

        public bool WasClaimed(uint owner, uint uid) => ClaimedReceipts.Contains($"{owner}:{uid}");

        public void RecordClaim(RaidsContractReceipt receipt)
        {
            Receipts.Remove(receipt.Key);
            if (!ClaimedReceipts.Contains(receipt.Key))
                ClaimedReceipts.Add(receipt.Key);
            if (ClaimedReceipts.Count > 64)
                ClaimedReceipts.RemoveRange(0, ClaimedReceipts.Count - 64);
        }
    }
}
