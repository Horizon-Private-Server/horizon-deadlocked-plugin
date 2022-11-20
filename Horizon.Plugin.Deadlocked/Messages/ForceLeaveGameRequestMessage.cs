using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class ForceLeaveGameRequestMessage : BasePluginMessage
    {
        public enum ForceLeaveReason
        {
            NONE,
            QUEUE_PLAYERS_NOT_JOINED
        }

        public override byte CustomMsgId => 31;
        public override bool SkipEncryption { get => true; set { } }

        public ForceLeaveReason Reason { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Reason = reader.Read<ForceLeaveReason>();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Reason);
        }
    }
}
