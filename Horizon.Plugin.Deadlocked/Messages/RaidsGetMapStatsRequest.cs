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
    public class RaidsGetMapStatsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 59;
        public override bool SkipEncryption { get => true; set { } }

        public uint ResponseAddress { get; set; }
        public int CollectiblesCount { get; set; }
        public int ChallengesCount { get; set; }
        public string MapFilename { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            ResponseAddress = reader.ReadUInt32();
            CollectiblesCount = reader.ReadInt32();
            ChallengesCount = reader.ReadInt32();
            MapFilename = reader.ReadString(64);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(ResponseAddress);
            writer.Write(CollectiblesCount);
            writer.Write(ChallengesCount);
            writer.Write(MapFilename, 64);
        }
    }
}
