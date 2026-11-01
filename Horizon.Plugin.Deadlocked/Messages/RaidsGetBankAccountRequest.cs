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
    public class RaidsGetBankAccountRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 53;
        public override bool SkipEncryption { get => true; set { } }

        public uint DestAddress { get; set; }
        public uint DestHasFlagAddress { get; set; }
        public uint DestTimeFlagAddress { get; set; }
        public uint AccountSize { get; set; }
        public bool IsCompatible => AccountSize == RaidsAccount.SERIALIZED_SIZE;

        public override void Deserialize(MessageReader reader)
        {
            AccountSize = 0;
            base.Deserialize(reader);
            if (reader.BaseStream.Length - reader.BaseStream.Position != 16) return;
            DestAddress = reader.ReadUInt32();
            DestHasFlagAddress = reader.ReadUInt32();
            DestTimeFlagAddress = reader.ReadUInt32();
            AccountSize = reader.ReadUInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            writer.Write(DestAddress);
            writer.Write(DestHasFlagAddress);
            writer.Write(DestTimeFlagAddress);
            writer.Write(AccountSize);
        }
    }
}
