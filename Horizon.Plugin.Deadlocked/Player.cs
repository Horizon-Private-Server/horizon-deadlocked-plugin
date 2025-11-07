using Horizon.Plugin.Deadlocked.CustomModes;
using Horizon.Plugin.Deadlocked.Messages;
using Newtonsoft.Json;
using Server.Medius;
using Server.Medius.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public static class Player
    {
        private static Dictionary<int, PlayerExtraInfo> _playerExtraInfos = new Dictionary<int, PlayerExtraInfo>();
        private static Dictionary<int, PlayerMetadata> _playerMetadatas = new Dictionary<int, PlayerMetadata>();

        public static event Action<DynamicPageContentBuilder> OnBuildDynamicPageContent;

        private static ConcurrentQueue<ClientObject> _sendPlayerMetadatasQueue = new ConcurrentQueue<ClientObject>();

        public static async Task Tick()
        {
            while (_sendPlayerMetadatasQueue.TryDequeue(out var client))
            {
                if (client == null) continue;
                if (!_playerMetadatas.TryGetValue(client.AccountId, out var metadata))
                    continue;

                if (!await Server.Medius.Program.Database.PostAccountMetadata(client.AccountId, client.Metadata))
                    Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unable to post player metadata to {client.AccountId}: {client.Metadata}");
            }
        }

        public static async Task SetPlayerMapVersion(ClientObject client, string mapFilename, int mapVersion)
        {
            var game = client.CurrentGame;
            if (game == null)
                return;

            var gameMetadata = await Game.GetGameMetadata(game);
            if (gameMetadata.CustomMapConfig?.Filename != mapFilename) return;

            var extraInfo = GetPlayerExtraInfo(client.AccountId);
            extraInfo.CurrentMapVersion = mapVersion;

            if (gameMetadata.CustomMapConfig.HasMap())
            {
                if (mapVersion == -1)
                {
                    // maps no enabled
                    client.CurrentChannel.BroadcastSystemMessage(client.CurrentChannel.Clients, $"A{client.AccountName} does not have custom maps enabled");
                }
                else if (mapVersion == -2)
                {
                    // player doesn't have map
                    client.CurrentChannel.BroadcastSystemMessage(client.CurrentChannel.Clients, $"A{client.AccountName} does not have {gameMetadata.CustomMapConfig.Name}");
                }
                else if (mapVersion > 0)
                {
                    // has map
                    var allPlayersResponded = true;
                    var highestVersion = 0;

                    foreach (var gameClient in game.Clients)
                    {
                        var playerMapVersion = GetPlayerExtraInfo(gameClient.Client.AccountId)?.CurrentMapVersion ?? 0;
                        if (playerMapVersion == 0)
                            allPlayersResponded = false;

                        if (playerMapVersion > highestVersion)
                            highestVersion = playerMapVersion;
                    }

                    // if all players responded then process
                    if (allPlayersResponded)
                    {
                        foreach (var gameClient in game.Clients)
                        {
                            var playerMapVersion = GetPlayerExtraInfo(gameClient.Client.AccountId)?.CurrentMapVersion ?? 0;
                            if (playerMapVersion > 0 && playerMapVersion < highestVersion)
                            {
                                gameClient.Client.CurrentChannel.BroadcastSystemMessage(gameClient.Client.CurrentChannel.Clients, $"A{gameClient.Client.AccountName} has an old version of {gameMetadata.CustomMapConfig.Name} (v{playerMapVersion} of v{highestVersion})");
                            }
                        }
                    }
                }
            }
        }

        public static Task<PlayerConfig> GetPatchConfig(ClientObject client)
        {
            var metadata = GetPlayerMetadata(client);
            if (metadata.Config == null)
                metadata.Config = new PlayerConfig();

            // update location
            client.Location = metadata.Config.PreferredGameServer;

            return Task.FromResult(metadata.Config);
        }

        public static async Task BroadcastPatchConfigToGameLobby(ClientObject client)
        {
            var metadata = GetPlayerMetadata(client);

            // broadcast to all clients in lobby if changed
            if (client.CurrentGame != null)
            {
                var dmeId = client.DmeClientId;
                if (dmeId.HasValue)
                {
                    foreach (var gameClient in client.CurrentGame.Clients)
                    {
                        if (gameClient.DmeId != dmeId)
                        {
                            gameClient.Client.Queue(new SetLobbyClientPatchConfigRequestMessage()
                            {
                                DmeId = dmeId.Value,
                                Config = metadata.Config
                            });
                        }
                    }
                }
            }
        }

        public static async Task SetPatchConfig(ClientObject client, PlayerConfig config)
        {
            // update
            var metadata = GetPlayerMetadata(client);
            var changed = metadata.Config == null || !metadata.Config.SameAs(config);
            metadata.Config = config;

            // update location
            client.Location = config.PreferredGameServer;

            // send to other game clients
            if (changed)
                await BroadcastPatchConfigToGameLobby(client);

            Player.SavePlayerMetadata(client);
        }

        public static async Task SetClientType(ClientObject client, PlayerClientType clientType)
        {
            // update
            var metadata = GetPlayerMetadata(client);
            metadata.LastLoginClientType = clientType;

            if (metadata.LastLoginPerClientType == null)
                metadata.LastLoginPerClientType = new Dictionary<PlayerClientType, DateTimeOffset?>();
            metadata.LastLoginPerClientType[clientType] = DateTimeOffset.UtcNow;

            Player.SavePlayerMetadata(client);
        }

        public static async Task OnPickedUpHorizonBolt(ClientObject client)
        {
            var total = client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_HBOLT_TOTAL_COUNT] += 1;
            var current = client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_HBOLT_CURRENT_COUNT] += 1;

            // send to db
            await Server.Medius.Program.Database.PostAccountLadderCustomStats(new Server.Database.Models.StatPostDTO()
            {
                AccountId = client.AccountId,
                Stats = client.CustomWideStats
            });

            // log
            await Program.Database.Log(client.AccountId, "OnPickedUpHorizonBolt", "Horizon Bolt Picked Up", $"Total {total}, current {current}", null, null);
        }

        public static string GetPlayerAccountName(ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata?.CompConfig != null && !String.IsNullOrEmpty(metadata.CompConfig.CompServerName))
            {
                var name = metadata.CompConfig.CompServerName;
                if (name.Length > 15)
                    name = name.Substring(0, 15);

                return name;
            }

            return client.AccountName;
        }

        public static PlayerMetadata GetPlayerMetadata(ClientObject client)
        {
            if (client == null) return null;

            if (_playerMetadatas.TryGetValue(client.AccountId, out var metadata) && metadata != null)
                return metadata;

            try { metadata = JsonConvert.DeserializeObject<PlayerMetadata>(client.Metadata); } catch (Exception) { }
            if (metadata == null)
                metadata = new PlayerMetadata();

            return _playerMetadatas[client.AccountId] = metadata;
        }

        public static bool SavePlayerMetadata(ClientObject client)
        {
            if (!_playerMetadatas.TryGetValue(client.AccountId, out var metadata) || metadata == null)
                return false;

            var json = JsonConvert.SerializeObject(metadata);
            if (client.Metadata != json)
            {
                client.Metadata = json;
                if (!_sendPlayerMetadatasQueue.Contains(client))
                    _sendPlayerMetadatasQueue.Enqueue(client);
                return true;
            }

            return false;
        }

        public static async Task<bool> SavePlayerMetadataImmediately(ClientObject client)
        {
            if (!_playerMetadatas.TryGetValue(client.AccountId, out var metadata) || metadata == null)
                return false;

            var json = JsonConvert.SerializeObject(metadata);
            if (client.Metadata != json)
            {
                client.Metadata = json;
                return await Server.Medius.Program.Database.PostAccountMetadata(client.AccountId, client.Metadata);
            }

            return false;
        }

        public static PlayerExtraInfo GetPlayerExtraInfo(int accountId)
        {
            if (!_playerExtraInfos.TryGetValue(accountId, out var extraInfo))
                _playerExtraInfos.Add(accountId, extraInfo = new PlayerExtraInfo());

            return extraInfo;
        }

        public static Task OnPlayerLoggedOut(ClientObject client)
        {
            if (_playerExtraInfos.ContainsKey(client.AccountId))
                _playerExtraInfos.Remove(client.AccountId);

            return Task.CompletedTask;
        }

        public static void OnPlayerRequestDynamicPageContent(ClientObject client, GetDynamicPageContentRequestMessage request)
        {
            var builder = new DynamicPageContentBuilder()
            {
                Client = client,
                PlayerMetadata = GetPlayerMetadata(client),
                Request = request
            };

            if (OnBuildDynamicPageContent != null)
                OnBuildDynamicPageContent.Invoke(builder);

            if (builder.Cancel)
            {
                client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.StateAddress, new byte[1] { 3 }));
            }
            else
            {
                if (request.LineItemsAddress != 0)
                {
                    using (var ms = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(ms))
                        {
                            foreach (var lineItem in builder.LineItems)
                            {
                                writer.Write(lineItem.Name, null);
                                writer.Write(lineItem.Value, null);
                            }
                            client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.LineItemsAddress, ms.ToArray()));
                        }
                    }
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.LineItemsCountAddress, BitConverter.GetBytes(builder.LineItems.Count)));
                }

                client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.StateAddress, new byte[1] { 2 }));
            }
        }
    }

    public enum PlayerClientType
    {
        Normal = 0,
        DZO = 1,
        PCSX2 = 2,
    }

    public class DynamicPageContentBuilder
    {
        public ClientObject Client { get; set; }
        public PlayerMetadata PlayerMetadata { get; set; }
        public GetDynamicPageContentRequestMessage Request { get; set; }
        public List<(string Name, string Value)> LineItems { get; } = new List<(string Name, string Value)>();
        public bool Cancel { get; set; }
    }

    public class PlayerMetadata
    {
        public PlayerConfig Config { get; set; } = new PlayerConfig();
        public PlayerClientType? LastLoginClientType { get; set; } = null;
        public Dictionary<PlayerClientType, DateTimeOffset?> LastLoginPerClientType { get; set; } = new Dictionary<PlayerClientType, DateTimeOffset?>();
        public Dictionary<string, SurvivalMapStat> SurvivalMapStats { get; set; } = new Dictionary<string, SurvivalMapStat>();
        public Dictionary<string, ObstacleCourseMapStat> ObstacleCourseStats { get; set; } = new Dictionary<string, ObstacleCourseMapStat>();
        public Dictionary<string, CollectathonMapStat> CollectathonStats { get; set; } = new Dictionary<string, CollectathonMapStat>();
        public PlayerCompConfig CompConfig { get; set; } = new PlayerCompConfig();
        public RaidsBank RaidsBank { get; set; } = new RaidsBank();
    }

    public class PlayerExtraInfo
    {
        public int CurrentMapVersion { get; set; }
        public byte[] PatchHash { get; set; }
        public bool PatchHandled { get; set; }
        public string LastChatCommand { get;set; }
    }
    
    public class CollectathonMapStat
    {
        public enum Difficulty
        {
            [Description("Easy")]
            Easy,
            [Description("Medium")]
            Medium,
            [Description("Hard")]
            Hard,
            [Description("Very Hard")]
            VeryHard
        }

        public Dictionary<uint, Difficulty> Bolts { get; set; } = new Dictionary<uint, Difficulty>();
        public HashSet<uint> CollectedBoltUids { get; set; } = new HashSet<uint>();

        public int GetCount(Difficulty difficulty) => Bolts.Count(x => x.Value == difficulty);
        public int GetCollectedCount(Difficulty difficulty) => Bolts.Count(x => x.Value == difficulty && CollectedBoltUids.Contains(x.Key));
        public float GetCompletion()
        {
            var completedTasks = Bolts.Count(x => CollectedBoltUids.Contains(x.Key));
            var totalTasks = Bolts.Count;
            var completion = completedTasks / (float)Math.Max(1, totalTasks);
            return completion;
        }
    }

    public class ObstacleCourseMapStat
    {
        public int Checkpoint { get; set; }
        public ulong CheckpointTicks { get; set; }
        public ulong BestCheckpointTicks { get; set; }
    }

    public class SurvivalMapStat
    {
        public string Name { get; set; }
        public Dictionary<int, SurvivalMapGambitStat> Gambits { get; set; } = new Dictionary<int, SurvivalMapGambitStat>();
        public int GambitCount { get; set; }
    }

    public class SurvivalMapGambitStat
    {
        public string Name { get; set; }
        public bool Completed { get; set; }
        public int BestRound { get; set; }
    }

    public class PlayerCompConfig
    {
        public string OriginalCompServerName { get; set; } = null;
        public string DiscordId { get; set; } = null;
        public string CompServerName { get; set; } = null;
        public List<string> CompServerNames { get; set; } = new List<string>();
        public DateTime? TimeLastNameChange { get; set; } = null;
    }

    public class PlayerConfig
    {
        public byte Framelimiter { get; set; } = 2; // Off
        public bool EnableGamemodeAnnouncements { get; set; }
        public bool EnableSpectate { get; set; }
        public bool EnableSingleplayerMusic { get; set; }
        public byte LevelOfDetail { get; set; } = 2; // normal
        public bool EnablePlayerStateSync { get; set; }
        public bool DisableAimAssist { get; set; }
        public bool EnableFpsCounter { get; set; }
        public bool DisableCircleHackerRay { get; set; }
        public bool DisableScavengerHunt { get; set; }
        public bool DisableCameraShake { get; set; }
        public sbyte MinimapScale { get; set; }
        public sbyte MinimapBigZoom { get; set; }
        public sbyte MinimapSmallZoom { get; set; }
        public sbyte EnableFusionReticule { get; set; }
        public sbyte PlayerFov { get; set; }
        public byte PreferredGameServer { get; set; }
        public byte FixedCycleOrder { get; set; }
        public bool EnableSingleTapChargeboot { get; set; }
        public bool EnableInGameScoreboard { get; set; }
        public bool EnableNPSLagComp { get; set; }
        public bool EnableFastUSBLoad { get; set; } = true;
        public byte LevelOfDetailMobs { get; set; } = 2; // normal
#if TWEAKERS
        public byte[] CharacterTweakers { get; set; } = new byte[1 + 7*2];
#endif

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write(Framelimiter);
                    writer.Write(EnableGamemodeAnnouncements);
                    writer.Write(EnableSpectate);
                    writer.Write(EnableSingleplayerMusic);
                    writer.Write(LevelOfDetail);
                    writer.Write(EnablePlayerStateSync);
                    writer.Write(DisableAimAssist);
                    writer.Write(EnableFpsCounter);
                    writer.Write(DisableCircleHackerRay);
                    writer.Write(DisableScavengerHunt);
                    writer.Write(DisableCameraShake);
                    writer.Write(MinimapScale);
                    writer.Write(MinimapBigZoom);
                    writer.Write(MinimapSmallZoom);
                    writer.Write(EnableFusionReticule);
                    writer.Write(PlayerFov);
                    writer.Write(PreferredGameServer);
                    writer.Write(FixedCycleOrder);
                    writer.Write(EnableSingleTapChargeboot);
                    writer.Write(EnableInGameScoreboard);
                    writer.Write(EnableNPSLagComp);
                    writer.Write(EnableFastUSBLoad);
                    writer.Write(LevelOfDetailMobs);
#if TWEAKERS
                    writer.Write(CharacterTweakers ?? new byte[1 + 7*2]);
#endif

                    return ms.ToArray();
                }
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            Framelimiter = reader.ReadByte();
            EnableGamemodeAnnouncements = reader.ReadBoolean();
            EnableSpectate = reader.ReadBoolean();
            EnableSingleplayerMusic = reader.ReadBoolean();
            LevelOfDetail = reader.ReadByte();
            EnablePlayerStateSync = reader.ReadBoolean();
            DisableAimAssist = reader.ReadBoolean();
            EnableFpsCounter = reader.ReadBoolean();
            DisableCircleHackerRay = reader.ReadBoolean();
            DisableScavengerHunt = reader.ReadBoolean();
            DisableCameraShake = reader.ReadBoolean();
            MinimapScale = reader.ReadSByte();
            MinimapBigZoom = reader.ReadSByte();
            MinimapSmallZoom = reader.ReadSByte();
            EnableFusionReticule = reader.ReadSByte();
            PlayerFov = reader.ReadSByte();
            PreferredGameServer = reader.ReadByte();
            FixedCycleOrder = reader.ReadByte();
            EnableSingleTapChargeboot = reader.ReadBoolean();
            EnableInGameScoreboard = reader.ReadBoolean();
            EnableNPSLagComp = reader.ReadBoolean();
            EnableFastUSBLoad = reader.ReadBoolean();
            LevelOfDetailMobs = reader.ReadByte();
