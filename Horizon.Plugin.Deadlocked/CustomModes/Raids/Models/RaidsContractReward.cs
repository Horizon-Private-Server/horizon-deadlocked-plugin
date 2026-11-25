using System;
using System.IO;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsContractReward
    {
        public uint Bolts { get; set; }
        public uint PlayerXp { get; set; }
        public uint WeaponXp { get; set; }
        public uint ClassXp { get; set; }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Bolts);
            writer.Write(PlayerXp);
            writer.Write(WeaponXp);
            writer.Write(ClassXp);
        }

        public static RaidsContractReward Read(BinaryReader reader) => new()
        {
            Bolts = reader.ReadUInt32(),
            PlayerXp = reader.ReadUInt32(),
            WeaponXp = reader.ReadUInt32(),
            ClassXp = reader.ReadUInt32()
        };

        public RaidsContractReward Add(RaidsContractReward other) => new()
        {
            Bolts = Saturate((ulong)Bolts + other.Bolts),
            PlayerXp = Saturate((ulong)PlayerXp + other.PlayerXp),
            WeaponXp = Saturate((ulong)WeaponXp + other.WeaponXp),
            ClassXp = Saturate((ulong)ClassXp + other.ClassXp)
        };

        public static uint Saturate(double value) => (uint)Math.Clamp(value, 0, uint.MaxValue);
    }
}
