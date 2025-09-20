using Horizon.Plugin.Deadlocked.CustomModes;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsUpdateMapMetadataRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 67;
        public override bool SkipEncryption { get => true; set { } }

        public string MapFilename { get; set; }
        public int MobOClass { get; set; }
        public int MobDifficulty { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            MapFilename = reader.ReadString(64);
            MobOClass = reader.ReadInt32();
            MobDifficulty = reader.ReadInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(MapFilename, 64);
            writer.Write(MobOClass);
            writer.Write(MobDifficulty);
        }
    }
}
