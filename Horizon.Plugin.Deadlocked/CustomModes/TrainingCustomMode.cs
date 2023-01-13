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
    public class TrainingCustomMode : BaseCustomMode
    {
        public enum TrainingTypes
        {
            FusionRifle,
            B6,
            Cycle
        }

        public static readonly Dictionary<TrainingTypes, string> TrainingTypeNames = new Dictionary<TrainingTypes, string>()
        {
            { TrainingTypes.FusionRifle, "Fusion Rifle" },
            { TrainingTypes.B6, "B6 Obliterator" },
            { TrainingTypes.Cycle, "Cycle" },
        };

        public override CustomModeId Id => CustomModeId.CMODE_ID_TRAINING;
        public override string Name => "Training";

        public override Task<int?> GetRank(ClientObject client)
        {
            return Task.FromResult((int?)null);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            // reject all
            args.Reject = true;
            return Task.CompletedTask;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var trainingType = (TrainingTypes)metadata.GameConfig.Training_Type;

            return Task.FromResult($"Training: {TrainingTypeNames[trainingType]}");
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/training-11184.bin"))));
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new TrainingCustomData();
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            if (game == null || metadata == null || gameData == null)
                return false;

            // need exactly one player
            if (gameData.StartGameSettings.PlayerAccountIds.Count(x => x > 0) != 1)
                return false;

            // game must have custom data
            if (gameData.CustomGameData == null)
                return false;

            return true;
        }

        protected override Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as TrainingCustomData;
            var gameData = args.GameData;
            var game = args.Game;

            // set rank and score of players
            //foreach (var player in args.Players)
            //{
            //    player.Rank = args.PlayerCustomStats[player.AccountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SPLEEF_RANK];
            //    player.Score = customGameData.Points[player.Index];

            //    // team score is max of team's players' scores
            //    if (teamScores[player.Team] < player.Score)
            //        teamScores[player.Team] = player.Score;
            //}

            //// rate game
            //Stats.RateGame(args.Players, teamScores, teamWager: 0.3f, ffaWager: 0.1f, rewardFlatness: 0.5f, drawPenalty: 0.2f);

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                // ignore players who leave
                if (player.Left)
                    continue;

                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_RANK] = player.Rank;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_TIME_PLAYED] += (int)Math.Ceiling(customGameData.Time / 1000f); // (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_TOTAL_KILLS] += customGameData.Kills;

                switch ((TrainingTypes)args.Metadata.GameConfig.Training_Type)
                {
                    case TrainingTypes.FusionRifle:
                        {
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_POINTS] = Math.Max(customGameData.Points, args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_POINTS]);
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_TIME] = Math.Min(customGameData.Time, args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_TIME]);
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_KILLS] += customGameData.Kills;
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_HITS] += customGameData.Hits;
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_MISSES] += customGameData.Misses;

                            float totalHits = args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_HITS];
                            float totalMisses = args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_MISSES];
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_ACCURACY] = 0;

                            if (totalMisses > 0 || totalHits > 0)
                                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_ACCURACY] = (int)(100 * 100 * (totalHits / (totalHits + totalMisses)));
                            break;
                        }
                    default:
                        {
                            Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, $"Unsupported training type {args.Metadata.GameConfig.Training_Type} gameCreated:{args.Game.UtcTimeCreated}");
                            break;
                        }
                }
            }

            return Task.CompletedTask;
        }
    }

    public class TrainingCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int Points { get; set; }
        public int Time { get; set; }
        public int Kills { get; set; }
        public int Hits { get; set; }
        public int Misses { get; set; }

        public void Deserialize(BinaryReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            switch (Version)
            {
                case 1:
                    {
                        Points = reader.ReadInt32();
                        Time = reader.ReadInt32();
                        Kills = reader.ReadInt32();
                        Hits = reader.ReadInt32();
                        Misses = reader.ReadInt32();
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported training data version {Version}");
                        break;
                    }
            }
        }
    }
}
