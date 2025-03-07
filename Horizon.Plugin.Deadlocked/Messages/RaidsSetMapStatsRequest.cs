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
    public class RaidsSetMapStatsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 60;
        public override bool SkipEncryption { get => true; set { } }

        public int CollectiblesCount { get; set; }
        public uint CollectiblesMask { get; set; }
        public int ChallengesCount { get; set; }
        public uint ChallengesMask { get; set; }
        public string MapFilename { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            CollectiblesCount = reader.ReadInt32();
            CollectiblesMask = reader.ReadUInt32();
            ChallengesCount = reader.ReadInt32();
            ChallengesMask = reader.ReadUInt32();
            MapFilename = reader.ReadString(64);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(CollectiblesCount);
            writer.Write(CollectiblesMask);
            writer.Write(ChallengesCount);
            writer.Write(ChallengesMask);
            writer.Write(MapFilename, 64);
        }
    }
}
