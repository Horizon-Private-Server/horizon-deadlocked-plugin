using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class ForceJoinGameRequestMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 26;
        public override bool SkipEncryption { get => true; set { } }

        public int DmeWorldId { get; set; }
        public int ChannelMediusWorldId { get; set; }
        public int GameMediusWorldId { get; set; }
        public int PlayerCount { get; set; }
        public uint WeaponFlags { get; set; }
        public bool AmIHost { get; set; }
        public byte Level { get; set; }
        public byte Ruleset { get; set; }
        public byte[] GameFlags { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            DmeWorldId = reader.ReadInt32();
            ChannelMediusWorldId = reader.ReadInt32();
            GameMediusWorldId = reader.ReadInt32();
            PlayerCount = reader.ReadInt32();
            WeaponFlags = reader.ReadUInt32();
            AmIHost = reader.ReadBoolean();
            Level = reader.ReadByte();
            Ruleset = reader.ReadByte();
            GameFlags = reader.ReadBytes(59);
            reader.ReadBytes(2);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(DmeWorldId);
            writer.Write(ChannelMediusWorldId);
            writer.Write(GameMediusWorldId);
            writer.Write(PlayerCount);
            writer.Write(WeaponFlags);
            writer.Write(AmIHost);
            writer.Write(Level);
            writer.Write(Ruleset);
            writer.Write(GameFlags ?? new byte[59]);
            writer.Write(new byte[2]);
        }
    }
}
