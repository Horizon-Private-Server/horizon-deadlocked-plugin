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
    public class RaidsActionContractRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 65;
        public override bool SkipEncryption { get => true; set { } }

        public uint ContractUid { get; set; }
        public ContractActions Action { get; set; }

        public enum ContractActions
        {
            Reroll = 0,
            Complete = 1,
            Activate = 2
        }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            ContractUid = reader.ReadUInt32();
            Action = (ContractActions)reader.ReadInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);
            writer.Write(ContractUid);
            writer.Write((int)Action);
        }
    }
}
