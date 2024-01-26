using Horizon.Plugin.Deadlocked.Messages;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public enum QueueIds
    {
        None,
        KOTH,
        CTF,
        FFA_DM
    }

    public static class Queue
    {
        static readonly Random _rng = new Random();

        // number of seconds before a queued player will start expanding their potential matches (by rank point delta)
        static readonly int secondsBeforeMatchExpansionBegins = 5;

        // initial max delta rank points for a match between two players
        static readonly int initialMaxRankPointsForMatch = 300;

        // foreach match expansion
        // how many rank points to increase the max possible rank delta for a match
        static readonly int rankPointsPerMatchExpansion = 300;

        // after match expansion begins
        // match expansion will trigger every n seconds
        static readonly int secondsForMatchExpansionIncrease = 1;

        // we add a delay after a match is found
        // so that players have a chance to trickle in before the queue pops
        public const int SecondsAfterPlayableMatchBeforeCreating = 5;



        /// <summary>
        /// Min number of seconds in queue before any client can join a game.
        /// This is to give time for things to 'settle' before making an action
        /// </summary>
        public const int MinQueueTimeSeconds = 15;

        public class QueueClient
        {
            public DateTime QueueBegan { get; set; } = DateTime.UtcNow;
            public DateTime? TimeLastJoinedMatch { get; set; }
            public DateTime? TimeLastLeftMatch { get; set; }
            public DateTime? TimeLastForceJoinSent { get; set; }
            public int AccountId { get; set; }
            public int Rank { get; set; }
            public ClientObject Client { get; set; }

            public QueueClient(ClientObject client)
            {
                AccountId = client.AccountId;
                Client = client;
            }

            public bool CanMatchWith(int rank)
            {
                var delta = Math.Abs(rank - Rank);

                // the longer in queue the more we want to just find a match
                var range = Math.Max(0, ((DateTime.UtcNow - QueueBegan).TotalSeconds - 15) * 60) + (Rank * 0.2);
                //var range = 10000;
                return delta < range;
            }

            public bool CanMatchWith(QueueClient b)
            {
                var delta = Math.Abs(this.Rank - b.Rank);
                var secondsSinceQueueTooLong = (DateTime.UtcNow - QueueBegan).TotalSeconds - secondsBeforeMatchExpansionBegins;
                var maxDelta = initialMaxRankPointsForMatch + Math.Max(0, (int)(secondsSinceQueueTooLong / secondsForMatchExpansionIncrease) * rankPointsPerMatchExpansion);


                //return CanMatchWith(b.Rank) && b.CanMatchWith(this.Rank);
                return delta <= maxDelta;
            }
        }

        public class QueueMatch
        {
            public DateTime MatchCreated { get; set; } = DateTime.UtcNow;
            public DateTime? MatchIdeal { get; set; }
            public DateTime? MatchPlayable { get; set; }
            public List<QueueClient> Clients { get; set; } = new List<QueueClient>();
            public bool IsDestroyed { get; private set; }
            public CompGame Game { get; private set; }
            public QueueInstance Queue { get; set; }

            public bool CanMatchWith(QueueClient client)
            {
                return Clients.All(x => client.CanMatchWith(x));
            }

            public int GetMatchScore(QueueClient client)
            {
                var averageDeltaRank = Math.Abs(Clients.Sum(x => x.Rank - client.Rank)) / Clients.Count;

                // flattens delta ranks so that similar delta ranks receive the same score
                return averageDeltaRank / 100;
            }

            public void Destroy(bool cancel)
            {
                if (Game != null && cancel)
                    Game.Cancel(ForceLeaveGameRequestMessage.ForceLeaveReason.QUEUE_PLAYERS_NOT_JOINED);
                IsDestroyed = true;
                Clients.Clear();
            }

            public bool HasGameAndGameIsStartedOrEnded()
            {
                return Game != null && (Game.WorldStatus == RT.Common.MediusWorldStatus.WorldActive || Game.WorldStatus == RT.Common.MediusWorldStatus.WorldClosed);
            }

            public void SetGame(CompGame game)
            {
                Game = game;
            }

            public void AddClient(QueueClient client)
            {
                Clients.Add(client);
                client.TimeLastJoinedMatch = DateTime.UtcNow;

                // if this match has a game then send the client to the game
                if (Game != null && Game.WorldStatus == RT.Common.MediusWorldStatus.WorldStaging && Game.Clients.Any(x => x.InGame))
                {
                    // tell client to join
                    Game.ForceJoinPlayer(client.Client);

                    // resend game flags
                    foreach (var qClient in Clients)
                        if (qClient != client)
                            Game.ForceJoinPlayer(qClient.Client);
                }

            }

            public void RemoveClient(QueueClient client)
            {
                Clients.Remove(client);
                client.TimeLastJoinedMatch = DateTime.UtcNow;

                // if this match has a game then tell the client to leave
                // and resend the game config to the others
                if (Game != null && Game.WorldStatus == RT.Common.MediusWorldStatus.WorldStaging && Game.Clients.Any(x => x.InGame))
                {
                    foreach (var qClient in Clients)
                    {
                        if (qClient != client)
                            Game.ForceJoinPlayer(qClient.Client);
                        else
                            qClient.Client.Queue(new ForceLeaveGameRequestMessage() { Reason = ForceLeaveGameRequestMessage.ForceLeaveReason.NONE });
                    }
                }
            }

            public void OnClientLeftGame(ClientObject client)
            {
                if (client == null)
                    return;

                // remove from match
                var queueClient = Clients.FirstOrDefault(x => x.Client == client);
                if (queueClient != null)
                {
                    queueClient.TimeLastLeftMatch = DateTime.UtcNow;
                    Clients.Remove(queueClient);
                }

                // remove from queue if game is still there
                //if (Game != null && Game.WorldStatus == RT.Common.MediusWorldStatus.WorldStaging)
                //    Queue?.Queue?.RemoveAll(x => x.Client == client);

                // if this match has a game then send the client to the game
                if (Game != null && Game.WorldStatus == RT.Common.MediusWorldStatus.WorldStaging && Game.Clients.Any(x => x.InGame && x.Client == client))
                    client.Queue(new ForceLeaveGameRequestMessage() { Reason = ForceLeaveGameRequestMessage.ForceLeaveReason.NONE });
            }

            public int GetBestNumTeams()
            {
                // determine number of teams
                var numTeams = Queue.IdealNumTeams;
                if ((Clients.Count % numTeams) != 0)
                {
                    numTeams = Queue.MaxNumTeams;
                    while ((Clients.Count % numTeams) != 0)
                    {
                        --numTeams;
                    }
                }

                return numTeams;
            }
        }

        public class QueueInstance
        {
            public QueueIds Id { get; }
            public int ApplicationId { get; }
            public List<QueueClient> Queue { get; set; } = new List<QueueClient>();

            public bool IsFreeForAll { get; set; } = false;
            public int MaxNumTeams { get; set; } = 10;
            public int IdealNumTeams { get; set; } = 2;
            public int[] IdealTeamIds { get; set; } = null;
            public int[] MaxTeamIds { get; set; } = null;
            public int MinNumPlayers { get; set; } = 2;
            public int MaxNumPlayers { get; set; } = 10;
            public int IdealNumPlayers { get; set; } = 10;

            public int GenericField1 { get; set; }
            public int GenericField3 { get; set; }
            public int GenericField4 { get; set; }
            public int GenericField5 { get; set; }
            public int GenericField6 { get; set; }
            public int GenericField7 { get; set; }
            public int GenericField8 { get; set; }
            public int RulesSet { get; set; }
            public MapId[] MapIds { get; set; }
            public CustomMapId[] CustomMapIds { get; set; }
            public uint WeaponFlags { get; set; }
            public Func<QueueMatch, byte[]> GetGameFlags { get; set; }
            public Func<QueueMatch, GameConfig> GetConfig { get; set; }
            public Func<CompGame, QueueMatch, int[]> GetTeamIds { get; set; }
            public Func<ClientObject, int> GetRank { get; set; }
            public Func<ClientObject, int> GetBaseRank { get; set; }

            private List<QueueMatch> matches { get; } = new List<QueueMatch>();

            public QueueInstance(int appId, QueueIds id)
            {
                ApplicationId = appId;
                Id = id;
            }

            public bool Has(ClientObject client)
            {
                return Queue.Any(x => x.AccountId == client.AccountId);
            }

            public void Leave(ClientObject client)
            {
                Queue.RemoveAll(x => x.AccountId == client.AccountId);
                foreach (var match in matches)
                {
                    if (match.Clients.RemoveAll(x => x.AccountId == client.AccountId) > 0)
                    {
                        if (match.Clients.Count < MinNumPlayers)
                        {
                            match.Destroy(true);
                        }
                    }
                }
            }

            public void Join(ClientObject client)
            {
                // prevent adding same client twice
                if (Has(client))
                    return;

                Queue.Add(new QueueClient(client) { Rank = Math.Max(100, Math.Min(10000, GetRank(client))) });
            }

            public async Task Tick()
            {
                // first pass - remove destroyed matches
                var matchesToRemove = matches.Where(x => x.IsDestroyed || x.HasGameAndGameIsStartedOrEnded());
                foreach (var match in matchesToRemove)
                {
                    foreach (var client in match.Clients)
                        this.Queue.Remove(client);
                }

                matches.RemoveAll(x => x.IsDestroyed || x.HasGameAndGameIsStartedOrEnded());

                // second pass - gather free clients (prioritize older queuers)
                var waitUntilTime = DateTime.UtcNow.AddSeconds(-5);
                var freeClients = Queue.Where(x => !matches.Any(m => m.Clients.Contains(x)) && (!x.TimeLastLeftMatch.HasValue || waitUntilTime > x.TimeLastLeftMatch) && waitUntilTime > x.QueueBegan).OrderBy(x => x.QueueBegan).ToList();

                // third pass - distribute free clients to best matching queue in need if they fit the ideal team constraint
                foreach (var match in matches.Where(x => MatchIsAcceptingNewClients(x)).OrderBy(x => GetMatchPriority(x)).ThenBy(x => x.MatchCreated))
                {
                    var freeClientsByFit = freeClients.OrderBy(x => (int)(DateTime.UtcNow - x.QueueBegan).TotalMinutes).ThenBy(x => match.GetMatchScore(x)).ToList();

                    if (IsFreeForAll)
                    {
                        // add players until ideal is met
                        int i = 0;
                        while (i < freeClientsByFit.Count && match.Clients.Count < IdealNumPlayers)
                        {
                            match.AddClient(freeClientsByFit[i]);
                            freeClients.Remove(freeClientsByFit[i]);
                            ++i;
                        }
                    }
                    else
                    {
                        var missingForGoodTeams = IdealNumTeams - (match.Clients.Count % IdealNumTeams);
                        if (missingForGoodTeams == IdealNumTeams)
                            missingForGoodTeams = 0;

                        // if it fits our constraints then add players to match
                        if (missingForGoodTeams > 0 && freeClientsByFit.Count >= missingForGoodTeams)
                        {
                            for (int i = 0; i < missingForGoodTeams; ++i)
                            {
                                match.AddClient(freeClientsByFit[i]);
                                freeClients.Remove(freeClientsByFit[i]);
                            }
                        }
                    }
                }

                // fourth pass - distribute remaining free clients to any match that will take them
                foreach (var match in matches.Where(x => MatchIsAcceptingNewClients(x)).OrderBy(x => GetMatchPriority(x)).ThenBy(x => x.MatchCreated))
                {
                    var freeClientsByFit = freeClients.OrderBy(x => (int)(DateTime.UtcNow - x.QueueBegan).TotalMinutes).ThenBy(x => match.GetMatchScore(x)).ToList();

                    if (IsFreeForAll)
                    {
                        // add first free
                        if (freeClientsByFit.Count > 0)
                        {
                            match.AddClient(freeClientsByFit[0]);
                            freeClients.Remove(freeClientsByFit[0]);
                        }
                    }
                    else
                    {
                        // if it fits our constraints then add players to match
                        if (freeClientsByFit.Count >= IdealNumTeams)
                        {
                            for (int i = 0; i < IdealNumTeams; ++i)
                            {
                                match.AddClient(freeClientsByFit[i]);
                                freeClients.Remove(freeClientsByFit[i]);
                            }
                        }
                    }
                }

                // fourth pass - mark each match as ideal/playable then start if applicable
                foreach (var match in matches)
                {
                    var playable = IsMatchPlayable(match);
                    var ideal = playable && IsMatchIdeal(match);

                    // mark time became playable
                    if (playable && !match.MatchPlayable.HasValue)
                    {
                        match.MatchPlayable = DateTime.UtcNow;
                    }
                    else if (!playable && match.MatchPlayable.HasValue)
                    {
                        match.MatchPlayable = null;
                    }

                    // mark time became ideal
                    if (ideal && !match.MatchIdeal.HasValue)
                    {
                        match.MatchIdeal = DateTime.UtcNow;
                    }
                    else if (!ideal && match.MatchIdeal.HasValue)
                    {
                        match.MatchIdeal = null;
                    }

                    // start match if enough time has passed since last playable
                    if (playable && (DateTime.UtcNow - match.MatchPlayable)?.TotalSeconds > SecondsAfterPlayableMatchBeforeCreating && match.Game == null)
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, "Game created");
                        await Create(match);
                    }
                }

                // fourth pass - move clients from newer matches to older ones
                var orderedMatches = matches.Where(x => !x.MatchIdeal.HasValue).OrderByDescending(x => GetMatchPriority(x)).ToList();
                for (int fromIdx = 0; fromIdx < orderedMatches.Count; ++fromIdx)
                {
                    // skip if match is ideal
                    var fromMatch = orderedMatches[fromIdx];
                    if (IsMatchIdeal(fromMatch))
                        continue;

                    for (int toIdx = fromIdx + 1; toIdx < orderedMatches.Count; ++toIdx)
                    {
                        // skip if target match is ideal or is full
                        var toMatch = orderedMatches[toIdx];
                        if (IsMatchIdeal(toMatch))
                            continue;

                        foreach (var client in fromMatch.Clients.OrderBy(x => x.QueueBegan))
                        {
                            // stop if target match is full
                            if (toMatch.Clients.Count >= MaxNumPlayers)
                                break;

                            if (toMatch.CanMatchWith(client))
                            {
                                // move to target match
                                fromMatch.RemoveClient(client);
                                toMatch.AddClient(client);
                            }
                        }
                    }

                    // destroy if less than min players
                    if (fromMatch.Clients.Count < MinNumPlayers)
                        fromMatch.Destroy(true);
                }

                // final pass - create new matches where applicable
                var remainingFreeClients = Queue.Where(x => !matches.Any(m => m.Clients.Contains(x))).OrderBy(x => x.QueueBegan).ToList();
                var matchedClients = new List<QueueClient>();
                foreach (var clientA in remainingFreeClients)
                {
                    // already added
                    if (matchedClients.Contains(clientA))
                        continue;

                    var pool = new List<QueueClient>() { clientA };
                    foreach (var clientB in remainingFreeClients)
                        if (clientB != clientA && !matchedClients.Contains(clientB) && clientB.CanMatchWith(clientA))
                            pool.Add(clientB);

                    if (pool.Count >= MinNumPlayers)
                    {
                        var match = new QueueMatch() { Queue = this };
                        foreach (var client in pool)
                            match.AddClient(client);
                        matches.Add(match);
                        matchedClients.AddRange(pool);
                    }
                }
            }

            private bool IsMatchPlayable(QueueMatch match)
            {
                // check number of clients
                var clientCount = match.Clients.Count(x => x.Client.IsConnected);
                if (clientCount < MinNumPlayers)
                    return false;
                if (clientCount > MaxNumPlayers)
                    return false;

                // number of clients can't be evenly divided into max num teams
                var numTeams = match.GetBestNumTeams();
                if (numTeams == 0 || (clientCount > numTeams && (clientCount % numTeams) != 0))
                    return false;

                return true;
            }

            private bool MatchIsAcceptingNewClients(QueueMatch match)
            {
                return match.Clients.Count < MaxNumPlayers && (match.Game == null || (match.Game.WorldStatus == RT.Common.MediusWorldStatus.WorldStaging && (!match.Game.ForceStartAt.HasValue || (match.Game.ForceStartAt.Value - DateTime.UtcNow).TotalSeconds > 5)));
            }

            private int GetMatchPriority(QueueMatch match)
            {
                // lower value is higher priority
                // prioritize by less than ideal player size
                // and closest to player size

                var pCount = match.Clients.Count;
                if (pCount == MaxNumPlayers)
                    return int.MaxValue;

                if (pCount % IdealNumTeams != 0)
                    return IdealNumTeams - (pCount % IdealNumTeams);

                if (pCount < IdealNumPlayers)
                    return IdealNumPlayers - pCount;

                if (pCount > IdealNumPlayers)
                    return 10 + (pCount - IdealNumPlayers);

                return 20;
            }

            private bool IsMatchIdeal(QueueMatch match)
            {
                // check number of clients
                return IdealNumPlayers == match.Clients.Count(x => x.Client.IsConnected);
            }

            public (MapId baseMap, CustomMapId? customMap) GetRandomMap()
            {
                var mapCount = MapIds.Length + CustomMapIds.Length;
                var idx = _rng.Next(mapCount);
                if (idx >= MapIds.Length)
                {
                    var customMap = Maps.FindCustomMapById(CustomMapIds[idx - MapIds.Length]);
                    return ((MapId)customMap.LoadingMapId, customMap.MapId);
                }
                else
                {
                    return (MapIds[idx], null);
                }
            }

            private async Task Create(QueueMatch match)
            {
                (var baseMap, var customMap) = GetRandomMap();

                var channel = new CompChannel(ApplicationId);
                var game = new CompGame(channel)
                {
                    GameLevel = (int)baseMap,
                    RulesSet = RulesSet,
                    GenericField1 = GenericField1,
                    GenericField3 = GenericField3,
                    GenericField4 = GenericField4,
                    GenericField5 = GenericField5,
                    GenericField6 = GenericField6,
                    GenericField7 = GenericField7,
                    GenericField8 = GenericField8,
                };

                // create metadata
                var metadata = await Game.GetGameMetadata(game);
                metadata.GameConfig = GetConfig(match) ?? new GameConfig();
                metadata.GameConfig.MapOverride = (byte?)customMap ?? 0;

                // create game
                if (game.Create(this, match))
                {
                    Server.Medius.Program.Manager.AddChannel(channel);
                }
                
                // send message to clients
                foreach (var client in match.Clients)
                {
                    client.Client.Queue(new ShowSnackMessageRequestMessage()
                    {
                        Message = "Match found.. creating lobby.."
                    });
                }

                // destroy match
                //match.Destroy();
                match.SetGame(game);
            }
        }

        static readonly QueueInstance[] _queues = new QueueInstance[]
        {
            new QueueInstance(11184, QueueIds.KOTH)
            {
                MaxNumTeams = 5,
                IdealNumTeams = 2,
                MinNumPlayers = 3,
                MaxNumPlayers = 10,
                IdealNumPlayers = 6,
                MapIds = new MapId[]
                {
                    MapId.CATACROM,
                    MapId.SARATHOS,
                    MapId.SHAAR,
                    MapId.VALIX,
                    MapId.TORVAL,
                    MapId.MARAXUS,
                },
                CustomMapIds = new CustomMapId[]
                {
                    CustomMapId.CMAP_ID_ALPINE_JUNCTION,
                    CustomMapId.CMAP_ID_BAKISI_ISLES,
                    CustomMapId.CMAP_ID_GHOST_HANGAR,
                },
                RulesSet = 3,
                GenericField1 = 0,
                GenericField3 = 0,
                GenericField4 = 60,
                GenericField5 = 0,
                GenericField6 = 0x006631540,
                GenericField7 = 0x0120C6F28,
                GenericField8 = 5609,
                WeaponFlags = 0x000660AA,
                GetGameFlags = (match) => Server.Common.Utils.FromString("0000000000000101000000000000010000010001FFFF00010A000000000000010101010005031E00010F010A3C0100000001000300000100000101"),
                GetConfig = (match) => new GameConfig()
                {
                    BetterHills = true,
                    NewPlayerSync = true,
                    BetterFlags = true,
                    DisableHealthBoxes = 1,
                    DisableWeaponPacks = true,
                    DisableInvHitTimer = true,
                    FusionShotsAlwaysHit = true,
                    HideWeaponPickups = true,
                },
                GetTeamIds = (game, match) =>
                {
                    return Enumerable.Range(0, 10).OrderBy(x=> Guid.NewGuid()).ToArray();
                },
                GetBaseRank = (client) =>
                {
                    return client.WideStats[(int)PlayerStatIds.STAT_KOTH_RANK];
                },
                GetRank = (client) =>
                {
                    return client.WideStats[(int)PlayerStatIds.STAT_KOTH_RANK];
                }
            },
            new QueueInstance(11184, QueueIds.CTF)
            {
                MaxNumTeams = 2,
                IdealNumTeams = 2,
                MinNumPlayers = 1,
                MaxNumPlayers = 10,
                IdealNumPlayers = 6,
                MapIds = new MapId[]
                {
                    MapId.CATACROM,
                    MapId.SARATHOS,
                    MapId.SHAAR,
                    MapId.VALIX,
                    MapId.TORVAL,
                    MapId.MARAXUS,
                },
                CustomMapIds = new CustomMapId[]
                {
                    CustomMapId.CMAP_ID_ALPINE_JUNCTION,
                    CustomMapId.CMAP_ID_BAKISI_ISLES,
                    CustomMapId.CMAP_ID_GHOST_HANGAR,
                    CustomMapId.CMAP_ID_MARCADIA_PALACE,
                    CustomMapId.CMAP_ID_BLACKWATER_CITY,
                    CustomMapId.CMAP_ID_BLACKWATER_DOCKS
                },
                RulesSet = 1,
                GenericField1 = 0,
                GenericField3 = 0,
                GenericField4 = 0,
                GenericField5 = 1,
                GenericField6 = 0x026631540,
                GenericField7 = 0x012046F18,
                GenericField8 = 5609,
                WeaponFlags = 0x000660AA,
                GetGameFlags = (match) =>  Server.Common.Utils.FromString("0000000000000101000000010000010000010100FFFF00010A000000000000010101010004021E00010F010A1E0100000001000300000100000100"),
                GetConfig = (match) => new GameConfig()
                {
                    HalfTime = true,
                    Overtime = true,
                    BetterFlags = true,
                    BetterHills = true,
                    NewPlayerSync = true,
                    DisableHealthBoxes = 1,
                    HideWeaponPickups = true,
                    DisableWeaponPacks = true,
                    DisableInvHitTimer = true,
                    FusionShotsAlwaysHit = true,
                },
                GetTeamIds = (game, match) =>
                {
                    switch ((MapId)game.GameLevel)
                    {
                        case MapId.SARATHOS:
                        case MapId.TEMPUS: return new int[] { 1, 2 }; // red/green
                        default: return new int[] { 0, 1 }; // red/blue
                    }
                },
                GetBaseRank = (client) =>
                {
                    return client.WideStats[(int)PlayerStatIds.STAT_CTF_RANK];
                },
                GetRank = (client) =>
                {
                    return client.WideStats[(int)PlayerStatIds.STAT_CTF_RANK];
                }
            },
            new QueueInstance(11184, QueueIds.FFA_DM)
            {
                IsFreeForAll = true,
                MaxNumTeams = 10,
                IdealNumTeams = 10,
                MinNumPlayers = 2,
                MaxNumPlayers = 10,
                IdealNumPlayers = 4,
                MapIds = new MapId[]
                {
                    MapId.CATACROM,
                    MapId.SARATHOS,
                    MapId.DARK_CATHEDRAL,
                    MapId.SHAAR,
                    MapId.VALIX,
                    MapId.MINING_FACILITY,
                    MapId.TORVAL,
                    MapId.TEMPUS,
                    MapId.MARAXUS,
                    MapId.GHOST_STATION
                },
                CustomMapIds = new CustomMapId[]
                {
                    CustomMapId.CMAP_ID_BAKISI_ISLES,
                    CustomMapId.CMAP_ID_GHOST_HANGAR,
                },
                RulesSet = 2,
                GenericField1 = 0,
                GenericField3 = 0,
                GenericField4 = 0,
                GenericField5 = 1,
                GenericField6 = 0x40631540,
                GenericField7 = 0x120C6F08,
                GenericField8 = 7824,
                WeaponFlags = 0x000660AA,
                GetGameFlags = (match) =>
                {
                    if (match.Clients.Count == 2)
                        return Server.Common.Utils.FromString("0000000000000000000000000100010000010000FFFF00010A000000000000010101010000031E00010F010A1E0100000001000300000100000101");

                    return Server.Common.Utils.FromString("0000000000000000000000000100010000010000FFFF00010A000000000000010101010005031E00010F010A1E0100000001000300000100000101");
                },
                GetConfig = (match) =>
                {
                    if (match.Clients.Count == 2)
                    {
                        return new GameConfig()
                        {
                            DisableWeaponPacks = true,
                            HideWeaponPickups = true,
                            DisableInvHitTimer = true,
                            FusionShotsAlwaysHit = true,
                            NewPlayerSync = true,
                            Vampire = 3,
                            DisableHealthBoxes = 2,
                            V2s = 2
                        };
                    }

                    return new GameConfig()
                    {
                        DisableWeaponPacks = true,
                        HideWeaponPickups = true,
                        DisableInvHitTimer = true,
                        FusionShotsAlwaysHit = true,
                        NewPlayerSync = true,
                        DisableHealthBoxes = 1,
                    };
                },
                GetTeamIds = (game, match) =>
                {
                    return Enumerable.Range(0, 10).OrderBy(x=> Guid.NewGuid()).ToArray();
                },
                GetBaseRank = (client) =>
                {
                    return client.WideStats[(int)PlayerStatIds.STAT_DEATHMATCH_RANK];
                },
                GetRank = (client) =>
                {
                    return client.WideStats[(int)PlayerStatIds.STAT_DEATHMATCH_RANK];
                }
            }
        };

        public static Task Tick()
        {
            return Task.WhenAll(_queues.Select(x => x.Tick()));
        }

        public static Task OnQueueRequest(ClientObject client, QueueIds queueId)
        {
            var newQueueId = AddToQueue(client, queueId);
            var newQueue = _queues.FirstOrDefault(x => x.Id == newQueueId);
            var me = newQueue?.Queue?.FirstOrDefault(x => x.AccountId == client.AccountId);
            
            // send negative status when requested id doesn't match resulting id (request rejected)
            client.Queue(new QueueBeginResponseMessage()
            {
                CurrentQueueId = newQueueId,
                PlayersInQueue = newQueue?.Queue?.Count ?? 0,
                SecondsInQueue = (int)((DateTime.UtcNow - me?.QueueBegan)?.TotalSeconds ?? 0),
                Status = newQueueId != queueId ? -1 : 0
            });

            return Task.CompletedTask;
        }

        public static Task OnGetMyQueue(ClientObject client)
        {
            var queue = GetQueue(client);
            var me = queue?.Queue?.FirstOrDefault(x => x.AccountId == client.AccountId);

            // send negative status when requested id doesn't match resulting id (request rejected)
            client.Queue(new GetMyQueueResponseMessage()
            {
                CurrentQueueId = queue?.Id ?? QueueIds.None,
                PlayersInQueue = queue?.Queue?.Count ?? 0,
                SecondsInQueue = (int)((DateTime.UtcNow - me?.QueueBegan)?.TotalSeconds ?? 0),
            });

            return Task.CompletedTask;
        }

        public static Task OnPlayerLoggedIn(ClientObject client)
        {
            // check if client has channel it can connect to
            // if not, make one
            var channel = Server.Medius.Program.Manager.GetChannelByChannelName("Default", client.ApplicationId);
            if (channel == null)
            { 
                Server.Medius.Program.Manager.AddChannel(new Channel() { Name = "Default", Type = ChannelType.Lobby, ApplicationId = client.ApplicationId });
            }

            return Task.CompletedTask;
        }

        public static Task OnPlayerLoggedOut(ClientObject client)
        {
            // remove client from current queue
            var currentQueue = GetQueue(client);
            if (currentQueue != null)
                currentQueue.Leave(client);

            return Task.CompletedTask;
        }

        private static QueueIds AddToQueue(ClientObject client, QueueIds id)
        {
            var queueInstance = _queues.FirstOrDefault(x => x.Id == id);
            var currentQueue = GetQueue(client);

            // leave current queue
            if (id == QueueIds.None)
            {
                currentQueue?.Leave(client);
                return id;
            }

            // invalid queue id
            if (queueInstance == null)
            {
                return currentQueue?.Id ?? QueueIds.None;
            }

            // already in queue
            if (id == currentQueue?.Id)
            {
                return id;
            }

            // leave current queue
            if (currentQueue != null)
                currentQueue.Leave(client);

            // add to new queue
            queueInstance.Join(client);

            return queueInstance.Id;
        }

        private static QueueInstance GetQueue(ClientObject client)
        {
            return _queues.FirstOrDefault(x => x.Has(client));
        }
    }
}
