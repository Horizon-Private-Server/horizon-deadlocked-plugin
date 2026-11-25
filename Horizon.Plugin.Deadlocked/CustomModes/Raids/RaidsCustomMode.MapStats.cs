using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode
    {
        public void OnClientGetMapStats(RaidsGetMapStatsRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            mapStats.CollectiblesCount = request.CollectiblesCount;
            mapStats.ChallengesCount = request.ChallengesCount;
            mapStats.MissionType = request.MissionType;
            mapStats.Name = request.MapName;
            Player.SavePlayerMetadata(client);

            // send back to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    mapStats.Serialize(writer, request.MapFilename, request.MissionType);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.ResponseAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                }
            }
        }

        public void OnClientSetMapStats(RaidsSetMapStatsRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            mapStats.CollectiblesCount = request.CollectiblesCount;
            mapStats.CollectiblesMask = request.CollectiblesMask;
            mapStats.ChallengesCount = request.ChallengesCount;
            mapStats.ChallengesMask = request.ChallengesMask;

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientSetMissionCompleted(RaidsSetMissionCompletedRequest request, ClientObject client)
        {
            if (request.Difficulty < 0 || request.Difficulty >= 5 || request.CompletedInMs == 0
                || string.IsNullOrWhiteSpace(request.MapFilename) || client.CurrentGame == null) return;

            var bank = GetBank(client);
            if (bank == null) return;

            lock (_contractLock)
            {
                // Retransmitting one mission completion must not count as another clear.
                var mission = (client.CurrentGame.UtcTimeCreated, request.MissionStartTime, request.MapFilename);
                if (bank.CompletedMissions.Contains(mission)) return;
                if (bank.CompletedMissions.Count >= 128) bank.CompletedMissions.Clear();
                bank.CompletedMissions.Add(mission);

                var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
                if (mapStats == null)
                    bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

                var bestTimeMs = mapStats.BestTimeMsPerDifficulty[request.Difficulty];
                if (request.CompletedInMs < bestTimeMs || bestTimeMs == 0)
                    mapStats.BestTimeMsPerDifficulty[request.Difficulty] = request.CompletedInMs;
                if (mapStats.CompletionsPerDifficulty[request.Difficulty] < uint.MaxValue)
                    ++mapStats.CompletionsPerDifficulty[request.Difficulty];

                Player.SavePlayerMetadata(client);
            }
        }
    }
}
