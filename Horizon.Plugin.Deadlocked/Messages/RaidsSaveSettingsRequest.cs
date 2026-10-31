using Horizon.Plugin.Deadlocked.CustomModes;
using Server.Common.Stream;
using System;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsSaveSettingsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 78;
        public override bool SkipEncryption { get => true; set { } }

        public RaidsUserSettings Settings { get; set; }
        public uint AckAddress { get; set; }
        public int Revision { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            Settings = new RaidsUserSettings
            {
                Version = reader.ReadInt32(),
                MapLod = reader.ReadInt32(),
                MobLod = reader.ReadInt32(),
                AmmoDropPopups = reader.ReadInt32()
            };
            AckAddress = reader.ReadUInt32();
            Revision = reader.ReadInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
