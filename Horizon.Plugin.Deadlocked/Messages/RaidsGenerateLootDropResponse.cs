using Horizon.Plugin.Deadlocked.CustomModes;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsGenerateLootDropResponse : BasePluginMessage
    {
        public override byte CustomMsgId => 56;
        public override bool SkipEncryption { get => true; set { } }

        public Vector3 Position { get; set; }
        public RaidsInventoryWeapon Drop { get; set; } = new RaidsInventoryWeapon();
        
        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            reader.ReadSingle(); // padding
            Drop.Deserialize(reader);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Position.X);
            writer.Write(Position.Y);
            writer.Write(Position.Z);
            writer.Write(0f);
            Drop.Serialize(writer);
        }
    }
}
