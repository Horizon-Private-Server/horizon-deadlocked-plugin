using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Server.Medius;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode : BaseCustomMode
    {
        public const int MAX_CLASSES = 16;
        public static readonly int MAX_CLASS_LEVEL = 10;
        public static readonly int MAX_ACCOUNT_LEVEL = 98;
        public static readonly int MAX_WEAPON_LEVEL = 98;
        public static readonly string HUB_MAP_FILENAME = "raids_hub";

        private readonly object _contractLock = new();

        public override CustomModeId Id => CustomModeId.CMODE_ID_RAIDS;
        public override string Name => "DreadZone Raids (Alpha)";

        private static RaidsBank GetBank(ClientObject client, bool initialize = true)
        {
            var bank = Player.GetPlayerMetadata(client)?.RaidsBank;
            if (initialize && bank?.Initialize() == true)
                Player.SavePlayerMetadata(client);

            return bank;
        }

        private static IEnumerable<ClientObject> GetParty(ClientObject client)
        {
            return client.CurrentGame.Clients.Select(c => c.Client).Where(c => c != null);
        }

        private int GetRatingFromXp(long xp)
        {
            //return (int)Math.Max(100, Math.Min(10000, 100 + Math.Sqrt(xp * 4)));
            return (int)Math.Max(100, Math.Min(10000, 100 + (xp / 5000f)));
        }

        public override Task OnModeSelected(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            game.ChatChannel.BroadcastSystemMessage(game.ChatChannel.Clients, $"ARaids is a new PvE mode currently in Alpha.");
            game.ChatChannel.BroadcastSystemMessage(game.ChatChannel.Clients, $"APlease submit feedback and bugs to the Horizon Discord.");
            game.ChatChannel.BroadcastSystemMessage(game.ChatChannel.Clients, $"AThe Raids map pack is required to play.");
            return Task.CompletedTask;
        }

        public override async Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return 0;

            return GetRatingFromXp(0);
        }

        public override Task<string> GetGameTitle(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult(Name);
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var mapName = metadata.CustomMapConfig.Name;

            // a space suppresses the default match rules when there is no map
            if (String.IsNullOrWhiteSpace(mapName))
                return Task.FromResult(" ");

            // Raids uses RoundNumber for mission status: high byte = mission type + 1
            // low byte = zero-based difficulty. Zero means the client has not reported it.
            var status = metadata.GameState.RoundNumber;
            var missionType = (RaidsMissionType)((status >> 8) - 1);
            var stars = (status & 0xFF) + 1;
            var info = missionType switch
            {
                RaidsMissionType.Hub => $"Gearing up at {mapName}",
                RaidsMissionType.OpenWorld => $"Leveling up on Planet {mapName}",
                RaidsMissionType.Raid => $"Running the {mapName} Raid",
                RaidsMissionType.Minigame => game.Clients.Count > 1 ? $"Playing {mapName} with the party" : $"Playing {mapName} alone",
                _ => $" "
            };

            if (missionType == RaidsMissionType.Raid && stars <= 5)
                info += $" · {new string('★', stars)}{new string('☆', 5 - stars)}";

            return Task.FromResult(info);
        }

        public override Task<string> GetGamePlayerInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var lines = new List<string>();

            foreach (var client in game.Clients.Select(x => x.Client).OrderBy(x => x.AccountName, StringComparer.Ordinal))
            {
                var account = GetBank(client, initialize: false)?.Account ?? new RaidsAccount();
                var level = RaidsAccount.GetAccountLevelFromXp(account.Experience) + 1;
                var raidsClass = account.GetEquippedClass() ?? RaidsClasses.Definitions[0];
                var className = RaidsClasses.GetName(raidsClass.ClassType);
                var classXp = account.ClassStats?.GetValueOrDefault(raidsClass.ClassType)?.Experience ?? 0;
                var classLevel = raidsClass.GetClassLevelFromXp(classXp);
                var levelSuffix = RaidsClasses.GetLevelSuffix(classLevel);
                var classSuffix = levelSuffix.Length > 0 ? $" {levelSuffix}" : String.Empty;

                lines.Add($"{client.AccountName} - Level {level} - {className}{classSuffix}");
            }

            return Task.FromResult(String.Join("\n", lines));
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            args.Reject = true; // reject all
            return Task.CompletedTask;
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            var payload = new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/raids-11184.bin")));
            return Task.FromResult(payload);
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new RaidsCustomData();
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
            var customGameData = args.GameData.CustomGameData as RaidsCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            var mapFilename = args.Metadata.CustomMapConfig.Filename;
            if (String.IsNullOrEmpty(mapFilename)) return;

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                //if (!player.Left)
                //{
                //    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                //}

                //// general
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_KILLS] += customGameData.Kills[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DEATHS] += (ushort)gameData.Data.Deaths[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_GAMES_PLAYED] += 1;
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_REVIVES] += customGameData.Revives[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_REVIVED] += customGameData.TimesRevived[gameIdx];

                //// general mechanics
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ROLLED_MYSTERY_BOX] += customGameData.TimesRolledMysteryBox[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_DEMON_BELL] += customGameData.TimesActivatedDemonBell[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_POWER] += customGameData.TimesActivatedPower[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TOKENS_USED_ON_GATES] += customGameData.TokensUsedOnGates[gameIdx];

                //// weapon stats
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_WRENCH_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][0];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DUAL_VIPER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][1];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAGMA_CANNON_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][2];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_ARBITER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][3];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_FUSION_RIFLE_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][4];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MINE_LAUNCHER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][5];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_B6_OBLITERATOR_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][6];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_SCORPION_FLAIL_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][7];

                //// high scores
                //int? statIndex = null;
                //int? b50StatIndex = null;
                //var coop = game.AccountIdsAtStart.Contains(',');
                //if (coop)
                //{
                //    if (_survivalMapToCoopHighScoreStatIndex.TryGetValue(mapFilename, out var customStatId))
                //        statIndex = (int)customStatId;
                //    if (_survivalMapToCoop50BestTimeStatIndex.TryGetValue(mapFilename, out var b50CustomStatId))
                //        b50StatIndex = (int)b50CustomStatId;
                //}
                //else
                //{
                //    if (_survivalMapToSoloHighScoreStatIndex.TryGetValue(mapFilename, out var customStatId))
                //        statIndex = (int)customStatId;
                //    if (_survivalMapToSolo50BestTimeStatIndex.TryGetValue(mapFilename, out var b50CustomStatId))
                //        b50StatIndex = (int)b50CustomStatId;
                //}

                //if (statIndex.HasValue)
                //{
                //    args.PlayerCustomStats[accountId][statIndex.Value] = Math.Max(args.PlayerCustomStats[accountId][statIndex.Value], customGameData.BestRound[gameIdx]);
                //}

                //// min round 50 best time unless 0
                //if (b50StatIndex.HasValue && customGameData.BestRound[gameIdx] >= 50 && customGameData.Round50TimeMs > 0)
                //{
                //    if (args.PlayerCustomStats[accountId][b50StatIndex.Value] == 0 || customGameData.Round50TimeMs < args.PlayerCustomStats[accountId][b50StatIndex.Value])
                //    {
                //        args.PlayerCustomStats[accountId][b50StatIndex.Value] = customGameData.Round50TimeMs;
                //    }
                //}
            }
        }

        public override Task OnGameStart(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            // set increased timeout times for raids
            foreach (var client in game.Clients)
            {
                client.Client.TimeoutSeconds = Program.GetAppSettingsOrDefault(client.Client.ApplicationId).ClientTimeoutSeconds * 10;
                client.Client.LongTimeoutSeconds = Program.GetAppSettingsOrDefault(client.Client.ApplicationId).ClientLongTimeoutSeconds * 10;
            }

            return Task.CompletedTask;
        }

    }
}
