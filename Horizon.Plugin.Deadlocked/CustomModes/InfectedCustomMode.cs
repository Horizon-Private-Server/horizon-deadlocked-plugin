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
    public class InfectedCustomMode : BaseCustomMode
    {
        public override CustomModeId Id => CustomModeId.CMODE_ID_INFECTED;
        public override string Name => "Infected";

        public override Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult((int?)client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_RANK]);
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
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/infected-11184.bin"))));
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new InfectedCustomData();
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

            // need either one or two teams
            if (teamIds.Count > 2)
                return false;

            // must be a ffa
            if (teamCounts.Max() > 1)
                return false;

            // game must last at least 30 seconds
            if (!game.UtcTimeEnded.HasValue || !game.UtcTimeStarted.HasValue || (game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds < 30)
                return false;

            // game must have a timelimit
            if ((game.GenericField7 & 0x38000000) == 0)
                return false;

            // game must not have a kill limit
            if (game.GenericField3 != 0)
                return false;

            // game must have custom data
            if (gameData.CustomGameData == null)
                return false;

            return true;
        }

        protected override Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as InfectedCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            var winningTeam = 1;

            // set rank and score of players
            foreach (var player in args.Players)
            {
                player.Rank = args.PlayerCustomStats[player.AccountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_RANK];
                player.Score = 0;
                player.Team = customGameData.IsInfected[player.Index] ? 1 : 0;
            }

            // survivors win if any survivors left
            if (args.Players.Any(p => !customGameData.IsInfected[p.Index] && !p.Left))
                winningTeam = 0;

            // rate game
            Stats.RateTeamGame(args.Players, new int[] { winningTeam }, wager: 0.3f, rewardFlatness: 0.5f, drawPenalty: 0.2f);

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                // give more points to first infected if they won
                if (player.Won && customGameData.IsFirstInfected[gameIdx])
                {
                    player.Rank = Math.Max(100, Math.Min(10000, player.Rank + 50));
                }

                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_RANK] = player.Rank;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_KILLS] += gameData.Data.Kills[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_DEATHS] += gameData.Data.Deaths[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_WINS] += player.Won ? 1 : 0;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_WINS_AS_FIRST_INFECTED] += (player.Won && customGameData.IsFirstInfected[gameIdx]) ? 1 : 0;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_WINS_AS_SURVIVOR] += (player.Won && player.Team == 0) ? 1 : 0;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_LOSSES] += player.Won ? 0 : 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_INFECTIONS] += customGameData.Infections[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_TIMES_INFECTED] += customGameData.IsInfected[gameIdx] ? 1 : 0;

                if (!player.Left)
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_INFECTED_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
            }

            return Task.CompletedTask;
        }
    }

    public class InfectedCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int[] Infections { get; set; }
        public bool[] IsInfected { get; set; }
        public bool[] IsFirstInfected { get; set; }

        public void Deserialize(MessageReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            switch (Version)
            {
                case 1:
                    {
                        Infections = reader.ReadArray<int>(10);
                        IsInfected = reader.ReadArray<bool>(10);
                        IsFirstInfected = reader.ReadArray<bool>(10);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported infected data version {Version}");
                        break;
                    }
            }
        }
    }
}
