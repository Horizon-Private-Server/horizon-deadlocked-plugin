using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class ForceTeamsRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 27;
        public override bool SkipEncryption { get => true; set { } }

        public int[] AccountIds { get; set; }
        public byte[] Teams { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            AccountIds = reader.ReadArray<int>(10);
            Teams = reader.ReadBytes(10);
            reader.ReadBytes(2);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            for (int i = 0; i < 10; ++i)
            {
                if (AccountIds == null || i >= AccountIds.Length)
                    writer.Write(-1);
                else
                    writer.Write(AccountIds[i]);
            }

            for (int i = 0; i < 10; ++i)
            {
                if (Teams == null || i >= Teams.Length)
                    writer.Write((byte)0);
                else
                    writer.Write(Teams[i]);
            }

            writer.Write(new byte[2]);
        }
    }
}
