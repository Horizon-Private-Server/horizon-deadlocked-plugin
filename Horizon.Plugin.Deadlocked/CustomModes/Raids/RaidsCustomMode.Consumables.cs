using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Server.Medius.Models;
using System.IO;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode
    {
        public void OnClientConsumableAction(RaidsConsumableActionRequest request, ClientObject client, GameMetadata metadata)
        {
            if (request.AccountSize != RaidsAccount.SERIALIZED_SIZE || request.RequestId == 0 || request.ReplyAddress == 0) return;
            var bank = GetBank(client);
            if (bank == null) return;

            lock (_contractLock)
            {
                bank.Consumables.PruneExpiredStacks(bank);
                if (bank.ConsumableReplies.TryGetValue(request.RequestId, out var previous))
                {
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.ReplyAddress, previous));
                    return;
                }

                var inHub = metadata?.CustomMapConfig?.Filename == HUB_MAP_FILENAME;
                var type = (RaidsMiscItemType)request.Argument;
                uint result = request.Action switch
                {
                    0 when inHub => HandleConsumablePurchase(bank, type),
                    1 when inHub => HandleConsumableActivation(bank, type),
                    2 => HandleConsumableStatus(),
                    _ => 0u
                };

                SendConsumableActionReply(request, client, bank, result);
            }
        }

        private static uint HandleConsumablePurchase(RaidsBank bank, RaidsMiscItemType type)
        {
            var price = RaidsConsumables.ConsumablePrice(type);
            if (price == 0 || bank.Account.Bolts < price) return 0;

            var stack = bank.Inventory.AllItems.FirstOrDefault(x => RaidsConsumables.IsStoreConsumable(x)
                && x.MiscData.Type == type);
            if (stack?.MiscData.Count == uint.MaxValue) return 0;

            bank.Account.Bolts -= price;
            if (stack != null)
            {
                ++stack.MiscData.Count;
                if (stack.Notify != 2) stack.Notify = 1;
            }
            else bank.Add(new RaidsInventoryItem {
                Type = RaidsItemType.Misc, Notify = 1,
                MiscData = new RaidsInventoryItem.ItemMiscData { Type = type, Count = 1 }
            });
            return 1;
        }

        private static uint HandleConsumableActivation(RaidsBank bank, RaidsMiscItemType type)
        {
            var item = bank.Inventory.AllItems.FirstOrDefault(x => RaidsConsumables.IsStoreConsumable(x)
                && x.MiscData.Type == type && x.MiscData.Count > 0);
            // Only the server clock decides expiry and available duration.
            if (item == null || !bank.Consumables.Activate(type)) return 0;

            --item.MiscData.Count;
            return 1;
        }

        // Timers can be loaded on any raid map; the shared reply contains the current snapshot.
        private static uint HandleConsumableStatus() => 1;

        private static void SendConsumableActionReply(RaidsConsumableActionRequest request, ClientObject client, RaidsBank bank, uint result)
        {
            using var stream = new MemoryStream(RaidsConsumables.ACTION_REPLY_SERIALIZED_SIZE);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(request.RequestId);
                writer.Write(result);
                writer.Write(bank.Account.Bolts);
                bank.Consumables.Serialize(writer);
                for (int i = 0; i < (int)RaidsConsumableType.Count; ++i)
                    writer.Write(RaidsConsumables.OwnedConsumables(bank, RaidsConsumables.GetItemType((RaidsConsumableType)i)));
            }
            var reply = stream.ToArray();
            if (bank.ConsumableReplies.Count >= 128) bank.ConsumableReplies.Clear();
            bank.ConsumableReplies[request.RequestId] = reply;
            Player.SavePlayerMetadata(client);
            client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.ReplyAddress, reply));
        }
    }
}
