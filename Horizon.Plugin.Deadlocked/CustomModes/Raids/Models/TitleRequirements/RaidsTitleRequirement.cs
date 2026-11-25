namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements
{
    // Each rule supplies its own progress and label; all rules on a title must pass.
    public abstract record RaidsTitleRequirement(uint Target)
    {
        public abstract uint GetProgress(RaidsBank bank);
        public abstract string GetDescription(RaidsBank bank);

        public bool IsMet(RaidsBank bank) => GetProgress(bank) >= Target;

        protected static string GetMapName(RaidsBank bank, string filename) =>
            bank.MapStats.TryGetValue(filename, out var stats) && !string.IsNullOrWhiteSpace(stats?.Name)
                ? stats.Name : filename;
    }
}
