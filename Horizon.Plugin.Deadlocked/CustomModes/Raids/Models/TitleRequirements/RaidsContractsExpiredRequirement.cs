namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsContractsExpiredRequirement(uint Count) : RaidsTitleRequirement(Count)
    {
        public override uint GetProgress(RaidsBank bank) => bank.ContractStore.GetExpiredCount();
        public override string GetDescription(RaidsBank bank) => "Contracts expired";
    }
}
