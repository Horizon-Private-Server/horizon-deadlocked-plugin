using System;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsLootDropsRequirement : RaidsTitleRequirement
    {
        public RaidsItemRarity? Rarity { get; }

        // A specified rarity counts exactly that rarity; omit it to count all drops.
        public RaidsLootDropsRequirement(uint count, RaidsItemRarity? rarity = null) : base(count)
        {
            if (rarity.HasValue && !Enum.IsDefined(rarity.Value)) throw new ArgumentOutOfRangeException(nameof(rarity));
            Rarity = rarity;
        }

        public override uint GetProgress(RaidsBank bank) => bank.PlayerStats.GetLootDrops(Rarity);
        public override string GetDescription(RaidsBank bank) => Rarity.HasValue ? $"{Rarity} loot drops received" : "Loot drops received";
    }
}
