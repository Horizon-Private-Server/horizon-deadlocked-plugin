using Horizon.Plugin.Deadlocked.CustomModes;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.ChatCommands
{
    public class PrestigeChatCommand : BaseChatCommand
    {
        public override string Command => "prestige";
        public override string Description => "Prestige your survival rank.";

        public override async Task Run(ClientObject source, string[] args)
        {
            if (source.CurrentGame == null) return;

            var mode = new SurvivalCustomMode();
            var metadata = await Game.GetGameMetadata(source.CurrentGame);
            if (metadata == null) return;
            if (metadata.GetRealCustomModeId() != CustomModeId.CMODE_ID_SURVIVAL) return;

            var prestiged = await mode.Prestige(source.CurrentGame, metadata, source);

            if (prestiged)
            {
                source.CurrentChannel.BroadcastSystemMessage(source.CurrentChannel.Clients, $"A{source.AccountName} has prestiged.");
                _ = Game.BroadcastCustomModeRanks(source.CurrentGame);
                _ = Game.BroadcastNameOverrides(source.CurrentGame);
            }
            else
            {
                source.CurrentChannel.BroadcastSystemMessage(source.CurrentChannel.Clients, $"A{source.AccountName} cannot prestige.");
            }

            return;
        }
    }
}
