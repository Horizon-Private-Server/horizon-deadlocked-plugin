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
    public class SpleefCustomMode : BaseCustomMode
    {
        public override CustomModeId Id => CustomModeId.CMODE_ID_SPLEEF;
        public override string Name => "Spleef";

        public override Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult((int?)client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_RANK]);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            // reject all
            args.Reject = true;
            return Task.CompletedTask;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var timelimit = (game.GenericField7 >> 27) & 7;
            var time = $"{timelimit * 5} minutes";
            if (timelimit == 0)
                time = "None";

            var scoreToWin = "None";
            if (game.GenericField3 > 0)
                scoreToWin = game.GenericField3.ToString();

            return Task.FromResult($"Timelimit: {time}\nScore to win: {scoreToWin}");
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/spleef-11184.bin"))));
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new SpleefCustomData();
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
            var customGameData = args.GameData.CustomGameData as SpleefCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            var teamScores = new double[10];

            // set rank and score of players
            foreach (var player in args.Players)
            {
                player.Rank = args.PlayerCustomStats[player.AccountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_RANK];
                player.Score = customGameData.Points[player.Index];

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

                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_RANK] = player.Rank;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_WINS] += player.Won ? 1 : 0;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_LOSSES] += player.Won ? 0 : 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_BOXES_BROKEN] += customGameData.BoxesDestroyed[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_POINTS] += customGameData.Points[gameIdx];

                if (!player.Left)
                {
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_ROUNDS_PLAYED] += customGameData.Rounds;
                }
            }

            return Task.CompletedTask;
        }
    }

    public class SpleefCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int Rounds { get; set; }
        public int[] Points { get; set; }
        public int[] BoxesDestroyed { get; set; }

        public void Deserialize(MessageReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            switch (Version)
            {
                case 1:
                    {
                        Rounds = reader.ReadInt32();
                        Points = reader.ReadArray<int>(10);
                        BoxesDestroyed = reader.ReadArray<int>(10);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported spleef data version {Version}");
                        break;
                    }
            }
        }
    }
}
