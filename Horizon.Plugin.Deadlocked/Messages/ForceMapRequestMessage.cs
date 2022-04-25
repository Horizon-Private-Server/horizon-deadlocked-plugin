using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class ForceMapRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 28;
        public override bool SkipEncryption { get => true; set { } }

        public int Level { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Level = reader.ReadInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Level);
        }
    }
}
