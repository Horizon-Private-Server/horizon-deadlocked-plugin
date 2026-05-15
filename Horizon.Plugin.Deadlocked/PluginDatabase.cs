using Horizon.Plugin.Deadlocked.DTO;
using Server.Database;
using Server.Medius;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public class PluginDatabase
    {
        public bool IsSimulated => Program.Database.IsSimulated;

        string _simulatedDbFilepath;
        string _simulatedEncryptionKey = "thisisn'tasafekeybutthedataisn'timportantsoisok";
        bool _saveSimulated = false;
        bool _loadSimulated = true;
        PluginDatabaseSimulated _simulatedDb = new PluginDatabaseSimulated();
        ConcurrentDictionary<string, CacheItem> _cache = new ConcurrentDictionary<string, CacheItem>();

        class CacheItem
        {
            public DateTime UtcExpiresAt { get; set; }
            public object Data { get; set; }
        }

        public PluginDatabase(string workingDirectory)
        {
            _simulatedDbFilepath = Path.Combine(workingDirectory, "dl-plugin.simulated.db");
        }

        public Task Tick()
        {
            if (_loadSimulated)
            {
                _loadSimulated = false;
                if (IsSimulated)
                    _simulatedDb.Load(_simulatedDbFilepath, _simulatedEncryptionKey);
            }

            if (_saveSimulated)
            {
                _saveSimulated = false;
                if (!_simulatedDb.Save(_simulatedDbFilepath, _simulatedEncryptionKey))
                    _saveSimulated = true; // try again next tick
            }

            return Task.CompletedTask;
        }

        private void SaveSimulated()
        {
            if (string.IsNullOrEmpty(_simulatedDbFilepath))
                return;

            _saveSimulated = true;
        }

        #region Cache

        bool TryGetCache<T>(string key, out T value)
        {
            value = default;

            if (!_cache.TryGetValue(key, out var cacheItem))
                return false;

            if (cacheItem == null)
                return false;

            if (DateTime.UtcNow > cacheItem.UtcExpiresAt)
                return false;

            if (cacheItem.Data is not T tValue)
                return false;

            value = tValue;
            return true;
        }

        T SetCache<T>(string key, T value, TimeSpan? expiresIn = null)
        {
            expiresIn ??= TimeSpan.FromMinutes(1);

            if (!_cache.TryGetValue(key, out var cacheItem))
            {
                _cache.TryAdd(key, cacheItem = new CacheItem() { Data = value, UtcExpiresAt = DateTime.UtcNow + expiresIn.Value });
            }
            else
            {
                cacheItem.Data = value;
                cacheItem.UtcExpiresAt = DateTime.UtcNow + expiresIn.Value;
            }

            return value;
        }

        void InvalidateCache(string key)
        {
            _cache.TryRemove(key, out _);
        }

        #endregion

        #region Survival Db

        public async Task<bool> GetIsSupportedSurvivalMapAsync(string mapFilename)
        {
            bool result = false;

            try
            {
                if (IsSimulated)
                {
                    // always accept map in simulated mode
                    return true;
                }
                else
                {
                    var uri = $"Survival/getIsMap/{Uri.EscapeDataString(mapFilename)}";
                    if (TryGetCache<bool>(uri, out var value))
                        result = value;
                    else
                        result = SetCache(uri, await Program.Database.GetDbAsync<bool>(uri));
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task<SurvivalAccountStatDTO> GetSurvivalStatsAsync(int accountId)
        {
            SurvivalAccountStatDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    result = _simulatedDb.SurvivalAccountStats.FirstOrDefault(x => x.AccountId == accountId) ?? new SurvivalAccountStatDTO() { AccountId = accountId };

                    // update rankings on query
                    result.Ranking = _simulatedDb.SurvivalAccountStats.OrderByDescending(x => x.TotalPercentCompleted).Index().FirstOrDefault(x => x.Item == result).Index + 1;
                }
                else
                {
                    var uri = $"Survival/getAccountStats?AccountId={accountId}";
                    if (TryGetCache<SurvivalAccountStatDTO>(uri, out var value))
                        result = value;
                    else
                        result = SetCache(uri, await Program.Database.GetDbAsync<SurvivalAccountStatDTO>(uri));
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task<SurvivalAccountStatDTO> UpdateSurvivalStatsAsync(SurvivalAccountStatDTO accountStats)
        {
            SurvivalAccountStatDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    var existing = _simulatedDb.SurvivalAccountStats.FirstOrDefault(x => x.AccountId == accountStats.AccountId);
                    if (existing != null)
                    {
                        existing.GamesPlayed = accountStats.GamesPlayed;
                        existing.TimePlayedMs = accountStats.TimePlayedMs;
                        existing.Kills = accountStats.Kills;
                        existing.Deaths = accountStats.Deaths;
                        existing.Revives = accountStats.Revives;
                        existing.TimesRevived = accountStats.TimesRevived;
                        existing.WrenchKills = accountStats.WrenchKills;
                        existing.DualViperKills = accountStats.DualViperKills;
                        existing.MagmaCannonKills = accountStats.MagmaCannonKills;
                        existing.ArbiterKills = accountStats.ArbiterKills;
                        existing.FusionRifleKills = accountStats.FusionRifleKills;
                        existing.MineLauncherKills = accountStats.MineLauncherKills;
                        existing.B6Kills = accountStats.B6Kills;
                        existing.HoloshieldKills = accountStats.HoloshieldKills;
                        existing.ScorpionFlailKills = accountStats.ScorpionFlailKills;
                        result = existing;
                    }
                    else
                    {
                        result = existing = new SurvivalAccountStatDTO()
                        {
                            AccountId = accountStats.AccountId,
                            GamesPlayed = accountStats.GamesPlayed,
                            TimePlayedMs = accountStats.TimePlayedMs,
                            Kills = accountStats.Kills,
                            Deaths = accountStats.Deaths,
                            Revives = accountStats.Revives,
                            TimesRevived = accountStats.TimesRevived,
                            WrenchKills = accountStats.WrenchKills,
                            DualViperKills = accountStats.DualViperKills,
                            MagmaCannonKills = accountStats.MagmaCannonKills,
                            ArbiterKills = accountStats.ArbiterKills,
                            FusionRifleKills = accountStats.FusionRifleKills,
                            MineLauncherKills = accountStats.MineLauncherKills,
                            B6Kills = accountStats.B6Kills,
                            HoloshieldKills = accountStats.HoloshieldKills,
                            ScorpionFlailKills = accountStats.ScorpionFlailKills,
                        };

                        _simulatedDb.SurvivalAccountStats.Add(existing);
                    }

                    SaveSimulated();
                }
                else
                {
                    InvalidateCache($"Survival/getAccountStats?AccountId={accountStats.AccountId}");
                    result = await Program.Database.PostDbAsync<SurvivalAccountStatDTO>($"Survival/updateAccountStats", accountStats);
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task<SurvivalAccountMapStatDTO> GetSurvivalMapStatsAsync(int accountId, string mapFilename)
        {
            SurvivalAccountMapStatDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    result = _simulatedDb.SurvivalAccountMapStats.FirstOrDefault(x => x.AccountId == accountId && x.MapFilename == mapFilename) ?? new SurvivalAccountMapStatDTO() { AccountId = accountId, MapFilename = mapFilename };

                    // update rankings on query
                    if (result.Solo50 > 0)
                        result.Solo50Ranking = _simulatedDb.SurvivalAccountMapStats.Where(x => x.Solo50 > 0).OrderBy(x => x.Solo50).Index().FirstOrDefault(x => x.Item == result).Index + 1;
                    else
                        result.Solo50Ranking = null;

                    if (result.SoloRound > 0)
                        result.SoloRoundRanking = _simulatedDb.SurvivalAccountMapStats.Where(x => x.SoloRound > 0).OrderByDescending(x => x.SoloRound).Index().FirstOrDefault(x => x.Item == result).Index + 1;
                    else
                        result.SoloRoundRanking = null;
                }
                else
                {
                    var uri = $"Survival/getAccountMapStats?AccountId={accountId}&MapFilename={Uri.EscapeDataString(mapFilename)}";
                    if (TryGetCache<SurvivalAccountMapStatDTO>(uri, out var value))
                        result = value;
                    else
                        result = SetCache(uri, await Program.Database.GetDbAsync<SurvivalAccountMapStatDTO>(uri));
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task<SurvivalAccountMapStatDTO> UpdateSurvivalAccountMapStatsAsync(SurvivalAccountMapStatDTO accountMapStats)
        {
            SurvivalAccountMapStatDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    var existing = _simulatedDb.SurvivalAccountMapStats.FirstOrDefault(x => x.AccountId == accountMapStats.AccountId && x.MapFilename == accountMapStats.MapFilename);
                    if (existing != null)
                    {
                        existing.Xp = accountMapStats.Xp;
                        existing.Rank = accountMapStats.Rank;
                        existing.Prestige = accountMapStats.Prestige;
                        existing.PercentCompleted = accountMapStats.PercentCompleted;
                        existing.PercentCompleted = accountMapStats.PercentCompleted;
                        result = existing;
                    }
                    else
                    {
                        result = existing = new SurvivalAccountMapStatDTO()
                        {
                            AccountId = accountMapStats.AccountId,
                            MapFilename = accountMapStats.MapFilename,
                            Xp = accountMapStats.Xp,
                            Rank = accountMapStats.Rank,
                            Prestige = accountMapStats.Prestige,
                            PercentCompleted = accountMapStats.PercentCompleted,
                        };

                        _simulatedDb.SurvivalAccountMapStats.Add(existing);
                    }

                    // update account total percent completed
                    var accountStats = _simulatedDb.SurvivalAccountStats.FirstOrDefault(x => x.AccountId == accountMapStats.AccountId);
                    if (accountStats == null)
                    {
                        accountStats = new SurvivalAccountStatDTO() { AccountId = accountMapStats.AccountId };
                        _simulatedDb.SurvivalAccountStats.Add(accountStats);
                    }

                    var totalMaps = _simulatedDb.SurvivalAccountMapStats.Where(x => x.AccountId == accountStats.AccountId).DistinctBy(x => x.MapFilename).Count();
                    accountStats.TotalPercentCompleted = _simulatedDb.SurvivalAccountMapStats
                        .Where(x => x.AccountId == accountStats.AccountId)
                        .Sum(x => x.PercentCompleted) / Math.Max(1, totalMaps);

                    SaveSimulated();
                }
                else
                {
                    InvalidateCache($"Survival/getAccountMapStats?AccountId={accountMapStats.AccountId}&MapFilename={Uri.EscapeDataString(accountMapStats.MapFilename)}");
                    result = await Program.Database.PostDbAsync<SurvivalAccountMapStatDTO>($"Survival/updateAccountMapStats", accountMapStats);
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task UpdateSurvivalAccountMapGambitStatsAsync(int accountId, string mapFilename, string gambit, int? bestRound, bool? completed)
        {
            try
            {
                if (IsSimulated)
                {
                    var accountStats = _simulatedDb.SurvivalAccountMapStats.FirstOrDefault(x => x.AccountId == accountId && x.MapFilename == mapFilename);
                    if (accountStats == null)
                    {
                        accountStats = new SurvivalAccountMapStatDTO()
                        {
                            AccountId = accountId,
                            MapFilename = mapFilename,
                        };

                        _simulatedDb.SurvivalAccountMapStats.Add(accountStats);
                    }

                    var existing = accountStats.Gambits.FirstOrDefault(x => x.Gambit == gambit);
                    if (existing != null)
                    {
                        if (bestRound.HasValue && bestRound > existing.BestRound)
                            existing.BestRound = bestRound.Value;

                        if (completed.HasValue)
                            existing.Completed = completed.Value;
                    }
                    else
                    {
                        existing = new SurvivalAccountMapGambitStatDTO()
                        {
                            Gambit = gambit,
                            Completed = completed ?? false,
                            BestRound = bestRound ?? 0
                        };

                        accountStats.Gambits.Add(existing);
                    }

                    SaveSimulated();
                }
                else
                {
                    InvalidateCache($"Survival/getAccountMapStats?AccountId={accountId}&MapFilename={Uri.EscapeDataString(mapFilename)}");
                    await Program.Database.PostDbAsync($"Survival/updateAccountMapGambitStats", new SurvivalAccountMapGambitStatPostDTO()
                    {
                        AccountId = accountId,
                        MapFilename = mapFilename,
                        Gambit = gambit,
                        BestRound = bestRound,
                        Completed = completed
                    });
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }
        }

        public async Task<SurvivalRunDTO> CreateSurvivalMapRunAsync(SurvivalRunDTO survivalRun)
        {
            SurvivalRunDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    var rounds = survivalRun.RoundsCompleted;
                    var time50 = survivalRun.Time50Ms;
                    var isSolo = survivalRun.PlayerCountAtStart == 1;
                    foreach (var accountId in survivalRun.AccountIds)
                    {
                        var accountStats = _simulatedDb.SurvivalAccountMapStats.FirstOrDefault(x => x.AccountId == accountId);
                        if (accountStats == null)
                        {
                            accountStats = new SurvivalAccountMapStatDTO()
                            {
                                AccountId = accountId,
                                MapFilename = survivalRun.MapFilename,
                            };

                            _simulatedDb.SurvivalAccountMapStats.Add(accountStats);
                        }

                        // update round high score
                        if (isSolo && (rounds > accountStats.SoloRound || !accountStats.SoloRound.HasValue))
                            accountStats.SoloRound = rounds;
                        if (!isSolo && (rounds > accountStats.CoopRound || !accountStats.CoopRound.HasValue))
                            accountStats.CoopRound = rounds;

                        // update time 50 high score
                        if (isSolo && time50.HasValue && (time50 > accountStats.Solo50 || !accountStats.Solo50.HasValue))
                            accountStats.Solo50 = time50;
                        if (!isSolo && time50.HasValue && (time50 > accountStats.Coop50 || !accountStats.Coop50.HasValue))
                            accountStats.Coop50 = time50;
                    }

                    SaveSimulated();
                }
                else
                {
                    result = await Program.Database.PostDbAsync<SurvivalRunDTO>($"Survival/createMapRun", survivalRun);
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        //public async Task UpdateSurvivalMapRunAsync(SurvivalRunDTO survivalRun)
        //{
        //    try
        //    {
        //        if (IsSimulated)
        //        {

        //        }
        //        else
        //        {
        //            await Program.Database.PostDbAsync<SurvivalRunDTO>($"Survival/updateMapRun/{survivalRun.Id}", survivalRun);
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
        //    }
        //}

        #endregion

        #region ForgeCgm Db

        public async Task<ForgeCgmMapDTO> GetForgeCgmMapAsync(string mapFilename)
        {
            ForgeCgmMapDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    result = _simulatedDb.ForgeCgmMaps.FirstOrDefault(x => x.MapFilename == mapFilename) ?? new ForgeCgmMapDTO() { MapFilename = mapFilename };
                }
                else
                {
                    var uri = $"ForgeCgm/getMap/{Uri.EscapeDataString(mapFilename)}";
                    if (TryGetCache<ForgeCgmMapDTO>(uri, out var value))
                        result = value;
                    else
                        result = SetCache(uri, await Program.Database.GetDbAsync<ForgeCgmMapDTO>(uri));
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task<ForgeCgmMapDTO> UpdateForgeCgmMapAsync(string mapFilename, string name, string? sharedRankCode, string metadata)
        {
            ForgeCgmMapDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    var existing = _simulatedDb.ForgeCgmMaps.FirstOrDefault(x => x.MapFilename == mapFilename);
                    if (existing != null)
                    {
                        existing.Name = name;
                        existing.SharedRankCode = sharedRankCode;
                        existing.Metadata = metadata;
                        result = existing;
                    }
                    else
                    {
                        result = existing = new ForgeCgmMapDTO()
                        {
                            MapFilename = mapFilename,
                            Name = name,
                            SharedRankCode = sharedRankCode,
                            Metadata = metadata,
                        };

                        _simulatedDb.ForgeCgmMaps.Add(existing);
                    }

                    SaveSimulated();
                }
                else
                {
                    InvalidateCache($"ForgeCgm/getMap/{Uri.EscapeDataString(mapFilename)}");
                    result = await Program.Database.PostDbAsync<ForgeCgmMapDTO>($"ForgeCgm/updateMap", new ForgeCgmMapDTO() { MapFilename = mapFilename, Name = name, SharedRankCode = sharedRankCode, Metadata = metadata });
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task<ForgeCgmMapAccountStatDTO> GetForgeCgmMapAccountStatsAsync(int accountId, string mapFilename)
        {
            ForgeCgmMapAccountStatDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    var map = _simulatedDb.ForgeCgmMaps.FirstOrDefault(x => x.MapFilename == mapFilename);
                    if (map == null)
                        return result;

                    result = _simulatedDb.ForgeCgmMapAccountStats.FirstOrDefault(x => x.AccountId == accountId && x.MapFilename == mapFilename) ?? new ForgeCgmMapAccountStatDTO() { AccountId = accountId, MapFilename = mapFilename };
                
                    // get rank as highest among maps with shared rank code
                    if (!string.IsNullOrEmpty(map.SharedRankCode))
                    {
                        var sharedRankMapFilenames = _simulatedDb.ForgeCgmMaps.Where(x => x.SharedRankCode == map.SharedRankCode).Select(x => x.MapFilename).ToHashSet();
                        result.Rank = _simulatedDb.ForgeCgmMapAccountStats.Where(x => x.AccountId == accountId && sharedRankMapFilenames.Contains(x.MapFilename)).DefaultIfEmpty().Max(x => x.Rank);
                    }
                }
                else
                {
                    var uri = $"ForgeCgm/getMapAccountStats?AccountId={accountId}&MapFilename={Uri.EscapeDataString(mapFilename)}";
                    if (TryGetCache<ForgeCgmMapAccountStatDTO>(uri, out var value))
                        result = value;
                    else
                        result = SetCache(uri, await Program.Database.GetDbAsync<ForgeCgmMapAccountStatDTO>(uri));
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task<ForgeCgmMapAggregatedAccountStatDTO> GetForgeCgmMapAggregatedAccountStatsAsync(int accountId, string mapFilename)
        {
            ForgeCgmMapAggregatedAccountStatDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    var map = _simulatedDb.ForgeCgmMaps.FirstOrDefault(x => x.MapFilename == mapFilename);
                    if (map == null)
                        return result;

                    var accountStat = _simulatedDb.ForgeCgmMapAccountStats.FirstOrDefault(x => x.AccountId == accountId && x.MapFilename == mapFilename) ?? new ForgeCgmMapAccountStatDTO() { AccountId = accountId, MapFilename = mapFilename };
                    
                    if (!string.IsNullOrEmpty(map.SharedRankCode))
                    {
                        var sharedRankMapFilenames = _simulatedDb.ForgeCgmMaps.Where(x => x.SharedRankCode == map.SharedRankCode).Select(x => x.MapFilename).ToHashSet();
                        var allAccountDbStats = _simulatedDb.ForgeCgmMapAccountStats.Where(x => x.AccountId == accountId && sharedRankMapFilenames.Contains(x.MapFilename)).ToArray();
                        var ranking = _simulatedDb.ForgeCgmMapAccountStats
                            .Where(x => sharedRankMapFilenames.Contains(x.MapFilename))
                            .GroupBy(x => x.AccountId)
                            .Select(x => x.MaxBy(x => x.Rank))
                            .OrderByDescending(x => x.Rank)
                            .Index()
                            .FirstOrDefault(x => x.Item == accountStat).Index;

                        result = new ForgeCgmMapAggregatedAccountStatDTO()
                        {
                            AccountId = accountId,
                            Rank = allAccountDbStats.Max(x => x.Rank),
                            Ranking = ranking + 1,
                            Wins = allAccountDbStats.Sum(x => x.Wins),
                            Losses = allAccountDbStats.Sum(x => x.Losses),
                            GamesPlayed = allAccountDbStats.Sum(x => x.GamesPlayed),
                            TimePlayedMs = allAccountDbStats.Sum(x => x.TimePlayedMs),
                            TrackedStats = accountStat.TrackedStats.ToArray(),
                        };
                    }
                    else
                    {
                        result = new ForgeCgmMapAggregatedAccountStatDTO()
                        {
                            AccountId = accountId,
                            Rank = accountStat.Rank,
                            Ranking = _simulatedDb.ForgeCgmMapAccountStats.Where(x => x.MapFilename == mapFilename).OrderByDescending(x => x.Rank).Index().FirstOrDefault(x => x.Item == accountStat).Index + 1,
                            Wins = accountStat.Wins,
                            Losses = accountStat.Losses,
                            GamesPlayed = accountStat.GamesPlayed,
                            TimePlayedMs = accountStat.GamesPlayed,
                            TrackedStats = accountStat.TrackedStats.ToArray(),
                        };
                    }
                }
                else
                {
                    var uri = $"ForgeCgm/getMapAggregatedAccountStats?AccountId={accountId}&MapFilename={Uri.EscapeDataString(mapFilename)}";
                    if (TryGetCache<ForgeCgmMapAggregatedAccountStatDTO>(uri, out var value))
                        result = value;
                    else
                        result = SetCache(uri, await Program.Database.GetDbAsync<ForgeCgmMapAggregatedAccountStatDTO>(uri));
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        public async Task<ForgeCgmMapAccountStatDTO> UpdateForgeCgmMapAccountStatsAsync(ForgeCgmMapAccountStatDTO stats)
        {
            ForgeCgmMapAccountStatDTO result = null;

            try
            {
                if (IsSimulated)
                {
                    var map = _simulatedDb.ForgeCgmMaps.FirstOrDefault(x => x.MapFilename == stats.MapFilename);
                    if (map == null)
                        return result;

                    var existing = _simulatedDb.ForgeCgmMapAccountStats.FirstOrDefault(x => x.AccountId == stats.AccountId && x.MapFilename == stats.MapFilename);
                    if (existing != null)
                    {
                        existing.Rank = stats.Rank;
                        existing.GamesPlayed = stats.GamesPlayed;
                        existing.TimePlayedMs = stats.TimePlayedMs;
                        existing.TrackedStats = stats.TrackedStats.ToArray();
                        result = existing;
                    }
                    else
                    {
                        result = existing = new ForgeCgmMapAccountStatDTO()
                        {
                            AccountId = stats.AccountId,
                            MapFilename = stats.MapFilename,
                            Rank = stats.Rank,
                            GamesPlayed = stats.GamesPlayed,
                            TimePlayedMs = stats.TimePlayedMs,
                            TrackedStats = stats.TrackedStats.ToArray(),
                        };

                        _simulatedDb.ForgeCgmMapAccountStats.Add(existing);
                    }

                    // update rank for all maps with shared rank code
                    if (!string.IsNullOrEmpty(map.SharedRankCode))
                    {
                        var sharedRankMapFilenames = _simulatedDb.ForgeCgmMaps.Where(x => x.SharedRankCode == map.SharedRankCode).Select(x => x.MapFilename).ToHashSet();
                        foreach (var sharedRankMapFilename in sharedRankMapFilenames)
                        {
                            var dbStats = _simulatedDb.ForgeCgmMapAccountStats.FirstOrDefault(x => x.AccountId == stats.AccountId && x.MapFilename == sharedRankMapFilename);
                            if (dbStats != null)
                            {
                                dbStats.Rank = stats.Rank;
                            }
                        }
                    }

                    SaveSimulated();
                }
                else
                {
                    InvalidateCache($"ForgeCgm/getMapAccountStats?AccountId={stats.AccountId}&MapFilename={Uri.EscapeDataString(stats.MapFilename)}");
                    InvalidateCache($"ForgeCgm/getMapAggregatedAccountStats?AccountId={stats.AccountId}&MapFilename={Uri.EscapeDataString(stats.MapFilename)}");
                    result = await Program.Database.PostDbAsync<ForgeCgmMapAccountStatDTO>($"ForgeCgm/updateMapAccountStats", stats);
                }
            }
            catch (Exception e)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, e);
            }

            return result;
        }

        #endregion

    }

    class PluginDatabaseSimulated
    {
        public List<SurvivalAccountStatDTO> SurvivalAccountStats { get; set; } = new List<SurvivalAccountStatDTO>();
        public List<SurvivalAccountMapStatDTO> SurvivalAccountMapStats { get; set; } = new List<SurvivalAccountMapStatDTO>();
        public List<ForgeCgmMapDTO> ForgeCgmMaps { get; set; } = new List<ForgeCgmMapDTO>();
        public List<ForgeCgmMapAccountStatDTO> ForgeCgmMapAccountStats { get; set; } = new List<ForgeCgmMapAccountStatDTO>();

        public bool Save(string filepath, string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Encryption key cannot be null or empty.");

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = DeriveKey(key, aes.KeySize / 8);
                    aes.GenerateIV();

                    using (var outStream = new MemoryStream())
                    {
                        outStream.Write(aes.IV, 0, aes.IV.Length); // store IV at start of file

                        using (var cryptoStream = new CryptoStream(outStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
                        using (var inStream = new MemoryStream(jsonBytes))
                        {
                            inStream.CopyTo(cryptoStream);
                        }

                        // save
                        File.WriteAllBytes(filepath, outStream.ToArray());
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save database: {ex}");
                return false;
            }
        }

        public void Load(string filepath, string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Encryption key cannot be null or empty.");

            if (!File.Exists(filepath))
                return;

            var inBytes = File.ReadAllBytes(filepath);
            using (var inStream = new MemoryStream(inBytes))
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = DeriveKey(key, aes.KeySize / 8);

                    var iv = new byte[aes.BlockSize / 8];
                    inStream.Read(iv, 0, iv.Length);
                    aes.IV = iv;

                    using (var cryptoStream = new CryptoStream(inStream, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    using (var outStream = new MemoryStream())
                    {
                        cryptoStream.CopyTo(outStream);

                        var jsonBytes = outStream.ToArray();
                        var json = Encoding.UTF8.GetString(jsonBytes);
                        var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<PluginDatabaseSimulated>(json);

                        // copy into this
                        this.SurvivalAccountStats = obj.SurvivalAccountStats;
                        this.SurvivalAccountMapStats = obj.SurvivalAccountMapStats;
                    }
                }
            }
        }


        // Derive a fixed-length key from a passphrase
        private static byte[] DeriveKey(string password, int length)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                Array.Resize(ref hash, length);
                return hash;
            }
        }
    }
}
