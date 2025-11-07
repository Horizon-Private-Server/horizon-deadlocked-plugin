using Horizon.Plugin.Deadlocked.Messages;
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

        public ObstacleCustomMode()
        {
            Player.OnBuildDynamicPageContent -= Player_OnBuildDynamicPageContent;
            Player.OnBuildDynamicPageContent += Player_OnBuildDynamicPageContent;
        }

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

                    // last checkpoint
                    var playerMetadata = Player.GetPlayerMetadata(client);
                    if (metadata.CustomMap != null && playerMetadata?.ObstacleCourseStats != null && playerMetadata.ObstacleCourseStats.TryGetValue(metadata.CustomMap, out var mapStats) && mapStats != null)
                    {
                        writer.Write(mapStats.CheckpointTicks);
                        writer.Write(mapStats.Checkpoint);
                    }
                }
            }

            return Task.FromResult(payload);
        }

        public override async Task OnRecvCustomMessage(ClientObject client, int messageId, MessageReader reader)
        {
            if (messageId == 101)
            {
                // SAVE CHECKPOINT
                var checkpointTicks = reader.ReadUInt64();
                var checkpointUid = reader.ReadInt32();

                var metadata = await Game.GetGameMetadata(client.CurrentGame);
                if (metadata == null) return;
                if (metadata.GetRealCustomModeId() != CustomModeId.CMODE_ID_OBSTACLE) return;

                var playerMetadata = Player.GetPlayerMetadata(client);
                if (playerMetadata == null) return;

                playerMetadata.ObstacleCourseStats ??= new Dictionary<string, ObstacleCourseMapStat>();
                if (!playerMetadata.ObstacleCourseStats.TryGetValue(metadata.CustomMap, out var obstacleMapStats))
                    playerMetadata.ObstacleCourseStats[metadata.CustomMap] = obstacleMapStats = new ObstacleCourseMapStat();

                obstacleMapStats.Checkpoint = checkpointUid;
                obstacleMapStats.CheckpointTicks = checkpointTicks;
                Player.SavePlayerMetadata(client);
            }
            else if (messageId == 102)
            {
                // FINISHED
                var totalTicks = reader.ReadUInt64();

                var metadata = await Game.GetGameMetadata(client.CurrentGame);
                if (metadata == null) return;
                if (metadata.GetRealCustomModeId() != CustomModeId.CMODE_ID_OBSTACLE) return;

                var playerMetadata = Player.GetPlayerMetadata(client);
                if (playerMetadata == null) return;

                playerMetadata.ObstacleCourseStats ??= new Dictionary<string, ObstacleCourseMapStat>();
                if (!playerMetadata.ObstacleCourseStats.TryGetValue(metadata.CustomMap, out var obstacleMapStats))
                    playerMetadata.ObstacleCourseStats[metadata.CustomMap] = obstacleMapStats = new ObstacleCourseMapStat();

                if (obstacleMapStats.BestCheckpointTicks == 0)
                    obstacleMapStats.BestCheckpointTicks = totalTicks;
                else
                    obstacleMapStats.BestCheckpointTicks = Math.Min(totalTicks, obstacleMapStats.BestCheckpointTicks);

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

        private void Player_OnBuildDynamicPageContent(DynamicPageContentBuilder builder)
        {
            if (builder.Request.Type != GetDynamicPageContentRequestMessage.ContentType.ObstacleMapStats) return;

            // get map stats
            if (!builder.PlayerMetadata.ObstacleCourseStats.TryGetValue(builder.Request.MapFilename, out var mapStats))
                builder.PlayerMetadata.ObstacleCourseStats[builder.Request.MapFilename] = mapStats = new ObstacleCourseMapStat();

            // build high scores
            if (mapStats.BestCheckpointTicks > 0)
            {
                var bestTime = TimeSpan.FromMilliseconds(mapStats.BestCheckpointTicks * 16.6666666);
                builder.LineItems.Add(("Status", "\x0A" + "Complete"));
                builder.LineItems.Add(("Best Time", $"{(int)bestTime.TotalHours}:{bestTime:mm\\:ss\\.fff}"));
            }
            else
            {
                builder.LineItems.Add(("Status", "Incomplete"));
            }

            builder.LineItems.Add(("", ""));

            if (mapStats.Checkpoint > 0)
            {
                var time = TimeSpan.FromMilliseconds(mapStats.CheckpointTicks * 16.6666666);
                builder.LineItems.Add(("Saved Checkpoint", "\x0A" + "Yes"));
                builder.LineItems.Add(("Saved Time", $"{(int)time.TotalHours}:{time:mm\\:ss\\.fff}"));
            }
            else
            {
                builder.LineItems.Add(("Saved Checkpoint", "No"));
            }
        }
    }
}
