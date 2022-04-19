using Horizon.Plugin.Deadlocked.Messages;
using Newtonsoft.Json;
using RT.Common;
using Server.Common;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public static class Game
    {
        private static readonly Dictionary<int, GameMetadata> _metadatas = new Dictionary<int, GameMetadata>();

        public static async Task BroadcastGameConfig(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);

            foreach (var gameClient in game.Clients)
            {
                // send config
                gameClient.Client.Queue(new SetGameConfigResponseMessage()
                {
                    Config = metadata.GameConfig
                });

                // send chat message
                game.ChatChannel.SendSystemMessage(gameClient.Client, "AThe host has updated the game settings.");

                // send custom map override
                var map = Maps.FindCustomMapById((CustomMapId)metadata.GameConfig.MapOverride);
                await Maps.SendMapOverride(gameClient.Client, map);
            }
        }

        public static async Task BroadcastMapOverride(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);

            // send custom map override
            var map = Maps.FindCustomMapById((CustomMapId)metadata.GameConfig.MapOverride);
            foreach (var gameClient in game.Clients)
            {
                await Maps.SendMapOverride(gameClient.Client, map);
            }
        }

        public static async Task SendMapOverride(ClientObject client)
        {
            var game = client.CurrentGame;
            if (game == null)
                return;

            var metadata = await GetGameMetadata(game);

            // send custom map override
            var map = Maps.FindCustomMapById((CustomMapId)metadata.GameConfig.MapOverride);
            await Maps.SendMapOverride(client, map);
        }

        public static async Task PlayerJoined(ClientObject client, Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);

            // send map override on join
            var map = Maps.FindCustomMapById((CustomMapId)metadata.GameConfig.MapOverride);
            await Maps.SendMapOverride(client, map);

            // send game config if not host
            if (game.Host != client)
            {
                client.Queue(new SetGameConfigResponseMessage()
                {
                    Config = metadata.GameConfig
                });
            }
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

            // pass to gamemode
            var mode = Modes.FindCustomModeById((CustomModeId)metadata.GameConfig.GamemodeOverride);
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
                            metadata.PostWideStats.Clans[args.Player.ClanId.Value] = args.WideStats.ToArray();
                    }
                    else
                    {
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

            // send to database
            return await SetGameMetadata(game, metadata);
        }

        public static async Task<bool> SetGameConfig(Server.Medius.Models.Game game, GameConfig config)
        {
            var metadata = await GetGameMetadata(game);

            // if no change, return false
            if (metadata.GameConfig.SameAs(config))
                return false;

            // update
            metadata.GameConfig = config;

            // update other metadata
            metadata.CustomMap = Maps.FindCustomMapById((CustomMapId)metadata.GameConfig.MapOverride)?.MapName;
            metadata.CustomGameMode = metadata.GameConfig.GamemodeOverride.ToString();
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
                metadata = new GameMetadata();

            // add to cache
            _metadatas.Add(game.Id, metadata);

            return Task.FromResult(metadata);
        }

        public static async Task UpdateGameData(ClientObject client, Server.Medius.Models.Game game, SetGameDataRequestMessage request)
        {
            var metadata = await GetGameMetadata(game);
            if (metadata.GameData == null)
                metadata.GameData = new byte[3152];

            // copy
            Array.Copy(request.Payload, 0, metadata.GameData, request.Offset, request.Payload.Length);

            // 
            if (request.EndOfList)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, "GAME STATS RECEIVED");

                metadata.ReceivedGameData = true;
            }

            // update
            await SetGameMetadata(game, metadata);
        }

        public static async Task OnGameStarted(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);

            // send map override to all clients
            await BroadcastMapOverride(game);

            // construct payloads to send to client
            var payloads = new List<Payload>();

            // gamerules module entry
            payloads.Add(new Payload(0x000CF000, new PatchModuleEntry()
            {
                Type = PatchModuleEntryType.RUN_ALWAYS,
                GameEntrypoint = 0x000EC000,
                LobbyEntrypoint = 0x000EC008,
                LoadEntrypoint = 0x000EC010,
            }.Serialize()));

            // parse gamemode
            var mode = Modes.FindCustomModeById((CustomModeId)metadata.GameConfig.GamemodeOverride);
            if (mode != null)
            {
                var modePayload = await mode.GetPayload(game);
                if (modePayload != null)
                {
                    // add mode payload
                    payloads.Add(modePayload);

                    // add mode module entry
                    payloads.Add(new Payload(0x000CF010, new PatchModuleEntry()
                    {
                        Type = PatchModuleEntryType.RUN_ONCE_GAME,
                        GameEntrypoint = modePayload.Address,
                        LobbyEntrypoint = modePayload.Address + 8,
                        LoadEntrypoint = modePayload.Address + 16,
                    }.Serialize()));
                }
            }

            // send payloads to all clients
            foreach (var gameClient in game.Clients)
                await Downloader.InitiateDataDownload(gameClient.Client, 201, payloads);

            // store player wide stats
            _ = Task.Run(async () =>
            {
                foreach (var gameClient in game.Clients)
                {
                    metadata.PreWideStats.Players.Add(gameClient.Client.AccountId, gameClient.Client.WideStats.ToArray());
                    metadata.PreCustomWideStats.Players.Add(gameClient.Client.AccountId, gameClient.Client.CustomWideStats.ToArray());
                    metadata.PostWideStats.Players.Add(gameClient.Client.AccountId, gameClient.Client.WideStats.ToArray());
                    metadata.PostCustomWideStats.Players.Add(gameClient.Client.AccountId, gameClient.Client.CustomWideStats.ToArray());

                    if (gameClient.Client.ClanId.HasValue)
                    {
                        var clanId = gameClient.Client.ClanId.Value;
                        if (!metadata.PreWideStats.Clans.ContainsKey(clanId))
                        {
                            var clan = await Server.Medius.Program.Database.GetClanById(clanId);
                            if (clan != null)
                            {
                                metadata.PreWideStats.Clans.Add(clanId, clan.ClanWideStats.ToArray());
                                metadata.PreCustomWideStats.Clans.Add(clanId, clan.ClanCustomWideStats.ToArray());
                                metadata.PostWideStats.Clans.Add(clanId, clan.ClanWideStats.ToArray());
                                metadata.PostCustomWideStats.Clans.Add(clanId, clan.ClanCustomWideStats.ToArray());
                            }
                        }
                    }
                }

                await SetGameMetadata(game, metadata);
            });
        }

        public static async Task OnGameEnded(Server.Medius.Models.Game game)
        {
            var metadata = await GetGameMetadata(game);
            Dictionary<int, int[]> playerCustomStats = null;

            // pass to gamemode
            var mode = Modes.FindCustomModeById((CustomModeId)metadata.GameConfig.GamemodeOverride);
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

            Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, "GAME ENDED");

            // send last metadata to server
            await SetGameMetadata(game, metadata);
        }

        public static async Task OnGameDestroyed(Server.Medius.Models.Game game)
        {
            Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, "GAME DESTROYED");

            var metadata = await GetGameMetadata(game);

            // send last metadata to server
            await SetGameMetadata(game, metadata);

            Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, game.Metadata);

            // remove from cache
            _metadatas.Remove(game.Id);
        }

        private static async Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            string gameInfo = null;

            // let custom game mode override the gameinfo string
            if (metadata.GameConfig.GamemodeOverride != 0)
            {
                var mode = Modes.FindCustomModeById((CustomModeId)metadata.GameConfig.GamemodeOverride);
                if (mode != null)
                    gameInfo = await mode.GetGameInfo(game);
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
        public GameConfig GameConfig { get; set; } = new GameConfig();
        public GameState GameState { get; set; } = new GameState();
        public byte[] GameData { get; set; }
        public bool ReceivedGameData { get; set; }
        public GameStats PreWideStats { get; set; } = new GameStats();
        public GameStats PostWideStats { get; set; } = new GameStats();
        public GameStats PreCustomWideStats { get; set; } = new GameStats();
        public GameStats PostCustomWideStats { get; set; } = new GameStats();
    }

    public class GameStats
    {
        public Dictionary<int, int[]> Players { get; set; } = new Dictionary<int, int[]>();
        public Dictionary<int, int[]> Clans { get; set; } = new Dictionary<int, int[]>();
    }

    public class PackedGameState
    {
        public bool TeamsEnabled { get; set; }
        public int RoundNumber { get; set; }
        public short[] TeamScores { get; set; }
        public sbyte[] ClientIds { get; set; }
        public sbyte[] Teams { get; set; }


        public void Deserialize(BinaryReader reader)
        {
            TeamsEnabled = reader.ReadInt32() != 0;
            RoundNumber = reader.ReadInt32();

            TeamScores = new short[10];
            for (int i = 0; i < 10; ++i)
                TeamScores[i] = reader.ReadInt16();

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
                if (player != null)
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


        public void Deserialize(BinaryReader reader)
        {
            Data = new NWGameData();
            StartGameSettings = new NWGameSettings();
            EndGameSettings = new NWGameSettings();
            GameOptions = new NWGameOptions();
            LastPackedGameState = new PackedGameState();

            Data.Deserialize(reader);
            StartGameSettings.Deserialize(reader);
            EndGameSettings.Deserialize(reader);
            GameOptions.Deserialize(reader);

            // custom data block is always 484 bytes long
            // we'll ensure that the variable sized data structure doesn't mess up our deserialization
            // by moving the stream to the end of the data block after deserializing the custom game data
            var targetEndPosition = reader.BaseStream.Position + 484;
            CustomGameData.Deserialize(reader);
            reader.BaseStream.Seek(targetEndPosition, SeekOrigin.Begin);

            LastPackedGameState.Deserialize(reader);
        }
    }

    public interface ICustomGameData
    {
        void Deserialize(BinaryReader reader);
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

        public void Deserialize(BinaryReader reader)
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

        public void Deserialize(BinaryReader reader)
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

    public class GameConfig
    {
        public byte MapOverride { get; set; }
        public byte GamemodeOverride { get; set; }
        public byte WeatherOverride { get; set; }
        public bool DisableWeaponPacks { get; set; }
        public bool DisableV2s { get; set; }
        public bool MirrorWorld { get; set; }
        public bool DisableHealthBoxes { get; set; }
        public byte Vampire { get; set; }
        public bool HalfTime { get; set; }
        public bool BetterHills { get; set; }
        public bool Healthbars { get; set; }
        public bool DisableNames { get; set; }
        public bool DisableInvHitTimer { get; set; }
        public byte Survival_Difficulty { get; set; }

        public byte[] Serialize()
        {
            byte[] output = new byte[14];
            using (var ms = new MemoryStream(output, true))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write(MapOverride);
                    writer.Write(GamemodeOverride);
                    writer.Write(WeatherOverride);
                    writer.Write(DisableWeaponPacks);
                    writer.Write(DisableV2s);
                    writer.Write(MirrorWorld);
                    writer.Write(DisableHealthBoxes);
                    writer.Write(Vampire);
                    writer.Write(HalfTime);
                    writer.Write(BetterHills);
                    writer.Write(Healthbars);
                    writer.Write(DisableNames);
                    writer.Write(DisableInvHitTimer);
                    writer.Write(Survival_Difficulty);
                }
            }

            return output;
        }

        public void Deserialize(BinaryReader reader)
        {
            MapOverride = reader.ReadByte();
            GamemodeOverride = reader.ReadByte();
            WeatherOverride = reader.ReadByte();
            DisableWeaponPacks = reader.ReadBoolean();
            DisableV2s = reader.ReadBoolean();
            MirrorWorld = reader.ReadBoolean();
            DisableHealthBoxes = reader.ReadBoolean();
            Vampire = reader.ReadByte();
            HalfTime = reader.ReadBoolean();
            BetterHills = reader.ReadBoolean();
            Healthbars = reader.ReadBoolean();
            DisableNames = reader.ReadBoolean();
            DisableInvHitTimer = reader.ReadBoolean();
            Survival_Difficulty = reader.ReadByte();
        }

        public bool SameAs(GameConfig other)
        {
            return MapOverride == other.MapOverride
                && GamemodeOverride == other.GamemodeOverride
                && WeatherOverride == other.WeatherOverride
                && DisableWeaponPacks == other.DisableWeaponPacks
                && DisableV2s == other.DisableV2s
                && MirrorWorld == other.MirrorWorld
                && DisableHealthBoxes == other.DisableHealthBoxes
                && Vampire == other.Vampire
                && HalfTime == other.HalfTime
                && BetterHills == other.BetterHills
                && Healthbars == other.Healthbars
                && DisableNames == other.DisableNames
                && DisableInvHitTimer == other.DisableInvHitTimer
                && Survival_Difficulty == other.Survival_Difficulty
                ;
        }
    }
}
