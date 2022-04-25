using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class QueueBeginRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 22;
        public override bool SkipEncryption { get => true; set { } }

        public QueueIds QueueId { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            QueueId = reader.Read<QueueIds>();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(QueueId);
        }
    }
}
