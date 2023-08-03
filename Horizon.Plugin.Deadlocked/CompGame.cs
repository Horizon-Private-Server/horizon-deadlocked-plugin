using DotNetty.Common.Internal.Logging;
using Horizon.Plugin.Deadlocked.Messages;
using Microsoft.Extensions.Logging;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using Server.Plugins.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public class CompGame : Server.Medius.Models.Game
    {
        static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance<CompGame>();
        static Random _rng = new Random();

        public override bool ReadyToDestroy => WorldStatus == MediusWorldStatus.WorldClosed;
        protected List<CompGameClient> _queueClients = null;
        protected Queue.QueueInstance _queue = null;
        protected Queue.QueueMatch _match = null;
        public DateTime? ForceStartAt { get; private set; } = null;
        protected DateTime LastStartAt { get; set; }
        protected List<MapId> SkippedMaps { get; } = new List<MapId>();

        public CompGame(Channel chatChannel) : base(null, null, chatChannel, null)
        {
            ApplicationId = chatChannel.ApplicationId;
            GameName = _rng.Next().ToString();
            //GamePassword = _rng.Next().ToString();
            MaxPlayers = 10;
            MinPlayers = 0;
            Attributes = MediusWorldAttributesType.GAME_WORLD_NONE;
            GameHostType = MediusGameHostType.MediusGameHostClientServerAuxUDP;
            GenericField2 = chatChannel.Id;
        }

        public bool Create(Queue.QueueInstance queue, Queue.QueueMatch match)
        {
            _queue = queue;
            _match = match;
            _queueClients = match.Clients.Select(x => new CompGameClient()
            {
                Client = x.Client,
                Team = 0
            }).ToList();

            var preferredLocations = match.Clients.GroupBy(x => x.Client.Location).ToDictionary(x => x.Key, x => x.Count());
            var preferredLocation = 0;
            var preferredLocationCount = 0;
            foreach (var item in preferredLocations)
            {
                if (item.Value > preferredLocationCount)
                {
                    preferredLocation = item.Key;
                    preferredLocation = item.Value;
                }
            }

            // Try to get next free dme server
            // If none exist, return error to clist
            DMEServer = Server.Medius.Program.ProxyServer.GetFreeDme(ApplicationId, preferredLocation);
            if (DMEServer == null)
                return false;

            // register game with medius
            Server.Medius.Program.Manager.AddGame(this);

            // Send create game request to dme server
            DMEServer.Queue(new MediusServerCreateGameWithAttributesRequest()
            {
                MessageID = new MessageId($"{this.Id}-{0}-0"),
                MediusWorldUID = (uint)this.Id,
                Attributes = this.Attributes,
                ApplicationID = this.ApplicationId,
                MaxClients = this.MaxPlayers
            });

            return true;
        }

        public void GenerateTeams()
        {
            // determine number of teams
            var numTeams = _match.GetBestNumTeams();

            // get team ids
            var teamIds = _queue.GetTeamIds(this, _match).OrderBy(x => Guid.NewGuid()).Take(numTeams).ToArray();

            // construct team pool
            var teamPool = new List<int>();
            while (teamPool.Count < _queueClients.Count)
            {
                foreach (var teamId in teamIds)
                    teamPool.Add(teamId);
            }

            // randomly assign teams to players
            teamPool = teamPool.OrderBy(x => Guid.NewGuid()).ToList();
            for (int i = 0; i < _queueClients.Count; ++i)
                _queueClients[i].Team = teamPool[i];

            SendLobbyState();
        }

        private void RebuildTeams()
        {
            // determine number of teams
            var numTeams = _match.GetBestNumTeams();

            // get new team ids
            var validTeamIds = _queue.GetTeamIds(this, _match);
            var teamIds = validTeamIds.OrderBy(x => Guid.NewGuid()).Take(numTeams).ToList();
            var currentTeamIds = _queueClients.Select(x => x.Team).Distinct().ToList();
            var playersInTeam = new int[10];

            // if growing in size then just generate new teams
            // otherwise try and move/merge teams if shrinking
            // or if not changed then just remap to fit valid team ids
            if (numTeams > currentTeamIds.Count)
            {
                GenerateTeams();
                return;
            }

            // current teams are fine
            if (currentTeamIds.All(x => validTeamIds.Contains(x)))
            {
                if (currentTeamIds.Count > numTeams)
                    teamIds = currentTeamIds.OrderBy(x => Guid.NewGuid()).Take(numTeams).ToList();
                else if (currentTeamIds.Count < numTeams)
                    teamIds = currentTeamIds.Union(teamIds).Take(numTeams).ToList();
                else
                    teamIds = currentTeamIds.ToList();
            }

            // match up current teams to ids
            var teamMap = new Dictionary<int, int>();

            // first map current teams to the same team if possible
            foreach (var currentTeamId in currentTeamIds)
            {
                if (teamIds.Contains(currentTeamId))
                {
                    teamMap.Add(currentTeamId, currentTeamId);
                    //teamIds.Remove(currentTeamId);
                }
            }

            // map clients to new teams
            foreach (var client in _queueClients)
                if (teamMap.ContainsKey(client.Team))
                    client.Team = teamMap[client.Team];

            // 
            playersInTeam = Enumerable.Range(0, 10).Select(x => _queueClients.Count(q => q.Team == x)).ToArray();
            var targetTeamSize = (int)Math.Ceiling(_queueClients.Count / (float)numTeams);
            var freeTeamIds = teamIds.SelectMany(x => Enumerable.Repeat(x, targetTeamSize)).OrderBy(x => Guid.NewGuid()).ToList();
            foreach (var client in _queueClients)
                freeTeamIds.Remove(client.Team);

            // second map leftover players to next available team
            foreach (var client in _queueClients)
            {
                if (!teamMap.ContainsKey(client.Team))
                {
                    client.Team = freeTeamIds[0];
                    freeTeamIds.RemoveAt(0);
                }
            }

            SendLobbyState();
        }

        private void SendLobbyState()
        {
            // send teams to host
            Host?.Queue(new ForceTeamsRequestMessage()
            {
                AccountIds = _queueClients.Select(x => x.Client.AccountId).ToArray(),
                Teams = _queueClients.Select(x => (byte)x.Team).ToArray(),
                BaseRanks = _queueClients.Select(x => (float)_queue.GetBaseRank(x.Client)).ToArray()
            });
        }

        public void SetMap(MapId map)
        {
            GameLevel = (int)map;

            // send map to clients
            foreach (var client in _queueClients)
            {
                client?.Client?.Queue(new ForceMapRequestMessage()
                {
                    Level = this.GameLevel
                });
            }

            RebuildTeams();
        }

        public int GetFreeTeam()
        {
            var teamIds = _queueClients.Select(x => x.Team).Distinct().ToList();
            if (_queue.IsFreeForAll)
            {
                var possibleTeams = _queue.GetTeamIds(this, _match);
                return possibleTeams.Where(x => !teamIds.Contains(x)).OrderBy(x => Guid.NewGuid()).FirstOrDefault();
            }
            else
            {
                var teamCounts = teamIds.Select(x => (x, _queueClients.Count(c => c.Team == x))).OrderBy(x => x.Item2).ToList();

                var smallestTeamCount = teamCounts[0].Item2;

                return teamCounts.Where(x => x.Item2 == smallestTeamCount).OrderBy(x => Guid.NewGuid()).FirstOrDefault().x;
            }
        }

        public Task Vote(ClientObject client, VoteRequestMessage request)
        {
            switch (request.Context)
            {
                case VoteContext.SkipMap:
                    {
                        // only accept if game hasn't started yet
                        if (this.WorldStatus != MediusWorldStatus.WorldStaging)
                            return Task.CompletedTask;

                        // set vote
                        var queueClient = _queueClients.FirstOrDefault(x => x.Client == client);
                        queueClient.VotedSkipMap = request.Vote != 0;

                        // tally
                        var neededToPass = (int)(1.0 + _queueClients.Count / 2.0);
                        int votes = _queueClients.Count(x => x.VotedSkipMap);

                        // broadcast to channel
                        if (queueClient.VotedSkipMap)
                        {
                            ChatChannel.BroadcastSystemMessage(ChatChannel.Clients, $"A{client.AccountName} has voted to skip the map. ({votes}/{neededToPass})");
                        }

                        // vote passed
                        if (votes >= neededToPass)
                        {
                            // reset votes
                            foreach (var qClient in _queueClients)
                                qClient.VotedSkipMap = false;

                            SkippedMaps.Add((MapId)GameLevel);
                            if (SkippedMaps.Count >= 10)
                                SkippedMaps.Clear();

                            // determine new map
                            var map = _queue.MapIds.Where(x => !SkippedMaps.Contains(x) && (int)x != GameLevel).OrderBy(x => Guid.NewGuid()).FirstOrDefault();
                            if (map > 0)
                            {
                                // set map
                                SetMap(map);

                                // broadcast to channel
                                ChatChannel.BroadcastSystemMessage(ChatChannel.Clients, $"AVote passed. Map skipped.");

                                // add 5 seconds to counter
                                TryAddForceStartTime(10);
                            }
                        }

                        break;
                    }
                case VoteContext.NewTeams:
                    {
                        // only accept if game hasn't started yet
                        if (this.WorldStatus != MediusWorldStatus.WorldStaging)
                            return Task.CompletedTask;

                        // set vote
                        var queueClient = _queueClients.FirstOrDefault(x => x.Client == client);
                        queueClient.VotedNewTeams = request.Vote != 0;

                        // tally
                        var neededToPass = (int)(1.0 + _queueClients.Count / 2.0);
                        int votes = _queueClients.Count(x => x.VotedNewTeams);

                        // broadcast to channel
                        if (queueClient.VotedNewTeams)
                        {
                            ChatChannel.BroadcastSystemMessage(ChatChannel.Clients, $"A{client.AccountName} has voted for new teams. ({votes}/{neededToPass})");
                        }

                        // vote passed
                        if (votes >= neededToPass)
                        {
                            // reset votes
                            foreach (var qClient in _queueClients) {
                                qClient.VotedNewTeams = false;
                                qClient.Team = 0;
                            }

                            // determine new teams
                            GenerateTeams();

                            // broadcast to channel
                            ChatChannel.BroadcastSystemMessage(ChatChannel.Clients, $"AVote passed. New teams generated.");

                            // add 5 seconds to counter
                            TryAddForceStartTime(10);
                        }

                        break;
                    }
            }

            return Task.CompletedTask;
        }

        public override async Task GameCreated()
        {
            await SetWorldStatus(MediusWorldStatus.WorldStaging);

            // pick random host
            if (Host == null)
                Host = _queueClients[_rng.Next(_queueClients.Count)].Client;

            // generate teams
            GenerateTeams();

            // have host join first
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);

                // send join message to client
                Host.Queue(new ShowSnackMessageRequestMessage()
                {
                    Message = "joining lobby.."
                });

                Host.Queue(new ForceJoinGameRequestMessage()
                {
                    DmeWorldId = this.DMEWorldId,
                    ChannelMediusWorldId = ChatChannel.Id,
                    GameMediusWorldId = this.Id,
                    AmIHost = true,
                    PlayerCount = _queueClients.Count,
                    Level = (byte)this.GameLevel,
                    Ruleset = (byte)this.RulesSet,
                    WeaponFlags = _queue.WeaponFlags,
                    GameFlags = _queue.GetGameFlags(_match)
                });
            });
        }

        public void ForceJoinPlayer(ClientObject client)
        {
            // tell client to join
            client.Queue(new ForceJoinGameRequestMessage()
            {
                DmeWorldId = this.DMEWorldId,
                ChannelMediusWorldId = ChatChannel.Id,
                GameMediusWorldId = this.Id,
                AmIHost = false,
                PlayerCount = _queueClients.Count,
                Level = (byte)this.GameLevel,
                Ruleset = (byte)this.RulesSet,
                WeaponFlags = _queue.WeaponFlags,
                GameFlags = _queue.GetGameFlags(_match)
            });

            if (!_queueClients.Any(c => c.Client == client))
                _queueClients.Add(new CompGameClient() { Client = client, Team = GetFreeTeam() });

            var matchClient = _match?.Clients?.FirstOrDefault(x => x.Client == client);
            if (matchClient != null)
                matchClient.TimeLastForceJoinSent = DateTime.UtcNow;
        }

        public void ForceStartIn(int seconds)
        {
            ForceStartAt = DateTime.UtcNow.AddSeconds(seconds);

            foreach (var client in Clients)
            {
                client.Client.Queue(new SetGameStartTimeRequestMessage()
                {
                    SecondsUntilStart = (int)Math.Max(0, (ForceStartAt.Value - DateTime.UtcNow).TotalSeconds)
                });
            }
        }

        public void StopForceStop()
        {
            ForceStartAt = null;

            foreach (var client in Clients)
            {
                client.Client.Queue(new SetGameStartTimeRequestMessage()
                {
                    SecondsUntilStart = -1
                });
            }
        }

        private void Start()
        {
            // prevent spamming
            if ((DateTime.UtcNow - LastStartAt).TotalSeconds < 1)
                return;

            LastStartAt = DateTime.UtcNow;
            Host?.Queue(new ForceStartGameRequestMessage());
        }

        public void Cancel(ForceLeaveGameRequestMessage.ForceLeaveReason reason)
        {
            foreach (var client in Clients)
                client.Client?.Queue(new ForceLeaveGameRequestMessage() { Reason = reason });

            // close
            SetWorldStatus(MediusWorldStatus.WorldClosed);
        }

        public override Task Tick()
        {
            // Remove timedout clients
            for (int i = 0; i < Clients.Count; ++i)
            {
                var client = Clients[i];

                if (client == null || client.Client == null || !client.Client.IsConnected || client.Client.CurrentGame?.Id != Id)
                {
                    _match?.OnClientLeftGame(Clients[i]?.Client);
                    Clients.RemoveAt(i);
                    --i;
                }
            }

            // check if any clients in match haven't joined game yet
            if (hasHostJoined)
            {
                foreach (var matchClient in _match.Clients)
                {
                    if (Clients.Any(c => c.Client == matchClient.Client && c.InGame))
                        continue;

                    //if (DateTime.UtcNow.AddSeconds(-10) > matchClient.TimeLastForceJoinSent)
                    //    ForceJoinPlayer(matchClient.Client);
                }
            }

            // Auto close when anyone leaves or if any client fails to connect after timeout time
            if (!utcTimeEmpty.HasValue && (Utils.GetHighPrecisionUtcTime() - utcTimeCreated).TotalSeconds > Server.Medius.Program.GetAppSettingsOrDefault(ApplicationId).GameTimeoutSeconds)
            {
                utcTimeEmpty = Utils.GetHighPrecisionUtcTime();
            }

            // start
            if (WorldStatus == MediusWorldStatus.WorldStaging && Clients.Count(x=>x.InGame) == _queueClients.Count && DateTime.UtcNow > ForceStartAt)
            {
                Start();
            }

            return Task.CompletedTask;
        }

        public override Task SetWorldStatus(MediusWorldStatus status)
        {
            // started
            if (status == MediusWorldStatus.WorldActive && WorldStatus != status)
            {
                // remove all clients from queue
                _queue?.Queue?.RemoveAll(x => Clients.Any(c => c.Client == x.Client));
                _match.Destroy(false);
            }

            return base.SetWorldStatus(status);
        }

        protected override async Task OnPlayerJoined(GameClient player)
        {
            try
            {
                if (Host == null)
                    Host = player.Client;

                // after host joins tell other clients to join
                if (Host == player.Client)
                {
                    _ = Task.Run(async () =>
                    {
                        foreach (var client in _queueClients)
                        {

                            // destroy world if someone leaves/disconnects
                            if (!client.Client.IsConnected)
                            {
                                await SetWorldStatus(MediusWorldStatus.WorldClosed);
                                break;
                            }

                            // already sent to host
                            if (Host == client.Client)
                                continue;

                            // send join message to client
                            client.Client.Queue(new ShowSnackMessageRequestMessage()
                            {
                                Message = "joining lobby.."
                            });

                            // tell client to join
                            ForceJoinPlayer(client.Client);

                            // wait in between sending join to each player
                            await Task.Delay(500);
                        }
                    });
                }

                RebuildTeams();

                // send time when game starts
                player.Client.Queue(new SetGameStartTimeRequestMessage()
                {
                    SecondsUntilStart = ForceStartAt.HasValue ? (int)Math.Max(0, (ForceStartAt.Value - DateTime.UtcNow).TotalSeconds) : -1
                });
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            await base.OnPlayerJoined(player);

            await UpdateGameConfig();
            UpdateForceStart();
        }

        public override async Task RemovePlayer(ClientObject client)
        {
            _match?.OnClientLeftGame(client);
            _queueClients.RemoveAll(x => x.Client == client);

            // pick new host
            // just need someone
            if (client == Host)
            {
                Host = null; // Clients.FirstOrDefault(x => x.Client != client && x.InGame && x.Client.IsConnected)?.Client;
                _match?.Destroy(true);
            }
            else
            {
                RebuildTeams();
            }

            // resend teams


            //if (this.WorldStatus <= MediusWorldStatus.WorldStaging)
            //{

            //    // if anyone leaves and in staging, then this queue is borked and we destroy
            //    await SetWorldStatus(MediusWorldStatus.WorldClosed);

            //    // 
            //    Cancel(ForceLeaveGameRequestMessage.ForceLeaveReason.QUEUE_PLAYERS_NOT_JOINED);
            //}

            await base.RemovePlayer(client);

            await UpdateGameConfig();
            UpdateForceStart();
        }

        private async Task UpdateGameConfig()
        {
            // update game config
            var metadata = await Game.GetGameMetadata(this);
            metadata.GameConfig = _queue.GetConfig(_match) ?? new GameConfig();

            // send game config to joining clients
            await Game.BroadcastGameConfig(this, true);
        }

        private void UpdateForceStart()
        {
            var numTeams = _match.GetBestNumTeams();
            var canStart = numTeams > 0 && (Clients.Count % numTeams == 0);
            var hasAllPlayers = _match.Clients.Count == Clients.Count(x => x.InGame);
            var currentForceStartIn = (int?)(ForceStartAt - DateTime.UtcNow)?.TotalSeconds;

            if (canStart && hasAllPlayers)
            {
                if (currentForceStartIn.HasValue)
                    ForceStartIn(currentForceStartIn < 30 ? 30 : currentForceStartIn.Value);
                else
                    ForceStartIn(30);
            }
            else
            {
                StopForceStop();
            }
        }

        private void TryAddForceStartTime(int seconds)
        {
            if (!ForceStartAt.HasValue)
                return;

            var currentForceStartIn = (int)Math.Ceiling((ForceStartAt.Value - DateTime.UtcNow).TotalSeconds);
            ForceStartIn(Math.Min(30, seconds + currentForceStartIn));
        }
    }

    public class CompGameClient
    {
        public ClientObject Client { get; set; }
        public int Team { get; set; }
        public bool VotedSkipMap { get; set; }
        public bool VotedNewTeams { get; set; }
    }
}
