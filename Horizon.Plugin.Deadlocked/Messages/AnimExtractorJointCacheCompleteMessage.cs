using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    class AnimExtractorJointCacheCompleteMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 101;
        public override bool SkipEncryption { get => true; set { } }

        public short OClass { get; set; }
        public short SeqId { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            OClass = reader.ReadInt16();
            SeqId = reader.ReadInt16();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);
        }
    }
}
