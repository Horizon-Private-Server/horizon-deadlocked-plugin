using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    // Lifetime progress belongs to the bank, outside client account snapshots.
    public class RaidsPlayerStats
    {
        public uint SuccessfulUpgrades { get; set; }
        public uint FailedUpgrades { get; set; }
        public uint MaterialsConsumed { get; set; }
        public ConcurrentDictionary<RaidsItemRarity, uint> LootDropsByRarity { get; set; } = new();

        [JsonIgnore] public uint LootDropsReceived =>
            (uint)Math.Min(uint.MaxValue, LootDropsByRarity.Values.Aggregate(0UL, (sum, count) => sum + count));

        public void RecordUpgrade(bool successful)
        {
            if (successful) SuccessfulUpgrades = Increment(SuccessfulUpgrades);
            else FailedUpgrades = Increment(FailedUpgrades);
        }

        public void RecordMaterialConsumed() => MaterialsConsumed = Increment(MaterialsConsumed);

        public void RecordLootDrop(RaidsInventoryItem item)
        {
            if (item?.IsValid() != true) return;

            var rarity = RaidsInventoryItem.GetRarityFromQuality(item.Quality);
            LootDropsByRarity.AddOrUpdate(rarity, 1, (_, count) => Increment(count));
        }

        public uint GetLootDrops(RaidsItemRarity? rarity = null) =>
            rarity.HasValue ? LootDropsByRarity.GetValueOrDefault(rarity.Value) : LootDropsReceived;

        private static uint Increment(uint count) => count == uint.MaxValue ? count : count + 1;
    }
}
