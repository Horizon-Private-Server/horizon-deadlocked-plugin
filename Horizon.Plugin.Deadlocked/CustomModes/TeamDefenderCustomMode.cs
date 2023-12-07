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
    public class TeamDefenderCustomMode : BaseCustomMode
    {
        public override CustomModeId Id => CustomModeId.CMODE_ID_TEAM_DEFENDER;
        public override string Name => "Team Defender";

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
            var timelimit = (game.GenericField7 >> 27) & 7;
            if (timelimit <= 0) timelimit = 1;
            var time = $"{timelimit * 5} minutes";

            return Task.FromResult($"Timelimit: {time}");
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/team-defender-11184.bin"))));
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new TeamDefenderCustomData();
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

    public class TeamDefenderCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int[] TeamScores { get; set; }
        public int[] PlayerPoints { get; set; }
        public float[] PlayerTimeWithFlag { get; set; }

        public void Deserialize(MessageReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            switch (Version)
            {
                case 1:
                    {
                        TeamScores = reader.ReadArray<int>(10);
                        PlayerPoints = reader.ReadArray<int>(10);
                        PlayerTimeWithFlag = reader.ReadArray<float>(10);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported team defender data version {Version}");
                        break;
                    }
            }
        }
    }
}
