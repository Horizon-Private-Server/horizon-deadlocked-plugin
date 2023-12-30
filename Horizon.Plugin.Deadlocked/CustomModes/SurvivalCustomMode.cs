using Horizon.Plugin.Deadlocked.Messages;
using Server.Common.Stream;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Horizon.Plugin.Deadlocked.CustomModes.SurvivalCustomMode;

namespace Horizon.Plugin.Deadlocked.CustomModes
{
    public class SurvivalCustomMode : BaseCustomMode
    {
        public enum SurvivalMobStatIds : int
        {
            None = 0,
            Zombie = 1,
            ZombieFreeze = 2,
            ZombieAcid = 3,
            ZombieGhost = 4,
            ZombieExplode = 5,
            Tremor = 6,
            Executioner = 7,
            Swarmer = 8,
            Reactor = 9,
            Reaper = 10
        }

        public static readonly int SurvivalMaxPrestige = 5;
        private static readonly Dictionary<int, string> _survivalPrestigeToNamePrefix = new Dictionary<int, string>()
        {
            { 0, "" },
            { 1, "\x09" },
            { 2, "\x0A" },
            { 3, "\x0B" },
            { 4, "\x0E" },
            { 5, "\x0D" },
        };

        private static readonly Dictionary<CustomMapId, CustomPlayerStatIds> _survivalMapToXpStatIndex = new Dictionary<CustomMapId, CustomPlayerStatIds>()
        {
            { CustomMapId.CMAP_ID_SURVIVAL_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_XP },
            { CustomMapId.CMAP_ID_SURVIVAL_MOUNTAIN_PASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_XP },
        };

        private static readonly Dictionary<CustomMapId, CustomPlayerStatIds> _survivalMapToPrestigeStatIndex = new Dictionary<CustomMapId, CustomPlayerStatIds>()
        {
            { CustomMapId.CMAP_ID_SURVIVAL_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_PRESTIGE },
            { CustomMapId.CMAP_ID_SURVIVAL_MOUNTAIN_PASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_PRESTIGE },
        };

        private static readonly Dictionary<CustomMapId, CustomPlayerStatIds> _survivalMapToSoloHighScoreStatIndex = new Dictionary<CustomMapId, CustomPlayerStatIds>()
        {
            { CustomMapId.CMAP_ID_SURVIVAL_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_SOLO_HIGH_SCORE },
            { CustomMapId.CMAP_ID_SURVIVAL_MOUNTAIN_PASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_SOLO_HIGH_SCORE },
        };

        private static readonly Dictionary<CustomMapId, CustomPlayerStatIds> _survivalMapToCoopHighScoreStatIndex = new Dictionary<CustomMapId, CustomPlayerStatIds>()
        {
            { CustomMapId.CMAP_ID_SURVIVAL_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_COOP_HIGH_SCORE },
            { CustomMapId.CMAP_ID_SURVIVAL_MOUNTAIN_PASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_COOP_HIGH_SCORE },
        };

