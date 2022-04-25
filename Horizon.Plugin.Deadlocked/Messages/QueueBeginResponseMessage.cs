using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class QueueBeginResponseMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 23;
        public override bool SkipEncryption { get => true; set { } }

        public int Status { get; set; }
        public QueueIds CurrentQueueId { get; set; }
        public int SecondsInQueue { get; set; }
        public int PlayersInQueue { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Status = reader.ReadInt32();
            CurrentQueueId = reader.Read<QueueIds>();
            SecondsInQueue = reader.ReadInt32();
            PlayersInQueue = reader.ReadInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Status);
            writer.Write(CurrentQueueId);
            writer.Write(SecondsInQueue);
            writer.Write(PlayersInQueue);
        }
    }
}
