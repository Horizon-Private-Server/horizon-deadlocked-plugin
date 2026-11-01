using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsGetBankInventoryRequest : BasePluginMessage
    {
        public enum InventorySort
        {
            Rarity = 0,
            Proficiency,
            Favorites
        }

        public override byte CustomMsgId => 51;
        public override bool SkipEncryption { get => true; set { } }

        public uint DestAddress { get; set; }
        public uint DestHasFlagAddress { get; set; }
        public uint DestTimeFlagAddress { get; set; }
        public int Filter { get; set; }
        public int Page { get; set; }
        public InventorySort Sort { get; set; }
        public int RarityFilter { get; set; } = -1; // -1 means all rarities
        public bool FavoritesOnly { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            DestAddress = reader.ReadUInt32();
            DestHasFlagAddress = reader.ReadUInt32();
            DestTimeFlagAddress = reader.ReadUInt32();
            Filter = reader.ReadInt32();
            Page = reader.ReadInt32();
            Sort = (InventorySort)reader.ReadInt32();
            RarityFilter = reader.ReadInt32();
            FavoritesOnly = reader.ReadInt32() != 0;
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            writer.Write(DestAddress);
            writer.Write(DestHasFlagAddress);
            writer.Write(DestTimeFlagAddress);
            writer.Write(Filter);
            writer.Write(Page);
            writer.Write((int)Sort);
            writer.Write(RarityFilter);
            writer.Write(FavoritesOnly ? 1 : 0);
        }
    }
}
