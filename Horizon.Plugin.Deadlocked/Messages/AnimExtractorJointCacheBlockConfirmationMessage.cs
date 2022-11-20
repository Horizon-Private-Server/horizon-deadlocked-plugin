using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    class AnimExtractorJointCacheBlockConfirmationMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 102;
        public override bool SkipEncryption { get => true; set { } }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);
        }
    }
}
