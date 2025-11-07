using Horizon.Plugin.Deadlocked.Messages;
using Newtonsoft.Json;
using Server.Common.Stream;
using Server.Medius;
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
    public class SurvivalCustomMode : BaseCustomMode, IDisposable
    {
        private static readonly string SURVIVAL_CMAP_FILENAME_ORXON = "survival v2 mf";
        private static readonly string SURVIVAL_CMAP_FILENAME_MPASS = "survival mpass";
        private static readonly string SURVIVAL_CMAP_FILENAME_VELDIN = "survival veldin";
        private static readonly string SURVIVAL_CMAP_FILENAME_VALIX = "survival_valix";
        private static readonly string SURVIVAL_CMAP_FILENAME_TORVAL = "survival_torval";

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
            { 4, "\x0F" },
            { 5, "\x0E" },
        };

        private static readonly Dictionary<string, CustomPlayerStatIds> _survivalMapToXpStatIndex = new Dictionary<string, CustomPlayerStatIds>()
        {
            { SURVIVAL_CMAP_FILENAME_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_XP },
            { SURVIVAL_CMAP_FILENAME_MPASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_XP },
            { SURVIVAL_CMAP_FILENAME_VELDIN, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP3_XP },
            { SURVIVAL_CMAP_FILENAME_VALIX, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP4_XP },
            { SURVIVAL_CMAP_FILENAME_TORVAL, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP5_XP },
        };

        private static readonly Dictionary<string, CustomPlayerStatIds> _survivalMapToPrestigeStatIndex = new Dictionary<string, CustomPlayerStatIds>()
        {
            { SURVIVAL_CMAP_FILENAME_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_PRESTIGE },
            { SURVIVAL_CMAP_FILENAME_MPASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_PRESTIGE },
            { SURVIVAL_CMAP_FILENAME_VELDIN, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP3_PRESTIGE },
            { SURVIVAL_CMAP_FILENAME_VALIX, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP4_PRESTIGE },
            { SURVIVAL_CMAP_FILENAME_TORVAL, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP5_PRESTIGE },
        };

        private static readonly Dictionary<string, CustomPlayerStatIds> _survivalMapToSoloHighScoreStatIndex = new Dictionary<string, CustomPlayerStatIds>()
        {
            { SURVIVAL_CMAP_FILENAME_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_SOLO_HIGH_SCORE },
            { SURVIVAL_CMAP_FILENAME_MPASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_SOLO_HIGH_SCORE },
            { SURVIVAL_CMAP_FILENAME_VELDIN, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP3_SOLO_HIGH_SCORE },
            { SURVIVAL_CMAP_FILENAME_VALIX, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP4_SOLO_HIGH_SCORE },
            { SURVIVAL_CMAP_FILENAME_TORVAL, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP5_SOLO_HIGH_SCORE },
        };

        private static readonly Dictionary<string, CustomPlayerStatIds> _survivalMapToCoopHighScoreStatIndex = new Dictionary<string, CustomPlayerStatIds>()
        {
            { SURVIVAL_CMAP_FILENAME_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_COOP_HIGH_SCORE },
            { SURVIVAL_CMAP_FILENAME_MPASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_COOP_HIGH_SCORE },
            { SURVIVAL_CMAP_FILENAME_VELDIN, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP3_COOP_HIGH_SCORE },
            { SURVIVAL_CMAP_FILENAME_VALIX, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP4_COOP_HIGH_SCORE },
            { SURVIVAL_CMAP_FILENAME_TORVAL, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP5_COOP_HIGH_SCORE },
        };

        private static readonly Dictionary<string, CustomPlayerStatIds> _survivalMapToSolo50BestTimeStatIndex = new Dictionary<string, CustomPlayerStatIds>()
        {
            { SURVIVAL_CMAP_FILENAME_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_SOLO_50_BEST_TIME },
            { SURVIVAL_CMAP_FILENAME_MPASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_SOLO_50_BEST_TIME },
            { SURVIVAL_CMAP_FILENAME_VELDIN, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP3_SOLO_50_BEST_TIME },
            { SURVIVAL_CMAP_FILENAME_VALIX, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP4_SOLO_50_BEST_TIME },
            { SURVIVAL_CMAP_FILENAME_TORVAL, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP5_SOLO_50_BEST_TIME },
        };

        private static readonly Dictionary<string, CustomPlayerStatIds> _survivalMapToCoop50BestTimeStatIndex = new Dictionary<string, CustomPlayerStatIds>()
        {
            { SURVIVAL_CMAP_FILENAME_ORXON, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP1_COOP_50_BEST_TIME },
            { SURVIVAL_CMAP_FILENAME_MPASS, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP2_COOP_50_BEST_TIME },
            { SURVIVAL_CMAP_FILENAME_VELDIN, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP3_COOP_50_BEST_TIME },
            { SURVIVAL_CMAP_FILENAME_VALIX, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP4_COOP_50_BEST_TIME },
            { SURVIVAL_CMAP_FILENAME_TORVAL, CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAP5_COOP_50_BEST_TIME },
        };

        public override CustomModeId Id => CustomModeId.CMODE_ID_SURVIVAL;
        public override string Name => "Survival";

        public SurvivalCustomMode()
        {
            Player.OnBuildDynamicPageContent -= Player_OnBuildDynamicPageContent;
            Player.OnBuildDynamicPageContent += Player_OnBuildDynamicPageContent;
        }

        public void Dispose()
        {
            Player.OnBuildDynamicPageContent -= Player_OnBuildDynamicPageContent;
        }

        private int GetRatingFromXp(long xp)
        {
            //return (int)Math.Max(100, Math.Min(10000, 100 + Math.Sqrt(xp * 4)));
            return (int)Math.Max(100, Math.Min(10000, 100 + (xp / 5000f)));
        }

        public override async Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return 0;

            // try to get stat id for map
            if (!_survivalMapToXpStatIndex.TryGetValue(metadata.CustomMapConfig.Filename, out var stat))
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
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return Task.FromResult((int?)0);

            if (_survivalMapToPrestigeStatIndex.TryGetValue(metadata.CustomMapConfig.Filename, out var stat))
                return Task.FromResult((int?)client.CustomWideStats[(int)stat]);

            return Task.FromResult((int?)0);
        }

        public async Task<bool> Prestige(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return false;

            if (!_survivalMapToXpStatIndex.TryGetValue(metadata.CustomMapConfig.Filename, out var xpStat))
                return false;
            if (!_survivalMapToPrestigeStatIndex.TryGetValue(metadata.CustomMapConfig.Filename, out var prestigeStat))
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
            client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_OVERALL_RANK] = overallRank;

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
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
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
            var hostMetadata = Player.GetPlayerMetadata(game.Host);
            var gambit = hostMetadata?.SurvivalMapStats?.GetValueOrDefault(metadata.CustomMapConfig.Filename)?.Gambits?.GetValueOrDefault(metadata.GameConfig.Survival_Gambit)?.Name;

            string info = "";
            if (metadata.GameConfig.Survival_Gambit > 0 && gambit != null)
                info += $"\nGambit: {gambit}";
            if (round > 0)
                info += $"\nRound: {round}";

            return Task.FromResult(info);
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            var payload = new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/survival-11184.bin")));
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
            var mapFilename = args.Metadata.CustomMapConfig.Filename;
            var hasGambit = args.Metadata.GameConfig.Survival_Gambit > 0;
            if (customGameData.Points == null || String.IsNullOrEmpty(mapFilename)) return;

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                var points = customGameData.Points[gameIdx];

                int? xpStatIndex = null;
                if (_survivalMapToXpStatIndex.TryGetValue(mapFilename, out var xpStat))
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

                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_OVERALL_RANK] = overallRank;
                }

                if (!player.Left)
                {
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                }

                // gambit
                if (hasGambit)
                {
                    var playerClient = args.Game.Clients.FirstOrDefault(x => x.Client?.AccountId == accountId)?.Client;
                    if (playerClient != null)
                    {
                        var playerMetadata = Player.GetPlayerMetadata(playerClient);
                        if (playerMetadata != null)
                        {
                            if (!playerMetadata.SurvivalMapStats.TryGetValue(mapFilename, out var mapStats))
                                playerMetadata.SurvivalMapStats[mapFilename] = mapStats = new SurvivalMapStat();

                            if (!mapStats.Gambits.TryGetValue(args.Metadata.GameConfig.Survival_Gambit, out var gambitStats))
                                mapStats.Gambits[args.Metadata.GameConfig.Survival_Gambit] = gambitStats = new SurvivalMapGambitStat();

                            gambitStats.BestRound = Math.Max(gambitStats.BestRound, customGameData.BestRound[gameIdx]);

                            // save
                            Player.SavePlayerMetadata(playerClient);
                        }
                    }
                }

                // general
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_KILLS] += customGameData.Kills[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DEATHS] += (ushort)gameData.Data.Deaths[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_REVIVES] += customGameData.Revives[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_REVIVED] += customGameData.TimesRevived[gameIdx];

                // general mechanics
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ROLLED_MYSTERY_BOX] += customGameData.TimesRolledMysteryBox[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_DEMON_BELL] += customGameData.TimesActivatedDemonBell[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_POWER] += customGameData.TimesActivatedPower[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TOKENS_USED_ON_GATES] += customGameData.TokensUsedOnGates[gameIdx];

                // weapon stats
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_WRENCH_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][0];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DUAL_VIPER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][1];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAGMA_CANNON_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][2];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_ARBITER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][3];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_FUSION_RIFLE_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][4];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MINE_LAUNCHER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][5];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_B6_OBLITERATOR_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][6];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_SCORPION_FLAIL_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][7];

                // high scores
                if (!hasGambit)
                {
                    int? statIndex = null;
                    int? b50StatIndex = null;
                    var coop = game.AccountIdsAtStart.Contains(',');
                    if (coop)
                    {
                        if (_survivalMapToCoopHighScoreStatIndex.TryGetValue(mapFilename, out var customStatId))
                            statIndex = (int)customStatId;
                        if (_survivalMapToCoop50BestTimeStatIndex.TryGetValue(mapFilename, out var b50CustomStatId))
                            b50StatIndex = (int)b50CustomStatId;
                    }
                    else
                    {
                        if (_survivalMapToSoloHighScoreStatIndex.TryGetValue(mapFilename, out var customStatId))
                            statIndex = (int)customStatId;
                        if (_survivalMapToSolo50BestTimeStatIndex.TryGetValue(mapFilename, out var b50CustomStatId))
                            b50StatIndex = (int)b50CustomStatId;
                    }

                    if (statIndex.HasValue)
                    {
                        args.PlayerCustomStats[accountId][statIndex.Value] = Math.Max(args.PlayerCustomStats[accountId][statIndex.Value], customGameData.BestRound[gameIdx]);
                    }

                    // min round 50 best time unless 0
                    if (b50StatIndex.HasValue && customGameData.BestRound[gameIdx] >= 50 && customGameData.Round50TimeMs > 0)
                    {
                        if (args.PlayerCustomStats[accountId][b50StatIndex.Value] == 0 || customGameData.Round50TimeMs < args.PlayerCustomStats[accountId][b50StatIndex.Value])
                        {
                            args.PlayerCustomStats[accountId][b50StatIndex.Value] = customGameData.Round50TimeMs;
                        }
                    }
                }
            }
        }


        public override Task OnGameStart(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            // set increased timeout times for survival
            foreach (var client in game.Clients)
            {
                client.Client.TimeoutSeconds = Program.GetAppSettingsOrDefault(client.Client.ApplicationId).ClientTimeoutSeconds * 10;
                client.Client.LongTimeoutSeconds = Program.GetAppSettingsOrDefault(client.Client.ApplicationId).ClientLongTimeoutSeconds * 10;
            }

            return Task.CompletedTask;
        }

        protected override Task UpdateCustomMapExData(ClientObject client, string mapFilename, byte[] data)
        {
            var playerMetadata = Player.GetPlayerMetadata(client);
            if (playerMetadata == null) return Task.CompletedTask;

            if (!playerMetadata.SurvivalMapStats.TryGetValue(mapFilename, out var mapStats))
                playerMetadata.SurvivalMapStats[mapFilename] = mapStats = new SurvivalMapStat();

            using (var ms = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(ms))
                {
                    var modeVersion = reader.ReadInt32();
                    var gambitCount = reader.ReadInt32();

                    for (int i = 0; i < gambitCount; ++i)
                    {
                        var gambitName = reader.ReadCString();
                        var gambitDesc = reader.ReadCString();

                        var key = i + 1;
                        if (!mapStats.Gambits.TryGetValue(key, out var gambitStats))
                            mapStats.Gambits[key] = gambitStats = new SurvivalMapGambitStat();

                        gambitStats.Name = gambitName;
                    }
                }
            }

            Player.SavePlayerMetadata(client);
            return Task.CompletedTask;
        }

        public void OnUpdateSurvivalGambitCompleted(UpdateSurvivalGambitCompletedRequestMessage request, ClientObject client)
        {
            var playerMetadata = Player.GetPlayerMetadata(client);
            if (playerMetadata != null)
            {
                if (!playerMetadata.SurvivalMapStats.TryGetValue(request.MapFilename, out var mapStats))
                    playerMetadata.SurvivalMapStats[request.MapFilename] = mapStats = new SurvivalMapStat();

                if (!mapStats.Gambits.TryGetValue(request.GambitIdx, out var gambitStats))
                    mapStats.Gambits[request.GambitIdx] = gambitStats = new SurvivalMapGambitStat() { Name = request.GambitName };

                gambitStats.Completed = true;

                // save
                Player.SavePlayerMetadata(client);
            }
        }

        private void Player_OnBuildDynamicPageContent(DynamicPageContentBuilder builder)
        {
            if (builder.Request.Type != GetDynamicPageContentRequestMessage.ContentType.SurvivalMapStats) return;

            if (!_survivalMapToSoloHighScoreStatIndex.ContainsKey(builder.Request.MapFilename))
            {
                builder.LineItems.Add(("", "Unsupported map"));
                return;
            }

            // get map stats
            if (!builder.PlayerMetadata.SurvivalMapStats.TryGetValue(builder.Request.MapFilename, out var mapStats))
                builder.PlayerMetadata.SurvivalMapStats[builder.Request.MapFilename] = mapStats = new SurvivalMapStat();

            var soloBestRound = builder.Client.CustomWideStats[(int)_survivalMapToSoloHighScoreStatIndex[builder.Request.MapFilename]];
            var soloBest50Ms = builder.Client.CustomWideStats[(int)_survivalMapToSolo50BestTimeStatIndex[builder.Request.MapFilename]];
            var coopBestRound = builder.Client.CustomWideStats[(int)_survivalMapToCoopHighScoreStatIndex[builder.Request.MapFilename]];
            var coopBest50Ms = builder.Client.CustomWideStats[(int)_survivalMapToCoop50BestTimeStatIndex[builder.Request.MapFilename]];
            var soloBest50Time = TimeSpan.FromMilliseconds(soloBest50Ms);
            var coopBest50Time = TimeSpan.FromMilliseconds(coopBest50Ms);

            // write percent complete
            var bestRound = Math.Max(soloBestRound, coopBestRound);
            var gambitsCompleted = mapStats.Gambits.Where(x => (x.Key - 1) < mapStats.GambitCount).Count(x => x.Value.Completed);
            var completedTasks = gambitsCompleted;
            if (bestRound >= 50) completedTasks += 1;
            var completion = completedTasks / (float)(mapStats.GambitCount + 1);
            var completionCode = completion >= 1 ? "\x0A" : (completion > 0 ? "\x09" : "");
            builder.LineItems.Add(($"{completionCode}Completion", $"{completionCode}{completion * 100:N0}%"));

            // build high scores
            //builder.LineItems.Add(("", ""));
            builder.LineItems.Add(("Solo High Score", $"{soloBestRound} Rounds"));
            builder.LineItems.Add(("Solo 50 Rounds", $"{(int)soloBest50Time.TotalHours}:{soloBest50Time:mm\\:ss\\.fff}"));
            builder.LineItems.Add(("Coop High Score", $"{coopBestRound} Rounds"));
            builder.LineItems.Add(("Coop 50 Rounds", $"{(int)coopBest50Time.TotalHours}:{coopBest50Time:mm\\:ss\\.fff}"));

            // build gambits
            builder.LineItems.Add(("", ""));
            builder.LineItems.Add(("Gambits", $"{gambitsCompleted}/{mapStats.GambitCount}"));
            foreach (var gambitStats in mapStats.Gambits.OrderBy(x => x.Key).Where(x => x.Key <= mapStats.GambitCount))
            {
                var code = gambitStats.Value.Completed ? "\x0A" : "";
                builder.LineItems.Add(($"{code}{gambitStats.Value.Name}", $"{code}{gambitStats.Value.BestRound} Rounds"));
            }
        }
    }

    public class SurvivalCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int Rounds { get; set; }
        public int Round50TimeMs { get; set; }
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
                case 5:
                    {
                        const int MAX_MOB_SPAWN_PARAMS = 10;
                        const int PLAYER_UPGRADE_COUNT = 7;

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
                            BestWeaponLevels[i] = reader.ReadArray<byte>(9);
                        break;
                    }
                case 6:
                    {
                        const int MAX_MOB_SPAWN_PARAMS = 10;
                        const int PLAYER_UPGRADE_COUNT = 7;

                        AlphaModsReceived = new byte[10][];
                        BestWeaponLevels = new byte[10][];
                        KillsPerMob = new int[10][];
                        DeathsByMob = new short[10][];
                        PlayerUpgrades = new short[10][];

                        Rounds = reader.ReadInt32();
                        Round50TimeMs = reader.ReadInt32();
                        reader.ReadInt32();
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
                            BestWeaponLevels[i] = reader.ReadArray<byte>(9);
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
}
