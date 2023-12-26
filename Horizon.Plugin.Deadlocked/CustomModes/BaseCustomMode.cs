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
    public abstract class BaseCustomMode
    {
        protected class CustomModeUpdateStatsArgs
        {
            public Server.Medius.Models.Game Game { get; set; }
            public GameMetadata Metadata { get; set; }
            public GameData GameData { get; set; }
            public Dictionary<int, int[]> PlayerCustomStats { get; set; }
            public List<StatsGamePlayer> Players { get; set; }
        }


        public abstract CustomModeId Id { get; }
        public abstract string Name { get; }

        public virtual Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            args.Reject = true; // reject by default
            return Task.CompletedTask;
        }

        public virtual Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult<string>(null);
        }

        public abstract Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client);

        public virtual Task<string> GetNameOverride(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult(client.AccountName);
        }

        public abstract Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata);


        protected abstract bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData);

        protected abstract ICustomGameData CreateCustomGameData();

        protected abstract Task UpdateCustomStats(CustomModeUpdateStatsArgs args);

        public async Task<Dictionary<int, int[]>> OnGameEnd(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            if (!metadata.ReceivedGameData)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, $"OnGameEnd called before GameData was received");
                return null;
            }

            var gameData = new GameData();
            gameData.CustomGameData = CreateCustomGameData();
            using (var ms = new MemoryStream(metadata.GameData))
            {
                using (var reader = new MessageReader(ms))
                {
                    gameData.Deserialize(reader);
                }
            }

            // 
            var args = new CustomModeUpdateStatsArgs()
            {
                Game = game,
                Metadata = metadata,
                GameData = gameData,
                PlayerCustomStats = new Dictionary<int, int[]>(),
                Players = new List<StatsGamePlayer>()
            };

            // collect custom stats for each players
            var accountIdsAsStart = game.AccountIdsAtStart.Split(',').Select(x => int.TryParse(x, out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x.Value).ToArray();
            foreach (var accountId in accountIdsAsStart)
            {
                var gameIdx = Array.FindIndex(gameData.StartGameSettings.PlayerAccountIds, x => x == accountId);
                if (gameIdx < 0)
                    continue;

                // get custom wide stats
                var client = Server.Medius.Program.Manager.GetClientByAccountId(accountId);
                if (client == null)
                {
                    var account = await Server.Medius.Program.Database.GetAccountById(accountId);
                    args.PlayerCustomStats.Add(accountId, account.AccountCustomWideStats);
                }
                else
                {
                    args.PlayerCustomStats.Add(accountId, client.CustomWideStats);
                }

                // construct stats gameplayer
                args.Players.Add(new StatsGamePlayer()
                {
                    Index = gameIdx,
                    AccountId = accountId,
                    Team = gameData.StartGameSettings.PlayerTeams[gameIdx],
                    Left = gameData.EndGameSettings.PlayerClients[gameIdx] < 0
                });
            }

            // reject if dev rules enabled
            if (metadata.GameConfig.HasDevRule())
                return args.PlayerCustomStats;

            // 
            if (!GameAcceptStats(game, metadata, gameData))
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, $"Custom GameAcceptStats returned false for {game.Id} {game.Metadata}");
                return args.PlayerCustomStats;
            }

            // process stats
            await UpdateCustomStats(args);

            // post stat changes
            foreach (var kvp in args.PlayerCustomStats)
            {
                // update local client
                var client = Server.Medius.Program.Manager.GetClientByAccountId(kvp.Key);
                if (client != null)
                {
                    client.CustomWideStats = kvp.Value;
                }

                // send to db
                var account = await Server.Medius.Program.Database.PostAccountLadderCustomStats(new Server.Database.Models.StatPostDTO()
                {
                    AccountId = kvp.Key,
                    Stats = kvp.Value
                });
            }

            return args.PlayerCustomStats;
        }
    
        public virtual Task OnRecvCustomMessage(ClientObject client, int messageId, MessageReader reader)
        {
            return Task.CompletedTask;
        }

        public virtual sbyte GetModuleArg3(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return 0;
        }
    }
}
