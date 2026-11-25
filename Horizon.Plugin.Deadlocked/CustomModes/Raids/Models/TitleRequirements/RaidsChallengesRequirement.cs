namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsChallengesRequirement(uint Count) : RaidsTitleRequirement(Count)
    {
        public override uint GetProgress(RaidsBank bank) => RaidsTitles.GetProgress(bank).Challenges;
        public override string GetDescription(RaidsBank bank) => "Challenges completed";
    }
}
