using System.IO;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsUserSettings
    {
        public const int SERIALIZED_SIZE = 3;

        public int MapLod { get; set; } = 2;
        public int MobLod { get; set; } = 2;
        public int AmmoDropPopups { get; set; } = 1;
        public int ContractProgressBars { get; set; } = 1;
        public int KillXpPopups { get; set; } = 1;
        public int AnnouncerCommentary { get; set; } = 1;
        public int DialoguePlaybackSpeed { get; set; } = 0;
        public int SpawnBoltMobys { get; set; } = 1;

        public bool IsValid()
        {
            return MapLod >= 0 && MapLod <= 3
                && MobLod >= 0 && MobLod <= 3 && AmmoDropPopups >= 0 && AmmoDropPopups <= 1
                && ContractProgressBars >= 0 && ContractProgressBars <= 1
                && KillXpPopups >= 0 && KillXpPopups <= 1
                && AnnouncerCommentary >= 0 && AnnouncerCommentary <= 1
                && DialoguePlaybackSpeed >= 0 && DialoguePlaybackSpeed <= 1
                && SpawnBoltMobys >= 0 && SpawnBoltMobys <= 1;
        }

        public void Deserialize(BinaryReader reader)
        {
            MapLod = reader.ReadByte();
            MobLod = reader.ReadByte();
            var flags = reader.ReadByte();
            if ((flags & ~0x3F) != 0) throw new InvalidDataException("Invalid raids settings flags.");
            AmmoDropPopups = flags & 0x01;
            ContractProgressBars = (flags >> 1) & 0x01;
            KillXpPopups = (flags >> 2) & 0x01;
            AnnouncerCommentary = (flags & 0x08) == 0 ? 1 : 0;
            DialoguePlaybackSpeed = (flags >> 4) & 0x01;
            SpawnBoltMobys = (flags & 0x20) == 0 ? 1 : 0;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)MapLod);
            writer.Write((byte)MobLod);
            writer.Write((byte)(AmmoDropPopups | (ContractProgressBars << 1) | (KillXpPopups << 2) | ((1 - AnnouncerCommentary) << 3) | (DialoguePlaybackSpeed << 4) | ((1 - SpawnBoltMobys) << 5)));
        }
    }
}
