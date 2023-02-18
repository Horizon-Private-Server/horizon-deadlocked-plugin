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

        private static readonly SurvivalConfig[] _configs = new SurvivalConfig[]
        {
            new SurvivalConfig(CustomMapId.CMAP_ID_SURVIVAL_MINING_FACILITY)
            {
                MapSize = 1.5f,
                BakedSpawnpoints = new List<SurvivalConfig.BakedSpawnpoint>()
                {
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.PlayerStart, 0, 328.6f, 544.8498f, 433.9998f, 0f, 0f, 0f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 525.73f, 537.75f, 429.64f, 0f, 0f, -1.570796f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 478.72f, 691.459f, 430.73f, 0f, 0f, -3.141592f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 478.9f, 508.54f, 430.73f, 0f, 0f, 0f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 339.205f, 562.895f, 431.67f, -0.0006243868f, 1.079918E-05f, -1.660341f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 525.73f, 661.89f, 429.64f, 0f, 0f, -1.570797f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.Upgrade, 0, 335.47f, 650.277f, 430.623f, -6.167562f, -7.500661E-09f, -1.570796f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 545.2199f, 513.7399f, 427.4603f, 0f, 0f, -3.951303f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 584.0499f, 537.26f, 427.5132f, 0f, 0f, -3.951303f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 569.03f, 578.3799f, 427.3436f, 0f, 0f, -2.904106f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 526.77f, 629.11f, 427.3436f, 0f, 0f, 0f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 531.7f, 599.54f, 427.3436f, 0f, 0f, -0.0726434f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 569.3099f, 617.9299f, 427.3436f, 0f, 0f, -4.359524f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 634.9299f, 663.7299f, 427.3435f, 0f, 0f, -1.811819f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 632.84f, 591.1099f, 427.3436f, 0f, 0f, -4.518756f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 576.21f, 599.5599f, 427.3436f, 0f, 0f, -0.3983178f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 634.3499f, 580.2599f, 427.3435f, 0f, 0f, -1.949298f),
                    new SurvivalConfig.BakedSpawnpoint(SurvivalConfig.BakedSpawnpoint.TypeId.MysteryBox, 0, 622.39f, 615.9099f, 427.4686f, 0f, 0f, -3.729555f),
                }
            },
            new SurvivalConfig(CustomMapId.CMAP_ID_SURVIVAL_MARCADIA) { MapSize = 1.0f },
            new SurvivalConfig(CustomMapId.CMAP_ID_SURVIVAL_VELDIN) { MapSize = 1.0f },
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

            // game must have survivor on
            if (gameData.GameOptions.GameFlags[0x1E] == 0)
                return false;

            // game must have unlimited ammo off
            if (gameData.GameOptions.GameFlags[0x20] != 0)
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
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_XP] = xp;
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

    public class SurvivalConfig
    {
        public const uint Offset = 0x18;

        public class BakedSpawnpoint
        {
            public enum TypeId
            {
                None = 0,
                Upgrade = 1,
                PlayerStart = 2,
                MysteryBox = 3,
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
        public float MapSize { get; set; }
        public List<BakedSpawnpoint> BakedSpawnpoints { get; set; }

        public SurvivalConfig(CustomMapId mapId)
        {
            CustomMapId = mapId;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.BaseStream.Seek(Offset, SeekOrigin.Begin);

            writer.Write(MapSize);

            // write 24 baked spawnpoints
            for (int i = 0; i < 24; ++i)
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
