using System.Collections.Generic;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public static class RaidsClasses
    {
        // Keep progression, weapons and unlock rules in sync with codegen/rc4/raids/classes/classes.c.
        public static readonly List<RaidsClassProgression> Definitions =
        [
            new(RaidsClassType.Gunner, 1.0m, 1, [Gadgets.Vipers]),
            new(RaidsClassType.Bomber, 1.0m, 1, [Gadgets.B6]),
            new(RaidsClassType.Brawler, 1.0m, 1, [Gadgets.Flail]),
            new(RaidsClassType.Marksman, 2.0m, 2, [Gadgets.Vipers, Gadgets.Fusion], [(RaidsClassType.Gunner, 10)]),
            new(RaidsClassType.Grenadier, 2.0m, 2, [Gadgets.Arbiter, Gadgets.B6], [(RaidsClassType.Bomber, 10)]),
            new(RaidsClassType.Melee, 2.0m, 2, [Gadgets.MagmaCannon, Gadgets.Flail], [(RaidsClassType.Brawler, 10)]),
            new(RaidsClassType.Scout, 2.0m, 2, [Gadgets.Vipers, Gadgets.Flail], [(RaidsClassType.Gunner, 5), (RaidsClassType.Brawler, 5)]),
            new(RaidsClassType.Support, 2.0m, 2, [Gadgets.MineLauncher, Gadgets.Holoshields], [(RaidsClassType.Bomber, 5), (RaidsClassType.Gunner, 5)]),
            new(RaidsClassType.Sharpshooter, 3.0m, 3, [Gadgets.Vipers, Gadgets.Arbiter, Gadgets.Fusion], [(RaidsClassType.Marksman, 10)]),
            new(RaidsClassType.Demolitionist, 3.0m, 3, [Gadgets.MagmaCannon, Gadgets.Arbiter, Gadgets.B6], [(RaidsClassType.Grenadier, 10)]),
            new(RaidsClassType.Juggernaut, 3.0m, 3, [Gadgets.MagmaCannon, Gadgets.Flail], [(RaidsClassType.Melee, 10)]),
            new(RaidsClassType.Infiltrator, 3.0m, 3, [Gadgets.Vipers, Gadgets.Arbiter, Gadgets.Flail], [(RaidsClassType.Scout, 10)]),
            new(RaidsClassType.FieldMedic, 3.0m, 3, [Gadgets.Fusion, Gadgets.MineLauncher, Gadgets.Holoshields], [(RaidsClassType.Support, 10)]),
        ];

        // Match the in-game class suffixes: level zero has no Roman numeral.
        private static readonly string[] ClassLevelSuffixes = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];

        public static string GetName(RaidsClassType classType)
        {
            return classType switch
            {
                RaidsClassType.Melee => "Berserker",
                RaidsClassType.FieldMedic => "Field Medic",
                _ => classType.ToString()
            };
        }

        public static string GetLevelSuffix(int level)
        {
            return level >= 0 && level < ClassLevelSuffixes.Length ? ClassLevelSuffixes[level] : string.Empty;
        }
    }
}
