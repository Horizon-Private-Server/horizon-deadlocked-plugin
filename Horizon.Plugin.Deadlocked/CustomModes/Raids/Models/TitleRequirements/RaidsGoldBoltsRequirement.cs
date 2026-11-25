namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsGoldBoltsRequirement(uint Count) : RaidsTitleRequirement(Count)
    {
        public override uint GetProgress(RaidsBank bank) => RaidsTitles.GetProgress(bank).GoldBolts;
        public override string GetDescription(RaidsBank bank) => "Gold bolts collected";
    }
}
