using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsClassProgression
    {
        public RaidsClassType ClassType { get; set; }
        public int Tier { get; set; }
        public decimal ExperienceCurveMultiplier { get; set; }
        public Gadgets[] Weapons { get; set; }
        public (RaidsClassType ClassType, int RequiredLevel)[] UnlockRules { get; set; }

        public RaidsClassProgression(RaidsClassType classType, decimal xpCurveMult, int tier, Gadgets[] weapons, (RaidsClassType ClassType, int RequiredLevel)[] unlockRules = null)
        {
            ClassType = classType;
            Tier = tier;
            ExperienceCurveMultiplier = xpCurveMult;
            Weapons = weapons;
            UnlockRules = unlockRules ?? [];
        }

        public Gadgets[] GetUnlockedWeapons(RaidsAccount account)
        {
            var skillRanks = account.ClassStats?.GetValueOrDefault(ClassType)?.SkillRanks;
            if (skillRanks == null) return Weapons;

            var weapons = Weapons.ToList();

            // map code validates allocations; use this class's confirmed purchased ranks
            foreach (var skill in skillRanks.Where(x => x.Value > 0))
            {
                Gadgets? weapon = (RaidsClassSkillId)skill.Key switch
                {
                    RaidsClassSkillId.ViperMaxRarity => Gadgets.Vipers,
                    RaidsClassSkillId.MagmaCannonMaxRarity => Gadgets.MagmaCannon,
                    RaidsClassSkillId.ArbiterMaxRarity => Gadgets.Arbiter,
                    RaidsClassSkillId.FusionMaxRarity => Gadgets.Fusion,
                    RaidsClassSkillId.MineLauncherMaxRarity => Gadgets.MineLauncher,
                    RaidsClassSkillId.B6MaxRarity => Gadgets.B6,
                    RaidsClassSkillId.FlailMaxRarity => Gadgets.Flail,
                    RaidsClassSkillId.HoloshieldMaxRarity => Gadgets.Holoshields,
                    _ => null,
                };
                if (weapon.HasValue && !weapons.Contains(weapon.Value))
                    weapons.Add(weapon.Value);
            }
            return weapons.ToArray();
        }

        public bool IsUnlocked(RaidsAccount account)
        {
            return UnlockRules.All(rule =>
            {
                var requiredClass = RaidsClasses.Definitions.FirstOrDefault(x => x.ClassType == rule.ClassType);
                var xp = account.ClassStats?.GetValueOrDefault(rule.ClassType)?.Experience ?? 0;
                return requiredClass != null && requiredClass.GetClassLevelFromXp(xp) >= rule.RequiredLevel;
            });
        }

        public int GetClassLevelFromXp(ulong xp)
        {
            int level = 0;
            while (level < RaidsCustomMode.MAX_CLASS_LEVEL && GetClassXpFromLevel(level + 1) <= xp)
                ++level;
            return level;
        }

        public ulong GetClassXpFromLevel(int level)
        {
            if (level <= 0) return 0;
            if (level > RaidsCustomMode.MAX_CLASS_LEVEL) level = RaidsCustomMode.MAX_CLASS_LEVEL;
            return (ulong)((10 * (double)Math.Pow(level, 4) + 250 * level) * (double)ExperienceCurveMultiplier);
        }
    }
}
