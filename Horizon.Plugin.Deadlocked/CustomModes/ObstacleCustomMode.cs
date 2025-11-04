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
    public class ObstacleCustomMode : BaseCustomMode
    {
        public override CustomModeId Id => CustomModeId.CMODE_ID_OBSTACLE;
        public override string Name => "Obstacle Course";

        public override Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult((int?)null);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            // reject all
            args.Reject = true;
            return Task.CompletedTask;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult((string)null);
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            var payload = new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/obstacle-11184.bin")));

            using (var ms = new MemoryStream(payload.Data, true))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    writer.BaseStream.Position = 8;
                    //writer.Write(0);
                    // last checkpoint
                    var playerMetadata = Player.GetPlayerMetadata(client);
                    if (playerMetadata?.ObstacleCourseStats != null && playerMetadata.ObstacleCourseStats.TryGetValue(metadata.CustomMap, out var mapStats) && mapStats != null)
                        writer.Write(mapStats.Checkpoint);
                }
            }

            return Task.FromResult(payload);
        }

        public override async Task OnRecvCustomMessage(ClientObject client, int messageId, MessageReader reader)
        {
            if (messageId == 101)
            {
                int checkpointUid = reader.ReadInt32();

                var metadata = await Game.GetGameMetadata(client.CurrentGame);
                if (metadata == null) return;
                if (metadata.GetRealCustomModeId() != CustomModeId.CMODE_ID_OBSTACLE) return;

                var playerMetadata = Player.GetPlayerMetadata(client);
                if (playerMetadata == null) return;

                playerMetadata.ObstacleCourseStats ??= new Dictionary<string, ObstacleCourseMapStat>();
                if (!playerMetadata.ObstacleCourseStats.TryGetValue(metadata.CustomMap, out var obstacleMapStats))
                    playerMetadata.ObstacleCourseStats[metadata.CustomMap] = obstacleMapStats = new ObstacleCourseMapStat();

                obstacleMapStats.Checkpoint = checkpointUid;
                Player.SavePlayerMetadata(client);
            }
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return null;
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            // we don't track stats for this mode
            return false;
        }

        protected override Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            throw new NotImplementedException();
        }
    }
}
