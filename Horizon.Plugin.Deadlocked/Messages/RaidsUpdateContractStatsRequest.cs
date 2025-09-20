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
    public class RaidsUpdateContractStatsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 66;
        public override bool SkipEncryption { get => true; set { } }

        public uint ContractUid { get; set; }
        public uint Kills { get; set; }
        public uint CompletedTimeMs { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            ContractUid = reader.ReadUInt32();
            Kills = reader.ReadUInt32();
            CompletedTimeMs = reader.ReadUInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);
            writer.Write(ContractUid);
            writer.Write(Kills);
            writer.Write(CompletedTimeMs);
        }
    }
}
