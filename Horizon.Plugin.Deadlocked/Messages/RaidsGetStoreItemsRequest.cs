using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsGetStoreItemsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 57;
        public override bool SkipEncryption { get => true; set { } }

        public uint DestAddress { get; set; }
        public uint DestTotalItemsAddress { get; set; }
        public uint DestRotateInSecondsAddress { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            DestAddress = reader.ReadUInt32();
            DestTotalItemsAddress = reader.ReadUInt32();
            DestRotateInSecondsAddress = reader.ReadUInt32();
            PageSize = reader.ReadInt32();
            Page = reader.ReadInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);
            writer.Write(DestAddress);
            writer.Write(DestTotalItemsAddress);
            writer.Write(DestRotateInSecondsAddress);
            writer.Write(PageSize);
            writer.Write(Page);
        }
    }
}
