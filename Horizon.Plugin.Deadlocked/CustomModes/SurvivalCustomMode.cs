using Horizon.Plugin.Deadlocked.DTO;
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

        private static Dictionary<(string, int), string> _survivalMapGambits = [];
        private static Dictionary<string, int> _survivalMapGambitCount = [];

        public override CustomModeId Id => CustomModeId.CMODE_ID_SURVIVAL;
        public override string Name => "Survival";

        public SurvivalCustomMode()
        {
            Player.OnBuildDynamicPageContentCallbacks.Remove(Player_OnBuildDynamicPageContentAsync);
            Player.OnBuildDynamicPageContentCallbacks.Add(Player_OnBuildDynamicPageContentAsync);
        }

        public void Dispose()
        {
            Player.OnBuildDynamicPageContentCallbacks.Remove(Player_OnBuildDynamicPageContentAsync);
        }

        private int GetRatingFromXp(long xp)
        {
            //return (int)Math.Max(100, Math.Min(10000, 100 + Math.Sqrt(xp * 4)));
            return (int)Math.Max(100, Math.Min(10000, 100 + (xp / 5000f)));
        }

        private decimal GetPercentComplete(SurvivalAccountMapStatDTO mapStats)
        {
            var gambitsCount = GetGambitCount(mapStats.MapFilename);
            var gambitsCompleted = Enumerable.Range(1, gambitsCount)
                .Select(x => GetGambitName(mapStats.MapFilename, x))
                .Where(x => mapStats.Gambits.Any(g => g.Gambit == x && g.Completed == true))
                .Count();

            if (mapStats.SoloRound >= 50 || mapStats.CoopRound >= 50)
                gambitsCompleted += 1;

            return gambitsCompleted / (decimal)(gambitsCount + 1);
        }

        private static string GetGambitName(string mapFilename, int gambitIdx)
        {
            return _survivalMapGambits.GetValueOrDefault((mapFilename, gambitIdx));
        }

        private void CacheGambitName(string mapFilename, int gambitIdx, string gambitName)
        {
            _survivalMapGambits[(mapFilename, gambitIdx)] = gambitName;
        }

        private static int GetGambitCount(string mapFilename)
        {
            return _survivalMapGambitCount.GetValueOrDefault(mapFilename);
        }

        private void CacheGambitCount(string mapFilename, int gambitCount)
        {
            _survivalMapGambitCount[mapFilename] = gambitCount;
        }

        public override async Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return 0;

            // invalid map
            if (!await Plugin.Database.GetIsSupportedSurvivalMapAsync(metadata.CustomMapConfig.Filename))
                return 0;

            // try to get stat id for map
            var mapStats = await Plugin.Database.GetSurvivalMapStatsAsync(client.AccountId, metadata.CustomMapConfig.Filename);
            if (mapStats == null)
                return 0;

            var rank = GetRatingFromXp(mapStats.Xp);
            var prestige = await GetPrestige(game, metadata, client) ?? 0;
            if (prestige >= SurvivalMaxPrestige && rank >= 10000)
                rank = 9999; // cap

            return rank;
        }

        public async Task<int?> GetPrestige(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return 0;

            // invalid map
            if (!await Plugin.Database.GetIsSupportedSurvivalMapAsync(metadata.CustomMapConfig.Filename))
                return 0;

            var mapStats = await Plugin.Database.GetSurvivalMapStatsAsync(client.AccountId, metadata.CustomMapConfig.Filename);
            if (mapStats == null)
                return 0;

            return mapStats.Prestige;
        }

        public async Task<bool> Prestige(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return false;

            // invalid map
            if (!await Plugin.Database.GetIsSupportedSurvivalMapAsync(metadata.CustomMapConfig.Filename))
                return false;

            var mapStats = await Plugin.Database.GetSurvivalMapStatsAsync(client.AccountId, metadata.CustomMapConfig.Filename);
            var rank = GetRatingFromXp(mapStats.Xp);
            var prestige = mapStats.Prestige;
            if (rank < 10000) return false;
            if (prestige >= SurvivalMaxPrestige) return false;

            // increment prestige
            prestige += 1;
            mapStats.Prestige = prestige;
            mapStats.Xp = 0;
            mapStats.Rank = 100;
            await Plugin.Database.UpdateSurvivalAccountMapStatsAsync(mapStats);
            return true;
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
            var gambit = GetGambitName(metadata.CustomMapConfig.Filename, metadata.GameConfig.Survival_Gambit);

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
            var gambit = GetGambitName(mapFilename, args.Metadata.GameConfig.Survival_Gambit);
            if (customGameData.Points == null || String.IsNullOrEmpty(mapFilename)) return;
            if (args.Metadata.GameConfig.HasDevRule()) return;

            // invalid map
            if (!await Plugin.Database.GetIsSupportedSurvivalMapAsync(mapFilename))
                return;

            if (hasGambit && string.IsNullOrEmpty(gambit))
            {
                gambit = "UNKNOWN_GAMBIT";
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Missing expected gambit name for {mapFilename}:{args.Metadata.GameConfig.Survival_Gambit}");
            }

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;
                var accountStats = await Plugin.Database.GetSurvivalStatsAsync(accountId);
                var mapStats = await Plugin.Database.GetSurvivalMapStatsAsync(accountId, mapFilename);
                var points = customGameData.Points[gameIdx];

                mapStats.Xp = (int)Math.Max(0, Math.Min(int.MaxValue, (ulong)mapStats.Xp + points));
                mapStats.Rank = GetRatingFromXp(mapStats.Xp);
                mapStats.PercentCompleted = GetPercentComplete(mapStats);
                await Plugin.Database.UpdateSurvivalAccountMapStatsAsync(mapStats);

                if (!player.Left)
                {
                    accountStats.TimePlayedMs += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                }

                // gambit
                if (hasGambit)
                {
                    var playerClient = args.Game.Clients.FirstOrDefault(x => x.Client?.AccountId == accountId)?.Client;
                    if (playerClient != null)
                    {
                        await Plugin.Database.UpdateSurvivalAccountMapGambitStatsAsync(playerClient.AccountId, mapFilename, gambit, customGameData.BestRound[gameIdx], null);
                    }
                }

                // general
                accountStats.Kills += customGameData.Kills[gameIdx];
                accountStats.Deaths += (ushort)gameData.Data.Deaths[gameIdx];
                accountStats.GamesPlayed += 1;
                accountStats.Revives += customGameData.Revives[gameIdx];
                accountStats.TimesRevived += customGameData.TimesRevived[gameIdx];

                // general mechanics
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ROLLED_MYSTERY_BOX] += customGameData.TimesRolledMysteryBox[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_DEMON_BELL] += customGameData.TimesActivatedDemonBell[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_POWER] += customGameData.TimesActivatedPower[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TOKENS_USED_ON_GATES] += customGameData.TokensUsedOnGates[gameIdx];

                // weapon stats
                accountStats.WrenchKills += (ushort)gameData.Data.WeaponKills[gameIdx][0];
                accountStats.DualViperKills += (ushort)gameData.Data.WeaponKills[gameIdx][1];
                accountStats.MagmaCannonKills += (ushort)gameData.Data.WeaponKills[gameIdx][2];
                accountStats.ArbiterKills += (ushort)gameData.Data.WeaponKills[gameIdx][3];
                accountStats.FusionRifleKills += (ushort)gameData.Data.WeaponKills[gameIdx][4];
                accountStats.MineLauncherKills += (ushort)gameData.Data.WeaponKills[gameIdx][5];
                accountStats.B6Kills += (ushort)gameData.Data.WeaponKills[gameIdx][6];
                accountStats.ScorpionFlailKills += (ushort)gameData.Data.WeaponKills[gameIdx][7];
                accountStats.HoloshieldKills += (ushort)gameData.Data.WeaponKills[gameIdx][8];

                // post update
                await Plugin.Database.UpdateSurvivalStatsAsync(accountStats);
            }

            // post each set of people that made it to a round
            // if a group of 3 players play, and 2 players make it to round 100, but one of them lags out at 50
            // then post two games, one that ended at 50 and another that ended at 100 for both sets of players.
            var roundsPosted = new HashSet<int>();
            var accountIdsAtStart = game.AccountIdsAtStart.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var bestRound in customGameData.BestRound)
            {
                if (bestRound <= 0) continue;
                if (roundsPosted.Contains(bestRound)) continue;

                roundsPosted.Add(bestRound);

                // get account ids that were in game at this point
                var accountIds = accountIdsAtStart
                    .Select(x => args.Players.FirstOrDefault(p => p.AccountId == int.Parse(x)))
                    .Where(x => x != null && customGameData.BestRound[x.Index] >= bestRound)
                    .Select(x => x.AccountId)
                    .ToArray();

                // post run
                await Plugin.Database.CreateSurvivalMapRunAsync(new DTO.SurvivalRunDTO()
                {
                    GameHistoryId = game.Id,
                    MapFilename = mapFilename,
                    Gambit = hasGambit ? gambit : null,
                    PlayerCountAtStart = accountIdsAtStart.Length,
                    AccountIds = accountIds,
                    RoundsCompleted = bestRound,
                    Time50Ms = (customGameData.Round50TimeMs <= 0 || bestRound < 50) ? null : customGameData.Round50TimeMs,
                    TimeMs = 0
                });
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
                        CacheGambitName(mapFilename, key, gambitName);

                        // create entries
                        _ = Plugin.Database.UpdateSurvivalAccountMapGambitStatsAsync(client.AccountId, mapFilename, gambitName, null, null);
                    }

                    CacheGambitCount(mapFilename, gambitCount);
                }
            }

            Player.SavePlayerMetadata(client);
            return Task.CompletedTask;
        }

        public async Task OnUpdateSurvivalGambitCompleted(UpdateSurvivalGambitCompletedRequestMessage request, ClientObject client)
        {
            // mark completed
            await Plugin.Database.UpdateSurvivalAccountMapGambitStatsAsync(client.AccountId, request.MapFilename, request.GambitName, null, true);
        }

        private static async Task Player_OnBuildDynamicPageContentAsync(DynamicPageContentBuilder builder)
        {
            if (builder.Request.Type != GetDynamicPageContentRequestMessage.ContentType.SurvivalMapStats) return;

            if (!await Plugin.Database.GetIsSupportedSurvivalMapAsync(builder.Request.MapFilename))
            {
                builder.LineItems.Add(("", "Unsupported map"));
                return;
            }

            // get map stats
            var gambitCount = GetGambitCount(builder.Request.MapFilename);
            var mapStats = await Plugin.Database.GetSurvivalMapStatsAsync(builder.Client.AccountId, builder.Request.MapFilename);

            var overallRanking = mapStats.OverallRanking;
            var soloRoundRanking = mapStats.SoloRoundRanking;
            var soloTime50Ranking = mapStats.Solo50Ranking;
            var soloBestRound = mapStats.SoloRound ?? 0;
            var soloBest50Ms = mapStats.Solo50 ?? 0;
            var coopBestRound = mapStats.CoopRound ?? 0;
            var coopBest50Ms = mapStats.Coop50 ?? 0;
            var soloBest50Time = TimeSpan.FromMilliseconds(soloBest50Ms);
            var coopBest50Time = TimeSpan.FromMilliseconds(coopBest50Ms);

            // write percent complete
            var completion = mapStats.PercentCompleted;
            var completionCode = completion >= 1 ? "\x0A" : (completion > 0 ? "\x09" : "");
            builder.LineItems.Add(($"{completionCode}Completion", $"{completionCode}{completion * 100:N0}%"));

            // build high scores
            //builder.LineItems.Add(("", ""));
            //builder.LineItems.Add(("Overall Ranking", $"{(overallRanking.HasValue ? overallRanking.Value.ToString() : "---")}"));
            builder.LineItems.Add(("Solo High Score", $"{soloBestRound} Rounds {(soloRoundRanking.HasValue ? $"(#{soloRoundRanking})" : "")}".Trim()));
            builder.LineItems.Add(("Solo 50 Rounds", $"{(int)soloBest50Time.TotalHours}:{soloBest50Time:mm\\:ss\\.fff} {(soloTime50Ranking.HasValue ? $"(#{soloTime50Ranking})" : "")}".Trim()));
            builder.LineItems.Add(("Coop High Score", $"{coopBestRound} Rounds"));
            builder.LineItems.Add(("Coop 50 Rounds", $"{(int)coopBest50Time.TotalHours}:{coopBest50Time:mm\\:ss\\.fff}"));

            // build gambits
            var gambitsCompleted = Enumerable.Range(1, gambitCount).Where(x => mapStats.Gambits.Any(g => g.Gambit == GetGambitName(builder.Request.MapFilename, x) && g.Completed == true)).Count();
            builder.LineItems.Add(("", ""));
            builder.LineItems.Add(("Gambits", $"{gambitsCompleted}/{gambitCount}"));
            for (int i = 0; i < gambitCount; ++i)
            {
                var key = i + 1;
                var gambitName = GetGambitName(builder.Request.MapFilename, key);
                if (string.IsNullOrEmpty(gambitName)) continue;

                var gambitStats = mapStats.Gambits.FirstOrDefault(x => x.Gambit == gambitName);
                //if (gambitStats == null) continue;

                var code = gambitStats?.Completed == true ? "\x0A" : "";
                builder.LineItems.Add(($"{code}{gambitName}", $"{code}{gambitStats?.BestRound ?? 0} Rounds"));
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
                case 7:
                    {
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
                        BestRound = reader.ReadArray<short>(10);
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
