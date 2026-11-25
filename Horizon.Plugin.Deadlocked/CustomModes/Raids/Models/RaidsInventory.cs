using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
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
                (item != null && item.IsWeapon() ? item : RaidsInventoryItem.Empty).Serialize(writer);
            }

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
        //    for (int i = 0; i < EQUIPPED_SIZE; ++i)
        //        EquippedWeaponUids[i] = reader.ReadUInt32();
        //}
    }
}
