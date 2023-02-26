using RT.Common;
using RT.Models;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class MapGlobalVersionMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 38;
        public override bool SkipEncryption { get => true; set { } }

        public int CustomMapsVersion { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            CustomMapsVersion = reader.ReadInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(CustomMapsVersion);
        }
    }
}
