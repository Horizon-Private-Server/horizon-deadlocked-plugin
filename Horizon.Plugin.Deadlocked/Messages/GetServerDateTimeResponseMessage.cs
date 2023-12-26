using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class GetServerDateTimeResponseMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 47;
        public override bool SkipEncryption { get => true; set { } }

        public ushort Year { get; set; } = (ushort)DateTime.Now.Year;
        public byte Month { get; set; } = (byte)DateTime.Now.Month;
        public byte Day { get; set; } = (byte)DateTime.Now.Day;

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Year = reader.ReadUInt16();
            Month = reader.ReadByte();
            Day = reader.ReadByte();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Year);
            writer.Write(Month);
            writer.Write(Day);
        }
    }
}
