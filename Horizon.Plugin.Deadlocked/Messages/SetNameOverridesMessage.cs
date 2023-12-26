using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class SetNameOverridesMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 45;
        public override bool SkipEncryption { get => true; set { } }

        public int[] AccountIds { get; set; } = new int[10];
        public string[] Names { get; set; } = new string[10];

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            AccountIds = reader.ReadArray<int>(10);
            Names = new string[10];
            for (int i = 0; i < 10; ++i)
                Names[i] = reader.ReadString(16);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            for (int i = 0; i < 10; ++i)
                writer.Write(i < AccountIds.Length ? AccountIds[i] : 0);
            for (int i = 0; i < 10; ++i)
                writer.WriteStr(i < Names.Length ? Names[i] : "", 16);
        }
    }
}
