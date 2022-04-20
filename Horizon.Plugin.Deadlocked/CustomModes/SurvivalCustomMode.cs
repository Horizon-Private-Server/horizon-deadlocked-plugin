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
    public class SurvivalCustomMode : BaseCustomMode
    {
        public static readonly string[] DifficultyNames = new string[]
        {
            "Couch Potato",
            "Contestant",
            "Gladiator",
            "Hero",
            "Exterminator"
        };

        public static readonly double[] DifficultyXpMultipliers = new double[]
        {
            0.1,
            0.3,
            0.6,
            1.0,
            2.0
        };

        public override CustomModeId Id => CustomModeId.CMODE_ID_SURVIVAL;
        public override string Name => "Survival";

        public override Task<int?> GetRank(ClientObject client)
        {
            return Task.FromResult((int?)client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_RANK]);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            args.Reject = true; // reject all
            return Task.CompletedTask;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var difficulty = metadata.GameConfig.Survival_Difficulty;
            var round = metadata.GameState.RoundNumber;

            var info = $"Difficulty: {DifficultyNames[difficulty]}";
            if (round > 0)
                info += $"\nRound: {round}";

            return Task.FromResult(info);
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
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
            if (gameData.CustomGameData == null)
                return false;

            // game must have survivor on
            if ((game.GenericField7 & 0x80) == 0)
                return false;

            // game must have unlimited ammo off
            if ((game.GenericField7 & 0x200) != 0)
                return false;

            return true;
        }

        protected override Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as SurvivalCustomData;
            var gameData = args.GameData;
            var game = args.Game;

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                var points = customGameData.Points[gameIdx] * DifficultyXpMultipliers[args.Metadata.GameConfig.Survival_Difficulty];
                var xp = (int)Math.Max(0, Math.Min(0x7FFFFFFF, args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_XP] + points));
                var rating = (int)Math.Max(100, Math.Min(10000, 100 + Math.Sqrt(xp)));

                if (!player.Left)
                {
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_RANK] = rating;
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_XP] += xp;
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                }

                // general
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_KILLS] += customGameData.Kills[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DEATHS] += gameData.Data.Deaths[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_REVIVES] += customGameData.Revives[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_REVIVED] += customGameData.TimesRevived[gameIdx];

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
                if (!player.Left)
                {
                    int? statIndex = null;
                    switch (args.Metadata.GameConfig.Survival_Difficulty)
                    {
                        case 0:
                            {
                                statIndex = (int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_D1_HIGH_SCORE;
                                break;
                            }
                        case 1:
                            {
                                statIndex = (int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_D2_HIGH_SCORE;
                                break;
                            }
                        case 2:
                            {
                                statIndex = (int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_D3_HIGH_SCORE;
                                break;
                            }
                        case 3:
                            {
                                statIndex = (int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_D4_HIGH_SCORE;
                                break;
                            }
                        case 4:
                            {
                                statIndex = (int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_D5_HIGH_SCORE;
                                break;
                            }
                    }

                    if (statIndex.HasValue)
                    {
                        args.PlayerCustomStats[accountId][statIndex.Value] = Math.Max(args.PlayerCustomStats[accountId][statIndex.Value], customGameData.Rounds);
                    }
                }
            }

            return Task.CompletedTask;
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

        public void Deserialize(BinaryReader reader)
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
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported survival data version {Version}");
                        break;
                    }
            }
        }
    }
}