        private static readonly SurvivalConfig[] _configs = new SurvivalConfig[]
        {
            new SurvivalConfig(CustomMapId.CMAP_ID_SURVIVAL_ORXON)
            {
                Difficulty = 1.0f,
                BakedSpawnpoints = new List<SurvivalConfig.BakedSpawnpoint>()
                {
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.PlayerStart, 0, 328.6f, 544.8498f, 433.9998f, 0f, 0f, 0f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 525.73f, 537.75f, 429.64f, 0f, 0f, -1.570796f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 478.72f, 691.459f, 430.73f, 0f, 0f, -3.141592f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 478.9f, 508.54f, 430.73f, 0f, 0f, 0f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 339.205f, 562.895f, 431.67f, -0.0006243868f, 1.079918E-05f, -1.660341f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 525.73f, 661.89f, 429.64f, 0f, 0f, -1.570797f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 335.47f, 650.277f, 430.623f, -6.167562f, -7.500661E-09f, -1.570796f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 516.416f, 600.07f, 437.062f, 0f, 0f, -4.712389f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 384.73f, 536.032f, 437.591f, -6.135677f, 0f, 0f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 426.446f, 670.574f, 438.23f, -0.1282649f, 9.390364E-10f, -2.937677f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 545.2199f, 513.7399f, 427.4603f, 0f, 0f, -3.951303f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 584.0499f, 537.26f, 427.5132f, 0f, 0f, -3.951303f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 569.03f, 578.3799f, 427.3436f, 0f, 0f, -2.904106f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 526.77f, 629.11f, 427.3436f, 0f, 0f, 0f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 531.7f, 599.54f, 427.3436f, 0f, 0f, -0.0726434f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 569.3099f, 617.9299f, 427.3436f, 0f, 0f, -4.359524f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 634.1599f, 660.43f, 427.3904f, 0f, 0f, -2.795064f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 633.17f, 592.33f, 427.3436f, 0f, 0f, -4.633354f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 579.17f, 598f, 427.3436f, 0f, 0f, -0.5679857f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 633.56f, 579.13f, 427.3435f, 0f, 0f, -2.09181f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 622.39f, 615.9099f, 427.4686f, 0f, 0f, -3.729555f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.DemonBell, 0, 324f, 566.83f, 440.94f, 0f, 0f, -1.570796f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.DemonBell, 0, 327f, 566.83f, 440.94f, 0f, 0f, -1.570796f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.DemonBell, 0, 330f, 566.83f, 440.94f, 0f, 0f, -1.570796f),
                }
            },
            new SurvivalConfig(CustomMapId.CMAP_ID_SURVIVAL_MOUNTAIN_PASS)
            {
                Difficulty = 1.0f,
                BakedSpawnpoints = new List<SurvivalConfig.BakedSpawnpoint>()
                {
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.PlayerStart, 0, 658.3901f, 828.0401f, 499.7961f, 0f, 0f, -3.141593f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 594.99f, 704.85f, 507.63f, 0f, 0f, 1.788139E-07f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 503.96f, 704.98f, 506.32f, 0f, 0f, -7.450579E-07f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 624.6f, 801.53f, 504.07f, 0f, 0f, -2.283245f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 688.592f, 858.57f, 514.264f, 0f, 0f, -4.712388f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 504.301f, 945.488f, 507.915f, -5.984646f, -7.795392E-09f, -2.412499f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 456.14f, 791.772f, 509.28f, 0f, 0f, -3.979972f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 587.583f, 819.116f, 502.458f, -0.2670273f, -2.3173E-08f, -5.328631f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 504.936f, 821.258f, 501.923f, -6.177232f, 0f, -2.660405f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 562.021f, 892.229f, 508.457f, -6.218049f, 8.900659E-16f, -3.141593f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 448.302f, 876.378f, 507.799f, 0f, 0f, -5.496722f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 380.211f, 826.473f, 514.736f, -0.3037879f, -0.07311971f, -2.909856f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 585.6502f, 905.4998f, 506.599f, 0f, 0f, 1.260871f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 683.7198f, 807.9098f, 501.27f, 0f, 0f, -3.427705f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 634f, 742.02f, 507.35f, 0f, 0f, -2.904106f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 513.0701f, 738.4099f, 504.54f, 0f, 0f, -0.7853986f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 397.75f, 793.05f, 520.38f, 0f, 0f, -0.27788f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 469.15f, 802.01f, 504.22f, 0f, 0f, 1.400064f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 541.81f, 778.23f, 505.95f, 0f, 0f, -1.747867f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 547.39f, 858.92f, 505.86f, 0f, 0f, -4.371555f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 575.52f, 809.11f, 500.15f, 0f, 0f, -4.23276f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 512.43f, 951.69f, 507.16f, 0f, 0f, -1.327776f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 370.8f, 813.12f, 514.0535f, 0f, 0f, -0.06436086f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.DemonBell, 0, 660.2f, 793.97f, 507.61f, 0f, 0f, -1.570796f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.DemonBell, 0, 663.2f, 793.97f, 507.61f, 0f, 0f, -1.570796f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.DemonBell, 0, 666.2f, 793.97f, 507.61f, 0f, 0f, -1.570796f),
                }
            },
        };

        public override CustomModeId Id => CustomModeId.CMODE_ID_SURVIVAL;
        public override string Name => "Survival";

        private int GetRatingFromXp(long xp)
        {
            //return (int)Math.Max(100, Math.Min(10000, 100 + Math.Sqrt(xp * 4)));
            return (int)Math.Max(100, Math.Min(10000, 100 + (xp / 2500f)));
        }

        public override async Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || metadata.GameConfig.MapOverride == 0)
                return 0;

            // try to get stat id for map
            if (!_survivalMapToXpStatIndex.TryGetValue((CustomMapId)metadata.GameConfig.MapOverride, out var stat))
                return 0;

            var rank = GetRatingFromXp(client.CustomWideStats[(int)stat]);
            var prestige = await GetPrestige(game, metadata, client) ?? 0;
            if (prestige >= SurvivalMaxPrestige && rank >= 10000)
                rank = 9999; // cap

            return rank;
        }

        public Task<int?> GetPrestige(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || metadata.GameConfig.MapOverride == 0)
                return Task.FromResult((int?)0);

            if (_survivalMapToPrestigeStatIndex.TryGetValue((CustomMapId)metadata.GameConfig.MapOverride, out var stat))
                return Task.FromResult((int?)client.CustomWideStats[(int)stat]);

            return Task.FromResult((int?)0);
        }

        public async Task<bool> Prestige(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || metadata.GameConfig.MapOverride == 0)
                return false;

            if (!_survivalMapToXpStatIndex.TryGetValue((CustomMapId)metadata.GameConfig.MapOverride, out var xpStat))
                return false;
            if (!_survivalMapToPrestigeStatIndex.TryGetValue((CustomMapId)metadata.GameConfig.MapOverride, out var prestigeStat))
                return false;

            var rank = GetRatingFromXp(client.CustomWideStats[(int)xpStat]);
            var prestige = client.CustomWideStats[(int)prestigeStat];
            if (rank < 10000) return false;
            if (prestige >= SurvivalMaxPrestige) return false;

            // increment prestige
            prestige += 1;
            client.CustomWideStats[(int)prestigeStat] = prestige;
            client.CustomWideStats[(int)xpStat] = 0;

            // recompute overall rank
            int overallRank = 0;
            foreach (var kvp in _survivalMapToXpStatIndex)
            {
                var mapXp = client.CustomWideStats[(int)kvp.Value];
                var mapPrestige = client.CustomWideStats[(int)_survivalMapToPrestigeStatIndex[kvp.Key]];
                overallRank += GetRatingFromXp(mapXp) + (10000 * mapPrestige);
            }
            client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_OVERALL_RANK] = overallRank / _survivalMapToXpStatIndex.Count;

            // send to db
            return await Server.Medius.Program.Database.PostAccountLadderCustomStats(new Server.Database.Models.StatPostDTO()
            {
                AccountId = client.AccountId,
                Stats = client.CustomWideStats
            });
        }

        public override async Task<string> GetNameOverride(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || metadata.GameConfig.MapOverride == 0)
                return await base.GetNameOverride(game, metadata, client);

            var prestige = await GetPrestige(game, metadata, client) ?? 0;
            var prefix = _survivalPrestigeToNamePrefix.GetValueOrDefault(prestige) ?? "";
            return prefix + client.AccountName;
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            args.Reject = true; // reject all
            return Task.CompletedTask;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var round = metadata.GameState.RoundNumber;

            string info = "";
            if (round > 0)
                info += $"\nRound: {round}";

            return Task.FromResult(info);
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var payload = new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/survival-11184.bin")));

            // find config by custom map then by regular map
            var config = _configs.FirstOrDefault(x => x.CustomMapId == (CustomMapId)metadata.GameConfig.MapOverride);
            if (config == null)
            {
                // default to first
                config = _configs[0];
            }

            // insert config into payload
            if (config != null)
            {
                using (var ms = new MemoryStream(payload.Data, true))
                {
                    using (var writer = new BinaryWriter(ms))
                    {
                        config.Serialize(writer);
                    }
                }
            }

            return Task.FromResult(payload);
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new SurvivalCustomData();
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            if (game == null || metadata == null || gameData == null)
                return false;

            // game must have custom data
            if (gameData.CustomGameData == null || gameData.GameOptions == null)
                return false;

            return true;
        }

        protected override async Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as SurvivalCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            if (customGameData.Points == null) return;

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                var points = customGameData.Points[gameIdx];

                int? xpStatIndex = null;
                if (_survivalMapToXpStatIndex.TryGetValue((CustomMapId)args.Metadata.GameConfig.MapOverride, out var xpStat))
                    xpStatIndex = (int?)xpStat;

                if (xpStatIndex.HasValue)
                {
                    var xp = (int)Math.Max(0, Math.Min(int.MaxValue, (ulong)args.PlayerCustomStats[accountId][xpStatIndex.Value] + points));

                    // xp
                    args.PlayerCustomStats[accountId][xpStatIndex.Value] = xp;

                    int overallRank = 0;
                    foreach (var kvp in _survivalMapToXpStatIndex)
                    {
                        var mapXp = args.PlayerCustomStats[accountId][(int)kvp.Value];
                        var mapPrestige = args.PlayerCustomStats[accountId][(int)_survivalMapToPrestigeStatIndex[kvp.Key]];
                        overallRank += GetRatingFromXp(mapXp) + (10000 * mapPrestige);
                    }

                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_OVERALL_RANK] = overallRank / _survivalMapToXpStatIndex.Count;
                }

                if (!player.Left)
                {
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                }

                // general
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_KILLS] += customGameData.Kills[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DEATHS] += gameData.Data.Deaths[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_REVIVES] += customGameData.Revives[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_REVIVED] += customGameData.TimesRevived[gameIdx];

                // general mechanics
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ROLLED_MYSTERY_BOX] += customGameData.TimesRolledMysteryBox[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_DEMON_BELL] += customGameData.TimesActivatedDemonBell[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_POWER] += customGameData.TimesActivatedPower[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TOKENS_USED_ON_GATES] += customGameData.TokensUsedOnGates[gameIdx];

                // weapon stats
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_WRENCH_KILLS] += gameData.Data.WeaponKills[gameIdx][0];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DUAL_VIPER_KILLS] += gameData.Data.WeaponKills[gameIdx][1];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAGMA_CANNON_KILLS] += gameData.Data.WeaponKills[gameIdx][2];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_ARBITER_KILLS] += gameData.Data.WeaponKills[gameIdx][3];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_FUSION_RIFLE_KILLS] += gameData.Data.WeaponKills[gameIdx][4];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MINE_LAUNCHER_KILLS] += gameData.Data.WeaponKills[gameIdx][5];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_B6_OBLITERATOR_KILLS] += gameData.Data.WeaponKills[gameIdx][6];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_SCORPION_FLAIL_KILLS] += gameData.Data.WeaponKills[gameIdx][7];

                // high scores
                int? statIndex = null;
                var coop = game.AccountIdsAtStart.Contains(',');
                if (coop)
                {
                    if (_survivalMapToCoopHighScoreStatIndex.TryGetValue((CustomMapId)args.Metadata.GameConfig.MapOverride, out var customStatId))
                        statIndex = (int)customStatId;
                }
                else
                {
                    if (_survivalMapToSoloHighScoreStatIndex.TryGetValue((CustomMapId)args.Metadata.GameConfig.MapOverride, out var customStatId))
                        statIndex = (int)customStatId;
                }

                if (statIndex.HasValue)
                {
                    args.PlayerCustomStats[accountId][statIndex.Value] = Math.Max(args.PlayerCustomStats[accountId][statIndex.Value], customGameData.BestRound[gameIdx]);
                }
            }
        }
    }

    public class SurvivalCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int Rounds { get; set; }
        public ulong[] Points { get; set; }
        public int[] Kills { get; set; }
        public int[] Revives { get; set; }
        public int[] TimesRevived { get; set; }
        public byte[][] AlphaModsReceived { get; set; }
        public byte[][] BestWeaponLevels { get; set; }
        public short[] BestRound { get; set; }
        public int[][] KillsPerMob { get; set; }
        public short[][] DeathsByMob { get; set; }
        public short[][] PlayerUpgrades { get; set; }
        public short[] TimesRolledMysteryBox { get; set; }
        public short[] TimesActivatedDemonBell { get; set; }
        public short[] TimesActivatedPower { get; set; }
        public short[] TokensUsedOnGates { get; set; }
        public SurvivalMobStatIds[] MobIds { get; set; }

        public void Deserialize(MessageReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();

            switch (Version)
            {
                case 1:
                    {
                        Points = new ulong[10];
                        AlphaModsReceived = new byte[10][];
                        BestWeaponLevels = new byte[10][];
                        BestRound = new short[10];
                        KillsPerMob = new int[10][];
                        DeathsByMob = new short[10][];
                        PlayerUpgrades = new short[10][];
                        TimesRolledMysteryBox = new short[10];
                        TimesActivatedDemonBell = new short[10];
                        TimesActivatedPower = new short[10];
                        TokensUsedOnGates = new short[10];
                        MobIds = new SurvivalMobStatIds[0];

                        Rounds = reader.ReadInt32();
                        Kills = reader.ReadArray<int>(10);
                        Revives = reader.ReadArray<int>(10);
                        TimesRevived = reader.ReadArray<int>(10);

                        for (int i = 0; i < 10; ++i)
                            AlphaModsReceived[i] = reader.ReadArray<byte>(8);

                        for (int i = 0; i < 10; ++i)
                            BestWeaponLevels[i] = reader.ReadArray<byte>(8);
                        break;
                    }
                case 2:
                    {
                        AlphaModsReceived = new byte[10][];
                        BestWeaponLevels = new byte[10][];
                        BestRound = new short[10];
                        KillsPerMob = new int[10][];
                        DeathsByMob = new short[10][];
                        PlayerUpgrades = new short[10][];
                        TimesRolledMysteryBox = new short[10];
                        TimesActivatedDemonBell = new short[10];
                        TimesActivatedPower = new short[10];
                        TokensUsedOnGates = new short[10];
                        MobIds = new SurvivalMobStatIds[0];

                        Rounds = reader.ReadInt32();
                        Points = reader.ReadArray<ulong>(10);
                        Kills = reader.ReadArray<int>(10);
                        Revives = reader.ReadArray<int>(10);
                        TimesRevived = reader.ReadArray<int>(10);

                        for (int i = 0; i < 10; ++i)
                            AlphaModsReceived[i] = reader.ReadArray<byte>(8);

                        for (int i = 0; i < 10; ++i)
                            BestWeaponLevels[i] = reader.ReadArray<byte>(8);
                        break;
                    }
                case 3:
                    {
                        const int MAX_MOB_SPAWN_PARAMS = 10;
                        const int PLAYER_UPGRADE_COUNT = 5;

                        AlphaModsReceived = new byte[10][];
                        BestWeaponLevels = new byte[10][];
                        KillsPerMob = new int[10][];
                        DeathsByMob = new short[10][];
                        PlayerUpgrades = new short[10][];

                        Rounds = reader.ReadInt32();
                        Points = reader.ReadArray<ulong>(10);
                        Kills = reader.ReadArray<int>(10);
                        Revives = reader.ReadArray<int>(10);
                        TimesRevived = reader.ReadArray<int>(10);

                        for (int i = 0; i < 10; ++i)
                            KillsPerMob[i] = reader.ReadArray<int>(MAX_MOB_SPAWN_PARAMS);

                        for (int i = 0; i < 10; ++i)
                            DeathsByMob[i] = reader.ReadArray<short>(MAX_MOB_SPAWN_PARAMS);

                        var mobIds = reader.ReadArray<short>(10);
                        MobIds = mobIds.Select(x => (SurvivalMobStatIds)x).ToArray();
                        BestRound = reader.ReadArray<short>(10);

                        for (int i = 0; i < 10; ++i)
                            PlayerUpgrades[i] = reader.ReadArray<short>(PLAYER_UPGRADE_COUNT);

                        TimesRolledMysteryBox = reader.ReadArray<short>(10);
                        TimesActivatedDemonBell = reader.ReadArray<short>(10);
                        TimesActivatedPower = reader.ReadArray<short>(10);
                        TokensUsedOnGates = reader.ReadArray<short>(10);

                        for (int i = 0; i < 10; ++i)
                            AlphaModsReceived[i] = reader.ReadArray<byte>(8);

                        for (int i = 0; i < 10; ++i)
                            BestWeaponLevels[i] = reader.ReadArray<byte>(8);
                        break;
                    }
                case 4:
                    {
                        const int MAX_MOB_SPAWN_PARAMS = 10;
                        const int PLAYER_UPGRADE_COUNT = 6;

                        AlphaModsReceived = new byte[10][];
                        BestWeaponLevels = new byte[10][];
                        KillsPerMob = new int[10][];
                        DeathsByMob = new short[10][];
                        PlayerUpgrades = new short[10][];

                        Rounds = reader.ReadInt32();
                        Points = reader.ReadArray<ulong>(10);
                        Kills = reader.ReadArray<int>(10);
                        Revives = reader.ReadArray<int>(10);
                        TimesRevived = reader.ReadArray<int>(10);

                        for (int i = 0; i < 10; ++i)
                            KillsPerMob[i] = reader.ReadArray<int>(MAX_MOB_SPAWN_PARAMS);

                        for (int i = 0; i < 10; ++i)
                            DeathsByMob[i] = reader.ReadArray<short>(MAX_MOB_SPAWN_PARAMS);

                        var mobIds = reader.ReadArray<short>(10);
                        MobIds = mobIds.Select(x => (SurvivalMobStatIds)x).ToArray();
                        BestRound = reader.ReadArray<short>(10);

                        for (int i = 0; i < 10; ++i)
                            PlayerUpgrades[i] = reader.ReadArray<short>(PLAYER_UPGRADE_COUNT);

                        TimesRolledMysteryBox = reader.ReadArray<short>(10);
                        TimesActivatedDemonBell = reader.ReadArray<short>(10);
                        TimesActivatedPower = reader.ReadArray<short>(10);
                        TokensUsedOnGates = reader.ReadArray<short>(10);

                        for (int i = 0; i < 10; ++i)
                            AlphaModsReceived[i] = reader.ReadArray<byte>(8);

                        for (int i = 0; i < 10; ++i)
                            BestWeaponLevels[i] = reader.ReadArray<byte>(8);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported survival data version {Version}");
                        break;
                    }
            }
        }
    }

    public class SurvivalConfig
    {
        public const uint Offset = 0x48;

        public class BakedSpawnpoint
        {
            public enum TypeId
            {
                None = 0,
                Upgrade = 1,
                PlayerStart = 2,
                MysteryBox = 3,
                DemonBell = 4,
            };

            public TypeId Type { get; set; }
            public int Params { get; set; }
            public float PositionX { get; set; }
            public float PositionY { get; set; }
            public float PositionZ { get; set; }
            public float RotationX { get; set; }
            public float RotationY { get; set; }
            public float RotationZ { get; set; }

            public BakedSpawnpoint() { }

            public BakedSpawnpoint(TypeId type, int @params, float positionX, float positionY, float positionZ, float rotationX, float rotationY, float rotationZ)
            {
                Type = type;
                Params = @params;
                PositionX = positionX;
                PositionY = positionY;
                PositionZ = positionZ;
                RotationX = rotationX;
                RotationY = rotationY;
                RotationZ = rotationZ;
            }
        }

        public CustomMapId CustomMapId { get; }
        public float Difficulty { get; set; }
        public List<BakedSpawnpoint> BakedSpawnpoints { get; set; }

        public SurvivalConfig(CustomMapId mapId)
        {
            CustomMapId = mapId;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.BaseStream.Seek(Offset, SeekOrigin.Begin);

            writer.Write(Difficulty);

            // write 32 baked spawnpoints
            for (int i = 0; i < 32; ++i)
            {
                var bakedSp = BakedSpawnpoints?.ElementAtOrDefault(i);
                if (bakedSp != null)
                {
                    writer.Write((int)bakedSp.Type);
                    writer.Write((int)bakedSp.Params);
                    writer.Write(bakedSp.PositionX);
                    writer.Write(bakedSp.PositionY);
                    writer.Write(bakedSp.PositionZ);
                    writer.Write(bakedSp.RotationX);
                    writer.Write(bakedSp.RotationY);
                    writer.Write(bakedSp.RotationZ);
                }
                else
                {
                    writer.Write((int)BakedSpawnpoint.TypeId.None);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                }
            }
        }
    }
}
