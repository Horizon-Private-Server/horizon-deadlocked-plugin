using Horizon.Plugin.Deadlocked.Messages;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages
{
    public class RaidsGetStoreItemsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 57;
        public override bool SkipEncryption { get => true; set { } }

        public uint ReplyAddress { get; set; }
        public uint RequestId { get; set; }
        public int Page { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            if (reader.BaseStream.Length - reader.BaseStream.Position != 12) return;
            ReplyAddress = reader.ReadUInt32();
            RequestId = reader.ReadUInt32();
            Page = reader.ReadInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            writer.Write(ReplyAddress);
            writer.Write(RequestId);
            writer.Write(Page);
        }
    }
}
