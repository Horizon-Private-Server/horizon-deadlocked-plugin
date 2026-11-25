namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsContractReceipt
    {
        public RaidsContract Contract { get; set; }
        public int ClassType { get; set; } = -1;
        public bool Claimed { get; set; }
        public bool PartyClaimPending { get; set; }

        public string Key => $"{Contract.OwnerAccountId}:{Contract.Uid}";
    }
}
