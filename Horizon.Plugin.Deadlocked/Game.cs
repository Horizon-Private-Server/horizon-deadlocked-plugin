using Horizon.Plugin.Deadlocked.Messages;
using Newtonsoft.Json;
using RT.Common;
using Server.Common;
using Server.Common.Stream;
using Server.Medius;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public static class Game
    {
        private static readonly Dictionary<int, GameMetadata> _metadatas = new Dictionary<int, GameMetadata>();

        public static async Task BroadcastGameConfig(Server.Medius.Models.Game game, bool skipSendChatMessage = false)
        {
            var metadata = await GetGameMetadata(game);

            // send custom ranks
            _ = BroadcastCustomModeRanks(game);
            _ = BroadcastNameOverrides(game);

            // send 
            var tasks = game.Clients.Select(async (gameClient) =>
            {
                // reset map version
                var extraInfo = Player.GetPlayerExtraInfo(gameClient.Client.AccountId);
                if (extraInfo != null)
                    extraInfo.CurrentMapVersion = 0;

                // send config
                gameClient.Client.Queue(new SetGameConfigResponseMessage()
                {
                    Config = metadata.GameConfig
                });

                // send chat message
                if (!skipSendChatMessage)
                {
                    game.ChatChannel.SendSystemMessage(gameClient.Client, "AThe host has updated the game settings.");
                }

                // send custom map override
                await Maps.SendMapOverride(gameClient.Client, metadata.CustomMapConfig);
            });

            await Task.WhenAll(tasks);
        }

        public static async Task BroadcastNameOverrides(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);
            if (metadata == null)
                return;

            // send custom ranks
            var msg = new SetNameOverridesMessage();
            var customMode = Modes.FindCustomModeById(metadata.GetRealCustomModeId());
            if (customMode != null)
            {
                for (int i = 0; i < game.Clients.Count; ++i)
                {
                    var nameOverride = await customMode.GetNameOverride(game, metadata, game.Clients[i].Client);
                    msg.Names[i] = String.IsNullOrEmpty(nameOverride) ? game.Clients[i].Client.AccountName : nameOverride;
                    msg.AccountIds[i] = game.Clients[i].Client.AccountId;
                }
            };

            foreach (var gameClient in game.Clients)
                gameClient?.Client?.Queue(msg);
        }

        public static async Task BroadcastCustomModeRanks(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);
            if (metadata == null)
                return;

            // send custom ranks
            var setRanksMessage = new SetPlayerRanksMessage();
            var customMode = Modes.FindCustomModeById(metadata.GetRealCustomModeId());
            if (customMode != null)
            {
                for (int i = 0; i < game.Clients.Count; ++i)
                {
                    // get rank
                    // if rank is null then mode doesn't have its own rank
                    // so just send disabled message
                    var rank = await customMode.GetRank(game, metadata, game.Clients[i].Client);
                    if (!rank.HasValue)
                    {
                        setRanksMessage.Enabled = false;
                        break;
                    }

                    setRanksMessage.AccountIds[i] = game.Clients[i].Client.AccountId;
                    setRanksMessage.Ranks[i] = rank ?? 0;
                    setRanksMessage.Enabled = true;
                }
            };

            foreach (var gameClient in game.Clients)
                gameClient?.Client?.Queue(setRanksMessage);
        }

        public static async Task BroadcastMapOverride(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);

            // send custom map override
            foreach (var gameClient in game.Clients)
            {
                await Maps.SendMapOverride(gameClient.Client, metadata.CustomMapConfig);
            }
        }

        public static async Task SendMapOverride(ClientObject client)
        {
            var game = client.CurrentGame;
            if (game == null)
                return;

            var metadata = await GetGameMetadata(game);

            // send custom map override
            await Maps.SendMapOverride(client, metadata.CustomMapConfig);
        }

        public static async Task PlayerJoined(ClientObject client, Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);

            // send map override on join
            await Maps.SendMapOverride(client, metadata.CustomMapConfig);

            // send global maps version
            await Maps.SendMapVersion(client);

            // send game config if not host
            if (game.Host != client)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);

                    client.Queue(new SetGameConfigResponseMessage()
                    {
                        Config = metadata.GameConfig
                    });

                    // resend custom mode stats
                    await BroadcastCustomModeRanks(game);
                    await BroadcastNameOverrides(game);
                });
            }

            // broadcast player's patch config
            await Player.BroadcastPatchConfigToGameLobby(client);
        }

        public static async Task PlayerLeft(ClientObject client, Server.Medius.Models.Game game)
        {
            // reset timeouts
            client.TimeoutSeconds = Program.GetAppSettingsOrDefault(client.ApplicationId).ClientTimeoutSeconds;
            client.LongTimeoutSeconds = Program.GetAppSettingsOrDefault(client.ApplicationId).ClientLongTimeoutSeconds;
        }

        public static async Task OnPlayerPostWideStats(OnPlayerWideStatsArgs args)
        {
            var game = args.Game;
            var client = args.Player;

            if (game == null)
                return;

            // must have game metadata
            if (!HasGameMetadata(game))
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"{client.AccountName} sent wide stats without any gamedata");
                return;
            }

            var metadata = await GetGameMetadata(game);

            // reject if freecam
            if (metadata.GameConfig.HasDevRule())
            {
                args.Reject = true;
            }

            // pass to gamemode
            var mode = Modes.FindCustomModeById(metadata.GetRealCustomModeId());
            if (mode != null)
            {
                await mode.OnClientPostWideStats(args);
            }

            // store new custom stats in PostStats metadata
            if (game.WorldStatus == MediusWorldStatus.WorldActive || game.WorldStatus == MediusWorldStatus.WorldClosed)
            {
                if (!args.Reject)
                {
                    if (args.IsClan)
                    {
                        if (args.Player.ClanId.HasValue)
                        {
                            // make sure we have a copy of the pre wide stats!
                            if (!metadata.PreWideStats.Clans.ContainsKey(args.Player.ClanId.Value))
                            {
                                var clan = await Server.Medius.Program.Database.GetClanById(args.Player.ClanId.Value);
                                if (clan != null)
                                {
                                    metadata.PreWideStats.Clans.TryAdd(args.Player.ClanId.Value, clan.ClanWideStats.ToArray());
                                    if (!metadata.PreCustomWideStats.Clans.ContainsKey(args.Player.ClanId.Value))
                                        metadata.PreCustomWideStats.Clans.TryAdd(args.Player.ClanId.Value, clan.ClanCustomWideStats.ToArray());
                                }
                            }

                            metadata.PostWideStats.Clans[args.Player.ClanId.Value] = args.WideStats.ToArray();
                        }
                    }
                    else
                    {
                        // make sure we have a copy of the pre wide stats!
                        if (!metadata.PreWideStats.Players.ContainsKey(args.Player.AccountId))
                            metadata.PreWideStats.Players.TryAdd(args.Player.AccountId, args.Player.WideStats.ToArray());
                        if (!metadata.PreCustomWideStats.Players.ContainsKey(args.Player.AccountId))
                            metadata.PreCustomWideStats.Players.TryAdd(args.Player.AccountId, args.Player.CustomWideStats.ToArray());

                        // store post wide stats
                        metadata.PostWideStats.Players[args.Player.AccountId] = args.WideStats.ToArray();
                    }
                }
            }

            Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"{client.AccountName} sent wide stats.. rejected={args.Reject}");
        }

        public static async Task<bool> SetGameState(Server.Medius.Models.Game game, PackedGameState packedGameState)
        {
            var metadata = await GetGameMetadata(game);

            // update game state
            metadata.GameState = new GameState(packedGameState, game);
            metadata.GameInfo = await GetGameInfo(game, metadata);

            // send to database
            return await SetGameMetadata(game, metadata);
        }

        public static async Task<bool> SetGameConfig(Server.Medius.Models.Game game, GameConfig config, GameCustomMapConfig mapConfig)
        {
            var metadata = await GetGameMetadata(game);

            // if no change, return false
            if (metadata.GameConfig.SameAs(config) && metadata.CustomMapConfig.SameAs(mapConfig))
                return false;

            // update
            metadata.GameConfig = config;
            metadata.CustomMapConfig = mapConfig ?? new GameCustomMapConfig();

            // force mode to maps custom mode
            // or 0 if map mode is selected on unsupported map
            if (mapConfig != null && mapConfig.ForcedModeId != 0)
                config.GamemodeOverride = (sbyte)mapConfig.ForcedModeId;
            else if (config.GamemodeOverride < 0)
                config.GamemodeOverride = 0;

            // update other metadata
            metadata.CustomMap = String.IsNullOrEmpty(metadata.CustomMapConfig.Name) ? null : metadata.CustomMapConfig.Name;
            metadata.CustomGameMode = Modes.FindCustomModeById(metadata.GetRealCustomModeId())?.Name;
            metadata.Weather = metadata.GameConfig.WeatherOverride.ToString();
            metadata.GameInfo = await GetGameInfo(game, metadata);


            // send to database
            return await SetGameMetadata(game, metadata);
        }

        public static bool HasGameMetadata(Server.Medius.Models.Game game)
        {
            return _metadatas.ContainsKey(game.Id);
        }

        public static Task<GameMetadata> GetGameMetadata(Server.Medius.Models.Game game)
        {
            // get from cache
            if (_metadatas.TryGetValue(game.Id, out var metadata))
                return Task.FromResult(metadata);

            // try parse from game
            try { metadata = JsonConvert.DeserializeObject<GameMetadata>(game.Metadata); } catch (Exception) { }
            if (metadata == null)
                metadata = new GameMetadata() { Location = game.DMEServer?.Location ?? 0 };

            // add to cache
            _metadatas.Add(game.Id, metadata);

            return Task.FromResult(metadata);
        }

        public static async Task UpdateGameData(ClientObject client, Server.Medius.Models.Game game, SetGameDataRequestMessage request)
        {
            if (game == null)
                return;

            var metadata = await GetGameMetadata(game);
            if (metadata.TempGameData == null)
                metadata.TempGameData = new byte[1024 * 6];

            // copy
            Array.Copy(request.Payload, 0, metadata.TempGameData, request.Offset, request.Payload.Length);

            // 
            if (request.EndOfList)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"GAME STATS RECEIVED from {client}");

                metadata.GameData = new byte[request.Offset + request.Payload.Length];
                Array.Copy(metadata.TempGameData, 0, metadata.GameData, 0, metadata.GameData.Length);
                metadata.TempGameData = null;
                metadata.ReceivedGameData = true;
            }

            // update
            await SetGameMetadata(game, metadata);
        }

        public static async Task SendGameMode(Server.Medius.Models.Game game, ClientObject targetClient = null)
        {
            var metadata = await GetGameMetadata(game);

            // construct payloads to send to client
            var payloads = new List<Payload>();

            // add remove module entry
            payloads.Add(new Payload(0x000CF000, new PatchModuleEntry()
            {
                Type = PatchModuleEntryType.DISABLED,
                ModeId = 0,
                Arg2 = 0,
                Arg3 = 0,
            }.Serialize()));

            // parse gamemode
            var mode = Modes.FindCustomModeById(metadata.GetRealCustomModeId());

            if (mode != null)
            {
                var modePayload = await mode.GetPayload(game, metadata);
                if (modePayload != null)
                {
                    // add mode payload
                    payloads.Add(modePayload);

                    // add mode module entry
                    payloads.Add(new Payload(0x000CF000, new PatchModuleEntry()
                    {
                        Type = PatchModuleEntryType.RUN_ONCE_GAME,
                        ModeId = (sbyte)mode.Id,
                        Arg2 = mode.GetModuleArg2(game, metadata),
                        Arg3 = mode.GetModuleArg3(game, metadata),
                        GameEntrypoint = modePayload.Address,
                        LobbyEntrypoint = modePayload.Address + 8,
                        LoadEntrypoint = modePayload.Address + 16,
                    }.Serialize()));
                }
            }

            // send payloads to all clients
            if (payloads.Count > 0)
                foreach (var gameClient in game.Clients)
                    if (targetClient == null || gameClient.Client == targetClient)
                        await Downloader.InitiateDataDownload(gameClient.Client, 201, payloads);
        }

        public static async Task OnGameStarted(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);

            var mode = Modes.FindCustomModeById(metadata.GetRealCustomModeId());
            if (mode != null)
                await mode.OnGameStart(game, metadata);

            // send map override to all clients
            await BroadcastGameConfig(game, true);

            // store player wide stats
            var tasks = game.Clients.Select(async (gameClient) =>
            {
                metadata.PreWideStats.Players.TryAdd(gameClient.Client.AccountId, gameClient.Client.WideStats.ToArray());
                metadata.PreCustomWideStats.Players.TryAdd(gameClient.Client.AccountId, gameClient.Client.CustomWideStats.ToArray());
                metadata.PostWideStats.Players.TryAdd(gameClient.Client.AccountId, gameClient.Client.WideStats.ToArray());
                metadata.PostCustomWideStats.Players.TryAdd(gameClient.Client.AccountId, gameClient.Client.CustomWideStats.ToArray());

                if (gameClient.Client.ClanId.HasValue)
                {
                    var clanId = gameClient.Client.ClanId.Value;
                    if (!metadata.PreWideStats.Clans.ContainsKey(clanId))
                    {
                        var clan = await Server.Medius.Program.Database.GetClanById(clanId);
                        if (clan != null)
                        {
                            metadata.PreWideStats.Clans.TryAdd(clanId, clan.ClanWideStats.ToArray());
                            metadata.PreCustomWideStats.Clans.TryAdd(clanId, clan.ClanCustomWideStats.ToArray());
                            metadata.PostWideStats.Clans.TryAdd(clanId, clan.ClanWideStats.ToArray());
                            metadata.PostCustomWideStats.Clans.TryAdd(clanId, clan.ClanCustomWideStats.ToArray());
                        }
                    }
                }
            });

            await Task.WhenAll(tasks);
            await SetGameMetadata(game, metadata);
        }

        public static async Task OnGameComplete(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);
            Dictionary<int, int[]> playerCustomStats = null;

            if (!metadata.ProcessedComplete)
            {
                try
                {
                    // pass to gamemode
                    var mode = Modes.FindCustomModeById(metadata.GetRealCustomModeId());
                    if (mode != null)
                        playerCustomStats = await mode.OnGameEnd(game, metadata);

                    // store new custom stats in PostStats metadata
                    if (playerCustomStats != null)
                    {
                        foreach (var kvp in playerCustomStats)
                        {
                            metadata.PostCustomWideStats.Players[kvp.Key] = kvp.Value.ToArray();
                        }
                    }

#warning TODO: Add support for custom clan stats
                }
                catch (Exception ex)
                {
                    Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, $"{ex}");
                }

                metadata.ProcessedComplete = true;
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, "GAME COMPLETED");
            }

            // send last metadata to server
            await SetGameMetadata(game, metadata);
        }

        public static async Task OnGameEnded(Server.Medius.Models.Game game)
        {

        }

        public static async Task OnGameDestroyed(Server.Medius.Models.Game game)
        {
            // pass to complete
            await OnGameComplete(game);

            Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, "GAME DESTROYED");

            // remove from cache
            _metadatas.Remove(game.Id);
        }

        private static async Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            string gameInfo = null;

            // let custom game mode override the gameinfo string
            var mode = Modes.FindCustomModeById(metadata.GetRealCustomModeId());
            if (mode != null)
            {
                gameInfo = await mode.GetGameInfo(game, metadata);
            }

            // default game info
            if (String.IsNullOrEmpty(gameInfo))
            {
                var scoreToWin = game.GenericField3;
                var timelimit = (game.GenericField7 >> 27) & 7;
                var isLockdown = (game.GenericField7 & (1 << 12)) != 0;
                var isHomenodes = (game.GenericField7 & (1 << 6)) != 0;
                string objectiveLabel = null;

                switch (game.RulesSet)
                {
                    case 0: // cq
                        {
                            objectiveLabel = "Bolts";
                            break;
                        }
                    case 1: // ctf
                        {
                            objectiveLabel = "Flags";
                            break;
                        }
                    case 2: // dm
                    case 4: // juggy
                        {
                            objectiveLabel = "Kills";
                            break;
                        }
                    case 3: // koth
                        {
                            objectiveLabel = "Hill Time";
                            break;
                        }
                }

                // timelimit if not cq
                if (game.RulesSet != 0)
                {
                    var time = $"{timelimit * 5} minutes";
                    if (timelimit == 0)
                        time = "None";

                    gameInfo += $"\nTimelimit: {time}";
                }

                // cq options
                if (game.RulesSet == 0 && (isLockdown || isHomenodes))
                {
                    objectiveLabel = null;
                    gameInfo += $"\nConquest Type: {(isLockdown ? "Lockdown" : "Homenodes")}";
                }

                // objective
                if (!String.IsNullOrEmpty(objectiveLabel))
                {
                    var score = $"{scoreToWin}";
                    if (scoreToWin == 0)
                        score = "None";

                    gameInfo += $"\n{objectiveLabel} to win: {score}";
                }
            }

            return gameInfo?.Trim()?.Trim('\n');
        }

        private static async Task<bool> SetGameMetadata(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            // add/update cache
            if (!_metadatas.ContainsKey(game.Id))
                _metadatas.Add(game.Id, metadata);
            else
                _metadatas[game.Id] = metadata;

            // save metadata to game
            game.Metadata = JsonConvert.SerializeObject(metadata);

            // send to db
            var result = await Server.Medius.Program.Database.UpdateGameMetadata(game.Id, game.Metadata);
            if (!result)
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unable to post game metadata {game.Id}: {game.Metadata}");

            return result;
        }
    }


    public class GameMetadata
    {
        public string CustomGameMode { get; set; }
        public string CustomMap { get; set; }
        public string Weather { get; set; }
        public string GameInfo { get; set; }
        public int Location { get; set; }
        public GameConfig GameConfig { get; set; } = new GameConfig();
        public GameCustomMapConfig CustomMapConfig { get; set; } = new GameCustomMapConfig();
        public GameState GameState { get; set; } = new GameState();
        public byte[] GameData { get; set; }
        public bool ReceivedGameData { get; set; }
        public GameStats PreWideStats { get; set; } = new GameStats();
        public GameStats PostWideStats { get; set; } = new GameStats();
        public GameStats PreCustomWideStats { get; set; } = new GameStats();
        public GameStats PostCustomWideStats { get; set; } = new GameStats();
        public bool ProcessedComplete { get; set; } = false;


        [NotMapped, JsonIgnore]
        public byte[] TempGameData { get; set; }

        public CustomModeId GetRealCustomModeId()
        {
            if (CustomMapConfig.HasMap() && CustomMapConfig.ForcedModeId != 0)
                return (CustomModeId)CustomMapConfig.ForcedModeId;

            return (CustomModeId)GameConfig.GamemodeOverride;
        }
    }

    public class GameStats
    {
        public ConcurrentDictionary<int, int[]> Players { get; set; } = new ConcurrentDictionary<int, int[]>();
        public ConcurrentDictionary<int, int[]> Clans { get; set; } = new ConcurrentDictionary<int, int[]>();
    }

    public class PackedGameState
    {
        public bool TeamsEnabled { get; set; }
        public int RoundNumber { get; set; }
        public int[] TeamScores { get; set; }
        public sbyte[] ClientIds { get; set; }
        public sbyte[] Teams { get; set; }


        public void Deserialize(BinaryReader reader)
        {
            TeamsEnabled = reader.ReadBoolean();
            reader.ReadBytes(1); // padding
            var version = reader.ReadInt16();
            RoundNumber = reader.ReadInt32();

            

            TeamScores = new int[10];
            for (int i = 0; i < 10; ++i)
            {
                if (version > 0)
                    TeamScores[i] = reader.ReadInt32();
                else
                    TeamScores[i] = reader.ReadInt16();
            }

            ClientIds = new sbyte[10];
            for (int i = 0; i < 10; ++i)
                ClientIds[i] = reader.ReadSByte();

            Teams = new sbyte[10];
            for (int i = 0; i < 10; ++i)
                Teams[i] = reader.ReadSByte();
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(TeamsEnabled ? 1 : 0);
            writer.Write(RoundNumber);

            for (int i = 0; i < 10; ++i)
            {
                if (TeamScores == null || i >= TeamScores.Length)
                    writer.Write((short)0);
                else
                    writer.Write(TeamScores[i]);
            }

            for (int i = 0; i < 10; ++i)
            {
                if (ClientIds == null || i >= ClientIds.Length)
                    writer.Write((sbyte)0);
                else
                    writer.Write(ClientIds[i]);
            }

            for (int i = 0; i < 10; ++i)
            {
                if (Teams == null || i >= Teams.Length)
                    writer.Write((sbyte)0);
                else
                    writer.Write(Teams[i]);
            }
        }
    }

    public class GameState
    {
        public bool TeamsEnabled { get; set; }
        public int RoundNumber { get; set; }
        public List<GameStateTeam> Teams { get; set; } = new List<GameStateTeam>();

        public GameState()
        {

        }

        public GameState(PackedGameState packedGameState, Server.Medius.Models.Game game)
        {
            TeamsEnabled = packedGameState.TeamsEnabled;
            RoundNumber = packedGameState.RoundNumber;

            var clientIdCounter = new int[10];

            for (int i = 0; i < 10; ++i)
            {
                var clientId = packedGameState.ClientIds[i];
                var teamId = packedGameState.Teams[i];
                var player = game.Clients.FirstOrDefault(x => x.DmeId == clientId);
                if (player != null && teamId >= 0 && teamId < 10)
                {
                    clientIdCounter[clientId]++;
                    var score = packedGameState.TeamScores[teamId];
                    var name = player.Client.AccountName;
                    if (clientIdCounter[clientId] > 1)
                        name += $" ~ {clientIdCounter[clientId]}";

                    var team = this.Teams.FirstOrDefault(x => x.Id == teamId);
                    if (team == null)
                    {
                        team = new GameStateTeam()
                        {
                            Id = teamId,
                            Name = Constants.Teams.ElementAtOrDefault(teamId) ?? $"Team {teamId}",
                            Players = new List<string>(new string[]{ name }),
                            Score = score
                        };
                        Teams.Add(team);
                    }
                    else
                    {
                        team.Players.Add(name);
                    }
                }
            }
        }
    }

    public class GameStateTeam
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Score { get; set; }
        public List<string> Players { get; set; } = new List<string>();
    }

    public class GameData
    {
        public NWGameData Data { get; set; }
        public NWGameSettings StartGameSettings { get; set; }
        public NWGameSettings EndGameSettings { get; set; }
        public NWGameOptions GameOptions { get; set; }
        public PackedGameState LastPackedGameState { get; set; }
        public ICustomGameData CustomGameData { get; set; }


        public void Deserialize(MessageReader reader)
        {
            int magic = reader.ReadInt32();
            int version = reader.ReadInt32();

            Data = new NWGameData();
            StartGameSettings = new NWGameSettings();
            EndGameSettings = new NWGameSettings();
            GameOptions = new NWGameOptions();
            LastPackedGameState = new PackedGameState();

            Data.Deserialize(reader);
            StartGameSettings.Deserialize(reader);
            EndGameSettings.Deserialize(reader);
            GameOptions.Deserialize(reader);
            LastPackedGameState.Deserialize(reader);

            // 
            CustomGameData?.Deserialize(reader);

        }
    }

    public interface ICustomGameData
    {
        void Deserialize(MessageReader reader);
    }

    public class NWGameData
    {
        public int TimeEnd { get; set; }
        public int TimeStart { get; set; }
        public int GameState { get; set; }
        public int NumTeams { get; set; }
        public int WinningTeam { get; set; }
        public int WinningPlayer { get; set; }
        public int BaseHoldTime { get; set; }
        public int FragDisplayCount { get; set; }
        public byte[] FragMsg { get; set; }
        public int GameEndReceived { get; set; }
        public int GameEndReason { get; set; }
        public int GameIsOver { get; set; }
        public int NumNodes { get; set; }
        public int NumStartPlayers { get; set; }
        public int NumStartTeams { get; set; }
        public int MyTotalSquats { get; set; }
        public int MyTotalTimeSquatted { get; set; }
        public int MyTotalGangSquats { get; set; }
        public int[] TeamCaptain { get; set; }

        // Player stats
        public short[][] WeaponKills { get; set; }
        public short[][] WeaponDeaths { get; set; }
        public short[][] WeaponShots { get; set; }
        public short[][] WeaponShotsHitBy { get; set; }
        public float[] VehicleTime { get; set; }
        public short[] VehicleWeaponKills { get; set; }
        public short[] VehicleWeaponDeaths { get; set; }
        public short[] VehicleRoadKills { get; set; }
        public short[] VehicleRoadDeaths { get; set; }
        public short[] VehicleShotsFired { get; set; }
        public short[] VehicleShotsHit { get; set; }
        public short[] Kills { get; set; }
        public short[] Deaths { get; set; }
        public short[] Suicides { get; set; }
        public short[] MultiKills { get; set; }
        public short[] SniperKills { get; set; }
        public short[] WrenchKills { get; set; }
        public byte[] ConquestNodesCaptured { get; set; }
        public byte[] ConquestNodeSaves { get; set; }
        public byte[] ConquestDefensiveKills { get; set; }
        public byte[] ConquestPoints { get; set; }
        public byte[] CtfFlagsCaptures { get; set; }
        public byte[] CtfFlagsSaved { get; set; }
        public float[] KingHillHoldTime { get; set; }
        public float[] InternalKingHillHoldTime { get; set; }
        public float[] JuggernautTime { get; set; }
        public short[] Squats { get; set; }
        public short[] VehicleSquats { get; set; }
        public short[] TicketScore { get; set; }

        // Team stats
        public short[] TeamTicketScore { get; set; }
        public byte[] TeamUpgradesLevel1 { get; set; }
        public byte[] TeamUpgradesLevel2 { get; set; }
        public byte[] TeamUpgradesLevel3 { get; set; }
        public float[] TeamCaptureTimer { get; set; }
        public short[] TeamCaptureTimerSettings { get; set; }
        public byte[] NumNodesOwned { get; set; }
        public float[] PercentNodesCaptured { get; set; }
        public float[] NodeHoldTime { get; set; }
        public byte[] FlagCaptureCounts { get; set; }

        public void Deserialize(MessageReader reader)
        {
            TimeEnd = reader.ReadInt32();
            TimeStart = reader.ReadInt32();
            GameState = reader.ReadInt32();
            NumTeams = reader.ReadInt32();
            WinningTeam = reader.ReadInt32();
            WinningPlayer = reader.ReadInt32();
            BaseHoldTime = reader.ReadInt32();
            FragDisplayCount = reader.ReadInt32();
            FragMsg = reader.ReadBytes(0x3C);
            GameEndReceived = reader.ReadInt32();
            GameEndReason = reader.ReadInt32();
            GameIsOver = reader.ReadInt32();
            NumNodes = reader.ReadInt32();
            NumStartPlayers = reader.ReadInt32();
            NumStartTeams = reader.ReadInt32();
            MyTotalSquats = reader.ReadInt32();
            MyTotalTimeSquatted = reader.ReadInt32();
            MyTotalGangSquats = reader.ReadInt32();

            TeamCaptain = new int[10];
            for (int i = 0; i < 10; ++i)
                TeamCaptain[i] = reader.ReadInt32();

            // player stats
            WeaponKills = new short[10][];
            for (int i = 0; i < 10; ++i)
                WeaponKills[i] = reader.ReadArray<short>(9);

            WeaponDeaths = new short[10][];
            for (int i = 0; i < 10; ++i)
                WeaponDeaths[i] = reader.ReadArray<short>(9);

            WeaponShots = new short[10][];
            for (int i = 0; i < 10; ++i)
                WeaponShots[i] = reader.ReadArray<short>(9);

            WeaponShotsHitBy = new short[10][];
            for (int i = 0; i < 10; ++i)
                WeaponShotsHitBy[i] = reader.ReadArray<short>(9);

            VehicleTime = reader.ReadArray<float>(10);
            VehicleWeaponKills = reader.ReadArray<short>(10);
            VehicleWeaponDeaths = reader.ReadArray<short>(10);
            VehicleRoadKills = reader.ReadArray<short>(10);
            VehicleRoadDeaths = reader.ReadArray<short>(10);
            VehicleShotsFired = reader.ReadArray<short>(10);
            VehicleShotsHit = reader.ReadArray<short>(10);
            Kills = reader.ReadArray<short>(10);
            Deaths = reader.ReadArray<short>(10);
            Suicides = reader.ReadArray<short>(10);
            MultiKills = reader.ReadArray<short>(10);
            SniperKills = reader.ReadArray<short>(10);
            WrenchKills = reader.ReadArray<short>(10);
            ConquestNodesCaptured = reader.ReadArray<byte>(10);
            ConquestNodeSaves = reader.ReadArray<byte>(10);
            ConquestDefensiveKills = reader.ReadArray<byte>(10);
            ConquestPoints = reader.ReadArray<byte>(10);
            CtfFlagsCaptures = reader.ReadArray<byte>(10);
            CtfFlagsSaved = reader.ReadArray<byte>(10);
            KingHillHoldTime = reader.ReadArray<float>(10);
            InternalKingHillHoldTime = reader.ReadArray<float>(10);
            JuggernautTime = reader.ReadArray<float>(10);
            Squats = reader.ReadArray<short>(10);
            VehicleSquats = reader.ReadArray<short>(10);
            TicketScore = reader.ReadArray<short>(10);
            TeamTicketScore = reader.ReadArray<short>(10);
            TeamUpgradesLevel1 = reader.ReadArray<byte>(10);
            TeamUpgradesLevel2 = reader.ReadArray<byte>(10);
            TeamUpgradesLevel3 = reader.ReadArray<byte>(10);
            reader.ReadBytes(2);
            TeamCaptureTimer = reader.ReadArray<float>(10);
            TeamCaptureTimerSettings = reader.ReadArray<short>(10);
            NumNodesOwned = reader.ReadArray<byte>(10);
            reader.ReadBytes(2);
            PercentNodesCaptured = reader.ReadArray<float>(10);
            NodeHoldTime = reader.ReadArray<float>(10);
            FlagCaptureCounts = reader.ReadArray<byte>(10);

            // ignore rest of tnw gamedata
            reader.ReadBytes(14);
        }

    }

    public class NWGameOptions
    {
        public byte[] GameFlags { get; set; }
        public int WeaponFlags { get; set; }
        public byte[] PointValues { get; set; }
        public byte[] UpgradeTimerMultipliers { get; set; }

        public void Deserialize(BinaryReader reader)
        {
            GameFlags = reader.ReadBytes(59);
            reader.ReadByte();
            WeaponFlags = reader.ReadInt32();
            PointValues = reader.ReadBytes(11);
            UpgradeTimerMultipliers = reader.ReadBytes(11);
            reader.ReadBytes(2);
        }
    }

    public class NWGameSettings
    {
        public string[] PlayerNames { get; set; }
        public string[] PlayerClanTags { get; set; }
        public sbyte[] PlayerSkins { get; set; }
        public sbyte[] PlayerTeams { get; set; }
        public sbyte[] PlayerClients { get; set; }
        public sbyte[] PlayerStates { get; set; }
        public sbyte[] PlayerTypes { get; set; }
        public float[] PlayerRanks { get; set; }
        public float[] PlayerRankDeviations { get; set; }
        public int[] PlayerAccountIds { get; set; }
        public int GameStartTime { get; set; }
        public int GameLoadStartTime { get; set; }
        public short GameLevel { get; set; }
        public sbyte PlayerCount { get; set; }
        public sbyte SuperCheat { get; set; }
        public sbyte PlayerCountAtStart { get; set; }
        public sbyte GameRules { get; set; }
        public sbyte GameType { get; set; }
        public short PlayerHeadset { get; set; }
        public bool PlayerNamesOn { get; set; }
        public sbyte[] TeamSpawnPointIds { get; set; }
        public uint SpawnSeed { get; set; }

        public void Deserialize(MessageReader reader)
        {
            PlayerNames = new string[10];
            for (int i = 0; i < 10; ++i)
                PlayerNames[i] = reader.ReadString(16);

            PlayerClanTags = new string[10];
            for (int i = 0; i < 10; ++i)
                PlayerClanTags[i] = reader.ReadString(8);

            PlayerSkins = new sbyte[10];
            for (int i = 0; i < 10; ++i)
                PlayerSkins[i] = reader.ReadSByte();

            PlayerTeams = new sbyte[10];
            for (int i = 0; i < 10; ++i)
                PlayerTeams[i] = reader.ReadSByte();

            PlayerClients = new sbyte[10];
            for (int i = 0; i < 10; ++i)
                PlayerClients[i] = reader.ReadSByte();

            PlayerStates = new sbyte[10];
            for (int i = 0; i < 10; ++i)
                PlayerStates[i] = reader.ReadSByte();

            PlayerTypes = new sbyte[10];
            for (int i = 0; i < 10; ++i)
                PlayerTypes[i] = reader.ReadSByte();

            reader.ReadBytes(2);
            PlayerRanks = new float[10];
            for (int i = 0; i < 10; ++i)
                PlayerRanks[i] = reader.ReadSingle();

            PlayerRankDeviations = new float[10];
            for (int i = 0; i < 10; ++i)
                PlayerRankDeviations[i] = reader.ReadSingle();

            PlayerAccountIds = new int[10];
            for (int i = 0; i < 10; ++i)
                PlayerAccountIds[i] = reader.ReadInt32();

            GameStartTime = reader.ReadInt32();
            GameLoadStartTime = reader.ReadInt32();
            GameLevel = reader.ReadInt16();
            PlayerCount = reader.ReadSByte();
            SuperCheat = reader.ReadSByte();
            PlayerCountAtStart = reader.ReadSByte();
            GameRules = reader.ReadSByte();
            GameType = reader.ReadSByte();
            reader.ReadByte();
            PlayerHeadset = reader.ReadInt16();
            PlayerNamesOn = reader.ReadBoolean();

            TeamSpawnPointIds = new sbyte[10];
            for (int i = 0; i < 10; ++i)
                TeamSpawnPointIds[i] = reader.ReadSByte();

            reader.ReadBytes(3);
            SpawnSeed = reader.ReadUInt32();
        }
    }

    public class GameCustomMapConfig
    {
        public string Filename { get; set; }
        public string Name { get; set; }
        public int Version { get; set; }
        public int CustomModeExtraDataMask { get; set; }
        public short ShrubMinRenderDistance { get; set; }
        public sbyte BaseMapId { get; set; }
        public sbyte ForcedModeId { get; set; }

        public bool HasMap() => Filename != null && Filename.Any();

        public byte[] Serialize()
        {
            byte[] output = new byte[64 + 32 + 4 + 4 + 2 + 1 + 1];
            using (var ms = new MemoryStream(output, true))
            {
                using (var writer = new MessageWriter(ms))
                {
                    writer.Write(Version);
                    writer.Write(CustomModeExtraDataMask);
                    writer.Write(ShrubMinRenderDistance);
                    writer.Write(BaseMapId);
                    writer.Write(ForcedModeId);
                    writer.Write(Name, 32);
                    writer.Write(Filename, 64);
                }
            }

            return output;
        }

        public void Deserialize(MessageReader reader)
        {
            Version = reader.ReadInt32();
            CustomModeExtraDataMask = reader.ReadInt32();
            ShrubMinRenderDistance = reader.ReadInt16();
            BaseMapId = reader.ReadSByte();
            ForcedModeId = reader.ReadSByte();
            Name = reader.ReadString(32);
            Filename = reader.ReadString(64);
        }

        public bool SameAs(GameCustomMapConfig other)
        {
            return Filename == other.Filename
                && Name == other.Name
                && Version == other.Version
                && CustomModeExtraDataMask == other.CustomModeExtraDataMask
                && ShrubMinRenderDistance == other.ShrubMinRenderDistance
                && BaseMapId == other.BaseMapId
                && ForcedModeId == other.ForcedModeId
                ;
        }
    }

    public class GameConfig
    {
        public sbyte GamemodeOverride { get; set; }
        public byte WeatherOverride { get; set; }
        public bool DisableWeaponPacks { get; set; }
        public byte V2s { get; set; }
        public bool MirrorWorld { get; set; }
        public byte DisableHealthBoxes { get; set; }
        public byte Vampire { get; set; }
        public bool HalfTime { get; set; }
        public bool Overtime { get; set; }
        public bool BetterHills { get; set; }
        public bool BetterFlags { get; set; }
        public bool Healthbars { get; set; }
        public bool DisableNames { get; set; }
        public bool DisableInvHitTimer { get; set; }
        public bool HideWeaponPickups { get; set; }
        public bool FusionShotsAlwaysHit { get; set; }
        public bool NoSniperHelpers { get; set; }
        public bool CqPersistentCapture { get; set; }
        public bool CqDisableTurrets { get; set; }
        public bool CqDisableUpgrades { get; set; }
        public bool NewPlayerSync { get; set; }
        public bool QuickChat { get; set; }
        public byte PlayerSize { get; set; }
        public bool RotatingWeapons { get; set; }
        public byte Headbutt { get; set; }
        public bool HeadbuttFriendlyFire { get; set; }
        public bool ChargebootForever { get; set; }
        public bool Freecam { get; set; }
        //public byte Survival_Difficulty { get; set; }
        public byte Payload_ContestMode { get; set; }
        public byte Training_Type { get; set; }
        public byte Training_Variation { get; set; }
        public byte Training_Aggression { get; set; }
        public byte Training_Opt3 { get; set; }
        public byte Hns_HideTime { get; set; }

        public bool HasDevRule() => Freecam;

        public byte[] Serialize()
        {
            byte[] output = new byte[34];
            using (var ms = new MemoryStream(output, true))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write(GamemodeOverride);
                    writer.Write(WeatherOverride);
                    writer.Write(DisableWeaponPacks);
                    writer.Write(V2s);
                    writer.Write(MirrorWorld);
                    writer.Write(DisableHealthBoxes);
                    writer.Write(Vampire);
                    writer.Write(HalfTime);
                    writer.Write(Overtime);
                    writer.Write(BetterHills);
                    writer.Write(BetterFlags);
                    writer.Write(Healthbars);
                    writer.Write(DisableNames);
                    writer.Write(DisableInvHitTimer);
                    writer.Write(HideWeaponPickups);
                    writer.Write(FusionShotsAlwaysHit);
                    writer.Write(NoSniperHelpers);
                    writer.Write(CqPersistentCapture);
                    writer.Write(CqDisableTurrets);
                    writer.Write(CqDisableUpgrades);
                    writer.Write(NewPlayerSync);
                    writer.Write(QuickChat);
                    writer.Write(PlayerSize);
                    writer.Write(RotatingWeapons);
                    writer.Write(Headbutt);
                    writer.Write(HeadbuttFriendlyFire);
                    writer.Write(ChargebootForever);
                    writer.Write(Freecam);
                    //writer.Write(Survival_Difficulty);
                    writer.Write(Payload_ContestMode);
                    writer.Write(Training_Type);
                    writer.Write(Training_Variation);
                    writer.Write(Training_Aggression);
                    writer.Write(Training_Opt3);
                    writer.Write(Hns_HideTime);
                }
            }

            return output;
        }

        public void Deserialize(BinaryReader reader)
        {
            GamemodeOverride = reader.ReadSByte();
            WeatherOverride = reader.ReadByte();
            DisableWeaponPacks = reader.ReadBoolean();
            V2s = reader.ReadByte();
            MirrorWorld = reader.ReadBoolean();
            DisableHealthBoxes = reader.ReadByte();
            Vampire = reader.ReadByte();
            HalfTime = reader.ReadBoolean();
            Overtime = reader.ReadBoolean();
            BetterHills = reader.ReadBoolean();
            BetterFlags = reader.ReadBoolean();
            Healthbars = reader.ReadBoolean();
            DisableNames = reader.ReadBoolean();
            DisableInvHitTimer = reader.ReadBoolean();
            HideWeaponPickups = reader.ReadBoolean();
            FusionShotsAlwaysHit = reader.ReadBoolean();
            NoSniperHelpers = reader.ReadBoolean();
            CqPersistentCapture = reader.ReadBoolean();
            CqDisableTurrets = reader.ReadBoolean();
            CqDisableUpgrades = reader.ReadBoolean();
            NewPlayerSync = reader.ReadBoolean();
            QuickChat = reader.ReadBoolean();
            PlayerSize = reader.ReadByte();
            RotatingWeapons = reader.ReadBoolean();
            Headbutt = reader.ReadByte();
            HeadbuttFriendlyFire = reader.ReadBoolean();
            ChargebootForever = reader.ReadBoolean();
            Freecam = reader.ReadBoolean();
            //Survival_Difficulty = reader.ReadByte();
            Payload_ContestMode = reader.ReadByte();
            Training_Type = reader.ReadByte();
            Training_Variation = reader.ReadByte();
            Training_Aggression = reader.ReadByte();
            Training_Opt3 = reader.ReadByte();
            Hns_HideTime = reader.ReadByte();
        }

        public bool SameAs(GameConfig other)
        {
            return GamemodeOverride == other.GamemodeOverride
                && WeatherOverride == other.WeatherOverride
                && DisableWeaponPacks == other.DisableWeaponPacks
                && V2s == other.V2s
                && MirrorWorld == other.MirrorWorld
                && DisableHealthBoxes == other.DisableHealthBoxes
                && Vampire == other.Vampire
                && HalfTime == other.HalfTime
                && Overtime == other.Overtime
                && BetterHills == other.BetterHills
                && BetterFlags == other.BetterFlags
                && Healthbars == other.Healthbars
                && DisableNames == other.DisableNames
                && DisableInvHitTimer == other.DisableInvHitTimer
                && HideWeaponPickups == other.HideWeaponPickups
                && FusionShotsAlwaysHit == other.FusionShotsAlwaysHit
                && NoSniperHelpers == other.NoSniperHelpers
                && CqPersistentCapture == other.CqPersistentCapture
                && CqDisableTurrets == other.CqDisableTurrets
                && CqDisableUpgrades == other.CqDisableUpgrades
                && NewPlayerSync == other.NewPlayerSync
                && QuickChat == other.QuickChat
                && PlayerSize == other.PlayerSize
                && RotatingWeapons == other.RotatingWeapons
                && Headbutt == other.Headbutt
                && HeadbuttFriendlyFire == other.HeadbuttFriendlyFire
                && ChargebootForever == other.ChargebootForever
                && Freecam == other.Freecam
                //&& Survival_Difficulty == other.Survival_Difficulty
                && Payload_ContestMode == other.Payload_ContestMode
                && Training_Type == other.Training_Type
                && Training_Variation == other.Training_Variation
                && Training_Aggression == other.Training_Aggression
                && Training_Opt3 == other.Training_Opt3
                && Hns_HideTime == other.Hns_HideTime
                ;
        }
    }
}
