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
using System.Text;
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

        private readonly object _contractLock = new();
        private readonly Dictionary<(int Owner, string Map, int Sender), (ushort Expected, List<RaidsContractRule> Rules)> _contractUploads = new();

        public override CustomModeId Id => CustomModeId.CMODE_ID_RAIDS;
        public override string Name => "DreadZone Raids (Alpha)";

        private static RaidsBank GetBank(ClientObject client, bool initialize = true)
        {
            var bank = Player.GetPlayerMetadata(client)?.RaidsBank;
            if (initialize)
                bank?.Initialize();

            return bank;
        }

        private static IEnumerable<ClientObject> GetParty(ClientObject client) =>
            client.CurrentGame.Clients.Select(c => c.Client).Where(c => c != null);

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
                var account = GetBank(client, initialize: false)?.Account ?? new RaidsAccount();
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

                    writer.Write(bank.Inventory.AllItems.Count);
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

        public void OnClientRequestBankInventoryItemUpdate(RaidsUpdateBankInventoryItemRequest request, ClientObject client)
        {
            var bank = GetBank(client, initialize: false);
            if (bank == null) return;

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
                        bank.Equip(existingItem);
                        break;
                    }
                case RaidsUpdateBankInventoryItemRequest.ItemUpdateAction.Unequip:
                    {
                        bank.Unequip(existingItem);
                        break;
                    }
            }

            // save
            Player.SavePlayerMetadata(client);
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

        public void OnClientRequestSettings(RaidsGetSettingsRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            var account = bank.Account;
            if (account.Settings == null || !account.Settings.IsValid())
                account.Settings = new RaidsUserSettings();
            using (var ms = new MemoryStream(RaidsUserSettings.SERIALIZED_SIZE))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    account.Settings.Serialize(writer);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                }
            }
            Player.SavePlayerMetadata(client);
        }

        public void OnClientSaveSettings(RaidsSaveSettingsRequest request, ClientObject client)
        {
            if (request.Settings == null || !request.Settings.IsValid() || request.Revision <= 0) return;
            var bank = GetBank(client);
            if (bank == null) return;

            bank.Account.Settings = request.Settings;
            Player.SavePlayerMetadata(client);
            client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.AckAddress, BitConverter.GetBytes(request.Revision)));
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

        public void OnClientRequestEquippedClassProgress(RaidsGetEquippedClassProgressRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    var raidsClass = bank.Account.GetEquippedClass();
                    var stats = bank.Account.GetEquippedClassStats();
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
            if (request.Offset < 0 || request.Count < 0 || request.Offset > 16 || request.Count > 16 - request.Offset) return;

            var bank = GetBank(client);
            if (bank == null) return;

            // send all requested slots; unused slots have an invalid class ID
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    for (int i = request.Offset; i < request.Offset + request.Count; ++i)
                    {
                        var raidsClass = RAIDS_CLASSES.ElementAtOrDefault(i);
                        var classType = raidsClass?.ClassType ?? (RaidsClassType)(-1);
                        var stats = bank.Account.ClassStats.GetValueOrDefault(classType);
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
            var bank = GetBank(client, initialize: false);
            if (bank == null) return;
            var account = bank.Account;
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
            var bank = GetBank(client, initialize: false);
            if (bank == null) return;
            var account = bank.Account;
            if (request.ClassType != account.EquippedClass || request.SkillId != 0 || account.GetEquippedClass() == null) return;

            var stats = account.GetEquippedClassStats();
            stats.SkillRanks.Clear();
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestStoreItems(RaidsGetStoreItemsRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    // refresh items
                    bank.ItemStore.Refresh(bank.Account, false);

                    // items
                    var items = bank.ItemStore.GetPaginatedWithFixedItems(request.Page, request.PageSize, out int itemsCount);
                    foreach (var item in items)
                    {
                        item.Price = RaidsInventoryItem.ComputeBuyPrice(item);
                        item.Serialize(writer);
                    }

                    int rotateInSeconds = (int)((bank.ItemStore.NextRefresh - DateTime.UtcNow)?.TotalSeconds ?? 0);

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
            if (!request.Item.IsValid()) return;

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

        public async Task OnClientRequestGenerateLootDrop(RaidsGenerateLootDropRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

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
            var bank = GetBank(client);
            if (bank == null) return;

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
            var bank = GetBank(client);
            if (bank == null) return;

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
            var bank = GetBank(client);
            if (bank == null) return;

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            // add & save
            if (request.MobOClass > 0 && mapStats.EnabledMobs.Add((request.MobOClass, request.MobDifficulty)))
            {
                Player.SavePlayerMetadata(client);
            }
        }

        public void OnClientSetMissionCompleted(RaidsSetMissionCompletedRequest request, ClientObject client)
        {
            if (request.Difficulty < 0 || request.Difficulty >= 5) return;

            var bank = GetBank(client);
            if (bank == null) return;

            var mapStats = bank.MapStats.GetValueOrDefault(request.MapFilename);
            if (mapStats == null)
                bank.MapStats.Add(request.MapFilename, mapStats = new RaidsMapStats());

            var bestTimeMs = mapStats.BestTimeMsPerDifficulty[request.Difficulty];
            if (request.CompletedInMs < bestTimeMs || bestTimeMs == 0)
                mapStats.BestTimeMsPerDifficulty[request.Difficulty] = request.CompletedInMs;

            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestContracts(RaidsGetContractsRequest request, ClientObject client, GameMetadata metadata)
        {
            if (client.CurrentGame?.Host == null
                || request.Difficulty >= 5
                || !Enum.IsDefined(request.Flags))
                return;

            lock (_contractLock)
            {
                var host = client.CurrentGame.Host;
                var bank = GetBank(host);
                if (bank == null) return;

                // fill empty board slots when a map page is requested
                if (!request.Flags.HasFlag(RaidsContractRequestFlags.Pending)
                    && !string.IsNullOrEmpty(request.MapFilename)
                    && bank.MapStats.TryGetValue(request.MapFilename, out var stats))
                {
                    var previousUid = bank.ContractStore.NextUid;
                    var previousCount = bank.ContractStore.Boards.GetValueOrDefault(request.MapFilename)?.Contracts.Count ?? 0;
                    var board = bank.ContractStore.GetBoard(request.MapFilename, stats.Name ?? request.MapFilename);
                    bank.ContractStore.Refresh(board, (uint)host.AccountId, stats.ContractRules, ContractLevels(host), DateTime.UtcNow,
                        RaidsAccount.GetAccountLevelFromXp(bank.Account.Experience) + 1);

                    // save if the board changed
                    var boardChanged = previousUid != bank.ContractStore.NextUid || previousCount != board.Contracts.Count;
                    if (boardChanged)
                        Player.SavePlayerMetadata(host);
                }

                // resume party claims that were interrupted by a disconnect or map change
                if (GetBank(client)?.ContractStore.Receipts.Values.Any(r => r.PartyClaimPending && !r.Claimed) == true)
                    NotifyPartyContractClaim(client, new RaidsActionContractRequest());

                // send requested page
                SendContractPage(client, request);
            }
        }

        public void OnClientUpdateContractStats(RaidsUpdateContractStatsRequest request, ClientObject client, GameMetadata metadata)
        {
            // only the host can report progress for its boards
            if (client.CurrentGame?.Host != client || request.OwnerAccountId != client.AccountId || request.Count > 3) return;

            lock (_contractLock)
            {
                var bank = GetBank(client);
                var board = bank?.ContractStore.Boards.Values.FirstOrDefault(b => b.BoardId == request.BoardId);
                if (board == null) return;

                // acknowledge stale updates without applying progress twice
                if (request.Sequence <= board.Sequence)
                {
                    SendContractProgress(client, request, board);
                    return;
                }

                // apply progress to active contracts
                var now = DateTime.UtcNow;
                foreach (var stat in request.Contracts)
                {
                    var contract = board.Contracts.FirstOrDefault(c => c.Uid == stat.ContractUid);
                    if (contract == null || contract.Completed || contract.IsExpired(now)) continue;

                    if (contract.Type == RaidsContractType.Kill)
                    {
                        contract.Kills = Math.Min(contract.RequiredKills, Math.Max(contract.Kills, stat.Progress));
                    }
                    else if (contract.Type == RaidsContractType.Clear
                        && stat.Progress > 0 && stat.Difficulty >= 0 && stat.Difficulty < 5)
                    {
                        contract.CompletedTimeMs = stat.Progress;
                        contract.CompletedDifficulty = stat.Difficulty;
                    }

                    if (!contract.Completed) continue;

                    // record rewards for all members present at completion
                    foreach (var member in GetParty(client))
                    {
                        var memberBank = GetBank(member);
                        memberBank.ContractStore.AddReceipt(contract, (int)memberBank.Account.EquippedClass);
                        if (member != client)
                            Player.SavePlayerMetadata(member);
                    }
                }

                // save host progress
                board.Sequence = request.Sequence;
                Player.SavePlayerMetadata(client);

                // send progress to the party
                foreach (var member in GetParty(client))
                    SendContractProgress(member, request, board);
            }
        }

        public void OnClientActionContract(RaidsActionContractRequest request, ClientObject client, GameMetadata metadata)
        {
            if (client.CurrentGame?.Host == null
                || (request.Action != RaidsActionContractRequest.ContractActions.Complete
                    && request.Action != RaidsActionContractRequest.ContractActions.DismissExpired
                    && request.Action != RaidsActionContractRequest.ContractActions.ClaimPartyRewards))
                return;

            lock (_contractLock)
            {
                var bank = GetBank(client);
                var host = client.CurrentGame.Host;
                var hostBank = GetBank(host);
                if (bank == null || hostBank == null) return;

                if (request.Action == RaidsActionContractRequest.ContractActions.DismissExpired)
                {
                    // the client decides when an offer can be dismissed
                    var dismissed = false;
                    var board = hostBank.ContractStore.Boards.Values.FirstOrDefault(b => b.Contracts.Any(c => c.Uid == request.ContractUid));
                    var contract = board?.Contracts.FirstOrDefault(c => c.Uid == request.ContractUid);
                    if (contract != null && hostBank.MapStats.TryGetValue(board.MapFilename, out var map))
                    {
                        hostBank.ContractStore.Replace(board, contract.Uid, (uint)host.AccountId, map.ContractRules,
                            ContractLevels(host), DateTime.UtcNow, RaidsAccount.GetAccountLevelFromXp(hostBank.Account.Experience) + 1);

                        // save
                        Player.SavePlayerMetadata(host);
                        dismissed = true;
                    }

                    // send result and account to client
                    ContractReply(client, request, writer =>
                    {
                        writer.Write(request.RequestId);
                        writer.Write(request.ContractUid);
                        writer.Write(dismissed ? 1u : 0u);
                        bank.Account.Serialize(writer);
                    });
                }
                else if (request.Action == RaidsActionContractRequest.ContractActions.ClaimPartyRewards)
                {
                    // each member saves its local account before collecting the marked receipts
                    // a retry cannot collect rewards from a later batch
                    if (request.ContractUid != 0 && request.ContractUid == bank.ContractStore.PartyClaimUid)
                    {
                        foreach (var receipt in bank.ContractStore.Receipts.Values.Where(r => r.PartyClaimPending && !r.Claimed).ToList())
                            ClaimContractReceipt(bank, receipt);
                        bank.ContractStore.PartyClaimUid = 0;
                    }

                    Player.SavePlayerMetadata(client);
                    ContractReply(client, request, writer =>
                    {
                        writer.Write(request.RequestId);
                        writer.Write(request.ContractUid);
                        writer.Write(1u);
                        bank.Account.Serialize(writer);
                    });
                }
                else if (request.Action == RaidsActionContractRequest.ContractActions.Complete)
                {
                    // claim only rewards recorded for this client
                    var receipt = bank.ContractStore.FindReceipt(request.OwnerAccountId, request.ContractUid);
                    if (receipt != null && !receipt.Claimed)
                    {
                        // mark eligible party receipts before replacing the shared offer
                        foreach (var member in GetParty(client))
                        {
                            if (member == client) continue;
                            var memberBank = GetBank(member);
                            var memberReceipt = memberBank?.ContractStore.FindReceipt(request.OwnerAccountId, request.ContractUid);
                            if (memberReceipt == null || memberReceipt.Claimed) continue;

                            memberReceipt.PartyClaimPending = true;
                            NotifyPartyContractClaim(member, request);
                        }

                        ClaimContractReceipt(bank, receipt);

                        // replace the claimed offer if it belongs to the current host's board
                        if (request.OwnerAccountId == host.AccountId
                            && hostBank.ContractStore.Boards.TryGetValue(receipt.Contract.MapFilename, out var board)
                            && hostBank.MapStats.TryGetValue(board.MapFilename, out var map))
                        {
                            hostBank.ContractStore.Replace(board, request.ContractUid, (uint)host.AccountId, map.ContractRules,
                                ContractLevels(host), DateTime.UtcNow, RaidsAccount.GetAccountLevelFromXp(hostBank.Account.Experience) + 1);
                            if (host != client)
                                Player.SavePlayerMetadata(host);
                        }

                        // save
                        Player.SavePlayerMetadata(client);
                    }

                    // send result and updated account to client
                    // a repeated claim returns success without awarding the reward again
                    ContractReply(client, request, writer =>
                    {
                        writer.Write(request.RequestId);
                        writer.Write(request.ContractUid);
                        writer.Write(receipt?.Claimed == true
                            || bank.ContractStore.WasClaimed(request.OwnerAccountId, request.ContractUid) ? 1u : 0u);
                        bank.Account.Serialize(writer);
                    });
                }
            }
        }

        public void OnClientUpdateMapContractRules(RaidsUpdateMapContractRulesRequest request, ClientObject client, GameMetadata metadata)
        {
            if (client.CurrentGame?.Host == null
                || string.IsNullOrWhiteSpace(request.MapFilename)
                || request.MissionType < 1
                || request.MissionType > 2)
                return;

            lock (_contractLock)
            {
                var host = client.CurrentGame.Host;
                var bank = GetBank(host);
                var key = (host.AccountId, request.MapFilename, client.AccountId);

                // validate this upload page before changing the accumulated rules
                if (request.TotalCount > 16
                    || request.Count > 7
                    || request.Offset > request.TotalCount
                    || request.Count > request.TotalCount - request.Offset
                    || request.ContractRules.Count != request.Count
                    || (request.Count == 0 && request.TotalCount != 0)
                    || request.ContractRules.Any(r => !r.IsValid()))
                    return;

                // the first page starts a new upload for this host, map and sender
                if (request.Offset == 0)
                    _contractUploads[key] = (request.TotalCount, new());

                // accept only the next page of the same upload
                if (!_contractUploads.TryGetValue(key, out var upload)
                    || upload.Expected != request.TotalCount
                    || upload.Rules.Count != request.Offset)
                    return;

                upload.Rules.AddRange(request.ContractRules);
                if (upload.Rules.Count != upload.Expected) return;

                _contractUploads.Remove(key);

                // validate the complete table before replacing the saved rules
                // clear contracts require a raid map with at most one clear rule
                if ((request.MissionType != 2 && upload.Rules.Any(r => r.Type == RaidsContractType.Clear))
                    || upload.Rules.Count(r => r.Type == RaidsContractType.Clear) > 1)
                    return;

                // update the host's map metadata
                if (!bank.MapStats.TryGetValue(request.MapFilename, out var stats))
                    bank.MapStats[request.MapFilename] = stats = new RaidsMapStats();

                stats.Name = request.MapName;
                stats.ContractRules = upload.Rules;
                stats.MissionType = (RaidsMissionType)request.MissionType;

                // save
                Player.SavePlayerMetadata(host);
            }
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

        private static void ClaimContractReceipt(RaidsBank bank, RaidsContractReceipt receipt)
        {
            // apply account and weapon rewards once per receipt
            var payout = receipt.Contract.Payout;
            bank.Account.Bolts = RaidsContractReward.Saturate((ulong)bank.Account.Bolts + payout.Bolts);
            bank.Account.AddXp(payout.PlayerXp);
            bank.Account.AddWeaponExperience(payout.WeaponXp);

            // class xp belongs to the class equipped at completion
            if (payout.ClassXp > 0 && RAIDS_CLASSES.Any(c => (int)c.ClassType == receipt.ClassType))
            {
                var type = (RaidsClassType)receipt.ClassType;
                if (!bank.Account.ClassStats.TryGetValue(type, out var stats))
                    bank.Account.ClassStats[type] = stats = new RaidsClassStats();

                var definition = RAIDS_CLASSES.First(c => c.ClassType == type);
                stats.Experience = Math.Min(stats.Experience + payout.ClassXp, definition.GetClassXpFromLevel(MAX_CLASS_LEVEL));
            }

            receipt.Claimed = true;
            bank.ContractStore.RecordClaim(receipt);
            ++bank.ContractsCompleted;
        }

        private static void NotifyPartyContractClaim(ClientObject client, BasePluginMessage request)
        {
            var store = GetBank(client).ContractStore;
            if (store.PartyClaimUid == 0)
                store.PartyClaimUid = store.AllocateUid();
            Player.SavePlayerMetadata(client);

            // result 2 asks the member to save its account and collect this batch
            ContractReply(client, request, writer =>
            {
                writer.Write(0u);
                writer.Write(store.PartyClaimUid);
                writer.Write(2u);
                writer.Write(new byte[152]);
            });
        }

        private static int[] ContractLevels(ClientObject client) =>
            GetParty(client)
                .Select(c => RaidsAccount.GetAccountLevelFromXp(GetBank(c).Account.Experience) + 1)
                .ToArray();

        private static void ContractReply(ClientObject client, BasePluginMessage request, Action<BinaryWriter> write)
        {
            // serialize within the lobby message limit
            using var stream = new MemoryStream(504);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                write(writer);

            if (stream.Length > 504)
                throw new InvalidDataException("Contract payload exceeded 504 bytes.");

            // send to client
            client.Queue(new RaidsContractResponse { ResponseId = request.CustomMsgId, Payload = stream.ToArray() });
        }

        private void SendContractPage(ClientObject client, RaidsGetContractsRequest request)
        {
            var owner = client.CurrentGame?.Host;
            var bank = GetBank(client);
            var ownerBank = owner == null ? null : GetBank(owner);
            if (bank == null || ownerBank == null || request.Difficulty >= 5) return;

            // pending rewards belong to the client; map boards belong to the host
            var pending = request.Flags.HasFlag(RaidsContractRequestFlags.Pending);
            var now = DateTime.UtcNow;
            var receipts = bank.ContractStore.Receipts.Values
                .Where(r => !r.Claimed)
                .OrderBy(r => r.Contract.OwnerAccountId)
                .ThenBy(r => r.Contract.Uid)
                .ToList();
            var board = pending ? null : ownerBank.ContractStore.Boards.GetValueOrDefault(request.MapFilename);
            var contracts = pending
                ? receipts.Skip(request.PendingOffset).Take(3).Select(r => r.Contract).ToList()
                : board?.Contracts ?? new List<RaidsContract>();

            // send to client
            ContractReply(client, request, writer =>
            {
                // page header
                writer.Write(board?.BoardId ?? 0);
                writer.Write((uint)(owner.AccountId));
                writer.Write(board?.Sequence ?? 0);
                writer.Write(request.RequestId);
                writer.Write((ushort)Math.Min(ushort.MaxValue, receipts.Count));
                writer.Write(request.Difficulty);
                writer.Write((byte)contracts.Count);
                writer.Write(pending ? "" : request.MapFilename, 64);
                writer.Write(pending ? "Unclaimed rewards" : board?.MapName ?? request.MapFilename, 32);

                // contract rewards and claim status for this client
                foreach (var contract in contracts)
                    contract.Write(writer, request.Difficulty, now,
                        bank.ContractStore.FindReceipt(contract.OwnerAccountId, contract.Uid),
                        bank.ContractStore.WasClaimed(contract.OwnerAccountId, contract.Uid),
                        pending ? $"{contract.MapName}: {contract.Name}" : null);

                // pad unused contract slots
                writer.Write(new byte[(3 - contracts.Count) * 92]);
            });
        }

        private void SendContractProgress(ClientObject client, RaidsUpdateContractStatsRequest request, RaidsContractBoard board)
        {
            var bank = GetBank(client);

            // send to client
            ContractReply(client, request, writer =>
            {
                // board header
                writer.Write(board.BoardId);
                writer.Write((uint)client.CurrentGame.Host.AccountId);
                writer.Write(board.Sequence);
                writer.Write((uint)board.Contracts.Count);

                // progress is shared, but claim status belongs to each client
                foreach (var contract in board.Contracts)
                {
                    var receipt = bank.ContractStore.FindReceipt(contract.OwnerAccountId, contract.Uid);
                    writer.Write(contract.Uid);

                    uint progress = 0;
                    if (contract.Type == RaidsContractType.Kill)
                        progress = contract.Kills;
                    else if (contract.Type == RaidsContractType.Clear)
                        progress = contract.CompletedTimeMs;

                    writer.Write(progress);

                    var flags = contract.Flags(DateTime.UtcNow);
                    if (receipt != null)
                        flags |= RaidsContractFlags.Claimable;
                    else if (bank.ContractStore.WasClaimed(contract.OwnerAccountId, contract.Uid))
                        flags |= RaidsContractFlags.Claimed;

                    writer.Write((byte)flags);
                    writer.Write((sbyte)contract.CompletedDifficulty);
                    writer.Write((ushort)0); // natural alignment
                }

                // pad unused progress slots
                writer.Write(new byte[(3 - board.Contracts.Count) * 12]);
            });
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
        ViperMaxRarity = 36,
        MagmaCannonMaxRarity = 37,
        ArbiterMaxRarity = 38,
        FusionMaxRarity = 39,
        MineLauncherMaxRarity = 40,
        B6MaxRarity = 41,
        FlailMaxRarity = 42,
        HoloshieldMaxRarity = 43,
        AlphaImpact = 44,
        BloodFrenzy = 45,
        Ironclad = 46,
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

    public enum RaidsContractType
    {
        Kill = 0,
        Clear = 1
    }

    [Flags]
    public enum RaidsContractFlags : byte
    {
        None = 0,
        Complete = 1 << 0,
        Expired = 1 << 1,
        Claimable = 1 << 2,
        Claimed = 1 << 3,
        SpeedBonusEarned = 1 << 4
    }

    public class RaidsBank
    {
        public const int RAIDS_BANK_VERSION = 5;

        public int Version { get; set; } = 0;
        public RaidsInventory Inventory { get; set; } = new RaidsInventory();
        public RaidsAccount Account { get; set; } = new RaidsAccount();
        public RaidsItemStore ItemStore { get; set; } = new RaidsItemStore();
        public RaidsContractStore ContractStore { get; set; } = new RaidsContractStore();
        public Dictionary<string, RaidsMapStats> MapStats { get; set; } = new Dictionary<string, RaidsMapStats>();
        public DateTime? TimeLastMobDeathLootDrop { get; set; } = null;
        public int ContractsCompleted { get; set; } = 0;
        public bool Initialized { get; set; } = false;

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
            if (Initialized && Version < RAIDS_BANK_VERSION)
            {
                Migrate();
                Account.ClampLevels();
                return true;
            }

            Account.ClampLevels();
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
                    case 5: // retain for accounts that have not migrated to shared weapon XP
                        {
                            Account.WeaponExperience = Account.WeaponXp?.Sum() ?? 0;
                            break;
                        }
                }
            }
        }
    }

    public class RaidsUserSettings
    {
        public const int CURRENT_VERSION = 1;
        public const int SERIALIZED_SIZE = 4;

        public int Version { get; set; } = CURRENT_VERSION;
        public int MapLod { get; set; } = 2;
        public int MobLod { get; set; } = 2;
        public int AmmoDropPopups { get; set; } = 1;
        public int ContractProgressBars { get; set; } = 1;

        public bool IsValid()
        {
            return Version == CURRENT_VERSION && MapLod >= 0 && MapLod <= 3
                && MobLod >= 0 && MobLod <= 3 && AmmoDropPopups >= 0 && AmmoDropPopups <= 1
                && ContractProgressBars >= 0 && ContractProgressBars <= 1;
        }

        public void Deserialize(BinaryReader reader)
        {
            Version = reader.ReadByte();
            MapLod = reader.ReadByte();
            MobLod = reader.ReadByte();
            var flags = reader.ReadByte();
            if ((flags & ~0x03) != 0) throw new InvalidDataException("Invalid raids settings flags.");
            AmmoDropPopups = flags & 0x01;
            ContractProgressBars = (flags >> 1) & 0x01;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)Version);
            writer.Write((byte)MapLod);
            writer.Write((byte)MobLod);
            writer.Write((byte)(AmmoDropPopups | (ContractProgressBars << 1)));
        }
    }

    public class RaidsAccount
    {
        public const int SKILLS_COUNT = 4;
        public const int LEGACY_WEAPON_COUNT = 8;
        public const int SERIALIZED_SIZE = 152;

        public ulong Experience { get; set; } = 0;

        // saved metadata only; migration 5 still needs these for returning accounts
        public double[] WeaponXp { get; set; } = new double[LEGACY_WEAPON_COUNT];
        public double WeaponExperience { get; set; } = 0;
        public RaidsClassType EquippedClass { get; set; } = RaidsClassType.Gunner;
        public Dictionary<RaidsClassType, RaidsClassStats> ClassStats { get; set; } = new Dictionary<RaidsClassType, RaidsClassStats>();
        public uint Bolts { get; set; } = 0;
        public RaidsUserSettings Settings { get; set; } = new RaidsUserSettings();
        public Dictionary<Gadgets, bool> HasWeapon { get; set; } = new Dictionary<Gadgets, bool>();

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

            ulong value = (ulong)level;
            return 3 * (10 * value * value * value + 250 * value);
        }

        public static int GetProficiencyFromXp(double xp)
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

        public static double GetXpFromProficiency(int proficiency)
        {
            if (proficiency <= 0) return 0;
            if (proficiency > RaidsCustomMode.MAX_WEAPON_LEVEL) proficiency = RaidsCustomMode.MAX_WEAPON_LEVEL;
            return 10 * Math.Pow(proficiency, 3) + 100 * proficiency + 250;
            //return GetXpFromLevelQuadratic(proficiency, 250, 500);
        }

        public double GetWeaponExperience() => WeaponExperience;
        public int GetWeaponProficiency() => GetProficiencyFromXp(WeaponExperience);

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

        public void AddWeaponExperience(double xp)
        {
            if (!double.IsFinite(xp) || xp <= 0 || !double.IsFinite(WeaponExperience + xp)) return;
            WeaponExperience += xp;
        }

        public void Update(RaidsAccount account)
        {
            this.Experience = account.Experience;
            this.Bolts = account.Bolts;
            this.WeaponExperience = account.WeaponExperience;
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
            writer.Write(Experience);
            writer.Write(Bolts);
            writer.Write((int)EquippedClass);
            for (int i = 0; i < RaidsCustomMode.MAX_CLASSES; ++i)
                writer.Write(ClassStats.GetValueOrDefault((RaidsClassType)i)?.Experience ?? 0);
            writer.Write(WeaponExperience);
        }

        public void Deserialize(BinaryReader reader)
        {
            Experience = reader.ReadUInt64();
            Bolts = reader.ReadUInt32();
            EquippedClass = (RaidsClassType)reader.ReadUInt32();
            for (int i = 0; i < RaidsCustomMode.MAX_CLASSES; ++i)
                ClassStats[(RaidsClassType)i] = new RaidsClassStats { Experience = reader.ReadUInt64() };
            WeaponExperience = reader.ReadDouble();
        }
    }

    public class RaidsInventory
    {
        public const int ITEMS_COUNT = 64;
        public const int EQUIPPED_SIZE = 8;

        [Obsolete]
        public RaidsInventoryItem[] Items { get; set; } = new RaidsInventoryItem[ITEMS_COUNT];

        [Obsolete]
        public Queue<RaidsInventoryItem> ItemsBacklog { get; set; } = new Queue<RaidsInventoryItem>();

        public List<RaidsInventoryItem> AllItems { get; set; } = new List<RaidsInventoryItem>();
        public uint[] EquippedWeaponUids { get; set; } = new uint[EQUIPPED_SIZE];
        public uint EquippedBadgeUid { get; set; } = 0;

        public RaidsInventoryItem Get(uint uid)
        {
            if (uid == 0) return null;
            return AllItems.FirstOrDefault(x => x != null && x.IsValid() && x.Uid == uid);
        }

        public IEnumerable<RaidsInventoryItem> GetByFilter(int filter)
        {
            return AllItems.Where(x => x != null && x.MatchFilter(filter));
        }

        public IEnumerable<RaidsInventoryItem> GetSortedByFilter(RaidsGetBankInventoryRequest request)
        {
            // sort a view without changing equipped UIDs or the stored inventory order
            var items = GetByFilter(request.Filter);
            IOrderedEnumerable<RaidsInventoryItem> sorted;
            switch (request.Sort)
            {
                case RaidsGetBankInventoryRequest.InventorySort.Proficiency:
                    sorted = items.OrderByDescending(x => x.WeaponData?.Proficiency ?? 0)
                        .ThenByDescending(x => RaidsInventoryItem.GetRarityFromQuality(x.Quality))
                        .ThenByDescending(x => x.Notify == 2);
                    break;
                case RaidsGetBankInventoryRequest.InventorySort.Favorites:
                    sorted = items.OrderByDescending(x => x.Notify == 2)
                        .ThenByDescending(x => RaidsInventoryItem.GetRarityFromQuality(x.Quality))
                        .ThenByDescending(x => x.WeaponData?.Proficiency ?? 0);
                    break;
                default:
                    sorted = items.OrderByDescending(x => RaidsInventoryItem.GetRarityFromQuality(x.Quality))
                        .ThenByDescending(x => x.WeaponData?.Proficiency ?? 0)
                        .ThenByDescending(x => x.Notify == 2);
                    break;
            }

            return sorted.ThenBy(x => x.Uid ?? 0);
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
            else if (EquippedWeaponUids.Length != EQUIPPED_SIZE)
            {
                var equippedWeaponUids = EquippedWeaponUids;
                Array.Resize(ref equippedWeaponUids, EQUIPPED_SIZE);
                EquippedWeaponUids = equippedWeaponUids;
            }

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
            { Gadgets.Holoshields, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech } },
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
            public int Damage { get; set; }
            public Gadgets GadgetId { get; set; }
            public RaidsWeaponPaints Paint { get; set; }
            public RaidsPaintSpecialFlags PaintSpecialMask { get; set; }
            public byte Proficiency { get; set; } // gadget lvl weapon created at (v1-v99)
            public byte CritChance { get; set; } // 0-255 (0-100%)
            public WeaponMods Mod { get; set; }
            public byte ModQuality { get; set; }
            public byte[] AlphaModCounts { get; set; } = new byte[8];
            public int Upgrades { get; set; } = 0;
            public int MaxUpgrades { get; set; } = 10;
        }

        public class ItemBadgeData
        {
            public List<RaidsBadgeType> Effects { get; set; } = new List<RaidsBadgeType>();
            public List<float> EffectStrength { get; set; } = new List<float>();
        }

        public uint? Uid { get; set; }
        public RaidsItemType Type { get; set; }
        public uint Price { get; set; }
        public byte Notify { get; set; }
        public byte Quality { get; set; } // rarity index (0-255)
        public ItemWeaponData WeaponData { get; set; } = null;
        public ItemBadgeData BadgeData { get; set; } = null;

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
            int accountProf = account?.GetWeaponProficiency() ?? 98;
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
                        else if (WeaponData.AlphaModCounts.Length != 8)
                        {
                            var alphaModCounts = WeaponData.AlphaModCounts;
                            Array.Resize(ref alphaModCounts, 8);
                            WeaponData.AlphaModCounts = alphaModCounts;
                        }

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

        public List<RaidsInventoryItem> Items { get; set; } = new List<RaidsInventoryItem>();
        public DateTime? NextRefresh { get; set; } = null;

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

    public class RaidsContractReward
    {
        public uint Bolts { get; set; }
        public uint PlayerXp { get; set; }
        public uint WeaponXp { get; set; }
        public uint ClassXp { get; set; }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Bolts);
            writer.Write(PlayerXp);
            writer.Write(WeaponXp);
            writer.Write(ClassXp);
        }

        public static RaidsContractReward Read(BinaryReader reader) => new()
        {
            Bolts = reader.ReadUInt32(),
            PlayerXp = reader.ReadUInt32(),
            WeaponXp = reader.ReadUInt32(),
            ClassXp = reader.ReadUInt32()
        };

        public RaidsContractReward Add(RaidsContractReward other) => new()
        {
            Bolts = Saturate((ulong)Bolts + other.Bolts),
            PlayerXp = Saturate((ulong)PlayerXp + other.PlayerXp),
            WeaponXp = Saturate((ulong)WeaponXp + other.WeaponXp),
            ClassXp = Saturate((ulong)ClassXp + other.ClassXp)
        };

        public static uint Saturate(double value) => (uint)Math.Clamp(value, 0, uint.MaxValue);
    }

    public class RaidsContractDifficulty
    {
        public uint TargetTimeMs { get; set; }
        public RaidsContractReward Reward { get; set; } = new();
        public RaidsContractReward SpeedBonus { get; set; } = new();
        public float BonusRewardFactor { get; set; } = 0.75f;

        // Keep resolved payouts in stored contracts so existing boards and receipts retain their rewards.
        public static RaidsContractDifficulty Read(BinaryReader reader)
        {
            var targetTimeMs = reader.ReadUInt32();
            var total = RaidsContractReward.Read(reader);
            var factor = reader.ReadSingle();
            if (!float.IsFinite(factor) || factor < 0 || factor > 1)
                throw new InvalidDataException("Bonus reward factor must be finite and between 0 and 1.");

            var completionFactor = targetTimeMs > 0 ? 1 - (double)factor : 1;
            uint Completion(uint amount, int step = 1) => (uint)Math.Min(amount,
                Math.Round(amount * completionFactor / step, MidpointRounding.AwayFromZero) * step);
            var completion = new RaidsContractReward
            {
                Bolts = Completion(total.Bolts),
                PlayerXp = Completion(total.PlayerXp),
                WeaponXp = Completion(total.WeaponXp),
                ClassXp = completionFactor == 1 ? total.ClassXp : Completion(total.ClassXp, 5)
            };
            return new()
            {
                TargetTimeMs = targetTimeMs,
                BonusRewardFactor = factor,
                Reward = completion,
                SpeedBonus = new()
                {
                    Bolts = total.Bolts - completion.Bolts,
                    PlayerXp = total.PlayerXp - completion.PlayerXp,
                    WeaponXp = total.WeaponXp - completion.WeaponXp,
                    ClassXp = total.ClassXp - completion.ClassXp
                }
            };
        }
    }

    public class RaidsContractRule
    {
        public RaidsContractType Type { get; set; }
        public ushort MobDefMask { get; set; }
        public ushort MinCount { get; set; }
        public ushort MaxCount { get; set; }
        public uint MinHeroLevel { get; set; } = 1;
        public float XpMult { get; set; }
        public float BoltMult { get; set; }
        public float ClassXpMult { get; set; }
        public string Name { get; set; }
        public RaidsContractDifficulty[] Difficulties { get; set; }

        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return false;

            // names must fit in the 32-byte field with a null terminator
            if (Encoding.UTF8.GetByteCount(Name) > 31 || Name.Any(char.IsControl))
                return false;

            if (Type == RaidsContractType.Kill)
            {
                if (MobDefMask == 0 || MinCount == 0 || MaxCount < MinCount || MinHeroLevel < 1 || MinHeroLevel > 99)
                    return false;

                if (!float.IsFinite(XpMult) || XpMult < 0)
                    return false;
                if (!float.IsFinite(BoltMult) || BoltMult < 0)
                    return false;
                if (!float.IsFinite(ClassXpMult) || ClassXpMult < 0)
                    return false;

                return true;
            }
            else if (Type == RaidsContractType.Clear)
            {
                if (MobDefMask != 0 || Difficulties?.Length != 5)
                    return false;

                foreach (var difficulty in Difficulties)
                {
                    if (difficulty == null || difficulty.Reward == null || difficulty.SpeedBonus == null
                        || !float.IsFinite(difficulty.BonusRewardFactor) || difficulty.BonusRewardFactor < 0 || difficulty.BonusRewardFactor > 1)
                        return false;
                }

                return true;
            }

            return false;
        }

        public void Deserialize(BinaryReader reader)
        {
            Type = (RaidsContractType)reader.ReadUInt16();
            MobDefMask = reader.ReadUInt16();
            Name = Encoding.UTF8.GetString(reader.ReadBytes(32)).TrimEnd('\0');

            if (Type == RaidsContractType.Kill)
            {
                XpMult = reader.ReadSingle();
                BoltMult = reader.ReadSingle();
                ClassXpMult = reader.ReadSingle();
                MinCount = reader.ReadUInt16();
                MaxCount = reader.ReadUInt16();
                MinHeroLevel = reader.ReadUInt32();
            }
            else if (Type == RaidsContractType.Clear)
            {
                Difficulties = new RaidsContractDifficulty[5];
                for (int i = 0; i < 5; ++i)
                    Difficulties[i] = RaidsContractDifficulty.Read(reader);
            }
            else
            {
                throw new InvalidDataException("Unknown contract rule type.");
            }
        }
    }

    public class RaidsContract
    {
        public uint Uid { get; set; }
        public uint OwnerAccountId { get; set; }
        public uint RequiredKills { get; set; }
        public uint Kills { get; set; }
        public uint CompletedTimeMs { get; set; }
        public RaidsContractType Type { get; set; }
        public ushort MobDefMask { get; set; }
        public string Name { get; set; }
        public string MapFilename { get; set; }
        public string MapName { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public int CompletedDifficulty { get; set; } = -1;
        public RaidsContractReward Reward { get; set; } = new();
        public RaidsContractDifficulty[] Difficulties { get; set; }

        public bool Completed => (Type == RaidsContractType.Kill && RequiredKills > 0 && Kills >= RequiredKills)
            || (Type == RaidsContractType.Clear && CompletedTimeMs > 0);

        public bool HasSpeedBonus => Type == RaidsContractType.Clear
            && CompletedDifficulty >= 0
            && Difficulties[CompletedDifficulty].TargetTimeMs > 0
            && Difficulties[CompletedDifficulty].BonusRewardFactor > 0
            && CompletedTimeMs <= Difficulties[CompletedDifficulty].TargetTimeMs;

        public RaidsContractReward Payout
        {
            get
            {
                if (Type == RaidsContractType.Kill)
                    return Reward;

                if (Type == RaidsContractType.Clear && CompletedDifficulty >= 0)
                {
                    var difficulty = Difficulties[CompletedDifficulty];
                    return HasSpeedBonus || difficulty.TargetTimeMs == 0 || difficulty.BonusRewardFactor == 0
                        ? difficulty.Reward.Add(difficulty.SpeedBonus) : difficulty.Reward;
                }

                return new RaidsContractReward();
            }
        }

        public bool IsExpired(DateTime now)
        {
            return !Completed && now >= ExpiresUtc;
        }

        public RaidsContractFlags Flags(DateTime now)
        {
            var flags = RaidsContractFlags.None;
            if (Completed)
                flags |= RaidsContractFlags.Complete;
            if (IsExpired(now))
                flags |= RaidsContractFlags.Expired;
            if (HasSpeedBonus)
                flags |= RaidsContractFlags.SpeedBonusEarned;

            return flags;
        }

        public void Write(BinaryWriter writer, int previewDifficulty, DateTime now, RaidsContractReceipt receipt = null, bool claimed = false, string label = null)
        {
            var difficulty = CompletedDifficulty >= 0 ? CompletedDifficulty : previewDifficulty;
            var flags = Flags(now);
            if (receipt != null)
                flags |= receipt.Claimed ? RaidsContractFlags.Claimed : RaidsContractFlags.Claimable;
            else if (claimed)
                flags |= RaidsContractFlags.Claimed;

            // identity and rewards
            writer.Write(Uid);
            writer.Write(OwnerAccountId);

            var reward = new RaidsContractReward();
            if (Type == RaidsContractType.Kill)
                reward = Reward;
            else if (Type == RaidsContractType.Clear)
                reward = Completed ? Payout : Difficulties[difficulty].Reward;

            reward.Write(writer);

            // kill and clear contracts share the same payload space
            if (Type == RaidsContractType.Kill)
            {
                writer.Write(RequiredKills);
                writer.Write(Kills);
                writer.Write(new byte[20]); // unused union space
            }
            else if (Type == RaidsContractType.Clear)
            {
                writer.Write(CompletedTimeMs);
                writer.Write(Difficulties[difficulty].TargetTimeMs);
                (!Completed ? Difficulties[difficulty].SpeedBonus : new RaidsContractReward()).Write(writer);
                writer.Write(Difficulties[difficulty].TargetTimeMs > 0 ? Difficulties[difficulty].BonusRewardFactor : 0);
            }
            else
            {
                // reserve the payload space for types without a serializer
                writer.Write(new byte[28]);
            }

            // status and display name
            writer.Write(MobDefMask);
            writer.Write((ushort)Math.Clamp(Math.Ceiling((ExpiresUtc - now).TotalSeconds), 0, 21600));
            writer.Write((byte)Type);
            writer.Write((byte)flags);
            writer.Write((sbyte)CompletedDifficulty);
            writer.Write((sbyte)(receipt?.ClassType ?? -1));
            writer.Write(label ?? Name, 32);
        }
    }

    public class RaidsContractReceipt
    {
        public RaidsContract Contract { get; set; }
        public int ClassType { get; set; } = -1;
        public bool Claimed { get; set; }
        public bool PartyClaimPending { get; set; }

        public string Key => $"{Contract.OwnerAccountId}:{Contract.Uid}";
    }

    public class RaidsContractBoard
    {
        public uint BoardId { get; set; }
        public uint Sequence { get; set; }
        public string MapFilename { get; set; }
        public string MapName { get; set; }
        public List<RaidsContract> Contracts { get; set; } = new();
    }

    public class RaidsContractStore
    {
        public const int MAX_CONTRACTS = 3;

        public Dictionary<string, RaidsContractBoard> Boards { get; set; } = new();
        public Dictionary<string, RaidsContractReceipt> Receipts { get; set; } = new();
        public List<string> ClaimedReceipts { get; set; } = new();
        public uint NextUid { get; set; }
        public uint PartyClaimUid { get; set; }

        public static uint TargetKills(RaidsContractRule rule, IEnumerable<int> heroLevels, Random random = null)
        {
            var strength = Math.Max(1, heroLevels.Sum(level => 1 + 3 * Math.Clamp((level - 10) / 89.0, 0, 1)));
            // round to 11 evenly spaced targets, biasing larger and stronger parties toward higher targets
            var progress = Math.Pow((random ?? Random.Shared).NextDouble(), 1 / strength);
            var step = Math.Round(progress * 10, MidpointRounding.AwayFromZero);
            var count = rule.MinCount + (rule.MaxCount - rule.MinCount) * (step / 10.0);
            return (uint)Math.Clamp(Math.Round(count, MidpointRounding.AwayFromZero), rule.MinCount, rule.MaxCount);
        }

        public uint AllocateUid()
        {
            if (NextUid == uint.MaxValue)
                throw new InvalidOperationException("Contract ID space exhausted.");

            return ++NextUid;
        }

        public RaidsContractBoard GetBoard(string filename, string name)
        {
            if (!Boards.TryGetValue(filename, out var board))
                Boards[filename] = board = new() { BoardId = AllocateUid(), MapFilename = filename, MapName = name };

            return board;
        }

        public void Refresh(RaidsContractBoard board, uint owner, IReadOnlyList<RaidsContractRule> rules,
            IEnumerable<int> heroLevels, DateTime now, int hostHeroLevel, RaidsContract previous = null)
        {
            var usedNames = board.Contracts.Where(c => c.Type == RaidsContractType.Kill).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var usedMasks = board.Contracts.Where(c => c.Type == RaidsContractType.Kill).Select(c => c.MobDefMask).ToHashSet();

            // add the map's clear contract if there is room
            var completion = rules.Where(r => r.Type == RaidsContractType.Clear && r.IsValid())
                .OrderBy(r => previous != null && r.Name == previous.Name ? 1 : 0)
                .FirstOrDefault();
            if (completion != null && !board.Contracts.Any(c => c.Type == RaidsContractType.Clear) && board.Contracts.Count < MAX_CONTRACTS)
            {
                board.Contracts.Add(new()
                {
                    Uid = AllocateUid(),
                    OwnerAccountId = owner,
                    Type = RaidsContractType.Clear,
                    Name = completion.Name,
                    MapFilename = board.MapFilename,
                    MapName = board.MapName,
                    Difficulties = completion.Difficulties,
                    ExpiresUtc = now.AddHours(6)
                });
            }

            // use the highest unlocked tier for each contract name
            var killRules = rules.Where(r => r.Type == RaidsContractType.Kill && r.IsValid() && hostHeroLevel >= r.MinHeroLevel)
                .GroupBy(r => r.Name, StringComparer.Ordinal)
                .SelectMany(group => group.Where(r => r.MinHeroLevel == group.Max(candidate => candidate.MinHeroLevel)));

            // prefer a different objective when replacing an offer
            foreach (var rule in killRules
                .OrderBy(r => previous != null && (r.Name == previous.Name || r.MobDefMask == previous.MobDefMask) ? 1 : 0)
                .ThenBy(_ => Random.Shared.Next()))
            {
                if (board.Contracts.Count >= MAX_CONTRACTS)
                    break;
                if (usedNames.Contains(rule.Name) || usedMasks.Contains(rule.MobDefMask))
                    continue;

                var target = TargetKills(rule, heroLevels);
                var heroXp = RaidsContractReward.Saturate(target * (double)rule.XpMult);
                board.Contracts.Add(new()
                {
                    Uid = AllocateUid(),
                    OwnerAccountId = owner,
                    Type = RaidsContractType.Kill,
                    Name = rule.Name,
                    MobDefMask = rule.MobDefMask,
                    MapFilename = board.MapFilename,
                    MapName = board.MapName,
                    RequiredKills = target,
                    ExpiresUtc = now.AddHours(6),
                    Reward = new()
                    {
                        Bolts = RaidsContractReward.Saturate(target * (double)rule.BoltMult),
                        PlayerXp = heroXp,
                        WeaponXp = (uint)((ulong)heroXp * 3 / 10),
                        ClassXp = RaidsContractReward.Saturate(target * (double)rule.ClassXpMult)
                    }
                });
                usedNames.Add(rule.Name);
                usedMasks.Add(rule.MobDefMask);
            }
        }

        public void Replace(RaidsContractBoard board, uint uid, uint owner, IReadOnlyList<RaidsContractRule> rules,
            IEnumerable<int> heroLevels, DateTime now, int hostHeroLevel)
        {
            var index = board.Contracts.FindIndex(c => c.Uid == uid);
            if (index < 0) return;

            var previous = board.Contracts[index];
            board.Contracts.RemoveAt(index);
            var remaining = board.Contracts.Count;
            Refresh(board, owner, rules, heroLevels, now, hostHeroLevel, previous);

            // move the first new offer into the vacated slot without moving other offers
            var replacement = board.Contracts.Count > remaining ? board.Contracts[remaining] : previous;
            if (board.Contracts.Count > remaining)
                board.Contracts.RemoveAt(remaining);
            board.Contracts.Insert(index, replacement);
            ++board.Sequence;
        }

        // retained for developer use; gameplay does not expose rejection
        public void Reroll(RaidsContractBoard board, uint uid) => board.Contracts.RemoveAll(c => c.Uid == uid);

        public void AddReceipt(RaidsContract contract, int classType)
        {
            var receipt = new RaidsContractReceipt { Contract = contract, ClassType = classType };
            if (!ClaimedReceipts.Contains(receipt.Key))
                Receipts.TryAdd(receipt.Key, receipt);
        }

        public RaidsContractReceipt FindReceipt(uint owner, uint uid) => Receipts.GetValueOrDefault($"{owner}:{uid}");

        public bool WasClaimed(uint owner, uint uid) => ClaimedReceipts.Contains($"{owner}:{uid}");

        public void RecordClaim(RaidsContractReceipt receipt)
        {
            Receipts.Remove(receipt.Key);
            if (!ClaimedReceipts.Contains(receipt.Key))
                ClaimedReceipts.Add(receipt.Key);
            if (ClaimedReceipts.Count > 64)
                ClaimedReceipts.RemoveRange(0, ClaimedReceipts.Count - 64);
        }
    }

    public class RaidsMapStats
    {
        public string Name { get; set; }
        public int CollectiblesCount { get; set; }
        public uint CollectiblesMask { get; set; }

        public int ChallengesCount { get; set; }
        public uint ChallengesMask { get; set; }

        public uint[] BestTimeMsPerDifficulty { get; set; } = new uint[5];

        public RaidsMissionType MissionType { get; set; } = RaidsMissionType.Hub;
        public int ContractsCompleted { get; set; } = 0;

        public List<RaidsContractRule> ContractRules { get; set; } = new List<RaidsContractRule>();

        // set of oclasses from kills on the map
        public HashSet<(int oclass, int difficultyStars)> EnabledMobs { get; set; } = new HashSet<(int oclass, int difficultyStars)>();

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

    public class RaidsClassProgression
    {
        public RaidsClassType ClassType { get; set; }
        public int Tier { get; set; }
        public decimal ExperienceCurveMultiplier { get; set; }
        public Gadgets[] Weapons { get; set; }
        public (RaidsClassType ClassType, int RequiredLevel)[] UnlockRules { get; set; }

        public RaidsClassProgression(RaidsClassType classType, decimal xpCurveMult, int tier, Gadgets[] weapons, (RaidsClassType ClassType, int RequiredLevel)[] unlockRules = null)
        {
            ClassType = classType;
            Tier = tier;
            ExperienceCurveMultiplier = xpCurveMult;
            Weapons = weapons;
            UnlockRules = unlockRules ?? [];
        }

        public Gadgets[] GetUnlockedWeapons(RaidsAccount account)
        {
            var skillRanks = account.ClassStats?.GetValueOrDefault(ClassType)?.SkillRanks;
            if (skillRanks == null) return Weapons;

            var weapons = Weapons.ToList();

            // map code validates allocations; use this class's confirmed purchased ranks
            foreach (var skill in skillRanks.Where(x => x.Value > 0))
            {
                Gadgets? weapon = (RaidsClassSkillId)skill.Key switch
                {
                    RaidsClassSkillId.ViperMaxRarity => Gadgets.Vipers,
                    RaidsClassSkillId.MagmaCannonMaxRarity => Gadgets.MagmaCannon,
                    RaidsClassSkillId.ArbiterMaxRarity => Gadgets.Arbiter,
                    RaidsClassSkillId.FusionMaxRarity => Gadgets.Fusion,
                    RaidsClassSkillId.MineLauncherMaxRarity => Gadgets.MineLauncher,
                    RaidsClassSkillId.B6MaxRarity => Gadgets.B6,
                    RaidsClassSkillId.FlailMaxRarity => Gadgets.Flail,
                    RaidsClassSkillId.HoloshieldMaxRarity => Gadgets.Holoshields,
                    _ => null,
                };
                if (weapon.HasValue && !weapons.Contains(weapon.Value))
                    weapons.Add(weapon.Value);
            }
            return weapons.ToArray();
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

        public ulong Experience { get; set; }

        // numeric keys keep persisted allocations tied to stable skill IDs
        public Dictionary<int, int> SkillRanks { get; set; } = [];

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
