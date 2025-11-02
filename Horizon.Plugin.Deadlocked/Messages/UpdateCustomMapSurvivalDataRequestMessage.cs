using Horizon.Plugin.Deadlocked.CustomModes;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class UpdateCustomMapSurvivalDataRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 70;
        public override bool SkipEncryption { get => true; set { } }

        public int GambitCount { get; set; }
        public string MapFilename { get; set; }
        public string MapName { get; set; }
        public List<string> Gambits { get; set; } = new List<string>();

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            GambitCount = reader.ReadInt32();
            MapFilename = reader.ReadString(64);
            MapName = reader.ReadString(32);
            for (int i = 0; i < GambitCount; ++i)
                Gambits.Add(reader.ReadString(32));
        }

        public override void Serialize(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
