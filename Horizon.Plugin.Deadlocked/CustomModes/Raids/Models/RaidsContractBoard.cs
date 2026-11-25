using System.Collections.Generic;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsContractBoard
    {
        public uint BoardId { get; set; }
        public uint Sequence { get; set; }
        public string MapFilename { get; set; }
        public string MapName { get; set; }
        public List<RaidsContract> Contracts { get; set; } = new();
    }
}
