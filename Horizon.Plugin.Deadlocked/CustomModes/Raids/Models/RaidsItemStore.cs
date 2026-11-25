using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
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

        public RaidsStorePage GetStorePage(int page, uint requestId, RaidsBank bank)
        {
            var consumableTypes = RaidsConsumables.StoreItemTypes
                .Where(type => type != RaidsMiscItemType.DoubleXp || RaidsConsumables.OwnedConsumables(bank, type) > 0).ToArray();
            int weaponPages = Math.Max(1, (FIXED_ITEMS.Count + RaidsStorePage.MAX_ITEMS - 1) / RaidsStorePage.MAX_ITEMS);
            int consumablePages = Math.Max(1, (consumableTypes.Length + RaidsStorePage.MAX_ITEMS - 1) / RaidsStorePage.MAX_ITEMS);
            int totalPages = weaponPages + consumablePages;
            if (page < 0 || page >= totalPages) return null;

            bool weapons = page >= consumablePages;
            var items = weapons
                ? FIXED_ITEMS.Skip((page - consumablePages) * RaidsStorePage.MAX_ITEMS).Take(RaidsStorePage.MAX_ITEMS)
                    .Select(item => { var copy = item.Copy(); copy.Price = 50_000; return copy; }).ToArray()
                : consumableTypes.Skip(page * RaidsStorePage.MAX_ITEMS).Take(RaidsStorePage.MAX_ITEMS)
                    .Select(type => new RaidsInventoryItem
                    {
                        Type = RaidsItemType.Misc,
                        Price = RaidsConsumables.ConsumablePrice(type),
                        MiscData = new RaidsInventoryItem.ItemMiscData { Type = type, Count = 1 },
                    }).ToArray();
            return new RaidsStorePage
            {
                RequestId = requestId,
                Page = page,
                TotalPages = totalPages,
                Title = weapons ? "Weapons" : "Consumables",
                RotateInSeconds = weapons ? Math.Max(0, (int)((NextRefresh - DateTime.UtcNow)?.TotalSeconds ?? 0)) : 0,
                Items = items,
            };
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

        public IEnumerable<RaidsInventoryItem> GetPaginatedFixedItems(int page, int pageSize, out int totalCount)
        {
            totalCount = FIXED_ITEMS.Count;
            return FIXED_ITEMS.Skip(page * pageSize).Take(pageSize);
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
}
