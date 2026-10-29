using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class SetGameStartTimeRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 32;
        public override bool SkipEncryption { get => true; set { } }

        public int SecondsUntilStart { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            SecondsUntilStart = reader.ReadInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            writer.Write(SecondsUntilStart);
        }
    }
}
