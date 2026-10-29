using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class GetMyQueueResponseMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 25;
        public override bool SkipEncryption { get => true; set { } }

        public QueueIds CurrentQueueId { get; set; }
        public int SecondsInQueue { get; set; }
        public int PlayersInQueue { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            CurrentQueueId = reader.Read<QueueIds>();
            SecondsInQueue = reader.ReadInt32();
            PlayersInQueue = reader.ReadInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            writer.Write(CurrentQueueId);
            writer.Write(SecondsInQueue);
            writer.Write(PlayersInQueue);
        }
    }
}
