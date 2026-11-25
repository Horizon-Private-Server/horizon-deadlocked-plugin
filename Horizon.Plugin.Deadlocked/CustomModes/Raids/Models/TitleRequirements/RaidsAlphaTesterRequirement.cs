namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsAlphaTesterRequirement() : RaidsTitleRequirement(1)
    {
        // All accounts qualify during alpha; use the saved account flag after alpha.
        public override uint GetProgress(RaidsBank bank) => 1;
        public override string GetDescription(RaidsBank bank) => "Alpha tester";
    }
}
