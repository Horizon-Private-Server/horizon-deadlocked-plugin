using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsConsumables
    {
        public const uint DURATION_MS = 30 * 60 * 1000;
        public const uint MAX_DURATION_MS = 2 * 60 * 60 * 1000;
        public const int SERIALIZED_SIZE = sizeof(uint) * (1 + (int)RaidsConsumableType.Count);
        public const int ACTION_REPLY_SERIALIZED_SIZE = 3 * sizeof(uint) + SERIALIZED_SIZE + (int)RaidsConsumableType.Count * sizeof(uint);

        public static readonly IReadOnlyDictionary<RaidsConsumableType, RaidsMiscItemType> ItemTypes = new Dictionary<RaidsConsumableType, RaidsMiscItemType>
        {
            [RaidsConsumableType.SalvagersSignal] = RaidsMiscItemType.SalvagersSignal,
            [RaidsConsumableType.FortunesFavor] = RaidsMiscItemType.FortunesFavor,
            [RaidsConsumableType.DoubleXp] = RaidsMiscItemType.DoubleXp,
        };

        public static readonly RaidsMiscItemType[] StoreItemTypes = ItemTypes.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();

        public uint Revision { get; set; }
        public ConcurrentDictionary<RaidsConsumableType, long> ExpiresAtUnixMs { get; set; } = new();

        public static RaidsConsumableType? GetConsumableType(RaidsMiscItemType itemType)
        {
            foreach (var pair in ItemTypes)
                if (pair.Value == itemType) return pair.Key;
            return null;
        }

        public static RaidsMiscItemType GetItemType(RaidsConsumableType type) =>
            ItemTypes.TryGetValue(type, out var itemType) ? itemType : RaidsMiscItemType.None;

        public uint GetRemainingMs(RaidsConsumableType type, long? nowUnixMs = null) =>
            type >= 0 && type < RaidsConsumableType.Count
                ? Remaining(ExpiresAtUnixMs.GetValueOrDefault(type), nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) : 0;

        public static uint Remaining(long expiresAtUnixMs, long nowUnixMs) =>
            expiresAtUnixMs <= nowUnixMs ? 0 : (uint)Math.Min(MAX_DURATION_MS, expiresAtUnixMs - nowUnixMs);

        public static bool IsConsumable(RaidsInventoryItem item) => item?.IsMisc() == true && item.MiscData != null
            && GetConsumableType(item.MiscData.Type).HasValue;

        public static bool IsStoreConsumable(RaidsInventoryItem item) => IsConsumable(item)
            && item.MiscData.Type != RaidsMiscItemType.DoubleXp;

        // Empty stacks keep their UID while active. Expiry never removes unused items.
        public int PruneExpiredStacks(RaidsBank bank, long? nowUnixMs = null)
        {
            long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return bank.Inventory.AllItems.RemoveAll(item => IsConsumable(item) && item.MiscData.Count == 0
                && GetRemainingMs(GetConsumableType(item.MiscData.Type).Value, now) == 0);
        }

        public static uint OwnedConsumables(RaidsBank bank, RaidsMiscItemType type) =>
            (uint)Math.Min(uint.MaxValue, bank.Inventory.AllItems.Where(x => x?.IsMisc() == true && x.MiscData.Type == type)
                .Aggregate(0UL, (sum, item) => sum + item.MiscData.Count));

        public static uint ConsumablePrice(RaidsMiscItemType type) => type switch
        {
            RaidsMiscItemType.DoubleXp => 0, // reward preview; never purchasable
            RaidsMiscItemType.SalvagersSignal => 25_000_000,
            RaidsMiscItemType.FortunesFavor => 25_000_000,
            _ => 0
        };

        public bool Activate(RaidsMiscItemType type, long? nowUnixMs = null)
        {
            var consumableType = GetConsumableType(type);
            if (!consumableType.HasValue || consumableType == RaidsConsumableType.DoubleXp) return false;

            long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            uint remaining = GetRemainingMs(consumableType.Value, now);
            if (remaining > MAX_DURATION_MS - DURATION_MS) return false;

            ExpiresAtUnixMs[consumableType.Value] = now + remaining + DURATION_MS;
            Revision = Revision == uint.MaxValue ? 1 : Revision + 1;
            return true;
        }

        public void Serialize(BinaryWriter writer, long? nowUnixMs = null)
        {
            long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            writer.Write(Revision);
            for (int i = 0; i < (int)RaidsConsumableType.Count; ++i)
                writer.Write(GetRemainingMs((RaidsConsumableType)i, now));
        }
    }
}