#if TWEAKERS
            CharacterTweakers = reader.ReadBytes(1 + 7*2);
#endif
        }

        public bool SameAs(PlayerConfig other)
        {
            return Framelimiter == other.Framelimiter
                && EnableGamemodeAnnouncements == other.EnableGamemodeAnnouncements
                && EnableSpectate == other.EnableSpectate
                && EnableSingleplayerMusic == other.EnableSingleplayerMusic
                && LevelOfDetail == other.LevelOfDetail
                && EnablePlayerStateSync == other.EnablePlayerStateSync
                && DisableAimAssist == other.DisableAimAssist
                && EnableFpsCounter == other.EnableFpsCounter
                && DisableCircleHackerRay == other.DisableCircleHackerRay
                && DisableScavengerHunt == other.DisableScavengerHunt
                && DisableCameraShake == other.DisableCameraShake
                && MinimapScale == other.MinimapScale
                && MinimapBigZoom == other.MinimapBigZoom
                && MinimapSmallZoom == other.MinimapSmallZoom
                && EnableFusionReticule == other.EnableFusionReticule
                && PlayerFov == other.PlayerFov
                && PreferredGameServer == other.PreferredGameServer
                && FixedCycleOrder == other.FixedCycleOrder
                && EnableSingleTapChargeboot == other.EnableSingleTapChargeboot
                && EnableInGameScoreboard == other.EnableInGameScoreboard
                && EnableNPSLagComp == other.EnableNPSLagComp
                && EnableFastUSBLoad == other.EnableFastUSBLoad
                && LevelOfDetailMobs == other.LevelOfDetailMobs
#if TWEAKERS
                && (CharacterTweakers?.SequenceEqual(other.CharacterTweakers) ?? false)
#endif
                ;
        }
    }
}
