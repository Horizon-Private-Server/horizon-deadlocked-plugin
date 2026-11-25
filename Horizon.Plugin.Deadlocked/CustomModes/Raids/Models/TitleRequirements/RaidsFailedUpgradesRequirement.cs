namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsFailedUpgradesRequirement(uint Count) : RaidsTitleRequirement(Count)
    {
        public override uint GetProgress(RaidsBank bank) => bank.PlayerStats.FailedUpgrades;
        public override string GetDescription(RaidsBank bank) => "Failed weapon upgrades";
    }
}
