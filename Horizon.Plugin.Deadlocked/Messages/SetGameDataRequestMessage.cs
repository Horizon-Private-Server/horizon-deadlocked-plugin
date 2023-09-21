using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class SetGameDataRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 15;
        public override bool SkipEncryption { get => true; set { } }

        public short Offset { get; set; }
        public bool EndOfList { get; set; }
        public byte[] Payload { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Offset = reader.ReadInt16();
            var len = reader.ReadInt16();
            EndOfList = reader.ReadBoolean();
            Payload = reader.ReadBytes(len);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Offset);
            writer.Write((short)(Payload?.Length ?? 0));
            writer.Write(EndOfList);
            if (Payload != null)
                writer.Write(Payload);
        }
    }
}
