using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Server.Medius.Models;
using System;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode
    {
        public async Task OnClientRequestGenerateLootDrop(RaidsGenerateLootDropRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            lock (_contractLock)
            {
                // min 1s cooldown on mob death drops
                if (request.Type == RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath && bank.TimeLastMobDeathLootDrop.HasValue && (DateTime.UtcNow - bank.TimeLastMobDeathLootDrop.Value).TotalSeconds < 1) return;

                var drop = RaidsInventoryItem.Generate(request, bank.Account, bank.Consumables.GetRemainingMs(RaidsConsumableType.FortunesFavor) > 0);
                if (!drop.IsValid()) return;

                // save
                bank.Add(drop);
                if (request.Type == RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath
                    || request.Type == RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent)
                    bank.PlayerStats.RecordLootDrop(drop);
                bank.TimeLastMobDeathLootDrop = DateTime.UtcNow;
                Player.SavePlayerMetadata(client);

                // send back to client
                client.Queue(new RaidsGenerateLootDropResponse() { Position = request.Position, Drop = drop });
            }
        }
    }
}
