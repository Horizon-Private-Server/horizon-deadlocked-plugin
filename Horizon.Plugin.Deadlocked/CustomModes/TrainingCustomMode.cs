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
    public class TrainingCustomMode : BaseCustomMode
    {
        public enum TrainingTypes
        {
            FusionRifle,
            Cycle,
            Rush,
        }

        public enum TrainingAggression
        {
            Aggro,
            AggroNoDamage,
            Passive,
            Idle
        }

        public static readonly Dictionary<TrainingTypes, string> TrainingTypeNames = new Dictionary<TrainingTypes, string>()
        {
            { TrainingTypes.FusionRifle, "Fusion Rifle" },
            { TrainingTypes.Rush, "Rushing" },
            { TrainingTypes.Cycle, "Cycle" },
        };

        public static readonly Dictionary<int, string> TrainingVariationNames = new Dictionary<int, string>()
        {
            { 0, "Ranked" },
            { 1, "Endless" },
        };

        public static readonly Dictionary<TrainingAggression, string> TrainingAggressionNames = new Dictionary<TrainingAggression, string>()
        {
            { TrainingAggression.Aggro, "Aggressive" },
            { TrainingAggression.AggroNoDamage, "No Damage" },
            { TrainingAggression.Passive, "Passive" },
            { TrainingAggression.Idle, "Idle" },
        };

        public override CustomModeId Id => CustomModeId.CMODE_ID_TRAINING;
        public override string Name => "Training";

        public override Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult((int?)null);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            // reject all
            args.Reject = true;
            return Task.CompletedTask;
        }

        public override sbyte GetModuleArg3(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return (sbyte)metadata.GameConfig.Training_Type;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var trainingType = (TrainingTypes)metadata.GameConfig.Training_Type;

            return Task.FromResult($"Training: {TrainingTypeNames[trainingType]} {TrainingVariationNames[metadata.GameConfig.Training_Variation]}\nAggression: {TrainingAggressionNames[(TrainingAggression)metadata.GameConfig.Training_Aggression]}");
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var trainingType = (TrainingTypes)metadata.GameConfig.Training_Type;

            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, $"bin/patch/training-{trainingType.ToString().ToLower()}-11184.bin"))));
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

            // needs to be ranked
            if (metadata.GameConfig.Training_Variation != 0)
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
            if (customGameData == null)
                return Task.CompletedTask;

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
                var timeSeconds = (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);

                // ignore players who leave
                if (player.Left)
                    continue;

                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_RANK] = player.Rank;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_TIME_PLAYED] += timeSeconds;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_TOTAL_KILLS] += customGameData.Kills;

                switch ((TrainingTypes)args.Metadata.GameConfig.Training_Type)
                {
                    case TrainingTypes.FusionRifle:
                        {
                            int bestTime = args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_TIME];
                            if (bestTime <= 0)
                                bestTime = int.MaxValue;

                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_POINTS] = Math.Max(customGameData.Points, args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_POINTS]);
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_COMBO] = Math.Max(customGameData.BestCombo, args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_COMBO]);
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_BEST_TIME] = 0; // Math.Min(customGameData.Time, bestTime);
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_MISSES] += customGameData.Misses;
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_KILLS] += customGameData.Kills;
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_HITS] += customGameData.Hits;

                            float totalHits = args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_HITS];
                            float totalMisses = args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_MISSES];
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_ACCURACY] = 0;

                            if (totalMisses > 0 || totalHits > 0)
                                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_FUSION_ACCURACY] = (int)(100 * 100 * (totalHits / (totalHits + totalMisses)));
                            break;
                        }
                    case TrainingTypes.Cycle:
                        {
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_BEST_POINTS] = Math.Max(customGameData.Points, args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_BEST_POINTS]);
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_BEST_COMBO] = Math.Max(customGameData.BestCombo, args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_BEST_COMBO]);
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_FUSION_MISSES] += customGameData.Misses;
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_FUSION_HITS] += customGameData.Hits;
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_KILLS] += customGameData.Kills;
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_DEATHS] += gameData.Data.Deaths[0];

                            float totalHits = args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_FUSION_HITS];
                            float totalMisses = args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_FUSION_MISSES];
                            args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_FUSION_ACCURACY] = 0;

                            if (totalMisses > 0 || totalHits > 0)
                                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_TRAINING_CYCLE_FUSION_ACCURACY] = (int)(100 * 100 * (totalHits / (totalHits + totalMisses)));
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
        public int BestCombo { get; set; }

        public void Deserialize(MessageReader reader)
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
                case 2:
                    {
                        Points = reader.ReadInt32();
                        Time = reader.ReadInt32();
                        Kills = reader.ReadInt32();
                        Hits = reader.ReadInt32();
                        Misses = reader.ReadInt32();
                        BestCombo = reader.ReadInt32();
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
