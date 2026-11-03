using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.IO;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsUpdateContractStatsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 66;
        public override bool SkipEncryption { get => true; set { } }

        public uint BoardId { get; set; }
        public uint OwnerAccountId { get; set; }
        public uint Sequence { get; set; }
        public uint Count { get; set; }
        public List<ContractStat> Contracts { get; set; } = new List<ContractStat>();

        public class ContractStat
        {
            public uint ContractUid { get; set; }
            public uint Progress { get; set; } // kills or completion time, based on the contract type
            public byte Flags { get; set; }
            public sbyte Difficulty { get; set; }
        }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            BoardId = reader.ReadUInt32();
            OwnerAccountId = reader.ReadUInt32();
            Sequence = reader.ReadUInt32();
            Count = reader.ReadUInt32();
            if (Count > 3)
                throw new InvalidDataException("Invalid contract progress count.");

            for (int i = 0; i < 3; ++i)
            {
                var stat = new ContractStat();
                stat.ContractUid = reader.ReadUInt32();
                stat.Progress = reader.ReadUInt32();
                stat.Flags = reader.ReadByte();
                stat.Difficulty = reader.ReadSByte();
                reader.ReadUInt16(); // natural alignment
                if (i < Count)
                    Contracts.Add(stat);
            }
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
