namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsMapCompletionRequirement(string MapFilename) : RaidsTitleRequirement(100)
    {
        public override uint GetProgress(RaidsBank bank) =>
            bank.MapStats.TryGetValue(MapFilename, out var stats) && stats != null
                ? (uint)(stats.GetPercentComplete(stats.MissionType != RaidsMissionType.Raid) * 100) : 0;

        public override string GetDescription(RaidsBank bank) => $"{GetMapName(bank, MapFilename)} completion (%)";
    }
}
