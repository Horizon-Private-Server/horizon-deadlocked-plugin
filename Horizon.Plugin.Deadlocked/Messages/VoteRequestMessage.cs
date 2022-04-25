using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public enum VoteContext : int
    {
        SkipMap = 0,
    };

    public class VoteRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 29;
        public override bool SkipEncryption { get => true; set { } }

        public VoteContext Context { get; set; }
        public int Vote { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Context = reader.Read<VoteContext>();
            Vote = reader.ReadInt32();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Context);
            writer.Write(Vote);
        }
    }
}
