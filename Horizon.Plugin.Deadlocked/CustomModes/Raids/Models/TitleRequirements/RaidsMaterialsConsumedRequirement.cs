namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsMaterialsConsumedRequirement(uint Count) : RaidsTitleRequirement(Count)
    {
        public override uint GetProgress(RaidsBank bank) => bank.PlayerStats.MaterialsConsumed;
        public override string GetDescription(RaidsBank bank) => "Upgrade materials consumed";
    }
}
