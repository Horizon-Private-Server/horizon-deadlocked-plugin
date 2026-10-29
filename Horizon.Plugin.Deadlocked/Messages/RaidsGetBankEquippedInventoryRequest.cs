using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsGetBankEquippedInventoryRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 62;
        public override bool SkipEncryption { get => true; set { } }

        public uint DestAddress { get; set; }
        public uint DestHasFlagAddress { get; set; }
        public uint DestTimeFlagAddress { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            DestAddress = reader.ReadUInt32();
            DestHasFlagAddress = reader.ReadUInt32();
            DestTimeFlagAddress = reader.ReadUInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            writer.Write(DestAddress);
            writer.Write(DestHasFlagAddress);
            writer.Write(DestTimeFlagAddress);
        }
    }
}
