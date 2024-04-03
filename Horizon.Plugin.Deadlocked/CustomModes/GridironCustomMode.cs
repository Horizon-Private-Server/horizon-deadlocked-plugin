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
    public class GridironCustomMode : BaseCustomMode
    {
        public override CustomModeId Id => CustomModeId.CMODE_ID_GRIDIRON;
        public override string Name => "DreadBall";

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

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/gridiron-11184.bin"))));
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
