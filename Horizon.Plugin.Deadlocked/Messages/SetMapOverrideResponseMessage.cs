using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class SetMapOverrideResponseMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 4;
        public override bool SkipEncryption { get => true; set { } }

        public string MapFilename { get; set; }
        public int ClientMapVersion { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            ClientMapVersion = reader.ReadInt32();
            MapFilename = reader.ReadString(64);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(ClientMapVersion);
            writer.Write(MapFilename, 64);
        }
    }
}
