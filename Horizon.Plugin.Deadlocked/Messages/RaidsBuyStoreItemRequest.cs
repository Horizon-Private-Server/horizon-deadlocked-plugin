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
    public class RaidsBuyStoreItemRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 58;
        public override bool SkipEncryption { get => true; set { } }

        public int PageSize { get; set; }
        public int Page { get; set; }
        public int ItemIdx { get; set; }
        public RaidsInventoryItem Item { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            PageSize = reader.ReadInt32();
            Page = reader.ReadInt32();
            ItemIdx = reader.ReadInt32();
            Item = new RaidsInventoryItem();
            Item.Deserialize(reader);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);
            writer.Write(PageSize);
            writer.Write(Page);
            writer.Write(ItemIdx);
            writer.Write(Item ?? RaidsInventoryItem.Empty);
        }
    }
}
