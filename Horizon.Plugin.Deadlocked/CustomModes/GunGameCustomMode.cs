using Server.Common.Stream;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.CustomModes
{
    public class GunGameCustomMode : BaseCustomMode
    {
        public override CustomModeId Id => CustomModeId.CMODE_ID_GUN_GAME;
        public override string Name => "Gun Game";

        public override Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult((int?)client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_RANK]);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            var client = args.Player;

            args.WideStats[(int)PlayerStatIds.STAT_OVERALL_RANK] = client.WideStats[(int)PlayerStatIds.STAT_OVERALL_RANK];
            for (int i = (int)PlayerStatIds.STAT_DEATHMATCH_RANK; i <= (int)PlayerStatIds.STAT_DEATHMATCH_DEATHS; ++i)
                args.WideStats[i] = client.WideStats[i];

            return Task.CompletedTask;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var timelimit = (game.GenericField7 >> 27) & 7;
            var time = $"{timelimit * 5} minutes";
            if (timelimit == 0)
                time = "None";

            return Task.FromResult($"Timelimit: {time}");
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/gun-game-11184.bin"))));
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new GunGameCustomData();
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            if (game == null || metadata == null || gameData == null)
                return false;

            // need at least two players
            if (gameData.StartGameSettings.PlayerAccountIds.Count(x => x > 0) <= 1)
                return false;

            // construct team info
            var teamIds = new List<int>();
            var teamCounts = new int[10];
            foreach (var team in gameData.StartGameSettings.PlayerTeams)
            {
                if (team >= 0 && team < 10)
                {
                    teamCounts[team] += 1;
                    if (!teamIds.Contains(team))
                        teamIds.Add(team);
                }
            }

            // need more than one team
            if (teamIds.Count < 2)
                return false;

            // must be a ffa
            if (teamCounts.Max() > 1)
                return false;

            // game must last at least a minute
            if (!game.UtcTimeEnded.HasValue || !game.UtcTimeStarted.HasValue || (game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds < 60)
                return false;

            // game must have custom data
            if (gameData.CustomGameData == null)
                return false;

            return true;
        }

        protected override Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as GunGameCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            var teamScores = new double[10];

            // set rank and score of players
            foreach (var player in args.Players)
            {
                player.Rank = args.PlayerCustomStats[player.AccountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_RANK];
                player.Score = customGameData.Guns[player.Index];

                // team score is max of team's players' scores
                if (teamScores[player.Team] < player.Score)
                    teamScores[player.Team] = player.Score;
            }

            // rate game
            Stats.RateGame(args.Players, teamScores, teamWager: 0.3f, ffaWager: 0.1f, rewardFlatness: 0.5f, drawPenalty: 0.2f);

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_RANK] = player.Rank;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_KILLS] += gameData.Data.Kills[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_DEATHS] += gameData.Data.Deaths[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_WINS] += player.Won ? 1 : 0;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_LOSSES] += player.Won ? 0 : 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_TIMES_PROMOTED] += customGameData.Promotions[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_TIMES_DEMOTED] += customGameData.Demotions[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_DEMOTIONS] += customGameData.TimesDemotedAnother[gameIdx];

                if (!player.Left)
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_GUNGAME_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
            }

            return Task.CompletedTask;
        }
    }

    public class GunGameCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int[] Guns { get; set; }
        public int[] Demotions { get; set; }
        public int[] Promotions { get; set; }
        public int[] TimesDemotedAnother { get; set; }

        public void Deserialize(MessageReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            switch (Version)
            {
                case 1:
                    {
                        Guns = reader.ReadArray<int>(10);
                        Demotions = reader.ReadArray<int>(10);
                        Promotions = reader.ReadArray<int>(10);
                        TimesDemotedAnother = reader.ReadArray<int>(10);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported gun game data version {Version}");
                        break;
                    }
            }
        }
    }
}
