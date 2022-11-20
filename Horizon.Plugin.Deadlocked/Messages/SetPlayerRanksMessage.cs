using RT.Common;
using RT.Models;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class SetPlayerRanksMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 20;
        public override bool SkipEncryption { get => true; set { } }

        public bool Enabled { get; set; }
        public int[] AccountIds { get; set; } = new int[10];
        public float[] Ranks { get; set; } = new float[10];

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Enabled = reader.ReadInt32() != 0;

            if (AccountIds == null || AccountIds.Length != 10)
                AccountIds = new int[10];
            for (int i = 0; i < 10; ++i)
                AccountIds[i] = reader.ReadInt32();

            if (Ranks == null || Ranks.Length != 10)
                Ranks = new float[10];
            for (int i = 0; i < 10; ++i)
                Ranks[i] = reader.ReadSingle();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Enabled ? 1 : 0);

            for (int i = 0; i < 10; ++i)
                writer.Write(i < AccountIds?.Length ? AccountIds[i] : 0);
            for (int i = 0; i < 10; ++i)
                writer.Write(i < Ranks?.Length ? Ranks[i] : 0);
        }
    }
}
