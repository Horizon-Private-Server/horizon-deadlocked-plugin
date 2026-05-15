using Horizon.Plugin.Deadlocked.DTO;
using Horizon.Plugin.Deadlocked.Messages;
using Newtonsoft.Json;
using Server.Common;
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
    public class ForgeCgmCustomMode : BaseCustomMode, IDisposable
    {
        public enum StatValueType
        {
            Integer,
            TimeSeconds,
            Float
        }

        public enum StatTrackerType
        {
            Cumulative,
            Largest,
            Smallest,
            Newest
        }

        public enum StatTrackerSave
        {
            WhenMinTeamsForStatsMet,
            WhenMinTeamsForRankMet,
        }

        public override CustomModeId Id => CustomModeId.CMODE_ID_FORGE_CUSTOM;
        public override string Name => "Forge Custom Mode";

        public ForgeCgmCustomMode()
        {
            Player.OnBuildDynamicPageContentCallbacks.Remove(Player_OnBuildDynamicPageContentAsync);
            Player.OnBuildDynamicPageContentCallbacks.Add(Player_OnBuildDynamicPageContentAsync);
        }

        public void Dispose()
        {
            Player.OnBuildDynamicPageContentCallbacks.Remove(Player_OnBuildDynamicPageContentAsync);
        }

        public override async Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return 0;

            // get map stats
            var accountStats = await Plugin.Database.GetForgeCgmMapAccountStatsAsync(client.AccountId, metadata.CustomMapConfig.Filename);
            if (accountStats == null)
                return 0;

            return (int)Math.Clamp(accountStats.Rank, 100, 10000);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            return Task.CompletedTask;
        }

        public override async Task<string> GetCustomModeName(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var defaultName = await base.GetCustomModeName(game, metadata);
            var mapFilename = metadata.CustomMapConfig.Filename;
            if (string.IsNullOrEmpty(mapFilename))
                return defaultName;

            var cgmMap = await Plugin.Database.GetForgeCgmMapAsync(mapFilename);
            return cgmMap?.Name ?? defaultName;
        }

        public override async Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var round = metadata.GameState.RoundNumber;
            var hostMetadata = Player.GetPlayerMetadata(game.Host);
            var mapFilename = metadata.CustomMapConfig.Filename;
            if (string.IsNullOrEmpty(mapFilename))
                return null;

            var cgmMap = await Plugin.Database.GetForgeCgmMapAsync(mapFilename);
            if (cgmMap.Metadata == null)
                return null;

            var cgmMapMetadata = GetCgmMapMetadata(cgmMap);

            string info = " "; // non-empty so we hide default info
            var paramValues = new byte[]
            {
                metadata.GameConfig.ForgeCgm_Param1,
                metadata.GameConfig.ForgeCgm_Param2,
                metadata.GameConfig.ForgeCgm_Param3,
                metadata.GameConfig.ForgeCgm_Param4,
            };
            for (int i = 0; i < cgmMapMetadata.CgmParameters.Count && i < paramValues.Length; ++i)
            {
                var parameter = cgmMapMetadata.CgmParameters[i];
                info += $"\n{parameter.Name}: {parameter.Options.ElementAtOrDefault(paramValues[i])}";
            }

            if (round > 0)
                info += $"\nRound: {round}";

            return info;
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/forge-cgm-11184.bin"))));
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new ForgeCgmCustomData();
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            return true;
        }

        protected override async Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as ForgeCgmCustomData;
            var mapFilename = args.Metadata.CustomMapConfig.Filename;
            var gameData = args.GameData;
            var game = args.Game;

            if (customGameData.TeamScores == null || String.IsNullOrEmpty(mapFilename)) return;
            if (args.Metadata.GameConfig.HasDevRule()) return;

            // update cgm map data
            var cgmMap = await Plugin.Database.GetForgeCgmMapAsync(mapFilename);
            var cgmMapMetadata = GetCgmMapMetadata(cgmMap);
            if (cgmMapMetadata.UpdateFrom(customGameData) || cgmMap.Name != customGameData.Name || cgmMap.SharedRankCode != customGameData.SharedRankCode)
            {
                cgmMap = await Plugin.Database.UpdateForgeCgmMapAsync(mapFilename, customGameData.Name, customGameData.SharedRankCode, JsonConvert.SerializeObject(cgmMapMetadata, Formatting.Indented));
            }

            // set rank and score of players
            var playerMapStats = new Dictionary<int, ForgeCgmMapAccountStatDTO>();
            foreach (var player in args.Players)
            {
                playerMapStats[player.AccountId] = await Plugin.Database.GetForgeCgmMapAccountStatsAsync(player.AccountId, mapFilename);
                player.Rank = (int)Math.Clamp(playerMapStats[player.AccountId]?.Rank ?? 100, 100, 10000);
                player.Team = customGameData.PlayerTeams[player.Index];
                player.Score = customGameData.TeamScores[player.Team];
            }

            // invert score if sort ascending
            if (customGameData.OrderScoreByAscending)
            {
                var highestScore = args.Players.Max(x => x.Score);
                foreach (var player in args.Players)
                    player.Score = highestScore - player.Score;
            }

            var teamCount = args.Players.Select(x => x.Team).Distinct().Count();
            var isRanked = teamCount >= customGameData.MinTeamsForRank;
            var saveStats = teamCount >= customGameData.MinTeamsForStats;

            // handle rank Team Game vs FFA Game
            if (isRanked)
            {
                if (customGameData.TeamsEnabled)
                {
                    var activePlayers = args.Players.Where(x => !x.Left).ToList();
                    var winningScore = activePlayers.Max(x => x.Score);
                    var winningTeams = activePlayers.Where(x => x.Score == winningScore).Select(x => x.Team).Distinct().ToArray();

                    // rate team game
                    Stats.RateTeamGame(args.Players, winningTeams, wager: 0.3f, rewardFlatness: 0.5f, drawPenalty: 0.2f);
                }
                else
                {
                    // rate ffa game
                    Stats.RateFfaGame(args.Players, wager: 0.1f, rewardCurve: [0.4f, 0.3f, 0.3f]);
                }
            }

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var stats = playerMapStats[accountId];
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                if (isRanked)
                {
                    stats.Rank = player.Rank;
                    stats.Wins += player.Won ? 1 : 0;
                    stats.Losses += player.Won ? 0 : 1;
                }

                if (saveStats)
                {
                    stats.GamesPlayed += 1;

                    // track time played if not left
                    if (!player.Left)
                        stats.TimePlayedMs += customGameData.RuntimeMs;
                }

                // update custom tracked stats
                for (int i = 0; i < customGameData.TrackedStats.Length; ++i)
                {
                    var trackedStat = customGameData.TrackedStats[i];
                    if (trackedStat.TrackerSlot < 0 || trackedStat.TrackerSlot >= stats.TrackedStats.Length)
                        continue;

                    if (trackedStat.TrackerSave == StatTrackerSave.WhenMinTeamsForRankMet && !isRanked)
                        continue;

                    if (trackedStat.TrackerSave == StatTrackerSave.WhenMinTeamsForStatsMet && !saveStats)
                        continue;

                    var curValue = stats.TrackedStats[trackedStat.TrackerSlot];
                    var gameValue = customGameData.TrackedStatValues[i].PlayerValues[player.Index];
                    long newValue = 0;

                    switch (trackedStat.TrackerType)
                    {
                        case StatTrackerType.Cumulative:
                            {
                                newValue = curValue + gameValue;
                                break;
                            }
                        case StatTrackerType.Largest:
                            {
                                newValue = Math.Max(curValue, gameValue);
                                break;
                            }
                        case StatTrackerType.Smallest:
                            {
                                // for smallest, db defaults to 0
                                // so assume a value of 0 means uninitialized
                                // prefer non-zero value, then min
                                if (curValue == 0)
                                    newValue = gameValue;
                                else if (gameValue == 0)
                                    newValue = curValue;
                                else
                                    newValue = Math.Min(curValue, gameValue);
                                break;
                            }
                        case StatTrackerType.Newest:
                            {
                                newValue = gameValue;
                                break;
                            }
                    }

                    stats.TrackedStats[trackedStat.TrackerSlot] = newValue;
                }

                // save
                await Plugin.Database.UpdateForgeCgmMapAccountStatsAsync(stats);
            }
        }

        protected override async Task UpdateCustomMapExData(ClientObject client, string mapFilename, byte[] data)
        {
            // update cgm map data
            var cgmMap = await Plugin.Database.GetForgeCgmMapAsync(mapFilename);
            var cgmMapMetadata = GetCgmMapMetadata(cgmMap);
            var cgmParameters = new List<ForgeCgmMapMetadata.CgmParameter>();
            var sharedRankCode = cgmMap.SharedRankCode;

            // parse ex data
            using (var ms = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(ms))
                {
                    var modeVersion = reader.ReadInt32();
                    var customModeName = reader.ReadString(32);
                    sharedRankCode = reader.ReadString(32);
                    var paramOffset = reader.ReadInt32();

                    var gadgets = reader.ReadInt32();
                    var baseGameMode = reader.ReadByte();
                    var radarBlips = reader.ReadByte();
                    var vehicles = reader.ReadByte();
                    var specialPickups = reader.ReadByte();
                    var spawnWithChargeboots = reader.ReadByte();
                    var autospawnWeapons = reader.ReadByte();
                    var unlimitedAmmo = reader.ReadByte();
                    var timelimit = reader.ReadByte();
                    var respawnTime = reader.ReadByte();
                    var killsToWin = reader.ReadByte();
                    var survivor = reader.ReadByte();
                    var juggernautVis = reader.ReadByte();
                    var juggernautHealing = reader.ReadByte();
                    var capsToWin = reader.ReadByte();
                    var crazyMode = reader.ReadByte();
                    var flagReturn = reader.ReadByte();
                    var vehicleCarry = reader.ReadByte();
                    var hillTimeToWin = reader.ReadByte();
                    var hillMovingTime = reader.ReadByte();
                    var hillSharing = reader.ReadByte();
                    var hillArmor = reader.ReadByte();
                    var boltsToWin = reader.ReadByte();
                    var specialRules = reader.ReadByte();
                    var nodeType = reader.ReadByte();
                    var turrets = reader.ReadByte();
                    var teleporterUpgrade = reader.ReadByte();
                    var upgradeTimer = reader.ReadByte();
                    var voteTime = reader.ReadByte();

                    var teamRule = reader.ReadByte();
                    var trackBaseStats = reader.ReadByte();
                    var trackCustomStats = reader.ReadByte();
                    var minTeamsForRank = reader.ReadByte();
                    var minTeamsForStats = reader.ReadByte();

                    // skip to params
                    reader.BaseStream.Position = paramOffset;
                    var paramCount = reader.ReadInt32();
                    for (int i = 0; i < paramCount; ++i)
                    {
                        var nextParamOffset = reader.ReadInt32();
                        var optionsCount = reader.ReadInt32();
                        var name = reader.ReadCString();
                        var desc = reader.ReadCString();
                        var options = new List<string>();
                        for (int o = 0; o < optionsCount; ++o)
                            options.Add(reader.ReadCString());

                        cgmParameters.Add(new ForgeCgmMapMetadata.CgmParameter()
                        {
                            Name = name,
                            Options = options
                        });

                        reader.BaseStream.Position = nextParamOffset;
                    }
                }
            }

            if (cgmMapMetadata.UpdateFrom(cgmParameters) || sharedRankCode != cgmMap.SharedRankCode)
            {
                cgmMap = await Plugin.Database.UpdateForgeCgmMapAsync(mapFilename, cgmMap.Name, sharedRankCode, JsonConvert.SerializeObject(cgmMapMetadata, Formatting.Indented));
            }
        }

        private static async Task Player_OnBuildDynamicPageContentAsync(DynamicPageContentBuilder builder)
        {
            if (builder.Request.Type != GetDynamicPageContentRequestMessage.ContentType.ForgeCgmMapStats) return;

            var mapFilename = builder.Request.MapFilename;
            var cgmMap = await Plugin.Database.GetForgeCgmMapAsync(mapFilename);
            if (cgmMap.Metadata == null)
                return;

            var cgmMapMetadata = GetCgmMapMetadata(cgmMap);

            // get map stats
            var mapStats = await Plugin.Database.GetForgeCgmMapAggregatedAccountStatsAsync(builder.Client.AccountId, mapFilename);

            var timePlayed = mapStats.TimePlayedMs / 1000;
            var timePlayedSec = timePlayed % 60;
            var timePlayedMin = (timePlayed / 60) % 60;
            var timePlayedHr = (timePlayed / 3600);

            // build rank
            if (cgmMapMetadata.HasRank)
            {
                builder.LineItems.Add(("Rank", $"#{mapStats.Ranking} ({mapStats.Rank})"));
                builder.LineItems.Add(("Skill Level", $"{Stats.RankToSkillLevel(mapStats.Rank):N2}"));
                builder.LineItems.Add(("Wins", $"{mapStats.Wins}"));
                builder.LineItems.Add(("Losses", $"{mapStats.Losses}"));
            }

            // build stats
            builder.LineItems.Add(("Games Played", $"{mapStats.GamesPlayed}"));
            builder.LineItems.Add(("Time Played", $"{timePlayedHr}h {timePlayedMin}m {timePlayedSec}s"));

            // tracked stats
            for (int i = 0; i < cgmMapMetadata.TrackedStats.Count; ++i)
            {
                var trackedStat = cgmMapMetadata.TrackedStats[i];
                if (trackedStat.TrackerSlot < 0 || trackedStat.TrackerSlot >= mapStats.TrackedStats.Length || string.IsNullOrEmpty(trackedStat.Name))
                    continue;

                var rawValue = mapStats.TrackedStats[trackedStat.TrackerSlot];
                var printValue = "";
                switch (trackedStat.ValueType)
                {
                    case StatValueType.Integer:
                        {
                            printValue = rawValue.ToString("N0");
                            break;
                        }
                    case StatValueType.TimeSeconds:
                        {
                            var sec = rawValue % 60;
                            var min = (rawValue / 60) % 60;
                            var hr = rawValue / 3600;
                            printValue = $"{hr}h {min}m {sec}s";
                            break;
                        }
                    case StatValueType.Float:
                        {
                            printValue = $"{rawValue / 1024f:N2}";
                            break;
                        }
                }

                builder.LineItems.Add((trackedStat.Name, printValue));
            }
        }

        private static ForgeCgmMapMetadata GetCgmMapMetadata(ForgeCgmMapDTO cgmMap)
        {
            var cgmMapMetadata = new ForgeCgmMapMetadata();
            if (!string.IsNullOrEmpty(cgmMap.Metadata))
            {
                try
                {
                    JsonConvert.PopulateObject(cgmMap.Metadata, cgmMapMetadata);
                }
                catch (Exception ex)
                {
                    Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, ex);
                }
            }

            return cgmMapMetadata;
        }
    }

    public class ForgeCgmMapMetadata
    {
        public class TrackedStat
        {
            public string Name { get; set; }
            public ForgeCgmCustomMode.StatValueType ValueType { get; set; }
            public ForgeCgmCustomMode.StatTrackerType TrackerType { get; set; }
            public int TrackerSlot { get; set; }
            public ForgeCgmCustomMode.StatTrackerSave TrackerSave { get; set; }
        }

        public class CgmParameter
        {
            public string Name { get; set; }
            public List<string> Options { get; set; } = new List<string>();
        }

        public List<TrackedStat> TrackedStats { get; set; } = new List<TrackedStat>();
        public List<CgmParameter> CgmParameters { get; set; } = new List<CgmParameter>();
        public bool HasRank { get; set; }
        public bool HasStats { get; set; }

        public bool UpdateFrom(List<CgmParameter> cgmParameters)
        {
            var changed = false;

            for (int i = 0; i < cgmParameters.Count; ++i)
            {
                var remoteParam = cgmParameters[i];
                var localParam = this.CgmParameters.ElementAtOrDefault(i);

                if (localParam == null)
                {
                    changed = true;
                    this.CgmParameters.Add(new CgmParameter()
                    {
                        Name = remoteParam.Name,
                        Options = remoteParam.Options
                    });
                }
                else
                {
                    if (localParam.Name != remoteParam.Name)
                    {
                        changed = true;
                        localParam.Name = remoteParam.Name;
                    }

                    if (!localParam.Options.SequenceEqual(remoteParam.Options))
                    {
                        changed = true;
                        localParam.Options = remoteParam.Options;
                    }
                }
            }

            // trim end
            while (this.CgmParameters.Count > cgmParameters.Count)
                this.CgmParameters.RemoveAt(this.CgmParameters.Count - 1);

            return changed;
        }

        public bool UpdateFrom(ForgeCgmCustomData customData)
        {
            var changed = false;

            var hasRank = customData.MinTeamsForRank >= 1 && customData.MinTeamsForRank <= 10;
            var hasStats = customData.MinTeamsForStats >= 1 && customData.MinTeamsForStats <= 10;

            if (HasRank != hasRank)
            {
                HasRank = hasRank;
                changed = true;
            }

            if (HasStats != hasStats)
            {
                HasStats = hasStats;
                changed = true;
            }

            // tracked stats
            for (int i = 0; i < customData.TrackedStats.Length; ++i)
            {
                var gameTrackerStat = customData.TrackedStats[i];
                var mapTrackerStat = this.TrackedStats.ElementAtOrDefault(i);

                if (mapTrackerStat == null)
                {
                    changed = true;
                    this.TrackedStats.Add(new TrackedStat()
                    {
                        Name = gameTrackerStat.Name,
                        ValueType = gameTrackerStat.ValueType,
                        TrackerType = gameTrackerStat.TrackerType,
                        TrackerSlot = gameTrackerStat.TrackerSlot,
                        TrackerSave = gameTrackerStat.TrackerSave
                    });
                }
                else
                {
                    if (mapTrackerStat.Name != gameTrackerStat.Name)
                    {
                        changed = true;
                        mapTrackerStat.Name = gameTrackerStat.Name;
                    }

                    if (mapTrackerStat.ValueType != gameTrackerStat.ValueType)
                    {
                        changed = true;
                        mapTrackerStat.ValueType = gameTrackerStat.ValueType;
                    }

                    if (mapTrackerStat.TrackerType != gameTrackerStat.TrackerType)
                    {
                        changed = true;
                        mapTrackerStat.TrackerType = gameTrackerStat.TrackerType;
                    }

                    if (mapTrackerStat.TrackerSlot != gameTrackerStat.TrackerSlot)
                    {
                        changed = true;
                        mapTrackerStat.TrackerSlot = gameTrackerStat.TrackerSlot;
                    }

                    if (mapTrackerStat.TrackerSave != gameTrackerStat.TrackerSave)
                    {
                        changed = true;
                        mapTrackerStat.TrackerSave = gameTrackerStat.TrackerSave;
                    }
                }
            }


            return changed;
        }
    }

    public class ForgeCgmCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int RuntimeMs { get; set; }
        public string Name { get; set; }
        public string SharedRankCode { get; set; }
        public int MinTeamsForRank { get; set; }
        public int MinTeamsForStats { get; set; }
        public int[] TeamScores { get; set; }
        public sbyte[] PlayerTeams { get; set; }
        public bool TeamsEnabled { get; set; }
        public bool OrderScoreByAscending { get; set; }
        public TrackedStat[] TrackedStats { get; set; }
        public TrackedStatValue[] TrackedStatValues { get; set; }

        public void Deserialize(MessageReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            RuntimeMs = reader.ReadInt32();
            Name = reader.ReadString(32);
            SharedRankCode = reader.ReadString(32);
            switch (Version)
            {
                case 1:
                    {
                        MinTeamsForRank = reader.ReadByte();
                        MinTeamsForStats = reader.ReadByte();
                        reader.ReadBytes(2);
                        TeamScores = reader.ReadArray<int>(10);
                        PlayerTeams = reader.ReadArray<sbyte>(10);
                        TeamsEnabled = reader.ReadBoolean();
                        OrderScoreByAscending = reader.ReadBoolean();
                        TrackedStats = reader.ReadArray<TrackedStat>(8);
                        TrackedStatValues = reader.ReadArray<TrackedStatValue>(8);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported forge cgm data version {Version}");
                        break;
                    }
            }
        }

        public struct TrackedStat : IStreamSerializer
        {
            public string Name { get; set; }
            public ForgeCgmCustomMode.StatValueType ValueType { get; set; }
            public ForgeCgmCustomMode.StatTrackerType TrackerType { get; set; }
            public sbyte TrackerSlot { get; set; }
            public ForgeCgmCustomMode.StatTrackerSave TrackerSave { get; set; }

            public void Deserialize(MessageReader reader)
            {
                Name = reader.ReadString(16);
                ValueType = (ForgeCgmCustomMode.StatValueType)reader.ReadByte();
                TrackerType = (ForgeCgmCustomMode.StatTrackerType)reader.ReadByte();
                TrackerSlot = reader.ReadSByte();
                TrackerSave = (ForgeCgmCustomMode.StatTrackerSave)reader.ReadByte();
            }

            public void Serialize(MessageWriter writer)
            {
                throw new NotImplementedException();
            }
        }

        public struct TrackedStatValue : IStreamSerializer
        {
            public int[] PlayerValues { get; set; }

            public void Deserialize(MessageReader reader)
            {
                PlayerValues = reader.ReadArray<int>(10);
            }

            public void Serialize(MessageWriter writer)
            {
                throw new NotImplementedException();
            }
        }
    }
}
