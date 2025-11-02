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
    public class UpdateSurvivalGambitCompletedRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 71;
        public override bool SkipEncryption { get => true; set { } }

        public int GambitIdx { get; set; }
        public string MapFilename { get; set; }
        public string GambitName { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            GambitIdx = reader.ReadInt32();
            MapFilename = reader.ReadString(64);
            GambitName = reader.ReadString(32);
        }

        public override void Serialize(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
