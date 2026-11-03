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
            if (reader.BaseStream.Length - reader.BaseStream.Position != RaidsUserSettings.SERIALIZED_SIZE + 8) return;
            Settings = new RaidsUserSettings();
            Settings.Deserialize(reader);
            AckAddress = reader.ReadUInt32();
            Revision = reader.ReadInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
