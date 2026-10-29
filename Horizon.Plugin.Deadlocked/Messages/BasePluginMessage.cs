using RT.Common;
using RT.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public abstract class BasePluginMessage : BaseMediusMessage
    {
        public override NetMessageTypes PacketClass => NetMessageTypes.MessageClassDME;
        public override byte PacketType => 7;
        public abstract byte CustomMsgId { get; }


        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            var customMsgId = reader.ReadByte();
            reader.ReadBytes(3);
            if (customMsgId != CustomMsgId)
                throw new InvalidOperationException($"{this} expected custom msg id {CustomMsgId} but read {customMsgId}");

            base.Deserialize(reader);
        }

        public sealed override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            var startPos = writer.BaseStream.Position;

            writer.Write(this.CustomMsgId);
            writer.Write(new byte[3]);
            SerializePayload(writer);

            // The payload must be complete before its size is written.
            var endPos = writer.BaseStream.Position;
            var dataSize = checked((ushort)(endPos - startPos - 4));
            writer.BaseStream.Position = startPos + 1;
            writer.Write((byte)1);
            writer.Write(dataSize);
            writer.BaseStream.Position = endPos;
        }

        protected abstract void SerializePayload(Server.Common.Stream.MessageWriter writer);
    }
}
