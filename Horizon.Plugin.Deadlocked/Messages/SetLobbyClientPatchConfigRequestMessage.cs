using RT.Common;
using RT.Models;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class SetLobbyClientPatchConfigRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 33;
        public override bool SkipEncryption { get => true; set { } }

        public int DmeId { get; set; }
        public PlayerConfig Config { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            DmeId = reader.ReadInt32();
            Config = new PlayerConfig();
            Config.Deserialize(reader);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(DmeId);
            writer.Write(Config.Serialize());
        }
    }
}
