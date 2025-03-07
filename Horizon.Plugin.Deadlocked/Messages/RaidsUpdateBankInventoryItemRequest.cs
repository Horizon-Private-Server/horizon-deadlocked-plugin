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
    public class RaidsUpdateBankInventoryItemRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 52;
        public override bool SkipEncryption { get => true; set { } }

        public enum ItemUpdateAction
        {
            None = 0,
            Sell,
            SetNofify,
            Equip,
            Unequip,
        }

        public RaidsInventoryItem Item { get; set; }
        public ItemUpdateAction Action { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Item = new RaidsInventoryItem();
            Item.Deserialize(reader);
            Action = (ItemUpdateAction)reader.ReadByte();
            reader.ReadBytes(3); // padding
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            throw new NotImplementedException();
        }
    }
}
