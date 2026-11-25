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
        public void OnClientRequestBankEquippedInventory(RaidsGetBankEquippedInventoryRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            // sort inventory
            // breaks equipped indices
            //Array.Sort(bank.Inventory.Weapons, RaidsInventoryWeapon.Compare);

            // send to client
            using (var ms = new MemoryStream(1024 * 4))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    bank.Inventory.SerializeEquipped(writer);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestBankInventory(RaidsGetBankInventoryRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            lock (_contractLock)
            {
                bank.Consumables.PruneExpiredStacks(bank);

                // send to client
                using (var ms = new MemoryStream(1024 * 4))
                {
                    using (var writer = new BinaryWriter(ms))
                    {
                        ushort filterHasNewMask = 0;
                        var startIdx = Math.Max(0L, (long)request.Page * RaidsInventory.ITEMS_COUNT);
                        var items = bank.Inventory.GetSortedByFilter(request).ToList();
                        for (int i = 0; i < RaidsInventory.ITEMS_COUNT; ++i)
                        {
                            var item = startIdx + i < items.Count ? items[(int)(startIdx + i)] : null;
                            if (item != null) item.Price = RaidsInventoryItem.ComputeSellPrice(item);
                            (item ?? RaidsInventoryItem.Empty).Serialize(writer);
                        }

                        writer.Write(bank.Inventory.AllItems.Count(x => x != null && x.IsValid()));
                        for (int i = 0; i < 9; ++i)
                        {
                            var filterItems = bank.Inventory.GetByFilter(i).ToList();
                            writer.Write((ushort)filterItems.Count());
                            if (filterItems.Any(x => x.Notify == 1))
                                filterHasNewMask |= (ushort)(1 << i);
                        }

                        writer.Write(filterHasNewMask);
                        writer.Write(1); // tells the client the server just sent a payload
                        writer.Write(request.Filter);
                        writer.Write(request.Page);
                        writer.Write((int)request.Sort);

                        //bank.Inventory.Serialize(writer);
                        client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                        client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                        client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                    }
                }

                // save
                Player.SavePlayerMetadata(client);
            }
        }

        public void OnClientRequestBankInventoryItemUpdate(RaidsUpdateBankInventoryItemRequest request, ClientObject client)
        {
            if (request.Item == null || request.RequestId == 0) return;
            var bank = GetBank(client, initialize: false);
            if (bank == null) return;

            lock (_contractLock)
            {
                var updateKey = (client.SessionKey, request.RequestId);
                if (bank.InventoryUpdates.Contains(updateKey)) return;

                // find item
                var existingItem = bank.Inventory.Get(request.Item.Uid ?? 0);
                if (existingItem == null) return;

                switch (request.Action)
                {
                    case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Sell:
                        {
                            bank.Remove(existingItem, true);
                            break;
                        }
                    case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Destroy:
                        {
                            bank.Remove(existingItem, false);
                            break;
                        }
                    case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.ConsumeMaterial:
                        {
                            if (!existingItem.IsWeapon() || existingItem.Notify == 2
                                || bank.Inventory.EquippedWeaponUids.Contains(existingItem.Uid.Value)) return;

                            bank.Remove(existingItem, false);
                            bank.PlayerStats.RecordMaterialConsumed();
                            break;
                        }
                    case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.UpgradeFailed:
                        {
                            if (!existingItem.IsWeapon() || !request.Item.IsWeapon() || request.Item.WeaponData == null
                                || request.Item.WeaponData.GadgetId != existingItem.WeaponData.GadgetId
                                || request.Item.WeaponData.Upgrades != existingItem.WeaponData.Upgrades) return;

                            bank.PlayerStats.RecordUpgrade(false);
                            break;
                        }
                    case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Upgrade:
                        {
                            if (!existingItem.IsWeapon() || !request.Item.IsWeapon() || request.Item.WeaponData == null
                                || request.Item.WeaponData.GadgetId != existingItem.WeaponData.GadgetId) return;

                            var previousUpgrades = existingItem.WeaponData.Upgrades;
                            if (request.Item.WeaponData.Upgrades != previousUpgrades + 1) return;

                            // only successful upgrades may improve an already attached mod
                            if (request.Item.WeaponData.Upgrades == previousUpgrades + 1 && existingItem.WeaponData.Mod != WeaponMods.None)
                            {
                                var modRarity = RaidsInventoryItem.GetRarityFromQuality(existingItem.WeaponData.ModQuality);
                                var rarity = RaidsInventoryItem.GetRarityFromQuality(existingItem.Quality);
                                var nextQuality = RaidsInventoryItem.GetQualityFromRarity(modRarity + 1);
                                if (modRarity < rarity && request.Item.WeaponData.ModQuality == nextQuality)
                                    existingItem.WeaponData.ModQuality = nextQuality;
                            }

                            existingItem.WeaponData.Upgrades = request.Item.WeaponData.Upgrades;
                            existingItem.WeaponData.AlphaModCounts = request.Item.WeaponData.AlphaModCounts;
                            bank.PlayerStats.RecordUpgrade(true);
                            break;
                        }
                    case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.SetNotify:
                        {
                            existingItem.Notify = request.Item.Notify;
                            break;
                        }
                    case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Equip:
                        {
                            bank.Equip(existingItem);
                            break;
                        }
                    case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Unequip:
                        {
                            bank.Unequip(existingItem);
                            break;
                        }
                    default:
                        return;
                }

                if (bank.InventoryUpdates.Count >= 128) bank.InventoryUpdates.Clear();
                bank.InventoryUpdates.Add(updateKey);

                // save
                Player.SavePlayerMetadata(client);
            }
        }

        public void OnClientRequestBankAccount(RaidsGetBankAccountRequest request, ClientObject client)
        {
            if (!request.IsCompatible) return;
            var bank = GetBank(client);
            if (bank == null) return;

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    bank.Account.Serialize(writer);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestBankAccountUpdate(RaidsUpdateBankAccountRequest request, ClientObject client)
        {
            if (request.Account == null) return;
            var bank = GetBank(client);
            if (bank == null) return;

            // apply account snapshot after bank initialization
            bank.Account.Update(request.Account);

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestAccountReset(RaidsResetAccountRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // reset bank
            metadata.RaidsBank = new RaidsBank();
            GetBank(client);

            // save
            Player.SavePlayerMetadata(client);
        }
    }
}
