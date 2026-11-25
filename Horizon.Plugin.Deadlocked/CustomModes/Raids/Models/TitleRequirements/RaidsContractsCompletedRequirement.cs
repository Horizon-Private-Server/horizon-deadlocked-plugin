using System;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    public sealed record RaidsContractsCompletedRequirement : RaidsTitleRequirement
    {
        public string MapFilename { get; }
        public string RuleName { get; }

        // Omit filters for all contracts; rule names match exactly across hero-level tiers.
        public RaidsContractsCompletedRequirement(uint count, string mapFilename = null, string ruleName = null) : base(count)
        {
            if (mapFilename != null && string.IsNullOrWhiteSpace(mapFilename)) throw new ArgumentException("Map filename cannot be blank.", nameof(mapFilename));
            if (ruleName != null && (mapFilename == null || string.IsNullOrWhiteSpace(ruleName))) throw new ArgumentException("A contract rule requires a map filename and a nonblank rule name.", nameof(ruleName));
            MapFilename = mapFilename;
            RuleName = ruleName;
        }

        public override uint GetProgress(RaidsBank bank) => bank.ContractStore.GetCompletedCount(MapFilename, RuleName);

        public override string GetDescription(RaidsBank bank) => MapFilename == null ? "Contracts completed"
            : RuleName == null ? $"{GetMapName(bank, MapFilename)} contracts completed"
            : $"{GetMapName(bank, MapFilename)}: {RuleName} contracts completed";
    }
}
