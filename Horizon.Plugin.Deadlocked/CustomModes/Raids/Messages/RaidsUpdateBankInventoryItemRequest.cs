using Horizon.Plugin.Deadlocked.Messages;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages
{
    public class RaidsUpdateBankInventoryItemRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 52;
        public override bool SkipEncryption { get => true; set { } }

        public enum ItemUpdateAction
        {
            None = 0,
            Sell,
            SetNotify,
            Equip,
            Unequip,
            Upgrade,
            Destroy,
            ConsumeMaterial,
            UpgradeFailed
        }

        public RaidsInventoryItem Item { get; set; }
        public uint RequestId { get; set; }
        public ItemUpdateAction Action { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            if (reader.BaseStream.Length - reader.BaseStream.Position != 40) return;

            Item = new RaidsInventoryItem();
            Item.Deserialize(reader);
            RequestId = reader.ReadUInt32();
            Action = (ItemUpdateAction)reader.ReadByte();
            reader.ReadBytes(3); // padding
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
