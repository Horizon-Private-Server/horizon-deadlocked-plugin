using RT.Common;
using RT.Models;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class SetClientTypeRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 41;
        public override bool SkipEncryption { get => true; set { } }

        public PlayerClientType ClientType { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            ClientType = (PlayerClientType)reader.ReadInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write((int)ClientType);
        }
    }
}
