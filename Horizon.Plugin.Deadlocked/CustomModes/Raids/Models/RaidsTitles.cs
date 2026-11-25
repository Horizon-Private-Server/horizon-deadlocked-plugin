using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models.TitleRequirements;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    // Append new ids; these values are saved in account metadata and shared with C.
    public enum RaidsPlayerTitle : uint
    {
        None = 0,
        BadWithMoney = 1,
        WillKillForBolts = 2,
        SunkCostEnthusiast = 3,
        CorporateLiability = 4,
        LootGoblin = 5,
        LegallyAWeapon = 6,
        MegacorpMiddleManager = 7,
        WalkingTaxWriteOff = 8,
        TrustFundBaby = 9,
        MorallyBankrupt = 10,
        FinanciallyUnsupervised = 11,
        SecretAgentClank = 12,
        OohShiny = 13,
        TooMuchFreeTime = 14,
        KingLeviathanCompletionist = 15,
        KingLeviathanExterminator = 16,
        WarrantyVoider = 17,
        UnlicensedEngineer = 18,
        SkillIssue = 19,
        MaterialGirl = 20,
        CompulsiveHoarder = 21,
        RngsFavoriteChild = 22,
        TheChosenOne = 23,
        BuiltDifferent = 24,
        NoLifer = 25,
        ChronicallyOnline = 26,
        GrassAvoider = 27,
        CertifiedButtonMasher = 28,
        ProfessionallyUnqualified = 29,
        ProbablyABot = 30,
        PublicNuisance = 31,
        InsuranceNightmare = 32,
        UnsupervisedAdult = 33,
        CourtneyGearsNumberOneFan = 34,
        ClankApologist = 35,
        QwarksDefenseAttorney = 36,
        AiSlop = 37,
        DefinitelyHuman = 38,
        DuctTapeEngineer = 39,
        CertifiedFireHazard = 40,
        WalkingRecall = 41,
        Uninsurable = 42,
        WarrantyCriminal = 43,
        FailedSuccessfully = 44,
        TaxEvader = 45,
        SleepDeprived = 46,
        PartTimeHuman = 47,
        CommunityProblem = 48,
        DiscountLombax = 49,
        DisposableTalent = 50,
        SponsorsRegret = 51,
        RatingsLiability = 52,
        BarelySentient = 53,
        FactoryDefect = 54,
        PreviouslyBanned = 55,
        ThreeBoltsInATrenchcoat = 56,
        CatacromCompletionist = 57,
        Rngesus = 58,
        NvidiaShareholder = 59,
        CircularFinancer = 60,
        Count = 61
    }

    public static class RaidsTitles
    {
        public const int TITLE_CAPACITY = 128;
        public const int MAX_REQUIREMENTS = 4;
        public const int REQUIREMENT_DESCRIPTION_LENGTH = 96;

        // Keep prices aligned with raids_hub/Source/rc4/titles.c. Requirements are server-owned.
        public static readonly IReadOnlyDictionary<RaidsPlayerTitle, uint> TitlePrices = new Dictionary<RaidsPlayerTitle, uint>
        {
            [RaidsPlayerTitle.None] = 0,
            [RaidsPlayerTitle.BadWithMoney] = 1_000_000,
            [RaidsPlayerTitle.WillKillForBolts] = 1_500_000,
            [RaidsPlayerTitle.SunkCostEnthusiast] = 2_000_000,
            [RaidsPlayerTitle.CorporateLiability] = 5_000_000,
            [RaidsPlayerTitle.LootGoblin] = 2_500_000,
            [RaidsPlayerTitle.LegallyAWeapon] = 15_000_000,
            [RaidsPlayerTitle.MegacorpMiddleManager] = 20_000_000,
            [RaidsPlayerTitle.WalkingTaxWriteOff] = 50_000_000,
            [RaidsPlayerTitle.TrustFundBaby] = 100_000_000,
            [RaidsPlayerTitle.MorallyBankrupt] = 200_000_000,
            [RaidsPlayerTitle.FinanciallyUnsupervised] = 500_000_000,
            [RaidsPlayerTitle.SecretAgentClank] = 30_000_000,
            [RaidsPlayerTitle.OohShiny] = 5_000_000,
            [RaidsPlayerTitle.TooMuchFreeTime] = 7_500_000,
            [RaidsPlayerTitle.KingLeviathanCompletionist] = 10_000_000,
            [RaidsPlayerTitle.KingLeviathanExterminator] = 20_000_000,
            [RaidsPlayerTitle.WarrantyVoider] = 5_000_000,
            [RaidsPlayerTitle.UnlicensedEngineer] = 25_000_000,
            [RaidsPlayerTitle.SkillIssue] = 10_000_000,
            [RaidsPlayerTitle.MaterialGirl] = 20_000_000,
            [RaidsPlayerTitle.CompulsiveHoarder] = 7_500_000,
            [RaidsPlayerTitle.RngsFavoriteChild] = 10_000_000,
            [RaidsPlayerTitle.TheChosenOne] = 10_000_000,
            [RaidsPlayerTitle.BuiltDifferent] = 100_000_000,
            [RaidsPlayerTitle.NoLifer] = 250_000_000,
            [RaidsPlayerTitle.ChronicallyOnline] = 20_000_000,
            [RaidsPlayerTitle.GrassAvoider] = 100_000_000,
            [RaidsPlayerTitle.CertifiedButtonMasher] = 5_000_000,
            [RaidsPlayerTitle.ProfessionallyUnqualified] = 2_000_000,
            [RaidsPlayerTitle.ProbablyABot] = 10_000_000,
            [RaidsPlayerTitle.PublicNuisance] = 3_000_000,
            [RaidsPlayerTitle.InsuranceNightmare] = 5_000_000,
            [RaidsPlayerTitle.UnsupervisedAdult] = 10_000_000,
            [RaidsPlayerTitle.CourtneyGearsNumberOneFan] = 15_000_000,
            [RaidsPlayerTitle.ClankApologist] = 10_000_000,
            [RaidsPlayerTitle.QwarksDefenseAttorney] = 25_000_000,
            [RaidsPlayerTitle.AiSlop] = 2_000_000,
            [RaidsPlayerTitle.DefinitelyHuman] = 5_000_000,
            [RaidsPlayerTitle.DuctTapeEngineer] = 10_000_000,
            [RaidsPlayerTitle.CertifiedFireHazard] = 7_500_000,
            [RaidsPlayerTitle.WalkingRecall] = 1_000_000,
            [RaidsPlayerTitle.Uninsurable] = 25_000_000,
            [RaidsPlayerTitle.WarrantyCriminal] = 75_000_000,
            [RaidsPlayerTitle.FailedSuccessfully] = 15_000_000,
            [RaidsPlayerTitle.TaxEvader] = 75_000_000,
            [RaidsPlayerTitle.SleepDeprived] = 20_000_000,
            [RaidsPlayerTitle.PartTimeHuman] = 25_000_000,
            [RaidsPlayerTitle.CommunityProblem] = 20_000_000,
            [RaidsPlayerTitle.DiscountLombax] = 1_000_000,
            [RaidsPlayerTitle.DisposableTalent] = 2_500_000,
            [RaidsPlayerTitle.SponsorsRegret] = 7_500_000,
            [RaidsPlayerTitle.RatingsLiability] = 10_000_000,
            [RaidsPlayerTitle.BarelySentient] = 500_000,
            [RaidsPlayerTitle.FactoryDefect] = 500_000,
            [RaidsPlayerTitle.PreviouslyBanned] = 30_000_000,
            [RaidsPlayerTitle.ThreeBoltsInATrenchcoat] = 3_000_000,
            [RaidsPlayerTitle.CatacromCompletionist] = 10_000_000,
            [RaidsPlayerTitle.Rngesus] = 50_000_000,
            [RaidsPlayerTitle.NvidiaShareholder] = 1_000_000_000,
            [RaidsPlayerTitle.CircularFinancer] = 250_000_000,
        };

        // Combine up to MAX_REQUIREMENTS rules. An empty list allows purchase without prerequisites.
        // Missing catalog entries cannot be purchased.
        public static readonly IReadOnlyDictionary<RaidsPlayerTitle, RaidsTitleRequirement[]> TitleRequirements = new Dictionary<RaidsPlayerTitle, RaidsTitleRequirement[]>
        {
            [RaidsPlayerTitle.None] = [],
            [RaidsPlayerTitle.BadWithMoney] = [],
            [RaidsPlayerTitle.WillKillForBolts] = [],
            [RaidsPlayerTitle.SunkCostEnthusiast] = [new RaidsMaterialsConsumedRequirement(100)],
            [RaidsPlayerTitle.CorporateLiability] = [],
            [RaidsPlayerTitle.LootGoblin] = [
                new RaidsLootDropsRequirement(50, rarity: RaidsItemRarity.Common),
                new RaidsLootDropsRequirement(20, rarity: RaidsItemRarity.Uncommon),
                new RaidsLootDropsRequirement(5, rarity: RaidsItemRarity.Rare),
                new RaidsLootDropsRequirement(1, rarity: RaidsItemRarity.Legendary),
            ],
            [RaidsPlayerTitle.LegallyAWeapon] = [new RaidsSuccessfulUpgradesRequirement(100)],
            [RaidsPlayerTitle.MegacorpMiddleManager] = [new RaidsChallengesRequirement(15)],
            [RaidsPlayerTitle.WalkingTaxWriteOff] = [],
            [RaidsPlayerTitle.TrustFundBaby] = [],
            [RaidsPlayerTitle.MorallyBankrupt] = [],
            [RaidsPlayerTitle.FinanciallyUnsupervised] = [],
            [RaidsPlayerTitle.SecretAgentClank] = [],
            [RaidsPlayerTitle.OohShiny] = [new RaidsGoldBoltsRequirement(15)],
            [RaidsPlayerTitle.TooMuchFreeTime] = [new RaidsChallengesRequirement(15)],
            [RaidsPlayerTitle.KingLeviathanCompletionist] = [new RaidsMapCompletionRequirement("raids_sarathos")],
            [RaidsPlayerTitle.KingLeviathanExterminator] = [new RaidsRaidCompletionsRequirement("raids_sarathos", stars: 5, count: 25)],
            [RaidsPlayerTitle.WarrantyVoider] = [new RaidsSuccessfulUpgradesRequirement(25)],
            [RaidsPlayerTitle.UnlicensedEngineer] = [new RaidsSuccessfulUpgradesRequirement(250)],
            [RaidsPlayerTitle.SkillIssue] = [new RaidsFailedUpgradesRequirement(100)],
            [RaidsPlayerTitle.MaterialGirl] = [new RaidsMaterialsConsumedRequirement(1000)],
            [RaidsPlayerTitle.CompulsiveHoarder] = [
                new RaidsLootDropsRequirement(500, rarity: RaidsItemRarity.Common),
                new RaidsLootDropsRequirement(200, rarity: RaidsItemRarity.Uncommon),
                new RaidsLootDropsRequirement(50, rarity: RaidsItemRarity.Rare),
                new RaidsLootDropsRequirement(10, rarity: RaidsItemRarity.Legendary),
            ],
            [RaidsPlayerTitle.RngsFavoriteChild] = [new RaidsLootDropsRequirement(25, rarity: RaidsItemRarity.Legendary)],
            [RaidsPlayerTitle.TheChosenOne] = [new RaidsLootDropsRequirement(1, rarity: RaidsItemRarity.Mythic)],
            [RaidsPlayerTitle.BuiltDifferent] = [
                new RaidsSuccessfulUpgradesRequirement(250),
                new RaidsMaterialsConsumedRequirement(1000),
                new RaidsLootDropsRequirement(25, rarity: RaidsItemRarity.Mythic),
            ],
            [RaidsPlayerTitle.NoLifer] = [
                new RaidsSuccessfulUpgradesRequirement(1000),
                new RaidsMaterialsConsumedRequirement(5000),
                new RaidsLootDropsRequirement(10000),
                new RaidsRaidCompletionsRequirement("raids_sarathos", stars: 5, count: 100),
            ],
            [RaidsPlayerTitle.ChronicallyOnline] = [
                new RaidsLootDropsRequirement(500, rarity: RaidsItemRarity.Rare),
                new RaidsLootDropsRequirement(50, rarity: RaidsItemRarity.Legendary),
            ],
            [RaidsPlayerTitle.GrassAvoider] = [new RaidsRaidCompletionsRequirement("raids_sarathos", stars: 5, count: 100)],
            [RaidsPlayerTitle.CertifiedButtonMasher] = [new RaidsMaterialsConsumedRequirement(250)],
            [RaidsPlayerTitle.ProfessionallyUnqualified] = [new RaidsFailedUpgradesRequirement(25)],
            [RaidsPlayerTitle.ProbablyABot] = [
                new RaidsLootDropsRequirement(250, rarity: RaidsItemRarity.Uncommon),
                new RaidsLootDropsRequirement(100, rarity: RaidsItemRarity.Rare),
            ],
            [RaidsPlayerTitle.PublicNuisance] = [],
            [RaidsPlayerTitle.InsuranceNightmare] = [new RaidsFailedUpgradesRequirement(50)],
            [RaidsPlayerTitle.UnsupervisedAdult] = [],
            [RaidsPlayerTitle.CourtneyGearsNumberOneFan] = [],
            [RaidsPlayerTitle.ClankApologist] = [],
            [RaidsPlayerTitle.QwarksDefenseAttorney] = [],
            [RaidsPlayerTitle.AiSlop] = [],
            [RaidsPlayerTitle.DefinitelyHuman] = [],
            [RaidsPlayerTitle.DuctTapeEngineer] = [
                new RaidsSuccessfulUpgradesRequirement(100),
                new RaidsMaterialsConsumedRequirement(250),
            ],
            [RaidsPlayerTitle.CertifiedFireHazard] = [],
            [RaidsPlayerTitle.WalkingRecall] = [new RaidsFailedUpgradesRequirement(10)],
            [RaidsPlayerTitle.Uninsurable] = [new RaidsFailedUpgradesRequirement(250)],
            [RaidsPlayerTitle.WarrantyCriminal] = [
                new RaidsSuccessfulUpgradesRequirement(1000),
                new RaidsMaterialsConsumedRequirement(2500),
            ],
            [RaidsPlayerTitle.FailedSuccessfully] = [
                new RaidsSuccessfulUpgradesRequirement(100),
                new RaidsFailedUpgradesRequirement(100),
            ],
            [RaidsPlayerTitle.TaxEvader] = [],
            [RaidsPlayerTitle.SleepDeprived] = [
                new RaidsRaidCompletionsRequirement("raids_sarathos", stars: 5, count: 25),
                new RaidsLootDropsRequirement(2500),
            ],
            [RaidsPlayerTitle.PartTimeHuman] = [new RaidsLootDropsRequirement(5, rarity: RaidsItemRarity.Mythic)],
            [RaidsPlayerTitle.CommunityProblem] = [],
            [RaidsPlayerTitle.DiscountLombax] = [],
            [RaidsPlayerTitle.DisposableTalent] = [],
            [RaidsPlayerTitle.SponsorsRegret] = [
                new RaidsFailedUpgradesRequirement(50),
                new RaidsMaterialsConsumedRequirement(100),
            ],
            [RaidsPlayerTitle.RatingsLiability] = [],
            [RaidsPlayerTitle.BarelySentient] = [],
            [RaidsPlayerTitle.FactoryDefect] = [new RaidsFailedUpgradesRequirement(5)],
            [RaidsPlayerTitle.PreviouslyBanned] = [],
            [RaidsPlayerTitle.ThreeBoltsInATrenchcoat] = [],
            [RaidsPlayerTitle.CatacromCompletionist] = [new RaidsMapCompletionRequirement("raids_catacrom")],
            [RaidsPlayerTitle.Rngesus] = [new RaidsLootDropsRequirement(10, rarity: RaidsItemRarity.Mythic)],
            [RaidsPlayerTitle.NvidiaShareholder] = [],
            [RaidsPlayerTitle.CircularFinancer] = [],
        };

        static RaidsTitles()
        {
            if ((uint)RaidsPlayerTitle.Count > TITLE_CAPACITY)
                throw new InvalidOperationException("Title IDs exceed the 128-bit ownership mask.");
            foreach (var (title, requirements) in TitleRequirements)
                if (requirements.Length > MAX_REQUIREMENTS)
                    throw new InvalidOperationException($"Title {title} exceeds the {MAX_REQUIREMENTS}-requirement limit.");
        }

        public static (uint GoldBolts, uint Challenges) GetProgress(RaidsBank bank)
        {
            ulong goldBolts = 0, challenges = 0;
            foreach (var stats in bank.MapStats.Values)
            {
                if (stats == null) continue;
                goldBolts += RaidsMapStats.CountCompleted(stats.CollectiblesMask, stats.CollectiblesCount);
                challenges += RaidsMapStats.CountCompleted(stats.ChallengesMask, stats.ChallengesCount);
            }
            return ((uint)Math.Min(uint.MaxValue, goldBolts), (uint)Math.Min(uint.MaxValue, challenges));
        }

        public static bool MeetsTitleRequirements(RaidsBank bank, uint title)
        {
            if (title >= (uint)RaidsPlayerTitle.Count
                || !TitleRequirements.TryGetValue((RaidsPlayerTitle)title, out var required)) return false;
            return required.Length <= MAX_REQUIREMENTS && required.All(rule => rule.IsMet(bank));
        }

    }
}
