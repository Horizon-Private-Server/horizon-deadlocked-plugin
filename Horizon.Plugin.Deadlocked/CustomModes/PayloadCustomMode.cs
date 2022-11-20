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
    public class PayloadCustomMode : BaseCustomMode
    {
        private static readonly Random _rng = new Random();
        private static readonly PayloadConfig[] _configs = new PayloadConfig[]
        {
            // vanilla maps
            new PayloadConfig(MapId.BATTLEDOME, "bin/payload/battledome_0.bin"),
            new PayloadConfig(MapId.CATACROM, "bin/payload/catacrom_0.bin"),
            new PayloadConfig(MapId.SARATHOS, "bin/payload/sarathos_0.bin"),
            new PayloadConfig(MapId.DARK_CATHEDRAL, "bin/payload/dark_cathedral_0.bin"),
            new PayloadConfig(MapId.SHAAR, "bin/payload/shaar_0.bin"),
            new PayloadConfig(MapId.VALIX, "bin/payload/valix_0.bin"),
            new PayloadConfig(MapId.MINING_FACILITY, "bin/payload/mining_facility_0.bin"),
            new PayloadConfig(MapId.TORVAL, "bin/payload/torval_0.bin"),
            new PayloadConfig(MapId.TEMPUS, "bin/payload/tempus_0.bin"),
            new PayloadConfig(MapId.MARAXUS, "bin/payload/maraxus_0.bin"),
            new PayloadConfig(MapId.GHOST_STATION, "bin/payload/ghost_station_0.bin"),

            // custom maps
            new PayloadConfig(CustomMapId.CMAP_ID_SARATHOS_SP, "bin/payload/sarathos_sp_0.bin"),
            new PayloadConfig(CustomMapId.CMAP_ID_DESERT_PRISON, "bin/payload/desert_prison_0.bin"),
            new PayloadConfig(CustomMapId.CMAP_ID_DESERT_PRISON, "bin/payload/desert_prison_1.bin"),
            new PayloadConfig(CustomMapId.CMAP_ID_DESERT_PRISON, "bin/payload/desert_prison_2.bin"),
            new PayloadConfig(CustomMapId.CMAP_ID_SNIVELAK, "bin/payload/snivelak_0.bin"),
        };

        public override CustomModeId Id => CustomModeId.CMODE_ID_PAYLOAD;
        public override string Name => "Payload";

        public override Task<int?> GetRank(ClientObject client)
        {
            return Task.FromResult((int?)client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_RANK]);
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

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var payload = new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/payload-11184.bin")));

            // insert config into payload
            List<PayloadConfig> configs = null;
            if (metadata.GameConfig.MapOverride != 0)
                configs = _configs.Where(x => x.CustomMapId == (CustomMapId)metadata.GameConfig.MapOverride).ToList();
            else
                configs = _configs.Where(x => x.MapId == (MapId)game.GameLevel).ToList();

            if (configs.Count > 0)
            {
                var config = configs[_rng.Next(configs.Count)];
                    
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
            return new PayloadCustomData();
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

            // need exactly two teams
            if (teamIds.Count != 2)
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
            var customGameData = args.GameData.CustomGameData as PayloadCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            int[] winningTeams = null;
            List<int> teamIds = new List<int>();

            // set rank and score of players
            foreach (var player in args.Players)
            {
                player.Rank = args.PlayerCustomStats[player.AccountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_RANK];
                player.Score = customGameData.Points[player.Index];

                // add team to list
                if (!teamIds.Contains(player.Team))
                    teamIds.Add(player.Team);
            }

            // 
            var teamA = teamIds[0];
            var teamB = teamIds[1];

            if (customGameData.TeamScore[teamA] > customGameData.TeamScore[teamB])
            {
                winningTeams = new int[] { teamA };
            }
            else if (customGameData.TeamScore[teamB] > customGameData.TeamScore[teamA])
            {
                winningTeams = new int[] { teamB };
            }

            // rate game
            Stats.RateTeamGame(args.Players, winningTeams, wager: 0.3f, rewardFlatness: 0.5f, drawPenalty: 0.2f);

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_RANK] = player.Rank;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_KILLS] += gameData.Data.Kills[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_DEATHS] += gameData.Data.Deaths[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_WINS] += player.Won ? 1 : 0;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_LOSSES] += player.Won ? 0 : 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_POINTS] += customGameData.Points[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_KILLS_WHILE_HOT] += customGameData.KillsWhileHot[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_KILLS_ON_HOT] += customGameData.KillsOnHotPlayers[gameIdx];

                if (!player.Left)
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_PAYLOAD_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
            }

            return Task.CompletedTask;
        }
    }

    public class PayloadCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int Rounds { get; set; }
        public int[] Points { get; set; }
        public int[] KillsOnHotPlayers { get; set; }
        public int[] KillsWhileHot { get; set; }
        public int[] TeamScore { get; set; }
        public int[] TeamRoundTime { get; set; }

        public void Deserialize(BinaryReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            switch (Version)
            {
                case 1:
                    {
                        Rounds = reader.ReadInt32();
                        Points = reader.ReadArray<int>(10);
                        KillsOnHotPlayers = reader.ReadArray<int>(10);
                        KillsWhileHot = reader.ReadArray<int>(10);
                        TeamScore = new int[10];
                        TeamRoundTime = new int[10];
                        break;
                    }
                case 2:
                    {
                        Rounds = reader.ReadInt32();
                        Points = reader.ReadArray<int>(10);
                        KillsOnHotPlayers = reader.ReadArray<int>(10);
                        KillsWhileHot = reader.ReadArray<int>(10);
                        TeamScore = reader.ReadArray<int>(10);
                        TeamRoundTime = reader.ReadArray<int>(10);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported payload data version {Version}");
                        break;
                    }
            }
        }
    }

    public class PayloadConfig
    {
        public const uint Offset = 0x20;

        public MapId? MapId { get; }
        public CustomMapId? CustomMapId { get; }
        public string Filepath { get; }

        public PayloadConfig(CustomMapId customMapId, string path)
        {
            CustomMapId = customMapId;
            Filepath = path;
        }

        public PayloadConfig(MapId mapId, string path)
        {
            MapId = mapId;
            Filepath = path;
        }

        public void Serialize(BinaryWriter writer)
        {
            var path = Path.Combine(Plugin.WorkingDirectory, Filepath);
            if (!File.Exists(path))
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, $"Unable to find payload config at {path}");
                return;
            }

            var bytes = File.ReadAllBytes(path);
            writer.BaseStream.Seek(Offset, SeekOrigin.Begin);
            writer.Write(bytes);
        }
    }
}
