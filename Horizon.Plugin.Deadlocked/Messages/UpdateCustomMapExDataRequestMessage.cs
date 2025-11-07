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
    public class UpdateCustomMapExDataRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 72;
        public override bool SkipEncryption { get => true; set { } }

        public string MapFilename { get; set; }
        public CustomModeId CustomModeId { get; set; }
        public bool End { get; set; }
        public ushort Offset { get; set; }
        public ushort Length { get; set; }
        public byte[] Data { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            Offset = reader.ReadUInt16();
            Length = reader.ReadUInt16();
            CustomModeId = (CustomModeId)reader.ReadByte();
            End = reader.ReadBoolean();
            MapFilename = reader.ReadString(64);
            Data = reader.ReadRest();
        }

        public override void Serialize(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
