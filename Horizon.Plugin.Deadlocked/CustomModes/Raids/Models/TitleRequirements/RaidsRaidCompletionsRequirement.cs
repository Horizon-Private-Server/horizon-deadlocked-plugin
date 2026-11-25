using System;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsRaidCompletionsRequirement : RaidsTitleRequirement
    {
        public string MapFilename { get; }
        public int Stars { get; }

        // Catalog star ratings are one-based; saved difficulty slots are zero-based.
        public RaidsRaidCompletionsRequirement(string mapFilename, int stars, uint count) : base(count)
        {
            if (stars < 1 || stars > 5) throw new ArgumentOutOfRangeException(nameof(stars));
            MapFilename = mapFilename;
            Stars = stars;
        }

        public override uint GetProgress(RaidsBank bank) =>
            bank.MapStats.TryGetValue(MapFilename, out var stats) && stats?.MissionType == RaidsMissionType.Raid
                ? stats.CompletionsPerDifficulty[Stars - 1] : 0;

        public override string GetDescription(RaidsBank bank) => $"{GetMapName(bank, MapFilename)} clears at {Stars} stars";
    }
}
