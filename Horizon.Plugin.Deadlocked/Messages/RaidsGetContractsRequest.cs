using Server.Common.Stream;
using System;

namespace Horizon.Plugin.Deadlocked.Messages
{
    [Flags]
    public enum RaidsContractRequestFlags : byte
    {
        None = 0,
        Pending = 1 << 1
    }

    public class RaidsGetContractsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 64;
        public override bool SkipEncryption { get => true; set { } }

        public uint RequestId { get; set; }
        public ushort PendingOffset { get; set; }
        public byte Difficulty { get; set; }
        public RaidsContractRequestFlags Flags { get; set; }
        public string MapFilename { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            RequestId = reader.ReadUInt32();
            PendingOffset = reader.ReadUInt16();
            Difficulty = reader.ReadByte();
            Flags = (RaidsContractRequestFlags)reader.ReadByte();
            MapFilename = reader.ReadString(64);
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
