using Horizon.Plugin.Deadlocked.Messages;
using Server.Common.Stream;
using Horizon.Plugin.Deadlocked.CustomModes.Raids;
using System;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages
{
    public class RaidsConsumableActionRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 80;
        public override bool SkipEncryption { get => true; set { } }
        public uint Action { get; set; }
        public uint Argument { get; set; }
        public uint RequestId { get; set; }
        public uint ReplyAddress { get; set; }
        public uint AccountSize { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            if (reader.BaseStream.Length - reader.BaseStream.Position != 20) return;
            Action = reader.ReadUInt32();
            Argument = reader.ReadUInt32();
            RequestId = reader.ReadUInt32();
            ReplyAddress = reader.ReadUInt32();
            AccountSize = reader.ReadUInt32();
        }

        protected override void SerializePayload(MessageWriter writer) => throw new NotImplementedException();
    }
}
