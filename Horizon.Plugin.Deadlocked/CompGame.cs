using Horizon.Plugin.Deadlocked.Messages;
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
        static Random _rng = new Random();

        public uint WeaponFlags { get; set; }
        public byte[] GameFlags { get; set; }

        public override bool ReadyToDestroy => WorldStatus == MediusWorldStatus.WorldClosed;
        protected List<CompGameClient> _queueClients = null;
        protected Queue.QueueInstance _queue = null;
        protected Queue.QueueMatch _match = null;
        protected DateTime ForceStartAt { get; } = DateTime.UtcNow.AddSeconds(60);
        protected DateTime LastStartAt { get; set; }

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

            // Try to get next free dme server
            // If none exist, return error to clist
            DMEServer = Server.Medius.Program.ProxyServer.GetFreeDme(ApplicationId);
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
            var numTeams = _queue.IdealNumTeams;
            if ((_queueClients.Count % numTeams) != 0)
            {
                numTeams = _queue.MaxNumTeams;
                while ((_queueClients.Count % numTeams) != 0)
                {
                    --numTeams;
                }
            }

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

            // send teams to host
            Host?.Queue(new ForceTeamsRequestMessage()
            {
                AccountIds = _queueClients.Select(x => x.Client.AccountId).ToArray(),
                Teams = _queueClients.Select(x => (byte)x.Team).ToArray()
            });
        }

        private void RemapTeams(Dictionary<int, int> map)
        {
            foreach (var client in _queueClients)
                client.Team = map[client.Team];

            // send teams to host
            Host?.Queue(new ForceTeamsRequestMessage()
            {
                AccountIds = _queueClients.Select(x => x.Client.AccountId).ToArray(),
                Teams = _queueClients.Select(x => (byte)x.Team).ToArray()
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

            // determine number of teams
            var numTeams = _queue.IdealNumTeams;
            if ((_queueClients.Count % numTeams) != 0)
            {
                numTeams = _queue.MaxNumTeams;
                while ((_queueClients.Count % numTeams) != 0)
                {
                    --numTeams;
                }
            }

            // get new team ids
            var teamIds = _queue.GetTeamIds(this, _match).OrderBy(x => Guid.NewGuid()).Take(numTeams).ToList();
            var currentTeamIds = _queueClients.Select(x => x.Team).ToList();

            // match up current teams to ids
            var teamMap = new Dictionary<int, int>();

            // first map current teams to the same team if possible
            foreach (var currentTeamId in currentTeamIds)
            {
                if (teamIds.Contains(currentTeamId))
                {
                    teamMap.Add(currentTeamId, currentTeamId);
                    teamIds.Remove(currentTeamId);
                }
            }

            // second map leftover teams to next available team
            foreach (var currentTeamId in currentTeamIds)
            {
                if (!teamMap.ContainsKey(currentTeamId))
                {
                    teamMap.Add(currentTeamId, teamIds[0]);
                    teamIds.RemoveAt(0);
                }
            }

            RemapTeams(teamMap);
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

                        // broadcast to channel
                        if (queueClient.VotedSkipMap)
                        {
                            ChatChannel.BroadcastSystemMessage(ChatChannel.Clients, $"A{client.AccountName} has voted to skip the map.");
                        }

                        // tally
                        var neededToPass = _queueClients.Count / 2.0;
                        int votes = _queueClients.Count(x => x.VotedSkipMap);

                        // vote passed
                        if (votes > neededToPass)
                        {
                            // reset votes
                            foreach (var qClient in _queueClients)
                                qClient.VotedSkipMap = false;

                            // determine new map
                            var map = _queue.MapIds.Where(x => (int)x != this.GameLevel).OrderBy(x => Guid.NewGuid()).FirstOrDefault();
                            if (map > 0)
                            {
                                // set map
                                SetMap(map);

                                // broadcast to channel
                                ChatChannel.BroadcastSystemMessage(ChatChannel.Clients, $"AVote passed. Map skipped.");
                            }
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
            Host.Queue(new ForceJoinGameRequestMessage()
            {
                ChannelMediusWorldId = ChatChannel.Id,
                GameMediusWorldId = this.Id,
                AmIHost = true,
                PlayerCount = _queueClients.Count,
                Level = (byte)this.GameLevel,
                Ruleset = (byte)this.RulesSet,
                WeaponFlags = this.WeaponFlags,
                GameFlags = this.GameFlags
            });
        }

        private void Start()
        {
            // prevent spamming
            if ((DateTime.UtcNow - LastStartAt).TotalSeconds < 1)
                return;

            LastStartAt = DateTime.UtcNow;
            Host?.Queue(new ForceStartGameRequestMessage());
        }

        private void Cancel()
        {
            foreach (var client in Clients)
                client.Client?.Queue(new ForceLeaveGameRequestMessage());
        }

        public override Task Tick()
        {
            // Remove timedout clients
            for (int i = 0; i < Clients.Count; ++i)
            {
                var client = Clients[i];

                if (client == null || client.Client == null || !client.Client.IsConnected || client.Client.CurrentGame?.Id != Id)
                {
                    Clients.RemoveAt(i);
                    --i;
                }
            }

            // Auto close when anyone leaves or if any client fails to connect after timeout time
            if (!utcTimeEmpty.HasValue && Clients.Count(x => x.InGame) != _queueClients.Count && (Utils.GetHighPrecisionUtcTime() - utcTimeCreated).TotalSeconds > Server.Medius.Program.Settings.GameTimeoutSeconds)
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

        protected override async Task OnPlayerJoined(GameClient player)
        {
            if (Host == null)
                Host = player.Client;

            // after host joins tell other clients to join
            if (Host == player.Client)
            {
                foreach (var client in _queueClients)
                {
                    // destroy world if someone leaves/disconnects
                    if (!client.Client.IsConnected)
                    {
                        await SetWorldStatus(MediusWorldStatus.WorldClosed);
                        return;
                    }

                    // already sent to host
                    if (Host == client.Client)
                        continue;

                    // tell client to join
                    client.Client.Queue(new ForceJoinGameRequestMessage()
                    {
                        ChannelMediusWorldId = ChatChannel.Id,
                        GameMediusWorldId = this.Id,
                        AmIHost = false,
                        PlayerCount = _queueClients.Count,
                        Level = (byte)this.GameLevel,
                        Ruleset = (byte)this.RulesSet,
                        WeaponFlags = this.WeaponFlags,
                        GameFlags = this.GameFlags
                    });
                }
            }

            // send game config to joining clients
            var metadata = await Game.GetGameMetadata(this);
            player.Client.Queue(new SetGameConfigResponseMessage()
            {
                Config = metadata.GameConfig
            });

            // send time when game starts
            player.Client.Queue(new SetGameStartTimeRequestMessage()
            {
                SecondsUntilStart = (int)Math.Max(0, (ForceStartAt - DateTime.UtcNow).TotalSeconds)
            });

            await base.OnPlayerJoined(player);
        }

        public override async Task RemovePlayer(ClientObject client)
        {
            // pick new host
            // just need someone
            if (client == Host)
            {
                Host = Clients.FirstOrDefault(x => x.Client != client && x.InGame && x.Client.IsConnected)?.Client;
            }

            // if anyone leaves and in staging, then this queue is borked and we destroy
            await SetWorldStatus(MediusWorldStatus.WorldClosed);

            // 
            Cancel();

            await base.RemovePlayer(client);
        }
    }

    public class CompGameClient
    {
        public ClientObject Client { get; set; }
        public int Team { get; set; }
        public bool VotedSkipMap { get; set; }
    }
}
