using Horizon.Plugin.Deadlocked.Messages;
using Newtonsoft.Json;
using Server.Common.Stream;
using Server.Medius;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.CustomModes
{
    public class RaidsCustomMode : BaseCustomMode
    {
        public const int MAX_CLASSES = 16;
        public static readonly int MAX_CLASS_LEVEL = 10;
        public static readonly int MAX_ACCOUNT_LEVEL = 98;
        public static readonly int MAX_WEAPON_LEVEL = 98;
        public static readonly string HUB_MAP_FILENAME = "raids_hub";
        public static readonly List<RaidsMobMetadata> RAIDS_MOB_METADATAS = new List<RaidsMobMetadata>()
        {
            new RaidsMobMetadata("Zombie", 0x20F6),
            new RaidsMobMetadata("Executioner", 0x20A1),
            new RaidsMobMetadata("Stalker Turret", 0x203A),
            new RaidsMobMetadata("Blue Swarmer", 0x2695),
            new RaidsMobMetadata("Orange Swarmer", 0x2051),
            new RaidsMobMetadata("Brown Swarmer", 0x26E0),
            new RaidsMobMetadata("Green Swarmer", 0x23FF),
            new RaidsMobMetadata("Leviathan", 0x20C4),
            new RaidsMobMetadata("DZ Striker", 0x2130),
            new RaidsMobMetadata("Reaper", 0x2570),
            new RaidsMobMetadata("Tremor", 0x24D3),
        };

        // Keep progression, weapons and unlock rules in sync with codegen/rc4/raids/classes.c.
        public static readonly List<RaidsClassProgression> RAIDS_CLASSES =
        [
            new(RaidsClassType.Gunner, 1.0m, 1, [Gadgets.Vipers]),
            new(RaidsClassType.Bomber, 1.0m, 1, [Gadgets.B6]),
            new(RaidsClassType.Brawler, 1.0m, 1, [Gadgets.Flail]),
            new(RaidsClassType.Marksman, 2.0m, 2, [Gadgets.Vipers, Gadgets.Fusion], [(RaidsClassType.Gunner, 10)]),
            new(RaidsClassType.Grenadier, 2.0m, 2, [Gadgets.Arbiter, Gadgets.B6], [(RaidsClassType.Bomber, 10)]),
            new(RaidsClassType.Melee, 2.0m, 2, [Gadgets.MagmaCannon, Gadgets.Flail], [(RaidsClassType.Brawler, 10)]),
            new(RaidsClassType.Scout, 2.0m, 2, [Gadgets.Vipers, Gadgets.Flail], [(RaidsClassType.Gunner, 5), (RaidsClassType.Brawler, 5)]),
            new(RaidsClassType.Support, 2.0m, 2, [Gadgets.MineLauncher, Gadgets.Holoshields], [(RaidsClassType.Bomber, 5), (RaidsClassType.Gunner, 5)]),
            new(RaidsClassType.Sharpshooter, 3.0m, 3, [Gadgets.Vipers, Gadgets.Arbiter, Gadgets.Fusion], [(RaidsClassType.Marksman, 10)]),
            new(RaidsClassType.Demolitionist, 3.0m, 3, [Gadgets.MagmaCannon, Gadgets.Arbiter, Gadgets.B6], [(RaidsClassType.Grenadier, 10)]),
            new(RaidsClassType.Juggernaut, 3.0m, 3, [Gadgets.MagmaCannon, Gadgets.Flail], [(RaidsClassType.Melee, 10)]),
            new(RaidsClassType.Infiltrator, 3.0m, 3, [Gadgets.Vipers, Gadgets.Arbiter, Gadgets.Flail], [(RaidsClassType.Scout, 10)]),
            new(RaidsClassType.FieldMedic, 3.0m, 3, [Gadgets.Fusion, Gadgets.MineLauncher, Gadgets.Holoshields], [(RaidsClassType.Support, 10)]),
        ];

        public override CustomModeId Id => CustomModeId.CMODE_ID_RAIDS;
        public override string Name => "DreadZone Raids (Alpha)";

        private int GetRatingFromXp(long xp)
        {
            //return (int)Math.Max(100, Math.Min(10000, 100 + Math.Sqrt(xp * 4)));
            return (int)Math.Max(100, Math.Min(10000, 100 + (xp / 5000f)));
        }

        public override Task OnModeSelected(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            game.ChatChannel.BroadcastSystemMessage(game.ChatChannel.Clients, $"ARaids is a new PvE mode currently in Alpha.");
            game.ChatChannel.BroadcastSystemMessage(game.ChatChannel.Clients, $"APlease submit feedback and bugs to the Horizon Discord.");
            game.ChatChannel.BroadcastSystemMessage(game.ChatChannel.Clients, $"AThe Raids map pack is required to play.");
            return Task.CompletedTask;
        }

        public override async Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return 0;

            return GetRatingFromXp(0);
        }

        public override Task<string> GetGameTitle(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult(Name);
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var mapName = metadata.CustomMapConfig.Name;

            // a space suppresses the default match rules when there is no map
            if (String.IsNullOrWhiteSpace(mapName))
                return Task.FromResult(" ");

            // Raids uses RoundNumber for mission status: high byte = mission type + 1
            // low byte = zero-based difficulty. Zero means the client has not reported it.
            var status = metadata.GameState.RoundNumber;
            var missionType = (RaidsMissionType)((status >> 8) - 1);
            var stars = (status & 0xFF) + 1;
            var info = missionType switch
            {
                RaidsMissionType.Hub => $"Gearing up at {mapName}",
                RaidsMissionType.OpenWorld => $"Leveling up on Planet {mapName}",
                RaidsMissionType.Raid => $"Running the {mapName} Raid",
                _ => $""
            };

            if (missionType == RaidsMissionType.Raid && stars <= 5)
                info += $" · {new string('★', stars)}{new string('☆', 5 - stars)}";

            return Task.FromResult(info);
        }

        public override Task<string> GetGamePlayerInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            // Match the in-game class suffixes: level zero has no Roman numeral.
            string[] classLevelSuffixes = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            var lines = new List<string>();

            foreach (var client in game.Clients.Select(x => x.Client).OrderBy(x => x.AccountName, StringComparer.Ordinal))
            {
                var account = Player.GetPlayerMetadata(client)?.RaidsBank?.Account ?? new RaidsAccount();
                var level = RaidsAccount.GetAccountLevelFromXp(account.Experience) + 1;
                var raidsClass = account.GetEquippedClass() ?? RAIDS_CLASSES[0];
                var className = raidsClass.ClassType switch
                {
                    RaidsClassType.Melee => "Berserker",
                    RaidsClassType.FieldMedic => "Field Medic",
                    _ => raidsClass.ClassType.ToString()
                };
                var classXp = account.ClassStats?.GetValueOrDefault(raidsClass.ClassType)?.Experience ?? 0;
                var classLevel = raidsClass.GetClassLevelFromXp(classXp);
                var classSuffix = classLevel > 0 ? $" {classLevelSuffixes[classLevel]}" : String.Empty;

                lines.Add($"{client.AccountName} - Level {level} - {className}{classSuffix}");
            }

            return Task.FromResult(String.Join("\n", lines));
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            args.Reject = true; // reject all
            return Task.CompletedTask;
        }

        public void OnClientRequestBankEquippedInventory(RaidsGetBankEquippedInventoryRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            var update = metadata.RaidsBank.Initialize();

            // sort inventory
            // breaks equipped indices
            //Array.Sort(metadata.RaidsBank.Inventory.Weapons, RaidsInventoryWeapon.Compare);

            // send to client
            using (var ms = new MemoryStream(1024 * 4))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    metadata.RaidsBank.Inventory.SerializeEquipped(writer);
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
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            var update = metadata.RaidsBank.Initialize();

            // sort inventory
            // breaks equipped indices
            //Array.Sort(metadata.RaidsBank.Inventory.Weapons, RaidsInventoryWeapon.Compare);

            // send to client
            using (var ms = new MemoryStream(1024 * 4))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    ushort filterHasNewMask = 0;
                    var startIdx = request.Page * RaidsInventory.ITEMS_COUNT;
                    var items = metadata.RaidsBank.Inventory.GetByFilter(request.Filter);
                    for (int i = 0; i < RaidsInventory.ITEMS_COUNT; ++i)
                    {
                        var item = items.ElementAtOrDefault(i + startIdx);
                        if (item != null) item.Price = RaidsInventoryItem.ComputeSellPrice(item);
                        (item ?? RaidsInventoryItem.Empty).Serialize(writer);
                    }

                    writer.Write(metadata.RaidsBank.Inventory.AllItems.Count);
                    for (int i = 0; i < 9; ++i)
                    {
                        var filterItems = metadata.RaidsBank.Inventory.GetByFilter(i);
                        writer.Write((ushort)filterItems.Count());
                        if (filterItems.Any(x => x.Notify == 1))
                            filterHasNewMask |= (ushort)(1 << i);
                    }

                    writer.Write(filterHasNewMask);
                    writer.Write(1); // tells the client the server just sent a payload
                    writer.Write(request.Filter);
                    writer.Write(request.Page);

                    //metadata.RaidsBank.Inventory.Serialize(writer);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestBankInventoryItemUpdate(RaidsUpdateBankInventoryItemRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // find item
            var existingItem = metadata.RaidsBank.Inventory.Get(request.Item.Uid ?? 0);
            if (existingItem == null) return;

            switch (request.Action)
            {
                case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Sell:
                    {
                        metadata.RaidsBank.Remove(existingItem, true);
                        break;
                    }
                case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Destroy:
                    {
                        metadata.RaidsBank.Remove(existingItem, false);
                        break;
                    }
                case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Upgrade:
                    {
                        existingItem.WeaponData.Upgrades = request.Item.WeaponData.Upgrades;
                        existingItem.WeaponData.MaxUpgrades = request.Item.WeaponData.MaxUpgrades;
                        existingItem.WeaponData.AlphaModCounts = request.Item.WeaponData.AlphaModCounts;
                        break;
                    }
                case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.UpgradeRarity:
                    {
                        existingItem.UpgradeRarity();
                        break;
                    }
                case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.SetNotify:
                    {
                        existingItem.Notify = request.Item.Notify;
                        break;
                    }
                case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Equip:
                    {
                        metadata.RaidsBank.Equip(existingItem);
                        break;
                    }
                case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Unequip:
                    {
                        metadata.RaidsBank.Unequip(existingItem);
                        break;
                    }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestBankAccount(RaidsGetBankAccountRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            var update = metadata.RaidsBank.Initialize();

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    metadata.RaidsBank.Account.Serialize(writer);
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
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // update
            metadata.RaidsBank.Account.Update(request.Account);

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestEquippedClassProgress(RaidsGetEquippedClassProgressRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            var update = metadata.RaidsBank.Initialize();

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    var raidsClass = metadata.RaidsBank.Account.GetEquippedClass();
                    var stats = metadata.RaidsBank.Account.GetEquippedClassStats();
                    RaidsClassStats.Serialize(writer, raidsClass?.ClassType ?? (RaidsClassType)(-1), stats);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestAllClassProgress(RaidsGetAllClassProgressRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            if (request.Offset < 0 || request.Count < 0 || request.Offset > 16 || request.Count > 16 - request.Offset) return;

            // ensure bank is initialized
            var update = metadata.RaidsBank.Initialize();

            // send all requested slots; unused slots have an invalid class ID
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    for (int i = request.Offset; i < request.Offset + request.Count; ++i)
                    {
                        var raidsClass = RAIDS_CLASSES.ElementAtOrDefault(i);
                        var classType = raidsClass?.ClassType ?? (RaidsClassType)(-1);
                        var stats = metadata.RaidsBank.Account.ClassStats.GetValueOrDefault(classType);
                        RaidsClassStats.Serialize(writer, classType, stats);
                    }

                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientSpendClassSkillPoint(RaidsClassSkillRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;
            var account = metadata.RaidsBank.Account;
            if (request.ClassType != account.EquippedClass || account.GetEquippedClass() == null
                || request.SkillId <= 0 || request.SkillId > byte.MaxValue
                || !Enum.IsDefined(typeof(RaidsClassSkillId), (byte)request.SkillId)) return;

            // Grid access, class levels and rank caps are checked by the shared map code.
            var stats = account.GetEquippedClassStats();
            int rank = stats.SkillRanks.GetValueOrDefault(request.SkillId);
            if (account.GetClassSkillPoints(request.ClassType) <= 0 || rank < 0 || rank >= byte.MaxValue
                || (!stats.SkillRanks.ContainsKey(request.SkillId) && stats.SkillRanks.Count >= RaidsClassStats.MAX_SKILLS)) return;

            stats.SkillRanks[request.SkillId] = rank + 1;
            Player.SavePlayerMetadata(client);
        }

        public void OnClientResetClassSkills(RaidsResetClassSkillsRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;
            var account = metadata.RaidsBank.Account;
            if (request.ClassType != account.EquippedClass || request.SkillId != 0 || account.GetEquippedClass() == null) return;

            var stats = account.GetEquippedClassStats();
            stats.SkillRanks.Clear();
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestStoreItems(RaidsGetStoreItemsRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            var update = metadata.RaidsBank.Initialize();

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    // refresh items
                    update = metadata.RaidsBank.ItemStore.Refresh(metadata.RaidsBank.Account, false);

                    // items
                    var items = metadata.RaidsBank.ItemStore.GetPaginatedWithFixedItems(request.Page, request.PageSize, out int itemsCount);
                    foreach (var item in items)
                    {
                        item.Price = RaidsInventoryItem.ComputeBuyPrice(item);
                        item.Serialize(writer);
                    }

                    int rotateInSeconds = (int)((metadata.RaidsBank.ItemStore.NextRefresh - DateTime.UtcNow)?.TotalSeconds ?? 0);

                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTotalItemsAddress, BitConverter.GetBytes(itemsCount)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestRotateInSecondsAddress, BitConverter.GetBytes(rotateInSeconds)));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientBuyStoreItem(RaidsBuyStoreItemRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;
            if (!request.Item.IsValid()) return;

            // ensure bank is initialized
            metadata.RaidsBank.Initialize();

            // add & unlock weapon
            metadata.RaidsBank.Add(request.Item);
            if (request.Item.IsWeapon())
                metadata.RaidsBank.Account.UnlockWeapon(request.Item.WeaponData.GadgetId);

            // pass to store
            metadata.RaidsBank.ItemStore.OnBuy(request.Page, request.PageSize, request.ItemIdx, request.Item, metadata.RaidsBank.Account);

            // check for refresh items
            metadata.RaidsBank.ItemStore.Refresh(metadata.RaidsBank.Account, false);

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestContracts(RaidsGetContractsRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            var update = metadata.RaidsBank.Initialize();

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    // refresh items
                    update = metadata.RaidsBank.ContractStore.Refresh(metadata.RaidsBank, false);

                    // contracts
                    var contracts = metadata.RaidsBank.ContractStore.Contracts;
                    for (int i = 0; i < RaidsContractStore.MAX_CONTRACTS; ++i)
                    {
                        var contract = contracts.ElementAtOrDefault(i);
                        if (contract != null)
                        {
                            var stats = request.ContractsStats.FirstOrDefault(x => x.ContractUid == contract.Uid);
                            if (stats != null && (stats.Kills != contract.Kills || stats.CompletedTimeMs != contract.CompletedTimeMs))
                            {
                                contract.Kills = stats.Kills;
                                contract.CompletedTimeMs = stats.CompletedTimeMs;
                                update = true;
                            }
                            if (stats != null && contract.RewardClassXp > 0 && !contract.CompletedClassType.HasValue
                                && contract.IsActivated() && !contract.IsExpired() && contract.HasRaidTimeRequirement()
                                && stats.CompletedTimeMs > 0 && stats.CompletedTimeMs < contract.RequiredRaidTimeMs
                                && RAIDS_CLASSES.Any(x => x.ClassType == stats.CompletedClassType))
                            {
                                contract.CompletedClassType = stats.CompletedClassType;
                                update = true;
                            }
                        }

                        // serialize
                        (contract ?? RaidsContract.Empty).Serialize(writer);
                    }

                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientActionContract(RaidsActionContractRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            metadata.RaidsBank.Initialize();

            switch (request.Action)
            {
                case RaidsActionContractRequest.ContractActions.Reroll:
                    {
                        // reroll
                        metadata.RaidsBank.ContractStore.Reroll(request.ContractUid, metadata.RaidsBank);
                        break;
                    }
                case RaidsActionContractRequest.ContractActions.Complete:
                    {
                        // complete
                        metadata.RaidsBank.ContractStore.Complete(request.ContractUid, metadata.RaidsBank);
                        break;
                    }
                case RaidsActionContractRequest.ContractActions.Activate:
                    {
                        // complete
                        metadata.RaidsBank.ContractStore.Activate(request.ContractUid, metadata.RaidsBank);
                        break;
                    }
            }

            // check for refresh of contracts
            metadata.RaidsBank.ContractStore.Refresh(metadata.RaidsBank, false);

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientUpdateContractStats(RaidsUpdateContractStatsRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            metadata.RaidsBank.Initialize();

            // get contract
            var contract = metadata.RaidsBank.ContractStore.Contracts.FirstOrDefault(x => x.Uid == request.ContractUid);
            if (contract == null) return;

            // update
            contract.Kills = request.Kills;
            contract.CompletedTimeMs = request.CompletedTimeMs;

            // Remember the class on the first successful raid contract update.
            if (contract.RewardClassXp > 0 && !contract.CompletedClassType.HasValue
                && contract.IsActivated() && !contract.IsExpired() && contract.HasRaidTimeRequirement()
                && request.CompletedTimeMs > 0 && request.CompletedTimeMs < contract.RequiredRaidTimeMs
                && RAIDS_CLASSES.Any(x => x.ClassType == request.CompletedClassType))
                contract.CompletedClassType = request.CompletedClassType;

            // check for refresh of contracts
            metadata.RaidsBank.ContractStore.Refresh(metadata.RaidsBank, false);

            // save
            Player.SavePlayerMetadata(client);
        }
        
        public async Task OnClientRequestGenerateLootDrop(RaidsGenerateLootDropRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            var bank = metadata.RaidsBank;
            bank.Initialize();

            // min 1s cooldown on mob death drops
            if (request.Type == RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath && bank.TimeLastMobDeathLootDrop.HasValue && (DateTime.UtcNow - bank.TimeLastMobDeathLootDrop.Value).TotalSeconds < 1) return;

            var drop = RaidsInventoryItem.Generate(request, bank.Account);
            if (!drop.IsValid()) return;

            // save
            bank.Add(drop);
            bank.TimeLastMobDeathLootDrop = DateTime.UtcNow;
            Player.SavePlayerMetadata(client);

            // send back to client
            client.Queue(new RaidsGenerateLootDropResponse() { Position = request.Position, Drop = drop });
        }

        public void OnClientGetMapStats(RaidsGetMapStatsRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            var bank = metadata.RaidsBank;
            var updated = bank.Initialize();

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            mapStats.CollectiblesCount = request.CollectiblesCount;
            mapStats.ChallengesCount = request.ChallengesCount;
            mapStats.MissionType = request.MissionType;
            mapStats.Name = request.MapName;
            Player.SavePlayerMetadata(client);

            // send back to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    mapStats.Serialize(writer, request.MapFilename, request.MissionType);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.ResponseAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                }
            }
        }

        public void OnClientSetMapStats(RaidsSetMapStatsRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            var bank = metadata.RaidsBank;
            bank.Initialize();

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            mapStats.CollectiblesCount = request.CollectiblesCount;
            mapStats.CollectiblesMask = request.CollectiblesMask;
            mapStats.ChallengesCount = request.ChallengesCount;
            mapStats.ChallengesMask = request.ChallengesMask;

            // save
            Player.SavePlayerMetadata(client);
        }
        
        public void OnClientUpdateMapMetadata(RaidsUpdateMapMetadataRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            var bank = metadata.RaidsBank;
            bank.Initialize();

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            // add & save
            if (request.MobOClass > 0 && mapStats.EnabledMobs.Add((request.MobOClass, request.MobDifficulty)))
            {
                Player.SavePlayerMetadata(client);
            }
        }

        public void OnClientUpdateMapContractRules(RaidsUpdateMapContractRulesRequest request, ClientObject client)
        {
            if (string.IsNullOrWhiteSpace(request.MapFilename)
                || request.TotalCount < 0 || request.TotalCount > RaidsUpdateMapContractRulesRequest.MAX_CONTRACTS
                || request.Offset < 0 || request.Offset > request.TotalCount
                || (request.TotalCount > 0 && request.Offset == request.TotalCount)
                || request.Offset % RaidsUpdateMapContractRulesRequest.RULES_PER_PAGE != 0
                || request.Count != Math.Min(RaidsUpdateMapContractRulesRequest.RULES_PER_PAGE, request.TotalCount - request.Offset)
                || request.ContractRules.Count != request.Count
                || request.ContractRules.Any(x => x.MobDefMask == 0 || string.IsNullOrWhiteSpace(x.Name)))
                return;

            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            var bank = metadata.RaidsBank;
            bank.Initialize();

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            // Apply each page directly; pages must not leave gaps in the list.
            var rules = mapStats.ContractRules;
            if (request.Offset > rules.Count) return;
            for (int i = 0; i < request.Count; ++i)
            {
                var index = request.Offset + i;
                if (index < rules.Count) rules[index] = request.ContractRules[i];
                else rules.Add(request.ContractRules[i]);
            }
            if (rules.Count > request.TotalCount)
                rules.RemoveRange(request.TotalCount, rules.Count - request.TotalCount);

            if (request.Offset + request.Count == request.TotalCount)
                Player.SavePlayerMetadata(client);
        }
        
        public void OnClientSetMissionCompleted(RaidsSetMissionCompletedRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;
            if (request.Difficulty < 0 || request.Difficulty >= 5) return;

            var bank = metadata.RaidsBank;
            bank.Initialize();

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            var bestTimeMs = mapStats.BestTimeMsPerDifficulty[request.Difficulty];
            if (request.CompletedInMs < bestTimeMs || bestTimeMs == 0)
                mapStats.BestTimeMsPerDifficulty[request.Difficulty] = request.CompletedInMs;

            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestAccountReset(RaidsResetAccountRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // reset bank
            metadata.RaidsBank = new RaidsBank();
            metadata.RaidsBank.Initialize();

            // save
            Player.SavePlayerMetadata(client);
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            var payload = new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/raids-11184.bin")));
            return Task.FromResult(payload);
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new RaidsCustomData();
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            if (game == null || metadata == null || gameData == null)
                return false;

            // game must have custom data
            if (gameData.CustomGameData == null || gameData.GameOptions == null)
                return false;

            return true;
        }

        protected override async Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as RaidsCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            var mapFilename = args.Metadata.CustomMapConfig.Filename;
            if (String.IsNullOrEmpty(mapFilename)) return;

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                //if (!player.Left)
                //{
                //    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                //}

                //// general
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_KILLS] += customGameData.Kills[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DEATHS] += (ushort)gameData.Data.Deaths[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_GAMES_PLAYED] += 1;
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_REVIVES] += customGameData.Revives[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_REVIVED] += customGameData.TimesRevived[gameIdx];

                //// general mechanics
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ROLLED_MYSTERY_BOX] += customGameData.TimesRolledMysteryBox[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_DEMON_BELL] += customGameData.TimesActivatedDemonBell[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TIMES_ACTIVATED_POWER] += customGameData.TimesActivatedPower[gameIdx];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_TOKENS_USED_ON_GATES] += customGameData.TokensUsedOnGates[gameIdx];

                //// weapon stats
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_WRENCH_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][0];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_DUAL_VIPER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][1];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MAGMA_CANNON_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][2];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_ARBITER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][3];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_FUSION_RIFLE_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][4];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_MINE_LAUNCHER_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][5];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_B6_OBLITERATOR_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][6];
                //args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SURVIVAL_SCORPION_FLAIL_KILLS] += (ushort)gameData.Data.WeaponKills[gameIdx][7];

                //// high scores
                //int? statIndex = null;
                //int? b50StatIndex = null;
                //var coop = game.AccountIdsAtStart.Contains(',');
                //if (coop)
                //{
                //    if (_survivalMapToCoopHighScoreStatIndex.TryGetValue(mapFilename, out var customStatId))
                //        statIndex = (int)customStatId;
                //    if (_survivalMapToCoop50BestTimeStatIndex.TryGetValue(mapFilename, out var b50CustomStatId))
                //        b50StatIndex = (int)b50CustomStatId;
                //}
                //else
                //{
                //    if (_survivalMapToSoloHighScoreStatIndex.TryGetValue(mapFilename, out var customStatId))
                //        statIndex = (int)customStatId;
                //    if (_survivalMapToSolo50BestTimeStatIndex.TryGetValue(mapFilename, out var b50CustomStatId))
                //        b50StatIndex = (int)b50CustomStatId;
                //}

                //if (statIndex.HasValue)
                //{
                //    args.PlayerCustomStats[accountId][statIndex.Value] = Math.Max(args.PlayerCustomStats[accountId][statIndex.Value], customGameData.BestRound[gameIdx]);
                //}

                //// min round 50 best time unless 0
                //if (b50StatIndex.HasValue && customGameData.BestRound[gameIdx] >= 50 && customGameData.Round50TimeMs > 0)
                //{
                //    if (args.PlayerCustomStats[accountId][b50StatIndex.Value] == 0 || customGameData.Round50TimeMs < args.PlayerCustomStats[accountId][b50StatIndex.Value])
                //    {
                //        args.PlayerCustomStats[accountId][b50StatIndex.Value] = customGameData.Round50TimeMs;
                //    }
                //}
            }
        }


        public override Task OnGameStart(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            // set increased timeout times for raids
            foreach (var client in game.Clients)
            {
                client.Client.TimeoutSeconds = Program.GetAppSettingsOrDefault(client.Client.ApplicationId).ClientTimeoutSeconds * 10;
                client.Client.LongTimeoutSeconds = Program.GetAppSettingsOrDefault(client.Client.ApplicationId).ClientLongTimeoutSeconds * 10;
            }

            return Task.CompletedTask;
        }
    }

    public class RaidsCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int[] Kills { get; set; }
        public int[] Revives { get; set; }
        public int[] TimesRevived { get; set; }

        public void Deserialize(MessageReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();

            switch (Version)
            {
                case 1:
                    {
                        Kills = reader.ReadArray<int>(10);
                        Revives = reader.ReadArray<int>(10);
                        TimesRevived = reader.ReadArray<int>(10);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported raids data version {Version}");
                        break;
                    }
            }
        }
    }


    public enum RaidsWeaponPaints
    {
        None = 0,
        Blue,
        Red,
        Green,
        Orange,
        Yellow,
        Purple,
        Pink,
        Aqua,
        Olive,
        Maroon,
        COUNT
    }

    [Flags]
    public enum RaidsPaintSpecialFlags
    {
        None = 0,
        Glow = 1 << 0,
        Additive = 1 << 1,
        ALL = Glow | Additive
    }

    public enum RaidsBadgeType
    {
        NONE = 0,
        HEALTH_REGEN,
        AMMO_REGEN,
        SHARPSHOOTER,
        BERSERKER,
        DAMAGE_COOLDOWN,
        HEALTH_BUFF,
        ALPHA_AMMO_BUFF,
        ALPHA_AREA_BUFF,
        ALPHA_SPEED_BUFF,
        ALPHA_IMPACT_BUFF,
        EXPLODING_ENEMIES,
        COUNT
    };

    public enum RaidsClassSkillId : byte
    {
        None = 0,
        Damage = 1,
        MovementSpeed = 2,
        CloseRangeDamage = 3,
        LongRangeDamage = 4,
        ExtraJumps = 5,
        ChargebootLength = 6,
        WrenchDamage = 7,
        AmmoDropChance = 8,
        HealingField = 9,
        AmmoField = 10,
        CriticalChance = 11,
        DamageImmunity = 12,
        AlphaArea = 13,
        AlphaSpeed = 14,
        AlphaNanoleech = 15,
        FlinchImmunity = 16,
        Hoverboots = 17,
        Visibomb = 18,
        WallWalking = 19,
        GoldBoltIndicator = 20,
        VehicleDamage = 21,
        VehicleHealth = 22,
        MaxHealth = 23,
        AlphaAmmo = 24,
        ViperProjectileSpeed = 25,
        B6ProjectileSpeed = 26,
        BlastReach = 27,
        SecondWind = 28,
        AirSuperiority = 29,
        EfficientSupplies = 30,
        Stealth = 31,
        Deadeye = 32,
        CombatMedic = 33,
        ClusterBombs = 34,
        UnstoppableSmash = 35,
    };

    public enum RaidsClassType
    {
        Gunner = 0,
        Melee = 1,
        Grenadier = 2,
        Support = 3,
        Scout = 4,
        Marksman = 5,
        Bomber = 6,
        Juggernaut = 7,
        Demolitionist = 8,
        FieldMedic = 9,
        Infiltrator = 10,
        Sharpshooter = 11,
        Brawler = 12,
    };

    public enum RaidsItemType
    {
        None = 0,
        Weapon,
        Badge
    };

    public enum RaidsItemRarity
    {
        Common = 0,
        Uncommon,
        Rare,
        Legendary,
        Mythic
    };

    public enum RaidsMissionType
    {
        Hub = 0,
        OpenWorld = 1,
        Raid = 2
    }

    public class RaidsBank
    {
        public const int RAIDS_BANK_VERSION = 4;

        public int Version = 0;
        public RaidsInventory Inventory = new RaidsInventory();
        public RaidsAccount Account = new RaidsAccount();
        public RaidsItemStore ItemStore = new RaidsItemStore();
        public RaidsContractStore ContractStore = new RaidsContractStore();
        public Dictionary<string, RaidsMapStats> MapStats = new Dictionary<string, RaidsMapStats>();
        public DateTime? TimeLastMobDeathLootDrop = null;
        public int ContractsCompleted = 0;
        public bool Initialized = false;

        public bool Initialize()
        {
            // make sure vipers & mag are always unlocked
            Account.UnlockWeapon(Gadgets.Vipers);
            Account.UnlockWeapon(Gadgets.MagmaCannon);
            Account.UnlockWeapon(Gadgets.Arbiter);
            Account.UnlockWeapon(Gadgets.Fusion);
            Account.UnlockWeapon(Gadgets.MineLauncher);
            Account.UnlockWeapon(Gadgets.B6);
            Account.UnlockWeapon(Gadgets.Holoshields);
            Account.UnlockWeapon(Gadgets.Flail);
            Account.ClampLevels();

            if (Initialized && Version < RAIDS_BANK_VERSION)
            {
                Migrate();
                return true;
            }

            if (Initialized) return false;

            // give default weapons
            Inventory = new RaidsInventory();
            Inventory.AllItems = new List<RaidsInventoryItem>();
            Inventory.EquippedWeaponUids = new uint[RaidsInventory.EQUIPPED_SIZE];

            // add base weapons
            for (var g = GadgetSlots.Vipers; g <= GadgetSlots.Holoshields; ++g)
                Inventory.AllItems.Add(RaidsInventoryItem.GenerateDefaultWeapon(g.ToGadget()).Copy());
            
            // init uids
            for (int i = 0; i < Inventory.AllItems.Count; ++i)
                Inventory.GenerateUid(Inventory.AllItems[i]);

            // equip base weapons
            Inventory.EquippedBadgeUid = 0;
            for (var i = 0; i < 8; ++i)
                Inventory.EquippedWeaponUids[i] = Inventory.AllItems[i].Uid.Value;

            MapStats = new Dictionary<string, RaidsMapStats>();

            Account = new RaidsAccount();
            Account.UnlockWeapon(Gadgets.Vipers);
            Account.UnlockWeapon(Gadgets.MagmaCannon);
            Account.UnlockWeapon(Gadgets.Arbiter);
            Account.UnlockWeapon(Gadgets.Fusion);
            Account.UnlockWeapon(Gadgets.MineLauncher);
            Account.UnlockWeapon(Gadgets.B6);
            Account.UnlockWeapon(Gadgets.Holoshields);
            Account.UnlockWeapon(Gadgets.Flail);
            // Account.Bolts = 300000; // start with 300k bolts

            Version = RAIDS_BANK_VERSION;

            //for (int i = 2; i < RaidsInventory.ITEMS_COUNT; ++i)
            //{
            //    Inventory.Items[i] = RaidsInventoryItem.GenerateBadge(new RaidsGenerateLootDropRequest() { Type = RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent }, Account);
            //}

            // give random stats/weapons
            if (false)
            {
                var rng = new Random();

                for (int i = 2; i < RaidsInventory.ITEMS_COUNT; ++i)
                {
                    // Inventory.Weapons[i] = RaidsInventoryWeapon.Random();
                    var item = RaidsInventoryItem.Generate(new RaidsGenerateLootDropRequest()
                    {
                        Type = RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent,
                        MobKilledByGadget = Gadgets.Vipers
                    }, Account);

                    // generate unique id
                    Add(item);
                }

                //for (int i = 0; i < Account.WeaponXp.Length; ++i)
                //    Account.WeaponXp[i] = (ulong)rng.Next(0, 100000);
            }

            Initialized = true;
            return true;
        }

        public void Add(RaidsInventoryItem item)
        {
            // generate new uid for item
            Inventory.GenerateUid(item);

            // add to backlog
            // let server move into inventory on next GetBank request (if room)
            //Inventory.ItemsBacklog.Enqueue(item);
            Inventory.AllItems.Add(item);
        }

        public void Remove(RaidsInventoryItem item, bool sell)
        {
            if (item == null) return;
            if (!item.IsValid()) return;
            if (!item.Uid.HasValue) return;
            if (sell && (Inventory.EquippedBadgeUid == item.Uid.Value || Inventory.EquippedWeaponUids.Contains(item.Uid.Value))) return;

            Unequip(item);
            Inventory.AllItems.Remove(item);
            if (sell)
                Account.Bolts += RaidsInventoryItem.ComputeSellPrice(item);
        }

        public void Equip(RaidsInventoryItem item)
        {
            if (item == null) return;
            if (!item.IsValid()) return;
            if (!item.Uid.HasValue) return;

            switch (item.Type)
            {
                case RaidsItemType.Badge:
                    {
                        Inventory.EquippedBadgeUid = item.Uid.Value;
                        break;
                    }
                case RaidsItemType.Weapon:
                    {
                        var slotId = RaidsInventoryItem.GetWeaponIndex(item.WeaponData.GadgetId);
                        if (!slotId.HasValue) return;

                        Inventory.EquippedWeaponUids[slotId.Value] = item.Uid.Value;
                        break;
                    }
            }
        }

        public void Unequip(RaidsInventoryItem item)
        {
            if (item == null) return;
            if (!item.IsValid()) return;
            if (!item.Uid.HasValue) return;

            switch (item.Type)
            {
                case RaidsItemType.Badge:
                    {
                        if (Inventory.EquippedBadgeUid == item.Uid)
                            Inventory.EquippedBadgeUid = 0;
                        break;
                    }
                case RaidsItemType.Weapon:
                    {
                        var slotId = RaidsInventoryItem.GetWeaponIndex(item.WeaponData.GadgetId);
                        if (!slotId.HasValue) return;

                        if (Inventory.EquippedWeaponUids[slotId.Value] == item.Uid)
                            Inventory.EquippedWeaponUids[slotId.Value] = 0;
                        break;
                    }
            }
        }

        private void Migrate()
        {
            while (Version < RAIDS_BANK_VERSION)
            {
                ++Version;

                switch (Version)
                {
                    case 1: // remove XP mods, implement 0.2 global damage scale mult
                        {
                            foreach (var item in Inventory.Items)
                            {
                                if (item == null) continue;
                                if (!item.IsWeapon()) continue;

                                item.WeaponData.AlphaModCounts[(int)AlphaMods.Xp - 1] = 0;

                                float baseDamage = RaidsInventoryItem.GetWeaponBaseDamage(item.WeaponData.GadgetId);
                                item.WeaponData.Damage = (int)(baseDamage + Math.Ceiling((item.WeaponData.Damage - baseDamage) * 0.2));
                            }
                            break;
                        }
                    case 2: // increase xp for level up every 10 levels (recalculate # of skill points) 
                        {
                            int expectedSkillPoints = RaidsAccount.GetAccountLevelFromXp(this.Account.Experience);

                            // reset skill points
                            //this.Account.SkillPoints = (uint)expectedSkillPoints;
                            //for (int i = 0; i < this.Account.Skills.Length; ++i)
                            //    this.Account.Skills[i] = 0;

                            break;
                        }
                    case 3: // merge items into AllItems, add uid to items
                        {
                            Inventory.AllItems = new List<RaidsInventoryItem>();
                            Inventory.AllItems.AddRange(Inventory.Items.Where(x => x != null && x.IsValid()));
                            Inventory.AllItems.AddRange(Inventory.ItemsBacklog.Where(x => x != null && x.IsValid()));

                            foreach (var item in Inventory.AllItems)
                                Inventory.GenerateUid(item);

                            break;
                        }
                    case 4: // remove badges
                        {
                            Inventory.AllItems.RemoveAll(x => x.Type == RaidsItemType.Badge);
                            Inventory.EquippedBadgeUid = 0;
                            break;
                        }
                }
            }
        }
    }

    public class RaidsAccount
    {
        public const int SKILLS_COUNT = 4;
        public const int PROFICIENCY_COUNT = 8;

        public ulong Experience = 0;
        public double[] WeaponXp = new double[PROFICIENCY_COUNT];
        public ulong[] WeaponPrestigeCount = new ulong[PROFICIENCY_COUNT];
        public RaidsClassType EquippedClass = RaidsClassType.Gunner;
        public Dictionary<RaidsClassType, RaidsClassStats> ClassStats = new Dictionary<RaidsClassType, RaidsClassStats>();
        public ulong PlayerPrestigeCount = 0;
        public uint Bolts = 0;
        public Dictionary<Gadgets, bool> HasWeapon = new Dictionary<Gadgets, bool>();


        public RaidsClassProgression GetEquippedClass()
        {
            return RaidsCustomMode.RAIDS_CLASSES.FirstOrDefault(x => x.ClassType == EquippedClass);
        }
        
        public int GetClassSkillPoints(RaidsClassType classType)
        {
            var raidsClass = RaidsCustomMode.RAIDS_CLASSES.FirstOrDefault(x => x.ClassType == classType);
            if (raidsClass == null || !ClassStats.TryGetValue(classType, out var stats)) return 0;
            return Math.Max(0, raidsClass.GetClassLevelFromXp(stats.Experience) - stats.SkillRanks.Values.Sum());
        }

        public RaidsClassStats GetEquippedClassStats()
        {
            if (ClassStats.TryGetValue(EquippedClass, out var classStats))
                return classStats;

            return ClassStats[EquippedClass] = new RaidsClassStats();
        }


        static double GetLevelFromXpQuadratic(double xp, double a, double b)
        {
            return (-b + Math.Sqrt(b * b + 4 * a * xp)) / (2 * a);
        }

        static double GetXpFromLevelQuadratic(double level, double a, double b)
        {
            return a * level * level + b * level;
        }

        public static int GetAccountLevelFromXp(ulong xp)
        {
            if (xp < 0) return 0;

            int level = 0;
            while (GetAccountXpFromLevel(level + 1) <= xp && level < RaidsCustomMode.MAX_ACCOUNT_LEVEL)
                ++level;

            return level;
        }

        public static ulong GetAccountXpFromLevel(int level)
        {
            if (level <= 0) return 0;
            if (level > RaidsCustomMode.MAX_ACCOUNT_LEVEL) level = RaidsCustomMode.MAX_ACCOUNT_LEVEL;

            return (ulong)(10 * (double)Math.Pow(level, 3) + 250 * level);
        }

        private static int GetProficiencyFromXp(double xp)
        {
            if (xp < 0) return 0;

            int level = 0;
            while (xp >= GetXpFromProficiency(level+1) && level < RaidsCustomMode.MAX_WEAPON_LEVEL)
                ++level;

            return level;
            //if (level < 0) return 0;
            //if (level > RaidsCustomMode.MAX_WEAPON_LEVEL) return RaidsCustomMode.MAX_WEAPON_LEVEL;
            //return (int)level;
        }

        private static double GetXpFromProficiency(int proficiency)
        {
            if (proficiency <= 0) return 0;
            if (proficiency > RaidsCustomMode.MAX_WEAPON_LEVEL) proficiency = RaidsCustomMode.MAX_WEAPON_LEVEL;
            return 10 * Math.Pow(proficiency, 3) + 100 * proficiency + 250;
            //return GetXpFromLevelQuadratic(proficiency, 250, 500);
        }

        public double GetWeaponXp(Gadgets gadget)
        {
            switch (gadget)
            {
                case Gadgets.Vipers: return WeaponXp[0];
                case Gadgets.MagmaCannon: return WeaponXp[1];
                case Gadgets.Arbiter: return WeaponXp[2];
                case Gadgets.Fusion: return WeaponXp[3];
                case Gadgets.MineLauncher: return WeaponXp[4];
                case Gadgets.B6: return WeaponXp[5];
                case Gadgets.Flail: return WeaponXp[6];
                case Gadgets.Holoshields: return WeaponXp[7];
            }

            return 0;
        }

        public int GetProficiency(Gadgets gadget)
        {
            return GetProficiencyFromXp(GetWeaponXp(gadget));
        }

        public bool HasUnlockedWeapon(Gadgets gadget)
        {
            return HasWeapon != null && HasWeapon.TryGetValue(gadget, out var unlocked) && unlocked;
        }

        public void UnlockWeapon(Gadgets gadget)
        {
            HasWeapon[gadget] = true;
        }

        public void ClampLevels()
        {
            var maxAccountXp = GetAccountXpFromLevel(RaidsCustomMode.MAX_ACCOUNT_LEVEL + 1);
            if (Experience > maxAccountXp)
                Experience = maxAccountXp;

            double maxWeaponXp = GetXpFromProficiency(RaidsCustomMode.MAX_WEAPON_LEVEL + 1);
            for (int i = 0; i < WeaponXp.Length; ++i)
                if (WeaponXp[i] > maxWeaponXp)
                    WeaponXp[i] = maxWeaponXp;

            var raidsClass = GetEquippedClass();
            var classStats = GetEquippedClassStats();
            if (raidsClass is not null && classStats is not null)
            {
                var maxClassXp = raidsClass.GetClassXpFromLevel(RaidsCustomMode.MAX_CLASS_LEVEL);
                if (classStats.Experience > maxClassXp)
                    classStats.Experience = maxClassXp;
            }
        }

        public void AddBolts(uint bolts)
        {
            long newBolts = Bolts + bolts;
            Bolts = (uint)Math.Clamp(newBolts, 0, uint.MaxValue);
        }

        public void AddXp(ulong xp)
        {
            Experience += xp;
            ClampLevels();
        }

        public void AddWeaponXp(double xp, Gadgets gadget)
        {
            switch (gadget)
            {
                case Gadgets.Vipers: WeaponXp[0] += xp; break;
                case Gadgets.MagmaCannon: WeaponXp[1] += xp; break;
                case Gadgets.Arbiter: WeaponXp[2] += xp; break;
                case Gadgets.Fusion: WeaponXp[3] += xp; break;
                case Gadgets.MineLauncher: WeaponXp[4] += xp; break;
                case Gadgets.B6: WeaponXp[5] += xp; break;
                case Gadgets.Flail: WeaponXp[6] += xp; break;
                case Gadgets.Holoshields: WeaponXp[7] += xp; break;
            }
            ClampLevels();
        }

        public void Update(RaidsAccount account)
        {
            this.Experience = account.Experience;
            this.Bolts = account.Bolts;
            this.WeaponXp = account.WeaponXp;
            if (RaidsCustomMode.RAIDS_CLASSES.Any(x => x.ClassType == account.EquippedClass))
                this.EquippedClass = account.EquippedClass;
            foreach (var raidsClass in RaidsCustomMode.RAIDS_CLASSES)
            {
                if (!ClassStats.TryGetValue(raidsClass.ClassType, out var stats))
                    ClassStats[raidsClass.ClassType] = stats = new RaidsClassStats();
                stats.Experience = Math.Min(account.ClassStats.GetValueOrDefault(raidsClass.ClassType)?.Experience ?? 0, raidsClass.GetClassXpFromLevel(RaidsCustomMode.MAX_CLASS_LEVEL));
            }
            ClampLevels();
        }

        public void Serialize(BinaryWriter writer)
        {
            if (WeaponXp == null) WeaponXp = new double[PROFICIENCY_COUNT];
            else if (WeaponXp.Length != PROFICIENCY_COUNT) Array.Resize(ref WeaponXp, PROFICIENCY_COUNT);

            for (int i = 0; i < PROFICIENCY_COUNT; ++i)
                writer.Write(WeaponXp[i]);
            writer.Write(Experience);
            writer.Write(Bolts);
            writer.Write((int)EquippedClass);
            for (int i = 0; i < RaidsCustomMode.MAX_CLASSES; ++i)
                writer.Write(ClassStats.GetValueOrDefault((RaidsClassType)i)?.Experience ?? 0);

        }

        public void Deserialize(BinaryReader reader)
        {
            if (WeaponXp == null) WeaponXp = new double[PROFICIENCY_COUNT];
            else if (WeaponXp.Length != PROFICIENCY_COUNT) Array.Resize(ref WeaponXp, PROFICIENCY_COUNT);

            for (int i = 0; i < PROFICIENCY_COUNT; ++i)
                WeaponXp[i] = reader.ReadDouble();
            Experience = reader.ReadUInt64();
            Bolts = reader.ReadUInt32();
            EquippedClass = (RaidsClassType)reader.ReadUInt32();
            for (int i = 0; i < RaidsCustomMode.MAX_CLASSES; ++i)
                ClassStats[(RaidsClassType)i] = new RaidsClassStats { Experience = reader.ReadUInt64() };

        }
    }

    public class RaidsInventory
    {
        public const int ITEMS_COUNT = 64;
        public const int EQUIPPED_SIZE = 8;

        [Obsolete]
        public RaidsInventoryItem[] Items = new RaidsInventoryItem[ITEMS_COUNT];
        [Obsolete]
        public Queue<RaidsInventoryItem> ItemsBacklog = new Queue<RaidsInventoryItem>();

        public List<RaidsInventoryItem> AllItems = new List<RaidsInventoryItem>();
        public uint[] EquippedWeaponUids = new uint[EQUIPPED_SIZE];
        public uint EquippedBadgeUid = 0;

        public RaidsInventoryItem Get(uint uid)
        {
            if (uid == 0) return null;
            return AllItems.FirstOrDefault(x => x != null && x.IsValid() && x.Uid == uid);
        }

        public IEnumerable<RaidsInventoryItem> GetByFilter(int filter)
        {
            return AllItems.Where(x => x.MatchFilter(filter));
        }

        public bool HasUid(uint uid)
        {
            return uid != 0 && AllItems.Any(x => x != null && x.IsValid() && x.Uid == uid);
        }

        public void GenerateUid(RaidsInventoryItem item)
        {
            // generate unique id
            item.Uid = RaidsInventoryItem.GenerateUid();
            while (AllItems.Any(x => x != null && x.IsValid() && x.Uid == item.Uid && x != item))
                item.Uid = RaidsInventoryItem.GenerateUid();
        }

        public void SerializeEquipped(BinaryWriter writer)
        {
            if (EquippedWeaponUids == null) EquippedWeaponUids = new uint[EQUIPPED_SIZE];
            else if (EquippedWeaponUids.Length != EQUIPPED_SIZE) Array.Resize(ref EquippedWeaponUids, EQUIPPED_SIZE);

            for (int i = 0; i < EQUIPPED_SIZE; ++i)
            {
                var item = Get(EquippedWeaponUids[i]);
                (item ?? RaidsInventoryItem.Empty).Serialize(writer);
            }

            var badge = Get(EquippedBadgeUid);
            (badge ?? RaidsInventoryItem.Empty).Serialize(writer);

            writer.Write(1); // tells the client the server just sent a payload
        }

        //public void Serialize(BinaryWriter writer)
        //{
        //    if (Items == null) Items = new RaidsInventoryItem[ITEMS_COUNT];
        //    else if (Items.Length != ITEMS_COUNT) Array.Resize(ref Items, ITEMS_COUNT);
        //    if (EquippedWeaponUids == null) EquippedWeaponUids = new uint[EQUIPPED_SIZE];
        //    else if (EquippedWeaponUids.Length != EQUIPPED_SIZE) Array.Resize(ref EquippedWeaponUids, EQUIPPED_SIZE);

        //    int weaponsCount = ItemsBacklog.Count;
        //    for (int i = 0; i < ITEMS_COUNT; ++i)
        //    {
        //        if (Items[i] != null)
        //        {
        //            Items[i].Price = RaidsInventoryItem.ComputeSellPrice(Items[i]);
        //            weaponsCount++;
        //        }

        //        (Items[i] ?? RaidsInventoryItem.Empty).Serialize(writer);
        //    }

        //    writer.Write(weaponsCount);
        //    writer.Write(1); // tells the client the server just sent a payload
        //    writer.Write(EquippedBadgeUid);
        //    for (int i = 0; i < EQUIPPED_SIZE; ++i)
        //        writer.Write(EquippedWeaponUids[i]);
        //}

        //public void Deserialize(BinaryReader reader)
        //{
        //    if (Items == null) Items = new RaidsInventoryItem[ITEMS_COUNT];
        //    else if (Items.Length != ITEMS_COUNT) Array.Resize(ref Items, ITEMS_COUNT);
        //    if (EquippedWeaponUids == null) EquippedWeaponUids = new uint[EQUIPPED_SIZE];
        //    else if (EquippedWeaponUids.Length != EQUIPPED_SIZE) Array.Resize(ref EquippedWeaponUids, EQUIPPED_SIZE);

        //    for (int i = 0; i < ITEMS_COUNT; ++i)
        //        Items[i].Deserialize(reader);
        //    reader.ReadInt32(); // total weapons count
        //    reader.ReadInt32(); // refresh flag
        //    EquippedBadgeUid = reader.ReadUInt32();
        //    for (int i = 0; i < EQUIPPED_SIZE; ++i)
        //        EquippedWeaponUids[i] = reader.ReadUInt32();
        //}
    }

    public class RaidsContract
    {
        public static readonly RaidsContract Empty = new RaidsContract();

        public uint? Uid;

        // map
        public string MapFilename;
        public string MapName;
        public int RequiredDifficultyStars;

        // kills to complete
        public uint RequiredKills;
        public string MobName;

        // if raid - time to complete
        public uint RequiredRaidTimeMs;

        // reward
        public uint RewardBolts;
        public uint RewardPlayerXp;
        public uint RewardWeaponXp;
        public uint RewardClassXp; // optional; zero means no Class XP reward
        public Gadgets RequiredKillsGadgetId = Gadgets.None;

        // expiration
        public uint ExpirationMinutes;

        // stats
        public uint Kills;
        public uint CompletedTimeMs;

        // for server
        public DateTime DateCreatedUtc = DateTime.UtcNow;
        public RaidsClassType? CompletedClassType;
        public DateTime? DateExpiresUtc;
        public DateTime DateRefreshUtc = DateTime.UtcNow.AddHours(6);

        public bool HasKillRequirement() => RequiredKills > 0;
        public bool HasRaidTimeRequirement() => RequiredRaidTimeMs > 0;
        public bool IsActivated() => DateExpiresUtc.HasValue;
        public bool IsExpired() => DateTime.UtcNow > DateExpiresUtc;
        public bool ShouldRefresh() => IsActivated() ? false : DateTime.UtcNow > DateRefreshUtc;
        public void Activate()
        {
            DateExpiresUtc = DateTime.UtcNow + TimeSpan.FromMinutes(ExpirationMinutes);
            Kills = 0;
            CompletedTimeMs = 0;
            CompletedClassType = null;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Uid ?? 0);
            writer.Write(IsActivated() ? 1 : 0);
            writer.Write(RequiredDifficultyStars);
            writer.Write(RequiredKills);
            writer.Write((int)RequiredKillsGadgetId);
            writer.Write(RequiredRaidTimeMs);
            writer.Write(RewardBolts);
            writer.Write(RewardPlayerXp);
            writer.Write(RewardWeaponXp);
            writer.Write(RewardClassXp);
            writer.Write((int)(DateRefreshUtc - DateTime.UtcNow).TotalMinutes);
            if (DateExpiresUtc.HasValue)
                writer.Write((int)(DateExpiresUtc.Value - DateTime.UtcNow).TotalMinutes);
            else
                writer.Write(ExpirationMinutes);
            writer.Write(Kills);
            writer.Write(CompletedTimeMs);
            writer.Write((int)(CompletedClassType ?? (RaidsClassType)(-1)));
            writer.Write(MapFilename, 64);
            writer.Write(MapName, 32);
            writer.Write(MobName, 32);
        }
    }

    public class RaidsInventoryItem
    {
        private static readonly Random _rng = new Random();
        private static readonly float _damageCurveMultMax = 10000;
        private static readonly float _globalDamageScaleMultiplier = 0.2f;
        private static readonly Gadgets[] _weaponGadgetIds = new Gadgets[] { Gadgets.Vipers, Gadgets.MagmaCannon, Gadgets.Arbiter, Gadgets.Fusion, Gadgets.MineLauncher, Gadgets.B6, Gadgets.Flail, Gadgets.Holoshields };
        private static readonly Dictionary<Gadgets, float> _baseDamages = new Dictionary<Gadgets, float>()
        {
            { Gadgets.Vipers, 5 },
            { Gadgets.MagmaCannon, 25 },
            { Gadgets.Arbiter, 50 },
            { Gadgets.Fusion, 75 },
            { Gadgets.MineLauncher, 25 },
            { Gadgets.B6, 50 },
            { Gadgets.Flail, 30 },
            { Gadgets.Holoshields, 15 },
        };
        private static readonly Dictionary<Gadgets, float> _damageScales = new Dictionary<Gadgets, float>()
        {
            { Gadgets.Vipers, 0.25f },
            { Gadgets.MagmaCannon, 1.0f },
            { Gadgets.Arbiter, 1.5f },
            { Gadgets.Fusion, 1.5f },
            { Gadgets.MineLauncher, 0.5f },
            { Gadgets.B6, 1.0f },
            { Gadgets.Flail, 1.0f },
            { Gadgets.Holoshields, 1.0f },
        };
        private static readonly Dictionary<Gadgets, AlphaMods[]> _gadgetAlphamods = new Dictionary<Gadgets, AlphaMods[]>()
        {
            { Gadgets.Vipers, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
            { Gadgets.MagmaCannon, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
            { Gadgets.Arbiter, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
            { Gadgets.Fusion, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
            { Gadgets.MineLauncher, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
            { Gadgets.B6, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
            { Gadgets.Flail, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
            { Gadgets.Holoshields, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
        };
        private static readonly Dictionary<AlphaMods, int> _gadgetAlphaModMax = new Dictionary<AlphaMods, int>()
        {
            { AlphaMods.Speed, 15 },
            { AlphaMods.Area, 10 },
            { AlphaMods.Aiming, 99 },
            { AlphaMods.Ammo, 99 },
            { AlphaMods.Xp, 0 },
            { AlphaMods.Jackpot, 15 },
            { AlphaMods.Nanoleech, 15 },
            { AlphaMods.Impact, 10 },
        };

        private static readonly int[] _gadgetMinProficiencyForDifficulty = new[] { 00, 15, 30, 50, 80 };
        private static readonly int[] _gadgetMaxProficiencyForDifficulty = new[] { 15, 40, 70, 95, 99 };
        private static readonly int[] _gadgetMinProficiencyForRarity = new[] { 0, 5, 15, 50, 95 };
        private static readonly float[] _gadgetMaxQualityForDifficulty = new[] { 0.4f, 0.5f, 0.65f, 0.75f, 0.999f };
        private static readonly float[] _gadgetMissionCompleteMinQualityForDifficulty = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        private static readonly int[] _badgeMinEffectCountForRarity = new[] { 1, 1, 2, 3, 4 };
        private static readonly int[] _badgeMaxEffectCountForRarity = new[] { 2, 3, 4, 5, 7 };
        private static readonly float[] _badgeEffectStrengthForRarity = new[] { 2f, 1.5f, 1.25f, 1f, 0.5f };

        public static readonly RaidsInventoryItem Empty = new RaidsInventoryItem();
        
        public class ItemWeaponData
        {
            public int Damage;
            public Gadgets GadgetId;
            public RaidsWeaponPaints Paint;
            public RaidsPaintSpecialFlags PaintSpecialMask;
            public byte Proficiency; // gadget lvl weapon created at (v1-v99)
            public byte CritChance; // 0-255 (0-100%)
            public WeaponMods Mod;
            public byte ModQuality;
            public byte[] AlphaModCounts = new byte[8];
            public int Upgrades = 0;
            public int MaxUpgrades = 10;
        }

        public class ItemBadgeData
        {
            public List<RaidsBadgeType> Effects = new List<RaidsBadgeType>();
            public List<float> EffectStrength = new List<float>();
        }

        public uint? Uid;
        public RaidsItemType Type;
        public uint Price;
        public byte Notify;
        public byte Quality; // rarity index (0-255)
        public ItemWeaponData WeaponData = null;
        public ItemBadgeData BadgeData = null;


        public bool IsBadge() => Type == RaidsItemType.Badge;
        public bool IsWeapon() => Type == RaidsItemType.Weapon;
        public bool IsSameItem(RaidsInventoryItem other)
        {
            if (this.IsBadge() && other.IsBadge())
            {
                return this.BadgeData.Effects.Intersect(other.BadgeData.Effects).Count() == this.BadgeData.Effects.Count;
            }
            else if (this.IsWeapon() && other.IsWeapon())
            {
                return this.WeaponData.GadgetId == other.WeaponData.GadgetId;
            }

            return false;
        }

        public bool IsValid()
        {
            if (IsBadge()) return BadgeData.Effects.Count > 0;
            if (IsWeapon()) return IsGadgetValid(WeaponData.GadgetId) && WeaponData.Damage > 0;

            return false;
        }

        public void UpgradeRarity()
        {
            if (!IsWeapon()) return;

            var rarity = GetRarityFromQuality(Quality);
            if (rarity == RaidsItemRarity.Mythic) return;

            // increase rarity
            rarity++;
            Quality = GetQualityFromRarity(rarity);

            // increase damage up to 50%
            WeaponData.Damage += (int)Math.Ceiling(WeaponData.Damage * _rng.NextDouble() * 0.5);

            // reset upgrades
            WeaponData.Upgrades = 0;
            WeaponData.MaxUpgrades = 10;

            // add alpha mods
            int numAlphaMods = GetRandomNumberOfAlphaMods(rarity, WeaponData.Proficiency);
            int currNumAlphaMods = WeaponData.AlphaModCounts.Sum(x => x);
            while (numAlphaMods > currNumAlphaMods)
            {
                var amod = GetRandomAlphaMod(WeaponData.GadgetId);
                var amodIdx = (int)amod - 1;
                if (amod != AlphaMods.None)
                {
                    if (WeaponData.AlphaModCounts[amodIdx] >= _gadgetAlphaModMax[amod]) continue;
                    WeaponData.AlphaModCounts[amodIdx]++;
                }

                ++currNumAlphaMods;
            }

            // 10% chance per rarity to add paint
            var paintChance = 0.1 * (int)rarity;
            if (WeaponData.Paint == RaidsWeaponPaints.None && _rng.NextDouble() < paintChance)
                WeaponData.Paint = (RaidsWeaponPaints)_rng.Next(1, (int)RaidsWeaponPaints.COUNT);

            // 5% chance per rarity to add special paint
            var paintSpecialChance = 0.05 * (int)rarity;
            if (WeaponData.PaintSpecialMask == RaidsPaintSpecialFlags.None && _rng.NextDouble() < paintChance)
                WeaponData.PaintSpecialMask = (RaidsPaintSpecialFlags)_rng.Next(1, (int)RaidsPaintSpecialFlags.ALL + 1);

            // 50% chance to increase mod quality
            // capped at weapon quality
            var modQualityChance = 0.5;
            var modNextQuality = GetQualityFromRarity(GetRarityFromQuality(WeaponData.ModQuality) + 1);
            if (WeaponData.Mod != WeaponMods.None && _rng.NextDouble() < modQualityChance)
                WeaponData.ModQuality = Math.Min(modNextQuality, Quality);

            // 25% chance per rarity to add mod
            // mod quality is capped at weapon quality
            // meaning only Mythic upgrades can get a level V mod
            var modChance = 0.25 * (int)rarity;
            if (WeaponData.Mod == WeaponMods.None && _rng.NextDouble() < modChance)
            {
                WeaponData.Mod = GetRandomWeaponMod(WeaponData.GadgetId);
                WeaponData.ModQuality = (byte)(Math.Pow(_rng.NextDouble(), 2) * (Quality + 1));
            }
        }

        public static uint GenerateUid()
        {
            uint uid = 0;

            while (uid == 0)
                uid = (uint)((long)_rng.Next(int.MinValue, int.MaxValue) - int.MinValue);

            return uid;
        }

        public static bool IsGadgetValid(Gadgets gadget)
        {
            return gadget == Gadgets.Vipers
                || gadget == Gadgets.MagmaCannon
                || gadget == Gadgets.Arbiter
                || gadget == Gadgets.Fusion
                || gadget == Gadgets.MineLauncher
                || gadget == Gadgets.B6
                || gadget == Gadgets.Flail
                || gadget == Gadgets.Holoshields
                ;
        }

        public static int? GetWeaponIndex(Gadgets gadget)
        {
            var slot = gadget.ToGadgetSlot();
            if (slot == null || slot < GadgetSlots.Vipers)
                return null;

            return (int)slot - 1;
        }

        public static float GetWeaponBaseDamage(Gadgets gadget)
        {
            return _baseDamages.GetValueOrDefault(gadget);
        }


        public static WeaponMods GetRandomWeaponMod(Gadgets gadget)
        {
            var viableMods = ((WeaponMods[])Enum.GetValues(typeof(WeaponMods))).ToList();

            // remove unused omega mods
            viableMods.Remove(WeaponMods.None);
            viableMods.Remove(WeaponMods.TimeBomb);
            viableMods.Remove(WeaponMods.Morph);
            viableMods.Remove(WeaponMods.Shock);
            viableMods.Remove(WeaponMods.Brainwash);

            // only explosive weapons work with napalm & minibomb
            if (gadget != Gadgets.Arbiter && gadget != Gadgets.B6 && gadget != Gadgets.MineLauncher)
            {
                viableMods.Remove(WeaponMods.Napalm);
                viableMods.Remove(WeaponMods.MiniBomb);
            }

            // no options
            if (!viableMods.Any()) return WeaponMods.None;

            // random
            return viableMods.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
        }

        public static AlphaMods GetRandomAlphaMod(Gadgets gadget)
        {
            AlphaMods[] options = _gadgetAlphamods[gadget];
            var r = _rng.Next(0, options.Length + 1);
            if (r == 0) return 0;

            return options[r - 1];
        }

        public static int GetRandomNumberOfAlphaMods(RaidsItemRarity rarity, int proficiency)
        {
            // mythic is guaranteed # of alphamods equal to max of legendary
            // mythic main benefit is being a v10
            if (rarity == RaidsItemRarity.Mythic)
            {
                return (2 * (int)Math.Pow(3, (int)RaidsItemRarity.Legendary)) + (proficiency / 25) + 2;
            }

            // min = 2*2^r - 1
            //  Common:     1
            //  Uncommon:   3
            //  Rare:       7
            //  Legendary:  15
            // max = 2*3^r + (p/25) + 2
            //  Common:     3 - 6
            //  Uncommon:   8 - 11
            //  Rare:       20 - 23
            //  Legendary:  56 - 59
            int minAmods = 2 * (int)Math.Pow(2, (int)rarity) - 1;
            int maxAmods = (2 * (int)Math.Pow(3, (int)rarity)) + (proficiency / 25) + 2;
            int amodCount = (int)(Math.Pow(_rng.NextDouble(), 1.1) * maxAmods);
            if (amodCount < minAmods) amodCount = minAmods;

            return amodCount;
        }

        public static RaidsItemRarity GetRarityFromQuality(int quality)
        {
            if (quality < 64) return RaidsItemRarity.Common;
            if (quality < 128) return RaidsItemRarity.Uncommon;
            if (quality < 192) return RaidsItemRarity.Rare;
            if (quality < 255) return RaidsItemRarity.Legendary;

            return RaidsItemRarity.Mythic;
        }

        public static byte GetQualityFromRarity(RaidsItemRarity rarity)
        {
            if (rarity == RaidsItemRarity.Common) return 0;
            if (rarity == RaidsItemRarity.Uncommon) return 64;
            if (rarity == RaidsItemRarity.Rare) return 128;
            if (rarity == RaidsItemRarity.Legendary) return 192;
            return 255;
        }

        public static RaidsInventoryItem GenerateDefaultWeapon(Gadgets gadget)
        {
            var weapon = new RaidsInventoryItem()
            {
                Type = RaidsItemType.Weapon,
                WeaponData = new RaidsInventoryItem.ItemWeaponData()
                {
                    GadgetId = gadget,
                    Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(gadget),
                    AlphaModCounts = new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 }
                }
            };
            weapon.Price = ComputeSellPrice(weapon);
            return weapon;
        }

        public static RaidsInventoryItem GenerateWeapon(RaidsGenerateLootDropRequest request, RaidsAccount account)
        {
            var drop = new RaidsInventoryItem();
            drop.Type = RaidsItemType.Weapon;
            drop.WeaponData = new ItemWeaponData();

            var availableGadgets = account == null ? _weaponGadgetIds : _weaponGadgetIds.Where(x => account.HasUnlockedWeapon(x));
            var availableQuickSelectGadgets = request.QuickSelectGadgets.Where(x => availableGadgets.Contains(x)).ToList();
            var unavailableGadgets = account == null ? new Gadgets[0] : _weaponGadgetIds.Where(x => !account.HasUnlockedWeapon(x));
            var paintChances = new[] { 0.05, 0.125, 0.175, 0.5, 1.0 };
            var specialChances = new[] { 0, 0.01, 0.02, 0.08, 0.25 };
            var omegaChances = new[] { 0, 0.1, 0.2, 0.5, 1.0 };
            var critChances = new[] { 0, 0.25, 0.5, 0.75, 1 };

            // small chance of drop for weapon not yet unlocked (1%)
            if (unavailableGadgets.Any() && request.Type == RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath && _rng.NextDouble() < 0.01)
            {
                var gadget = unavailableGadgets.OrderBy(x => Guid.NewGuid()).First();
                account.UnlockWeapon(gadget);
                return GenerateDefaultWeapon(gadget);
            }

            // can't generate for player
            if (!availableGadgets.Any()) return drop;

            // 50% chance to drop requested gadget
            // otherwise pick random from 3 quick select gadgets
            // or any random available gadget it quick select isn't valid
            if (availableGadgets.Contains(request.MobKilledByGadget) && _rng.NextDouble() < 0.5)
                drop.WeaponData.GadgetId = request.MobKilledByGadget;
            else if (availableQuickSelectGadgets.Any())
                drop.WeaponData.GadgetId = availableQuickSelectGadgets.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
            else
                drop.WeaponData.GadgetId = availableGadgets.OrderBy(x => Guid.NewGuid()).FirstOrDefault();

            // ensure account has dropped weapon unlocked
            account.UnlockWeapon(drop.WeaponData.GadgetId);

            // 
            int accountProf = account?.GetProficiency(drop.WeaponData.GadgetId) ?? 98;
            int minProficiency = account == null ? 0 : Math.Max(0, accountProf - 5);
            int maxProficiency = account == null ? 99 : Math.Min(99, accountProf + 5);
            int difficultyMinProficiency = _gadgetMinProficiencyForDifficulty[request.DifficultyStars];
            int difficultyMaxProficiency = _gadgetMaxProficiencyForDifficulty[request.DifficultyStars];
            var difficultyMaxQuality = _gadgetMaxQualityForDifficulty[request.DifficultyStars] - 0.00001;
            var proficiencyCurve = 2;

            // determine quality
            var quality = _rng.NextDouble() * _rng.NextDouble() * difficultyMaxQuality;

            switch (request.Type)
            {
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath:
                    {
                        //minProficiency = difficultyMinProficiency;
                        maxProficiency = Math.Min(maxProficiency, difficultyMaxProficiency);
                        minProficiency = Math.Max(0, maxProficiency - 5);
                        proficiencyCurve = 1;
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent:
                    {
                        var minQuality = _gadgetMissionCompleteMinQualityForDifficulty[request.DifficultyStars];
                        quality = Math.Pow(((1 - minQuality) * quality + minQuality), 1 - minQuality) + 0.01;
                        //quality = Math.Min(((1 - minQuality) * quality * (1 + minQuality)) + minQuality, Math.Min(1, difficultyMaxQuality + 0.05));

                        // move min closer to account prof unless account prof is below
                        minProficiency = (int)Math.Min((maxProficiency + difficultyMinProficiency) * 0.5, difficultyMinProficiency);
                        maxProficiency = difficultyMaxProficiency;
                        proficiencyCurve = 1;
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Store:
                    {
                        // reduce quality to max legendary
                        quality = _rng.NextDouble() * (200.0 / 256.0);
                        minProficiency = (int)(quality * 98);
                        maxProficiency = 99;
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Prestige:
                    {
                        quality = (0.5 + (0.5 * _rng.NextDouble()));
                        minProficiency = (int)(quality * 98);
                        maxProficiency = 99;
                        break;
                    }
            }

            drop.Quality = (byte)Math.Clamp(quality * 256, 0, 255);
            drop.WeaponData.Proficiency = (byte)(minProficiency + (Math.Pow(_rng.NextDouble() * _rng.NextDouble(), proficiencyCurve / 2f) * (maxProficiency - minProficiency)));
            drop.Notify = 1;
            var rarity = (int)GetRarityFromQuality(drop.Quality);

            // ensure rarity spawns within bounds of proficiency
            // ie mythics only spawn for P95+
            while (rarity > 0 && drop.WeaponData.Proficiency < _gadgetMinProficiencyForRarity[rarity])
            {
                rarity--;
                drop.Quality = GetQualityFromRarity((RaidsItemRarity)rarity);
            }

            // paint
            if (_rng.NextDouble() < paintChances[rarity])
                drop.WeaponData.Paint = (RaidsWeaponPaints)_rng.Next(0, (int)RaidsWeaponPaints.COUNT);

            // special
            if (_rng.NextDouble() < specialChances[rarity])
                drop.WeaponData.PaintSpecialMask = (RaidsPaintSpecialFlags)_rng.Next(0, (int)RaidsPaintSpecialFlags.ALL + 1);

            // mod
            if (_rng.NextDouble() < omegaChances[rarity])
            {
                drop.WeaponData.Mod = GetRandomWeaponMod(drop.WeaponData.GadgetId);
                drop.WeaponData.ModQuality = (byte)(Math.Pow(_rng.NextDouble(), 2) * 256);
            }

            // crit
            if (_rng.NextDouble() < critChances[rarity])
                drop.WeaponData.CritChance = (byte)(_rng.NextDouble() * drop.Quality);

            // damage
            var damageScale = _damageScales[drop.WeaponData.GadgetId] * _globalDamageScaleMultiplier;
            double damage = _baseDamages[drop.WeaponData.GadgetId];
            damage += damageScale * _damageCurveMultMax * Math.Pow(drop.WeaponData.Proficiency / 98.0, 2);
            damage += damageScale * damage * 0.50 * (drop.Quality / 255.0) * _rng.NextDouble(); // up to +50% for higher rarity
            damage += damageScale * damage * 0.1 * (_rng.NextDouble() - 0.5); // +/- 5%
            damage += 10 * (_rng.NextDouble() - 0.5); // +/- 5 (good for early levels)
            if (damage < _baseDamages[drop.WeaponData.GadgetId])
                damage = _baseDamages[drop.WeaponData.GadgetId];
            drop.WeaponData.Damage = (int)damage;

            // alpha mods
            int amodCount = GetRandomNumberOfAlphaMods((RaidsItemRarity)rarity, drop.WeaponData.Proficiency);
            for (int i = 0; i < amodCount; ++i)
            {
                while (true)
                {
                    var amod = GetRandomAlphaMod(drop.WeaponData.GadgetId);
                    var amodIdx = (int)amod - 1;
                    if (amod != AlphaMods.None)
                    {
                        if (drop.WeaponData.AlphaModCounts[amodIdx] >= _gadgetAlphaModMax[amod]) continue;
                        drop.WeaponData.AlphaModCounts[amodIdx]++;
                    }

                    break;
                }
            }

            drop.Price = ComputeSellPrice(drop);
            return drop;
        }

        public static RaidsInventoryItem GenerateBadge(RaidsGenerateLootDropRequest request, RaidsAccount account)
        {
            var drop = new RaidsInventoryItem();
            drop.Type = RaidsItemType.Badge;
            drop.BadgeData = new ItemBadgeData();

            var difficultyMaxQuality = _gadgetMaxQualityForDifficulty[request.DifficultyStars] - 0.00001;

            // determine quality
            var quality = _rng.NextDouble() * _rng.NextDouble() * difficultyMaxQuality;

            switch (request.Type)
            {
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath:
                    {

                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent:
                    {
                        var minQuality = _gadgetMissionCompleteMinQualityForDifficulty[request.DifficultyStars];
                        quality = Math.Pow(((1 - minQuality) * quality + minQuality), 1 - minQuality) + 0.01;
                        //quality = Math.Min(((1 - minQuality) * quality * (1 + minQuality)) + minQuality, Math.Min(1, difficultyMaxQuality + 0.05));
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Store:
                    {
                        // reduce quality to max legendary
                        quality = _rng.NextDouble() * (200.0 / 256.0);
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Prestige:
                    {
                        quality = (0.5 + (0.5 * _rng.NextDouble()));
                        break;
                    }
            }

            drop.Quality = (byte)Math.Clamp(quality * 256, 0, 255);

            int rarity = (int)GetRarityFromQuality(drop.Quality);
            int numEffects = rarity + 1;
            var availableBadgeTypes = ((RaidsBadgeType[])Enum.GetValues(typeof(RaidsBadgeType))).ToList();
            availableBadgeTypes.Remove(RaidsBadgeType.COUNT);
            availableBadgeTypes.Remove(RaidsBadgeType.NONE);
            for (int i = 0; i < numEffects; ++i)
            {
                if (!availableBadgeTypes.Any()) break;

                int idx = _rng.Next(0, availableBadgeTypes.Count);
                var badgeType = availableBadgeTypes[idx];
                var strength = (float)Math.Clamp(Math.Pow(_rng.NextDouble() * Math.Pow(quality, 0.5), _badgeEffectStrengthForRarity[rarity]), 0.01, 1);
                drop.BadgeData.Effects.Add(badgeType);
                drop.BadgeData.EffectStrength.Add(strength);
                availableBadgeTypes.Remove(badgeType);
            }

            drop.Notify = 1;
            drop.Price = ComputeSellPrice(drop);
            return drop;
        }

        public static RaidsInventoryItem Generate(RaidsGenerateLootDropRequest request, RaidsAccount account)
        {
            // 15% chance to generate badge
            //if ((!request.IsPrestige() && _rng.NextDouble() < 0.15) || request.IsAccountPrestige())
            //    return GenerateBadge(request, account);

            return GenerateWeapon(request, account);
        }

        public static uint ComputeBuyPrice(RaidsInventoryItem item)
        {
            int rarity = (int)GetRarityFromQuality(item.Quality);
            float quality = item.Quality / 255.0f;
            int roundTo = 100000;

            if (item.IsBadge())
            {
                double bolts = (item.BadgeData.Effects.Count * 100000) + (item.BadgeData.EffectStrength.Sum() * 1000000) + Math.Pow(1000000, 1 + Math.Pow((rarity / 6.0), 2)) + (10000000 * quality);
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }
            else if (item.IsWeapon())
            {
                double bolts = (item.WeaponData.Proficiency * 100000) + Math.Pow(500000, 1 + Math.Pow((rarity / 6.0), 2)) + (10000000 * quality);
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }

            throw new NotImplementedException();
        }

        public static uint ComputeSellPrice(RaidsInventoryItem item)
        {
            int rarity = (int)GetRarityFromQuality(item.Quality);
            float quality = item.Quality / 255.0f;
            int roundTo = 100;

            if (item.IsBadge())
            {
                double bolts = (5000 + Math.Pow(1000, 1 + Math.Pow((rarity / 4.0), 1.25)) + (10000 * quality));
                return (uint)((Math.Round(bolts / roundTo) + 1) * roundTo);
            }
            else if (item.IsWeapon())
            {
                double bolts = (item.WeaponData.Proficiency * 250) + Math.Pow(1000, 1 + Math.Pow((rarity / 4.0), 2)) + (10000 * quality);
                return (uint)((Math.Round(bolts / roundTo) + 1) * roundTo);
            }

            return 0;
        }

        public RaidsInventoryItem Copy()
        {
            return JsonConvert.DeserializeObject<RaidsInventoryItem>(JsonConvert.SerializeObject(this));
        }

        public bool MatchFilter(int filter)
        {
            if (filter == 0) return this.Type == RaidsItemType.Badge;
            
            if (filter > 0 && filter < 9)
            {
                var gadgetId = ((GadgetSlots)filter).ToGadget();
                return this.Type == RaidsItemType.Weapon && this.WeaponData.GadgetId == gadgetId;
            }

            return false;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)Type);
            writer.Write((byte)Notify);
            writer.Write((byte)Quality);
            writer.Write((byte)0); // padding
            writer.Write(Price);
            writer.Write(Uid ?? (uint)0);

            switch (Type)
            {
                case RaidsItemType.Weapon:
                    {
                        if (WeaponData == null) WeaponData = new ItemWeaponData();
                        if (WeaponData.AlphaModCounts == null) WeaponData.AlphaModCounts = new byte[8];
                        else if (WeaponData.AlphaModCounts.Length != 8) Array.Resize(ref WeaponData.AlphaModCounts, 8);

                        var paintCombined = ((int)WeaponData.PaintSpecialMask << 4) | ((int)WeaponData.Paint);

                        writer.Write(WeaponData.Damage);
                        writer.Write((byte)WeaponData.GadgetId);
                        writer.Write((byte)paintCombined);
                        writer.Write((byte)WeaponData.Proficiency);
                        writer.Write((byte)WeaponData.CritChance);
                        writer.Write((byte)WeaponData.Mod);
                        writer.Write((byte)WeaponData.ModQuality);
                        writer.Write(WeaponData.AlphaModCounts);
                        writer.Write((byte)WeaponData.Upgrades);
                        writer.Write((byte)WeaponData.MaxUpgrades);
                        break;
                    }
                case RaidsItemType.Badge:
                    {
                        if (BadgeData == null) BadgeData = new ItemBadgeData();

                        for (int i = 0; i < 8; ++i)
                            writer.Write(i < BadgeData.Effects.Count ? (byte)BadgeData.Effects[i] : (byte)0);
                        for (int i = 0; i < 8; ++i)
                            writer.Write((byte)Math.Ceiling(i < BadgeData.EffectStrength.Count ? (BadgeData.EffectStrength[i] * 255) : 0));

                        writer.Write(new byte[4]);
                        break;
                    }
                default:
                    {
                        writer.Write(new byte[20]);
                        break;
                    }
            }

        }

        public void Deserialize(BinaryReader reader)
        {
            Type = (RaidsItemType)reader.ReadByte();
            Notify = reader.ReadByte();
            Quality = reader.ReadByte();
            reader.ReadByte();
            Price = reader.ReadUInt32();
            Uid = reader.ReadUInt32();

            switch (Type)
            {
                case RaidsItemType.Weapon:
                    {
                        if (WeaponData == null) WeaponData = new ItemWeaponData();

                        WeaponData.Damage = reader.ReadInt32();
                        WeaponData.GadgetId = (Gadgets)reader.ReadByte();
                        var paintCombined = reader.ReadByte();
                        WeaponData.Proficiency = reader.ReadByte();
                        WeaponData.CritChance = reader.ReadByte();
                        WeaponData.Mod = (WeaponMods)reader.ReadByte();
                        WeaponData.ModQuality = reader.ReadByte();
                        WeaponData.AlphaModCounts = reader.ReadBytes(8);
                        WeaponData.Upgrades = reader.ReadByte();
                        WeaponData.MaxUpgrades = reader.ReadByte();

                        WeaponData.Paint = (RaidsWeaponPaints)(paintCombined & 0xf);
                        WeaponData.PaintSpecialMask = (RaidsPaintSpecialFlags)(paintCombined >> 4);
                        break;
                    }
                case RaidsItemType.Badge:
                    {
                        if (BadgeData == null) BadgeData = new ItemBadgeData();

                        BadgeData.Effects = reader.ReadBytes(8).Select(x => (RaidsBadgeType)x).ToList();
                        BadgeData.EffectStrength = reader.ReadBytes(8).Select(x => x * 255f).ToList();
                        reader.ReadBytes(4);
                        break;
                    }
                default:
                    {
                        reader.ReadBytes(20);
                        break;
                    }
            }

        }
    }

    public class RaidsItemStore
    {
        // one xp mod
        public static readonly byte[] STORE_FIXED_ITEM_ALPHAMODS = new byte[8] { 0, 0, 0, 0, 0, 1, 0, 0 };
        public static readonly List<RaidsInventoryItem> FIXED_ITEMS = new List<RaidsInventoryItem>()
        {
            RaidsInventoryItem.GenerateDefaultWeapon(Gadgets.Vipers),
            RaidsInventoryItem.GenerateDefaultWeapon(Gadgets.MagmaCannon),
            RaidsInventoryItem.GenerateDefaultWeapon(Gadgets.Arbiter),
            RaidsInventoryItem.GenerateDefaultWeapon(Gadgets.Fusion),
            RaidsInventoryItem.GenerateDefaultWeapon(Gadgets.MineLauncher),
            RaidsInventoryItem.GenerateDefaultWeapon(Gadgets.B6),
            RaidsInventoryItem.GenerateDefaultWeapon(Gadgets.Holoshields),
            RaidsInventoryItem.GenerateDefaultWeapon(Gadgets.Flail),
        };

        public List<RaidsInventoryItem> Items = new List<RaidsInventoryItem>();
        public DateTime? NextRefresh = null;

        public bool Refresh(RaidsAccount account, bool force)
        {
            // check if refresh is needed
            // refresh start of day UTC
            if (!force && DateTime.UtcNow < NextRefresh)
                return false;

            Items.Clear();

            const int count = 8;
            for (int i = 0; i < count; ++i)
            {
                int rerollCount = 0;
                RaidsInventoryItem item = null;
                while (rerollCount < 3)
                {
                    item = RaidsInventoryItem.Generate(new RaidsGenerateLootDropRequest() { Type = RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Store }, account);
                    if (!Items.Any(x => x.IsSameItem(item)))
                        break;
                    ++rerollCount;
                }
                item.Price = RaidsInventoryItem.ComputeBuyPrice(item);
                Items.Add(item);
            }

            Items = Items.OrderByDescending(x => x.Price).ThenBy(x => x.IsWeapon()).ToList();

            NextRefresh = DateTime.UtcNow.AddHours(0.5f);
            return true;
        }

        public IEnumerable<RaidsInventoryItem> GetPaginated(int page, int pageSize, out int totalCount)
        {
            // always have  pages
            // first page is featured rotating items
            int itemsPages = (int)Math.Ceiling(Items.Count / (float)pageSize);
            totalCount = itemsPages * pageSize;

            int idx = page * pageSize;
            if (idx < Items.Count)
            {
                return Items.Skip(idx).Take(pageSize);
            }

            return [];
        }

        public IEnumerable<RaidsInventoryItem> GetPaginatedWithFixedItems(int page, int pageSize, out int totalCount)
        {
            // always have  pages
            // first page is featured rotating items
            // second page is the base weapons
            var itemsPages = (int)Math.Ceiling(Items.Count / (float)pageSize);
            var fixedPages = (int)Math.Ceiling(FIXED_ITEMS.Count / (float)pageSize);
            totalCount = (itemsPages * pageSize) + FIXED_ITEMS.Count;

            int idx = page * pageSize;
            if (idx < FIXED_ITEMS.Count)
            {
                return FIXED_ITEMS.Skip(idx).Take(pageSize);
            }
            else
            {
                return Items.Skip((page - fixedPages) * pageSize).Take(pageSize);
            }

            // old where store had fixed items on last page
            // if (idx < Items.Count)
            // {
            //     return Items.Skip(idx).Take(pageSize);
            // }
            // else
            // {
            //     return FIXED_ITEMS.Skip((page - itemsPages) * pageSize).Take(pageSize);
            // }
        }

        public void OnBuy(int page, int pageSize, int itemIdx, RaidsInventoryItem item, RaidsAccount account)
        {
            // try and rotate the bought item
            int itemsPages = (int)Math.Ceiling(Items.Count / (float)pageSize);

            // bought item was not in the list of rotating weapons
            if (page >= itemsPages) return;

            // get item idx
            var idx = (page * pageSize) + itemIdx;
            if (idx >= Items.Count) return;

            // validate item matches
            RaidsInventoryItem storeItem = Items[idx];
            if (storeItem.Type != item.Type) return;

            // generate new item
            Items[idx] = RaidsInventoryItem.Generate(new RaidsGenerateLootDropRequest() { Type = RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Store }, account);
            Items[idx].Price = RaidsInventoryItem.ComputeBuyPrice(Items[idx]);
            Items = Items.OrderByDescending(x => x.Price).ThenBy(x => x.IsWeapon()).ToList();
        }
    }

    public class RaidsContractStore
    {
        private static readonly Random _rng = new Random();
        public static readonly int MAX_CONTRACTS = 3;
        public static readonly int MIN_LEVEL = 9;
        public static readonly uint[] RAID_CLASS_XP_REWARDS = new uint[] { 250, 750, 1500, 2500, 4000 };

        public int ContractsCompleted = 0;
        public List<RaidsContract> Contracts = new List<RaidsContract>();

        public bool Refresh(RaidsBank bank, bool force)
        {
            var changed = false;

            // must be min level before contracts available
            if (RaidsAccount.GetAccountLevelFromXp(bank.Account.Experience) < MIN_LEVEL)
                return false;

            for (int i = 0; i < MAX_CONTRACTS; ++i)
            {
                var contract = Contracts.ElementAtOrDefault(i);
                if (contract == null)
                {
                    contract = GenerateContract(bank);
                    Contracts.Add(contract);
                    changed = true;
                }

                // expired, regenerate
                if (contract.Uid == null || contract.Uid == 0 || contract.ShouldRefresh() || force)
                {
                    Contracts[i] = GenerateContract(bank);
                    changed = true;
                }
            }

            if (!changed) return false;
                
            return changed;
        }

        public RaidsContract Reroll(uint uid, RaidsBank bank)
        {
            var contract = Contracts.FirstOrDefault(x => x.Uid == uid);
            if (contract == null) return null;

            // generate new contract
            var idx = Contracts.IndexOf(contract);
            Contracts[idx] = GenerateContract(bank);

            return contract;
        }

        public RaidsContract Complete(uint uid, RaidsBank bank)
        {
            var contract = Contracts.FirstOrDefault(x => x.Uid == uid);
            if (contract == null) return null;

            ++ContractsCompleted;

            // give reward
            // now done on client
            //bank.Account.AddBolts(contract.RewardBolts);
            //bank.Account.AddXp(contract.RewardPlayerXp);
            //bank.Account.AddWeaponXp(contract.RewardWeaponXp, contract.RequiredKillsGadgetId);

            // generate new one
            Reroll(uid, bank);

            return contract;
        }

        public RaidsContract Activate(uint uid, RaidsBank bank)
        {
            var contract = Contracts.FirstOrDefault(x => x.Uid == uid);
            if (contract == null) return null;
            
            // already activated
            if (contract.IsActivated()) return contract;

            contract.Activate();
            return contract;
        }

        private uint GenerateUid()
        {
            uint uid = 0;

            while (uid == 0)
                uid = (uint)((long)_rng.Next(int.MinValue, int.MaxValue) - int.MinValue);

            return uid;
        }

        private Gadgets GenerateContractWeapon(RaidsAccount account)
        {
            var unlockedClasses = RaidsCustomMode.RAIDS_CLASSES.Where(x => x.IsUnlocked(account)).ToArray();
            var weapons = unlockedClasses.SelectMany(x => x.Weapons).Distinct().ToArray();
            var equippedClass = unlockedClasses.FirstOrDefault(x => x.ClassType == account.EquippedClass);

            // Prefer the current class while keeping other unlocked weapons in rotation.
            if (equippedClass != null && equippedClass.Weapons.Length > 0 && _rng.NextDouble() < 0.8)
                weapons = equippedClass.Weapons;

            return weapons[_rng.Next(weapons.Length)];
        }

        private RaidsContract GenerateContract(RaidsBank bank)
        {
            var contract = new RaidsContract();

            // select random map
            var selectedMap = bank.MapStats.Where(x => (x.Value.MissionType == RaidsMissionType.OpenWorld || x.Value.MissionType == RaidsMissionType.Raid) && x.Value.ContractRules.Any()).OrderBy(x => Guid.NewGuid()).FirstOrDefault();
            if (selectedMap.Value == null) return RaidsContract.Empty;

            // update name
            contract.MapFilename = selectedMap.Key;
            contract.MapName = selectedMap.Value.Name;

            // generate unique id
            contract.Uid = GenerateUid();
            while (Contracts.Any(x => x != null && x.Uid == contract.Uid && x != contract))
                contract.Uid = GenerateUid();

            // required raid time - if raid and 25% chance
            if (selectedMap.Value.MissionType == RaidsMissionType.Raid && _rng.NextDouble() < 0.25)
            {
                contract.RequiredDifficultyStars = Enumerable.Range(0, 5).Where(x => selectedMap.Value.BestTimeMsPerDifficulty.ElementAtOrDefault(x) > 0).OrderBy(x => Guid.NewGuid()).FirstOrDefault();
                contract.RewardBolts = (uint)((contract.RequiredDifficultyStars + 1) * 500000) + (uint)(selectedMap.Value.ContractsCompleted * 100000);

                // beat last record
                var timeToBeat = (long)selectedMap.Value.BestTimeMsPerDifficulty[contract.RequiredDifficultyStars];
                var timeInc = 1000 * 60; // 60sec

                // default to 30min if not yet completed
                if (timeToBeat <= 0) contract.RequiredRaidTimeMs = 1000 * 60 * 30;
                else contract.RequiredRaidTimeMs = (uint)(timeToBeat + (timeInc - (timeToBeat % timeInc)));

                // determine xp
                var starsXpFactor = Math.Pow(contract.RequiredDifficultyStars + 1, 2); // squared
                var min = Math.Clamp(Math.Ceiling(contract.RequiredRaidTimeMs / (1000f * 60f)), 1, 10);
                contract.RewardPlayerXp = (uint)(_rng.Next(100, 300) * 10 * starsXpFactor);
                contract.RewardWeaponXp = (uint)(_rng.Next(100, 300) * 10 * starsXpFactor);
                contract.RewardClassXp = RAID_CLASS_XP_REWARDS[contract.RequiredDifficultyStars];
                contract.RequiredKillsGadgetId = GenerateContractWeapon(bank.Account);
                contract.ExpirationMinutes = (uint)Math.Ceiling((timeToBeat * 2) / (1000f * 60)); // give double time to complete
            }

            // required kills
            else // if (selectedMap.Value.MissionType == RaidsMissionType.OpenWorld)
            {
                var contractRule = selectedMap.Value.ContractRules.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
                contract.RequiredDifficultyStars = 0; // _rng.Next(0, 5);
                contract.MobName = contractRule.Name;
                contract.RequiredKills = (uint)_rng.Next(contractRule.MinCount, contractRule.MaxCount);

                // round kills to 10
                if (contract.RequiredKills % 10 != 0)
                    contract.RequiredKills += 10 - (contract.RequiredKills % 10);

                contract.RewardPlayerXp = (uint)(contract.RequiredKills * contractRule.XpMult * 10 * _rng.Next(2, 5) * 0.5f);
                contract.RewardWeaponXp = (uint)(contract.RequiredKills * contractRule.XpMult * 10 * _rng.Next(2, 5) * 0.5f);

                // weapon
                contract.RequiredKillsGadgetId = GenerateContractWeapon(bank.Account);
                contract.RewardBolts += (uint)(contractRule.BoltMult * contract.RequiredKills * 100);
                contract.ExpirationMinutes = contractRule.ExpirationMinutes;
            }

            return contract;
        }
    }

    public class RaidsContractRule
    {
        public ushort MobDefMask;
        public string Name;
        public ushort MinCount;
        public ushort MaxCount;
        public ushort ExpirationMinutes;
        public float XpMult;
        public float BoltMult;

        public override bool Equals(object obj)
        {
            if (obj is RaidsContractRule other)
                return this.MobDefMask == other.MobDefMask && this.Name == other.Name && this.MinCount == other.MinCount && this.MaxCount == other.MaxCount && this.ExpirationMinutes == other.ExpirationMinutes && this.XpMult == other.XpMult && this.BoltMult == other.BoltMult;

            return false;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public void Deserialize(BinaryReader reader)
        {
            MobDefMask = reader.ReadUInt16();
            MinCount = reader.ReadUInt16();
            MaxCount = reader.ReadUInt16();
            ExpirationMinutes = reader.ReadUInt16();
            XpMult = reader.ReadSingle();
            BoltMult = reader.ReadSingle();
            Name = reader.ReadString(32);
        }
    }

    public class RaidsMapStats
    {
        public string Name;
        public int CollectiblesCount;
        public uint CollectiblesMask;

        public int ChallengesCount;
        public uint ChallengesMask;

        public uint[] BestTimeMsPerDifficulty = new uint[5];

        public RaidsMissionType MissionType = RaidsMissionType.Hub;
        public int ContractsCompleted = 0;

        // set of oclasses from kills on the map
        public List<RaidsContractRule> ContractRules = new List<RaidsContractRule>();
        public HashSet<(int oclass, int difficultyStars)> EnabledMobs = new HashSet<(int oclass, int difficultyStars)>();

        public float GetPercentComplete(bool ignoreTimes = false)
        {
            float max = 0;
            float sum = 0;

            if (CollectiblesCount > 0)
            {
                max += CollectiblesCount;
                sum += BinaryHelper.CountBits(CollectiblesMask);
            }

            if (ChallengesCount > 0)
            {
                max += ChallengesCount;
                sum += BinaryHelper.CountBits(ChallengesMask);
            }

            // limit to 5 (50% of progress)
            if (max > 5)
            {
                var ratio = 5 / max;
                max *= ratio;
                sum *= ratio;
            }

            if (!ignoreTimes)
            {
                max += 5;
                sum += BestTimeMsPerDifficulty.Count(x => x > 0);
            }

            if (max == 0) return 0;

            return sum / max;
        }

        public void Serialize(BinaryWriter writer, string mapFilename, RaidsMissionType missionType)
        {
            writer.Write(0); // is valid
            writer.Write(CollectiblesCount);
            writer.Write(CollectiblesMask);
            writer.Write(ChallengesCount);
            writer.Write(ChallengesMask);
            writer.Write(GetPercentComplete(missionType != RaidsMissionType.Raid));
            for (int i = 0; i < 5; ++i)
                writer.Write(BestTimeMsPerDifficulty[i]);
            writer.Write(mapFilename, 64);
        }
    }

    public class RaidsMobMetadata
    {
        public string Name { get; set; }
        public string NamePlural { get; set; }
        public int OClass { get; set; }

        public RaidsMobMetadata() { }
        public RaidsMobMetadata(string name, int oclass, string namePlural = null)
        {
            Name = name;
            NamePlural = namePlural ?? (name + "s");
            OClass = oclass;
        }
    }

    public class RaidsClassProgression
    {
        public RaidsClassType ClassType;
        public int Tier;
        public decimal ExperienceCurveMultiplier;
        public Gadgets[] Weapons;
        public (RaidsClassType ClassType, int RequiredLevel)[] UnlockRules;

        public RaidsClassProgression(RaidsClassType classType, decimal xpCurveMult, int tier, Gadgets[] weapons, (RaidsClassType ClassType, int RequiredLevel)[] unlockRules = null)
        {
            ClassType = classType;
            Tier = tier;
            ExperienceCurveMultiplier = xpCurveMult;
            Weapons = weapons;
            UnlockRules = unlockRules ?? [];
        }

        public bool IsUnlocked(RaidsAccount account)
        {
            return UnlockRules.All(rule =>
            {
                var requiredClass = RaidsCustomMode.RAIDS_CLASSES.FirstOrDefault(x => x.ClassType == rule.ClassType);
                var xp = account.ClassStats?.GetValueOrDefault(rule.ClassType)?.Experience ?? 0;
                return requiredClass != null && requiredClass.GetClassLevelFromXp(xp) >= rule.RequiredLevel;
            });
        }

        public int GetClassLevelFromXp(ulong xp)
        {
            int level = 0;
            while (level < RaidsCustomMode.MAX_CLASS_LEVEL && GetClassXpFromLevel(level + 1) <= xp)
                ++level;
            return level;
        }

        public ulong GetClassXpFromLevel(int level)
        {
            if (level <= 0) return 0;
            if (level > RaidsCustomMode.MAX_CLASS_LEVEL) level = RaidsCustomMode.MAX_CLASS_LEVEL;
            return (ulong)((10 * (double)Math.Pow(level, 4) + 250 * level) * (double)ExperienceCurveMultiplier);
        }
    }

    public class RaidsClassStats
    {
        public const int MAX_SKILLS = 16;

        public ulong Experience;
        // Numeric keys keep persisted allocations tied to stable skill IDs.
        public Dictionary<int, int> SkillRanks = [];

        public static void Serialize(BinaryWriter writer, RaidsClassType classType, RaidsClassStats stats)
        {
            writer.Write((int)classType);

            var ranks = stats?.SkillRanks.OrderBy(x => x.Key).ToArray() ?? [];
            for (int i = 0; i < MAX_SKILLS; ++i)
            {
                writer.Write(i < ranks.Length ? (byte)ranks[i].Key : (byte)0);
                writer.Write(i < ranks.Length ? (byte)ranks[i].Value : (byte)0);
            }
        }
    }

}
