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
    public class RaidsGenerateLootDropRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 55;
        public override bool SkipEncryption { get => true; set { } }

        public enum GenerateLootDropRequestType
        {
            MobDeath = 0,
            MissionCompleteEvent,
            Prestige,
            Store
        }

        public Vector3 Position { get; set; }
        public GenerateLootDropRequestType Type { get; set; }
        public int DifficultyStars { get; set; }

        // MOB (if mob death triggered loot)
        public float MobHealth { get; set; }
        public float MobSpeed { get; set; }
        public float MobDamage { get; set; }
        public int MobMobyOClass { get; set; }
        public Gadgets MobKilledByGadget { get; set; }

        public bool IsAccountPrestige() => Type == GenerateLootDropRequestType.Prestige && (int)MobKilledByGadget == 0;
        public bool IsPrestige() => Type == GenerateLootDropRequestType.Prestige;
        public bool IsStore() => Type == GenerateLootDropRequestType.Store;

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            reader.ReadSingle(); // padding
            Type = (GenerateLootDropRequestType)reader.ReadInt32();
            DifficultyStars = reader.ReadInt32();
            MobHealth = reader.ReadSingle();
            MobDamage = reader.ReadSingle();
            MobSpeed = reader.ReadSingle();
            MobKilledByGadget = (Gadgets)reader.ReadInt32();
            MobMobyOClass = reader.ReadInt16();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Position.X);
            writer.Write(Position.Y);
            writer.Write(Position.Z);
            writer.Write(0f);
            writer.Write(Type);
            writer.Write(DifficultyStars);
            writer.Write(MobHealth);
            writer.Write(MobDamage);
            writer.Write(MobSpeed);
            writer.Write(MobKilledByGadget);
            writer.Write((ushort)MobMobyOClass);
        }
    }
}
