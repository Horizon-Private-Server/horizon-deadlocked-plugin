using Server.Common.Stream;
using System;
using System.IO;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsContractResponse : BasePluginMessage
    {
        public override byte CustomMsgId => ResponseId;
        public override bool SkipEncryption { get => true; set { } }

        public byte ResponseId { get; set; }
        public byte[] Payload { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            throw new NotImplementedException();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            if (Payload == null || Payload.Length > 504)
                throw new InvalidDataException("Contract response exceeds the custom payload budget.");

            writer.Write(Payload);
        }
    }
}
