using Horizon.Plugin.Deadlocked.CustomModes;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsGetContractsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 64;
        public override bool SkipEncryption { get => true; set { } }

        public uint DestAddress { get; set; }
        public uint DestHasFlagAddress { get; set; }
        public List<ContractStat> ContractsStats { get; set; } = new List<ContractStat>();

        public class ContractStat
        {
            public uint ContractUid { get; set; }
            public uint Kills { get; set; }
            public uint CompletedTimeMs { get; set; }
        }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            DestAddress = reader.ReadUInt32();
            DestHasFlagAddress = reader.ReadUInt32();

            for (int i = 0; i < RaidsContractStore.MAX_CONTRACTS; ++i)
            {
                var contractStats = new ContractStat();
                contractStats.ContractUid = reader.ReadUInt32();
                contractStats.Kills = reader.ReadUInt32();
                contractStats.CompletedTimeMs = reader.ReadUInt32();
                if (contractStats.ContractUid > 0)
                    ContractsStats.Add(contractStats);
            }
        }

        public override void Serialize(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
