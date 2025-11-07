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
    public class CollectathonCustomMode : BaseCustomMode
    {
        public override CustomModeId Id => CustomModeId.CMODE_ID_COLLECTATHON;
        public override string Name => "Collectathon";
        public const int MaxBolts = 64;

        public CollectathonCustomMode()
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
            var round = metadata.GameState.RoundNumber;
            var hostMetadata = Player.GetPlayerMetadata(game.Host);
            var mapStats = hostMetadata?.CollectathonStats?.GetValueOrDefault(metadata.CustomMapConfig.Filename);
            if (mapStats == null) return Task.FromResult(string.Empty);

            var sb = new StringBuilder();
            sb.AppendLine($"Completion: {mapStats.GetCompletion()*100:N0}%");
            foreach (CollectathonMapStat.Difficulty difficulty in Enum.GetValues(typeof(CollectathonMapStat.Difficulty)))
                sb.AppendLine($"{difficulty.GetDescription()} Bolts: {mapStats.GetCollectedCount(difficulty)}/{mapStats.GetCount(difficulty)}");
            return Task.FromResult(sb.ToString().Trim());
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            var payload = new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/collectathon-11184.bin")));

            using (var ms = new MemoryStream(payload.Data, true))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    writer.BaseStream.Position = 8;

                    // write out collected bolt uids
                    var playerMetadata = Player.GetPlayerMetadata(client);
                    if (metadata.CustomMap != null && playerMetadata?.CollectathonStats != null && playerMetadata.CollectathonStats.TryGetValue(metadata.CustomMap, out var mapStats) && mapStats != null)
                    {
                        for (int i = 0; i < MaxBolts; ++i)
                        {
                            var uid = mapStats.CollectedBoltUids.ElementAtOrDefault(i);
                            if (!mapStats.Bolts.ContainsKey(uid)) uid = 0;

                            writer.Write(uid);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < MaxBolts; ++i)
                        {
                            writer.Write(0);
                        }
                    }
                }
            }

            return Task.FromResult(payload);
        }

        public override async Task OnRecvCustomMessage(ClientObject client, int messageId, MessageReader reader)
        {
            if (messageId == 100)
            {
                // COLLECT BOLT
                var playerId = reader.ReadInt32();
                var boltUid = reader.ReadUInt32();

                var metadata = await Game.GetGameMetadata(client.CurrentGame);
                if (metadata == null) return;
                if (metadata.GetRealCustomModeId() != CustomModeId.CMODE_ID_COLLECTATHON) return;

                var playerMetadata = Player.GetPlayerMetadata(client);
                if (playerMetadata == null) return;

                playerMetadata.CollectathonStats ??= new Dictionary<string, CollectathonMapStat>();
                if (!playerMetadata.CollectathonStats.TryGetValue(metadata.CustomMap, out var mapStats))
                    playerMetadata.CollectathonStats[metadata.CustomMap] = mapStats = new CollectathonMapStat();

                if (mapStats.CollectedBoltUids.Add(boltUid))
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

        protected override Task UpdateCustomMapExData(ClientObject client, string mapFilename, byte[] data)
        {
            var playerMetadata = Player.GetPlayerMetadata(client);
            if (playerMetadata == null) return Task.CompletedTask;

            if (!playerMetadata.CollectathonStats.TryGetValue(mapFilename, out var mapStats))
                playerMetadata.CollectathonStats[mapFilename] = mapStats = new CollectathonMapStat();

            using (var ms = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(ms))
                {
                    var modeVersion = reader.ReadInt32();
                    var easyBoltCount = reader.ReadByte();
                    var mediumBoltCount = reader.ReadByte();
                    var hardBoltCount = reader.ReadByte();
                    var veryHardBoltCount = reader.ReadByte();
                    var totalCount = easyBoltCount + mediumBoltCount + hardBoltCount + veryHardBoltCount;

                    mapStats.Bolts.Clear();
                    for (int i = 0; i < easyBoltCount; ++i)
                        mapStats.Bolts[reader.ReadUInt32()] = CollectathonMapStat.Difficulty.Easy;
                    for (int i = 0; i < mediumBoltCount; ++i)
                        mapStats.Bolts[reader.ReadUInt32()] = CollectathonMapStat.Difficulty.Medium;
                    for (int i = 0; i < hardBoltCount; ++i)
                        mapStats.Bolts[reader.ReadUInt32()] = CollectathonMapStat.Difficulty.Hard;
                    for (int i = 0; i < veryHardBoltCount; ++i)
                        mapStats.Bolts[reader.ReadUInt32()] = CollectathonMapStat.Difficulty.VeryHard;
                }
            }

            Player.SavePlayerMetadata(client);
            return Task.CompletedTask;
        }

        private void Player_OnBuildDynamicPageContent(DynamicPageContentBuilder builder)
        {
            if (builder.Request.Type != GetDynamicPageContentRequestMessage.ContentType.CollectathonMapStats) return;

            // get map stats
            if (!builder.PlayerMetadata.CollectathonStats.TryGetValue(builder.Request.MapFilename, out var mapStats))
                builder.PlayerMetadata.CollectathonStats[builder.Request.MapFilename] = mapStats = new CollectathonMapStat();

            var completion = mapStats.GetCompletion();
            var completionCode = completion >= 1 ? "\x0A" : (completion > 0 ? "\x09" : "");
            builder.LineItems.Add(($"{completionCode}Completion", $"{completionCode}{completion * 100:N0}%"));

            foreach (CollectathonMapStat.Difficulty difficulty in Enum.GetValues(typeof(CollectathonMapStat.Difficulty)))
            {
                builder.LineItems.Add((difficulty.GetDescription(), $"{mapStats.GetCollectedCount(difficulty)}/{mapStats.GetCount(difficulty)}"));
            }
        }
    }
}
