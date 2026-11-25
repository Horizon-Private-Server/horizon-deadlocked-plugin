using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Server.Medius.Models;
using System;
using System.IO;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode
    {
        public void OnClientRequestStoreItems(RaidsGetStoreItemsRequest request, ClientObject client)
        {
            if (request.ReplyAddress == 0 || request.RequestId == 0 || request.Page < 0) return;
            var bank = GetBank(client);
            if (bank == null) return;

            bank.ItemStore.Refresh(bank.Account, false);
            var page = bank.ItemStore.GetStorePage(request.Page, request.RequestId, bank);
            if (page == null) return;

            using var stream = new MemoryStream(RaidsStorePage.SERIALIZED_SIZE);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                page.Serialize(writer);
            client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.ReplyAddress, stream.ToArray()));
            Player.SavePlayerMetadata(client);
        }

        public void OnClientBuyStoreItem(RaidsBuyStoreItemRequest request, ClientObject client)
        {
            if (!request.Item.IsValid() || !request.Item.IsWeapon()) return;

            var bank = GetBank(client);
            if (bank == null) return;

            // add & unlock weapon
            bank.Add(request.Item);
            if (request.Item.IsWeapon())
                bank.Account.UnlockWeapon(request.Item.WeaponData.GadgetId);

            // pass to store
            bank.ItemStore.OnBuy(request.Page, request.PageSize, request.ItemIdx, request.Item, bank.Account);

            // check for refresh items
            bank.ItemStore.Refresh(bank.Account, false);

            // save
            Player.SavePlayerMetadata(client);
        }
    }
}
