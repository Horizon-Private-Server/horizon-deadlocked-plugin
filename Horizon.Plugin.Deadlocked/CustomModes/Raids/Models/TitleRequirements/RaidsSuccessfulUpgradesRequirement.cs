namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsSuccessfulUpgradesRequirement(uint Count) : RaidsTitleRequirement(Count)
    {
        public override uint GetProgress(RaidsBank bank) => bank.PlayerStats.SuccessfulUpgrades;
        public override string GetDescription(RaidsBank bank) => "Successful weapon upgrades";
    }
}
