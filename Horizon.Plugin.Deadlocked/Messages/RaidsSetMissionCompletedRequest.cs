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
    public class RaidsSetMissionCompletedRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 61;
        public override bool SkipEncryption { get => true; set { } }

        public uint CompletedInMs { get; set; }
        public int Difficulty { get; set; }
        public string MapFilename { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            CompletedInMs = reader.ReadUInt32();
            Difficulty = reader.ReadInt32();
            MapFilename = reader.ReadString(64);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(CompletedInMs);
            writer.Write(Difficulty);
            writer.Write(MapFilename, 64);
        }
    }
}
