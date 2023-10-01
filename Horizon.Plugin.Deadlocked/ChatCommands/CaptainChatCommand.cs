using Horizon.Plugin.Deadlocked.Messages;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.ChatCommands
{
    public class CaptainChatCommand : BaseChatCommand
    {
        private static readonly Random _rng = new Random();

        public override string Command => "cpt";
        public override string Description => "Assigns captains to teams, gives order, and assigns everyone else to remain team.";

        public override Task Run(ClientObject source, string[] args)
        {
            var game = source.CurrentGame;
            var channel = source.CurrentChannel;

            // must be host
            if (game == null || game.Host != source)
                return Task.CompletedTask;

            // construct pool
            var numPlayers = game.Clients.Count;
            var gamemode = game.RulesSet;
            var teamsEnabled = (game.GenericField7 & (1 << 11)) != 0;
            var teams = new List<int>();
            var defaultTeam = 0;

            // ensure teams are enabled
            if (!teamsEnabled || gamemode == 4)
            {
                channel.BroadcastSystemMessage(channel.Clients, "ATeams are not enabled.");
                return Task.CompletedTask;
            }

            if (args.Length == 0)
            {
                // auto - assign to red/blue
                teams.Add(0);
                teams.Add(1);
                defaultTeam = 2;
            }
            else
            {
                // custom teams
                var customTeams = new List<int>();

                foreach (var arg in args)
                {
                    var teamId = GetTeamIdFromValue(arg, customTeams);
                    if (!teamId.HasValue)
                        channel.BroadcastSystemMessage(channel.Clients, $"A'{arg}' is not a valid team.");
                    else if (gamemode == 0 && teamId >= 2)
                        channel.BroadcastSystemMessage(channel.Clients, $"A'{Constants.Teams[teamId.Value]}' is not a valid team for Conquest.");
                    else if (gamemode == 1 && teamId >= 4)
                        channel.BroadcastSystemMessage(channel.Clients, $"A'{Constants.Teams[teamId.Value]}' is not a valid team for CTF.");
                    else
                        customTeams.Add(teamId.Value);
                }

                // prevent empty list of teams
                if (customTeams.Count == 0)
                    return Task.CompletedTask;

                // add to teams
                teams.AddRange(customTeams);
            }

            // find next free team for default
            while (teams.Contains(defaultTeam))
                ++defaultTeam;

            // validate default team
            if (defaultTeam >= 10) defaultTeam = -1;
            else if (gamemode == 0 && defaultTeam >= 2) defaultTeam = -1;
            else if (gamemode == 1 && defaultTeam >= 4) defaultTeam = -1;

            // fill teams with default
            var teamPool = new List<int>();
            teamPool.AddRange(teams);
            while (teamPool.Count < numPlayers)
                teamPool.Add(defaultTeam);

            // shuffle teams
            teamPool = teamPool.OrderBy(x => Guid.NewGuid()).ToList();

            // get pick order
            var pickOrder = teams.OrderBy(x => Guid.NewGuid()).ToList();

            // correct -1 teams
            for (int i = 0; i < teamPool.Count; ++i)
                if (teamPool[i] < 0)
                    teamPool[i] = 0;

            // send to requestor
            source.Queue(new SetLobbyTeamsRequestMessage()
            {
                Seed = _rng.Next(int.MinValue, int.MaxValue),
                TeamIdPool = teamPool
            });

            channel.BroadcastSystemMessage(channel.Clients, $"APick order: {String.Join(", ", pickOrder.Select(x => Constants.Teams[x]))}.");

            return Task.CompletedTask;
        }

        private int? GetTeamIdFromValue(string value, IEnumerable<int> excludeIds = null)
        {
            if (int.TryParse(value, out var intValue) && intValue >= 0 && intValue < 10)
                return intValue;

            for (int i = 0; i < Constants.Teams.Length; ++i)
            {
                if (excludeIds != null && excludeIds.Contains(i))
                    continue;
                if (Constants.Teams[i].StartsWith(value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return null;
        }
    }
}
