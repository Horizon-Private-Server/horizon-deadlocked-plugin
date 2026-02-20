using RT.Common;
using RT.Models;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class DataDownloadResponseMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 14;

        public int Id { get; set; }
        public int BytesReceived { get; set; }
        public bool Stop { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Id = reader.ReadInt32();
            BytesReceived = reader.ReadInt32();
            Stop = reader.ReadInt32() != 0;
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Id);
            writer.Write(BytesReceived);
            writer.Write(Stop ? 1 : 0);
        }
    }
}
