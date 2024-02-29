using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class NameChangeRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 48;
        public override bool SkipEncryption { get => true; set { } }

        public string Name { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Name = reader.ReadString(16);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.WriteStr(Name ?? "", 16);
        }
    }
}
