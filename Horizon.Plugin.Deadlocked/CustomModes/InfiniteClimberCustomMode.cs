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
    public class InfiniteClimberCustomMode : BaseCustomMode
    {
        public override CustomModeId Id => CustomModeId.CMODE_ID_INFINITE_CLIMBER;
        public override string Name => "Infinite Climber";

        public override Task<int?> GetRank(ClientObject client)
        {
            return Task.FromResult((int?)client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_RANK]);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            args.Reject = true; // reject all
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

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/climber-11184.bin"))));
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new InfiniteClimberCustomData();
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            if (game == null || metadata == null || gameData == null)
                return false;

            // game must have custom data
            if (gameData.CustomGameData == null)
                return false;

            return true;
        }

        protected override Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as InfiniteClimberCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            var teamScores = new double[10];
            var teamSizes = new int[10];

            // set rank and score of players
            foreach (var player in args.Players)
            {
                player.Rank = args.PlayerCustomStats[player.AccountId][(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_RANK];
                player.Score = (int)customGameData.BestScores[player.Index];

                // team score is max of team's players' scores
                if (teamScores[player.Team] < player.Score)
                    teamScores[player.Team] = player.Score;

                teamSizes[player.Team]++;
            }

            // rate game if more than one team
            var rankGame = teamSizes.Count(x => x > 0) > 1;
            if (rankGame)
                Stats.RateGame(args.Players, teamScores, teamWager: 0.3f, ffaWager: 0.1f, rewardFlatness: 0.5f, drawPenalty: 0.2f);

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                // rank stats
                if (rankGame)
                {
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_RANK] = player.Rank;
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_WINS] += player.Won ? 1 : 0;
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_LOSSES] += player.Won ? 0 : 1;
                }

                // general stats
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_HIGH_SCORE] = Math.Max(args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_HIGH_SCORE], (int)player.Score);
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_GAMES_PLAYED] += 1;

                if (!player.Left)
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_CLIMBER_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
            }

            return Task.CompletedTask;
        }
    }

    public class InfiniteClimberCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public float[] FinalScores { get; set; }
        public float[] BestScores { get; set; }

        public void Deserialize(BinaryReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            switch (Version)
            {
                case 1:
                    {
                        FinalScores = reader.ReadArray<float>(10);
                        BestScores = reader.ReadArray<float>(10);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported infinite climber data version {Version}");
                        break;
                    }
            }
        }
    }
}
