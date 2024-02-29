using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class NameChangeResponseMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 49;
        public override bool SkipEncryption { get => true; set { } }

        public bool Success;

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Success = reader.ReadBoolean();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Success);
        }
    }
}
