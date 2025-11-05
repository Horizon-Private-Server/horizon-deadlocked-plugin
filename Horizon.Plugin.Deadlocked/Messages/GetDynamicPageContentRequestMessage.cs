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
    public class GetDynamicPageContentRequestMessage : BasePluginMessage
    {
        public enum ContentType
        {
            None = 0,
            SurvivalMapStats = 1,
            ObstacleMapStats = 2,
        }

        public override byte CustomMsgId => 69;
        public override bool SkipEncryption { get => true; set { } }

        public ContentType Type { get; set; }
        public uint StateAddress { get; set; }
        public uint LineItemsCountAddress { get; set; }
        public uint LineItemsAddress { get; set; }
        public string MapFilename { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            Type = (ContentType)reader.ReadInt32();
            StateAddress = reader.ReadUInt32();
            LineItemsCountAddress = reader.ReadUInt32();
            LineItemsAddress = reader.ReadUInt32();
            MapFilename = reader.ReadString(64);
        }

        public override void Serialize(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
