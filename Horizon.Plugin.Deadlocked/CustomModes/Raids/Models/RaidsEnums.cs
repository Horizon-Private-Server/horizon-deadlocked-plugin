using System;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public enum RaidsWeaponPaints
    {
        None = 0,
        Blue,
        Red,
        Green,
        Orange,
        Yellow,
        Purple,
        Pink,
        Aqua,
        Olive,
        Maroon,
        COUNT
    }

    [Flags]
    public enum RaidsPaintSpecialFlags
    {
        None = 0,
        Glow = 1 << 0,
        Additive = 1 << 1,
        ALL = Glow | Additive
    }

    public enum RaidsClassSkillId : byte
    {
        None = 0,
        Damage = 1,
        MovementSpeed = 2,
        CloseRangeDamage = 3,
        LongRangeDamage = 4,
        ExtraJumps = 5,
        ChargebootLength = 6,
        WrenchDamage = 7,
        AmmoDropChance = 8,
        HealingField = 9,
        AmmoField = 10,
        CriticalChance = 11,
        DamageImmunity = 12,
        AlphaArea = 13,
        AlphaSpeed = 14,
        AlphaNanoleech = 15,
        FlinchImmunity = 16,
        Hoverboots = 17,
        Visibomb = 18,
        WallWalking = 19,
        GoldBoltIndicator = 20,
        VehicleDamage = 21,
        VehicleHealth = 22,
        MaxHealth = 23,
        AlphaAmmo = 24,
        ViperProjectileSpeed = 25,
        B6ProjectileSpeed = 26,
        BlastReach = 27,
        SecondWind = 28,
        AirSuperiority = 29,
        EfficientSupplies = 30,
        Stealth = 31,
        Deadeye = 32,
        CombatMedic = 33,
        ClusterBombs = 34,
        UnstoppableSmash = 35,
        ViperMaxRarity = 36,
        MagmaCannonMaxRarity = 37,
        ArbiterMaxRarity = 38,
        FusionMaxRarity = 39,
        MineLauncherMaxRarity = 40,
        B6MaxRarity = 41,
        FlailMaxRarity = 42,
        HoloshieldMaxRarity = 43,
        AlphaImpact = 44,
        BloodFrenzy = 45,
        Ironclad = 46,
    };

    public enum RaidsClassType
    {
        Gunner = 0,
        Melee = 1,
        Grenadier = 2,
        Support = 3,
        Scout = 4,
        Marksman = 5,
        Bomber = 6,
        Juggernaut = 7,
        Demolitionist = 8,
        FieldMedic = 9,
        Infiltrator = 10,
        Sharpshooter = 11,
        Brawler = 12,
    };

    public enum RaidsItemType
    {
        None = 0,
        Weapon,
        Misc,
    };

    public enum RaidsMiscItemType : uint
    {
        None = 0,
        DoubleXp,
        SalvagersSignal,
        FortunesFavor,
    };

    // Append types; these indices are shared with the C timer/count arrays.
    public enum RaidsConsumableType
    {
        SalvagersSignal = 0,
        FortunesFavor = 1,
        DoubleXp = 2,
        Count = 3,
    };

    public enum RaidsItemRarity
    {
        Common = 0,
        Uncommon,
        Rare,
        Legendary,
        Mythic
    };

    public enum RaidsMissionType
    {
        Hub = 0,
        OpenWorld = 1,
        Raid = 2,
        Minigame = 3
    }

    public enum RaidsContractType
    {
        Kill = 0,
        Clear = 1,
        Task = 2
    }

    [Flags]
    public enum RaidsContractFlags : byte
    {
        None = 0,
        Complete = 1 << 0,
        Expired = 1 << 1,
        Claimable = 1 << 2,
        Claimed = 1 << 3,
        SpeedBonusEarned = 1 << 4,
        TaskStarted = 1 << 5
    }
}
