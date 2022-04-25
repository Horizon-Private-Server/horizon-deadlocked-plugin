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
        CTF
    }

    public static class Queue
    {
        static readonly Random _rng = new Random();

        public class QueueClient
        {
            public DateTime QueueBegan { get; set; } = DateTime.UtcNow;
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
                return CanMatchWith(b.Rank) && b.CanMatchWith(this.Rank);
            }
        }

        public class QueueMatch
        {
            public DateTime MatchCreated { get; set; } = DateTime.UtcNow;
            public DateTime? MatchIdeal { get; set; }
            public DateTime? MatchPlayable { get; set; }
            public List<QueueClient> Clients { get; set; } = new List<QueueClient>();
            public bool IsDestroyed { get; private set; }

            public bool CanMatchWith(QueueClient client)
            {
                return Clients.All(x => x.CanMatchWith(client));
            }

            public void Destroy()
            {
                IsDestroyed = true;
                Clients.Clear();
            }
        }

        public class QueueInstance
        {
            // we add a delay after a match is found
            // so that players have a chance to trickle in before the queue pops
            public const int SecondsAfterPlayableMatchBeforeCreating = 3; //15;

            public QueueIds Id { get; }
            public int ApplicationId { get; }
            public List<QueueClient> Queue { get; set; } = new List<QueueClient>();

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
            public uint WeaponFlags { get; set; }
            public byte[] GameFlags { get; set; }
            public GameConfig Config { get; set; }
            public Func<CompGame, QueueMatch, int[]> GetTeamIds { get; set; }
            public Func<ClientObject, int> GetRank { get; set; }

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
                    if (match.Clients.RemoveAll(x=>x.AccountId == client.AccountId) > 0)
                    {
                        if (match.Clients.Count < MinNumPlayers)
                        {
                            match.Destroy();
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
                matches.RemoveAll(x => x.IsDestroyed);

                // second pass - gather free clients (prioritize older queuers)
                var freeClients = Queue.Where(x => !matches.Any(m => m.Clients.Contains(x))).OrderBy(x => x.QueueBegan).ToList();

                // third pass - distribute free clients to best matching queue in need
                foreach (var freeClient in freeClients)
                {
                    // grab old possible match (prioritize older matches that don't already have ideal contraints met)
                    var match = matches.Where(x => x.CanMatchWith(freeClient) && x.Clients.Count < MaxNumPlayers).OrderBy(x=>x.MatchIdeal.HasValue ? 1 : 0).ThenBy(x=>x.MatchCreated).FirstOrDefault();

                    // add to match
                    if (match != null)
                        match.Clients.Add(freeClient);
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
                    if (playable && (DateTime.UtcNow - match.MatchPlayable)?.TotalSeconds > SecondsAfterPlayableMatchBeforeCreating)
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, "Game created");
                        await Create(match);
                    }
                }

                // fourth pass - move clients from newer matches to older ones
                var orderedMatches = matches.Where(x => !x.MatchIdeal.HasValue).OrderByDescending(x => x.MatchCreated).ToList();
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

                        foreach (var client in fromMatch.Clients.OrderBy(x=>x.QueueBegan))
                        {
                            // stop if target match is full
                            if (toMatch.Clients.Count >= MaxNumPlayers)
                                break;

                            if (toMatch.CanMatchWith(client))
                            {
                                // move to target match
                                fromMatch.Clients.Remove(client);
                                toMatch.Clients.Add(client);
                            }
                        }
                    }

                    // destroy if less than min players
                    if (fromMatch.Clients.Count < MinNumPlayers)
                        fromMatch.Destroy();
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
                        matches.Add(new QueueMatch() { Clients = pool });
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
                if (clientCount > MaxNumTeams && (clientCount % MaxNumTeams) != 0)
                    return false;

                return true;
            }

            private bool IsMatchIdeal(QueueMatch match)
            {
                // check number of clients
                return IdealNumPlayers == match.Clients.Count(x => x.Client.IsConnected);
            }

            private async Task Create(QueueMatch match)
            {
                var channel = new CompChannel(ApplicationId);
                var game = new CompGame(channel)
                {
                    GameLevel = (int)MapIds[_rng.Next(MapIds.Length)],
                    RulesSet = RulesSet,
                    GenericField1 = GenericField1,
                    GenericField3 = GenericField3,
                    GenericField4 = GenericField4,
                    GenericField5 = GenericField5,
                    GenericField6 = GenericField6,
                    GenericField7 = GenericField7,
                    GenericField8 = GenericField8,
                    WeaponFlags = WeaponFlags,
                    GameFlags = GameFlags
                };

                // create metadata
                var metadata = await Game.GetGameMetadata(game);
                metadata.GameConfig = Config ?? new GameConfig();

                // create game
                if (game.Create(this, match))
                {
                    Server.Medius.Program.Manager.AddChannel(channel);
                }

                // remove clients from queue
                foreach (var client in match.Clients)
                    this.Queue.Remove(client);

                // destroy match
                match.Destroy();
            }
        }

        static readonly QueueInstance[] _queues = new QueueInstance[]
        {
            new QueueInstance(11184, QueueIds.KOTH)
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
                    MapId.TEMPUS,
                    MapId.TORVAL,
                    MapId.MARAXUS
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
                GameFlags = Server.Common.Utils.FromString("0000000000000101000000000000010000010001FFFF00010A000000000000010101010005031E00010F010A3C0100000001000300000100000101"),
                Config = new GameConfig()
                {
                    BetterHills = true,
                    DisableWeaponPacks = true,
                    DisableInvHitTimer = true
                },
                GetTeamIds = (game, match) =>
                {
                    // pick two random teams
                    return Enumerable.Range(0, 10).OrderBy(x=> Guid.NewGuid()).Take(2).ToArray();
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
                IdealNumPlayers = 8,
                MapIds = new MapId[]
                {
                    MapId.CATACROM,
                    MapId.SARATHOS,
                    MapId.SHAAR,
                    MapId.VALIX,
                    MapId.TORVAL,
                    MapId.MARAXUS
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
                GameFlags = Server.Common.Utils.FromString("0000000000000101000000010000010000010100FFFF00010A000000000000010101010005021E00010F010A1E0100000001000300000100000100"),
                Config = new GameConfig()
                {
                    HalfTime = true,
                    DisableWeaponPacks = true,
                    DisableInvHitTimer = true
                },
                GetTeamIds = (game, match) =>
                {
                    switch ((MapId)game.GameLevel)
                    {
                        case MapId.SARATHOS:
                        case MapId.TORVAL: return new int[] { 2, 3 }; // green/orange
                        default: return new int[] { 0, 1 }; // red/blue
                    }
                },
                GetRank = (client) =>
                {
                    return client.WideStats[(int)PlayerStatIds.STAT_CTF_RANK];
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
