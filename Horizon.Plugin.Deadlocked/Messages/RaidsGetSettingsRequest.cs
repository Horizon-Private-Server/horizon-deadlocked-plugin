using Server.Common.Stream;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsGetSettingsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 77;
        public override bool SkipEncryption { get => true; set { } }

        public uint DestAddress { get; set; }
        public uint DestHasFlagAddress { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            DestAddress = reader.ReadUInt32();
            DestHasFlagAddress = reader.ReadUInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            writer.Write(DestAddress);
            writer.Write(DestHasFlagAddress);
        }
    }
}
