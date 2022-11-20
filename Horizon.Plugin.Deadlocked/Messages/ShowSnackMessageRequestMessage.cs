using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class ShowSnackMessageRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 34;
        public override bool SkipEncryption { get => true; set { } }

        public string Message { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Message = reader.ReadString(64);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.WriteStr(Message, 64);
        }
    }
}
