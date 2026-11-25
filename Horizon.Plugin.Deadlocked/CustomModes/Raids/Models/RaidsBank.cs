using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public partial class RaidsBank
    {
        public const int RAIDS_BANK_VERSION = 9;

        public int Version { get; set; } = 0;
        public RaidsInventory Inventory { get; set; } = new RaidsInventory();
        public RaidsAccount Account { get; set; } = new RaidsAccount();
        public RaidsPlayerStats PlayerStats { get; set; } = new();
        [JsonIgnore] public HashSet<(string SessionKey, uint RequestId)> InventoryUpdates { get; } = new();
        public RaidsItemStore ItemStore { get; set; } = new RaidsItemStore();
        public RaidsContractStore ContractStore { get; set; } = new RaidsContractStore();
        public Dictionary<string, RaidsMapStats> MapStats { get; set; } = new Dictionary<string, RaidsMapStats>();
        public DateTime? TimeLastMobDeathLootDrop { get; set; } = null;
        public bool Initialized { get; set; } = false;

        [JsonIgnore] public HashSet<(DateTime GameCreatedUtc, int MissionStartTime, string MapFilename)> CompletedMissions { get; } = new();

        [JsonIgnore] public Dictionary<uint, byte[]> TitleReplies { get; } = new();
        public RaidsConsumables Consumables { get; set; } = new();
        [JsonIgnore] public Dictionary<uint, byte[]> ConsumableReplies { get; } = new();

        public bool Initialize()
        {
            Consumables ??= new RaidsConsumables();
            PlayerStats ??= new RaidsPlayerStats();

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
            for (var i = 0; i < 8; ++i)
                Inventory.EquippedWeaponUids[i] = Inventory.AllItems[i].Uid.Value;

            MapStats = new Dictionary<string, RaidsMapStats>();

            Account = new RaidsAccount();
            PlayerStats = new RaidsPlayerStats();
            InventoryUpdates.Clear();
            Consumables = new RaidsConsumables();
            CompletedMissions.Clear();
            TitleReplies.Clear();
            ConsumableReplies.Clear();
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
            if (RaidsConsumables.IsConsumable(item))
            {
                var stack = Inventory.AllItems.FirstOrDefault(x => RaidsConsumables.IsConsumable(x)
                    && x.MiscData.Type == item.MiscData.Type);
                if (stack != null)
                {
                    if (stack == item) return;
                    stack.MiscData.Count = checked(stack.MiscData.Count + item.MiscData.Count);
                    item.Uid = stack.Uid;
                    if (stack.Notify != 2 && item.Notify != 0) stack.Notify = item.Notify;
                    return;
                }
            }

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
            if (sell && (RaidsConsumables.IsConsumable(item) || Inventory.EquippedWeaponUids.Contains(item.Uid.Value))) return;

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
                    case 5: // retain for accounts that have not migrated to shared weapon XP
                        {
                            Account.WeaponExperience = Account.WeaponXp?.Sum() ?? 0;
                            break;
                        }
                    case 6: // seed global dreadpoints from saved class xp
                        {
                            Account.Dreadpoints = 0;
                            foreach (var stats in Account.ClassStats.Values)
                                Account.Dreadpoints += Math.Min(stats.Experience, ulong.MaxValue - Account.Dreadpoints);
                            break;
                        }
                    case 7: // regenerate contracts with rule indices and generic count fields
                        {
                            // discard old offers and receipts without reusing their ids
                            ContractStore = new RaidsContractStore { NextUid = ContractStore.NextUid };
                            break;
                        }
                }
            }
        }
    }
}
