using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public partial class RaidsAccount
    {
        public const int SKILLS_COUNT = 4;
        public const int LEGACY_WEAPON_COUNT = 8;
        public const int SERIALIZED_SIZE = 192;

        [JsonConverter(typeof(UInt128JsonConverter))]
        public UInt128 OwnedTitles { get; set; }

        public ulong Experience { get; set; } = 0;
        public ulong Dreadpoints { get; set; } = 0;

        // saved metadata only; migration 5 still needs these for returning accounts
        public double[] WeaponXp { get; set; } = new double[LEGACY_WEAPON_COUNT];
        public double WeaponExperience { get; set; } = 0;
        public RaidsClassType EquippedClass { get; set; } = RaidsClassType.Gunner;
        public Dictionary<RaidsClassType, RaidsClassStats> ClassStats { get; set; } = new Dictionary<RaidsClassType, RaidsClassStats>();
        public uint Bolts { get; set; } = 0;
        public RaidsUserSettings Settings { get; set; } = new RaidsUserSettings();
        public Dictionary<Gadgets, bool> HasWeapon { get; set; } = new Dictionary<Gadgets, bool>();

        public RaidsPlayerTitle EquippedTitle { get; set; }

        public bool HasTitle(uint title)
        {
            return title == 0 || (title < RaidsTitles.TITLE_CAPACITY
                && (OwnedTitles & ((UInt128)1 << (int)title)) != 0);
        }

        public RaidsClassProgression GetEquippedClass()
        {
            return RaidsClasses.Definitions.FirstOrDefault(x => x.ClassType == EquippedClass);
        }

        public int GetClassSkillPoints(RaidsClassType classType)
        {
            var raidsClass = RaidsClasses.Definitions.FirstOrDefault(x => x.ClassType == classType);
            if (raidsClass == null || !ClassStats.TryGetValue(classType, out var stats)) return 0;
            return Math.Max(0, raidsClass.GetClassLevelFromXp(stats.Experience) - stats.SkillRanks.Values.Sum());
        }

        public RaidsClassStats GetEquippedClassStats()
        {
            if (ClassStats.TryGetValue(EquippedClass, out var classStats))
                return classStats;

            return ClassStats[EquippedClass] = new RaidsClassStats();
        }

        static double GetLevelFromXpQuadratic(double xp, double a, double b)
        {
            return (-b + Math.Sqrt(b * b + 4 * a * xp)) / (2 * a);
        }

        static double GetXpFromLevelQuadratic(double level, double a, double b)
        {
            return a * level * level + b * level;
        }

        public static int GetAccountLevelFromXp(ulong xp)
        {
            if (xp < 0) return 0;

            int level = 0;
            while (GetAccountXpFromLevel(level + 1) <= xp && level < RaidsCustomMode.MAX_ACCOUNT_LEVEL)
                ++level;

            return level;
        }

        public static ulong GetAccountXpFromLevel(int level)
        {
            if (level <= 0) return 0;
            if (level > RaidsCustomMode.MAX_ACCOUNT_LEVEL) level = RaidsCustomMode.MAX_ACCOUNT_LEVEL;

            ulong value = (ulong)level;
            return 3 * (10 * value * value * value + 250 * value);
        }

        public static int GetProficiencyFromXp(double xp)
        {
            if (xp < 0) return 0;

            int level = 0;
            while (xp >= GetXpFromProficiency(level+1) && level < RaidsCustomMode.MAX_WEAPON_LEVEL)
                ++level;

            return level;
            //if (level < 0) return 0;
            //if (level > RaidsCustomMode.MAX_WEAPON_LEVEL) return RaidsCustomMode.MAX_WEAPON_LEVEL;
            //return (int)level;
        }

        public static double GetXpFromProficiency(int proficiency)
        {
            if (proficiency <= 0) return 0;
            if (proficiency > RaidsCustomMode.MAX_WEAPON_LEVEL) proficiency = RaidsCustomMode.MAX_WEAPON_LEVEL;
            return 10 * Math.Pow(proficiency, 3) + 100 * proficiency + 250;
            //return GetXpFromLevelQuadratic(proficiency, 250, 500);
        }

        public double GetWeaponExperience() => WeaponExperience;
        public int GetWeaponProficiency() => GetProficiencyFromXp(WeaponExperience);

        public bool HasUnlockedWeapon(Gadgets gadget)
        {
            return HasWeapon != null && HasWeapon.TryGetValue(gadget, out var unlocked) && unlocked;
        }

        public void UnlockWeapon(Gadgets gadget)
        {
            HasWeapon[gadget] = true;
        }

        public void ClampLevels()
        {
            var maxAccountXp = GetAccountXpFromLevel(RaidsCustomMode.MAX_ACCOUNT_LEVEL + 1);
            if (Experience > maxAccountXp)
                Experience = maxAccountXp;

            var raidsClass = GetEquippedClass();
            var classStats = GetEquippedClassStats();
            if (raidsClass is not null && classStats is not null)
            {
                var maxClassXp = raidsClass.GetClassXpFromLevel(RaidsCustomMode.MAX_CLASS_LEVEL);
                if (classStats.Experience > maxClassXp)
                    classStats.Experience = maxClassXp;
            }
        }

        public void AddBolts(uint bolts)
        {
            long newBolts = Bolts + bolts;
            Bolts = (uint)Math.Clamp(newBolts, 0, uint.MaxValue);
        }

        public void AddXp(ulong xp)
        {
            Experience += xp;
            ClampLevels();
        }

        public void AddWeaponExperience(double xp)
        {
            if (!double.IsFinite(xp) || xp <= 0 || !double.IsFinite(WeaponExperience + xp)) return;
            WeaponExperience += xp;
        }

        public void Update(RaidsAccount account)
        {
            this.Experience = account.Experience;
            this.Dreadpoints = account.Dreadpoints;
            this.Bolts = account.Bolts;
            this.WeaponExperience = account.WeaponExperience;
            if (RaidsClasses.Definitions.Any(x => x.ClassType == account.EquippedClass))
                this.EquippedClass = account.EquippedClass;
            foreach (var raidsClass in RaidsClasses.Definitions)
            {
                if (!ClassStats.TryGetValue(raidsClass.ClassType, out var stats))
                    ClassStats[raidsClass.ClassType] = stats = new RaidsClassStats();
                stats.Experience = Math.Min(account.ClassStats.GetValueOrDefault(raidsClass.ClassType)?.Experience ?? 0, raidsClass.GetClassXpFromLevel(RaidsCustomMode.MAX_CLASS_LEVEL));
            }
            ClampLevels();
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.WriteUInt128(OwnedTitles);
            writer.Write(Experience);
            for (int i = 0; i < RaidsCustomMode.MAX_CLASSES; ++i)
                writer.Write(ClassStats.GetValueOrDefault((RaidsClassType)i)?.Experience ?? 0);
            writer.Write(WeaponExperience);
            writer.Write(Dreadpoints);
            writer.Write(Bolts);
            writer.Write((int)EquippedClass);
            writer.Write((uint)EquippedTitle);
            writer.Write(new byte[12]); // align the PS2 account to 16 bytes
        }

        public void Deserialize(BinaryReader reader)
        {
            OwnedTitles = reader.ReadUInt128();
            Experience = reader.ReadUInt64();
            for (int i = 0; i < RaidsCustomMode.MAX_CLASSES; ++i)
                ClassStats[(RaidsClassType)i] = new RaidsClassStats { Experience = reader.ReadUInt64() };
            WeaponExperience = reader.ReadDouble();
            Dreadpoints = reader.ReadUInt64();
            Bolts = reader.ReadUInt32();
            EquippedClass = (RaidsClassType)reader.ReadUInt32();
            EquippedTitle = (RaidsPlayerTitle)reader.ReadUInt32();
            reader.ReadBytes(12); // 16-byte account alignment
        }
    }
}
