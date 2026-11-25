using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
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
        }

        public class ItemMiscData
        {
            public RaidsMiscItemType Type { get; set; }
            public uint Count { get; set; }
        }

        public uint? Uid { get; set; }
        public RaidsItemType Type { get; set; }
        public uint Price { get; set; }
        public byte Notify { get; set; }
        public byte Quality { get; set; } // rarity index (0-255)
        public ItemWeaponData WeaponData { get; set; } = null;
        public ItemMiscData MiscData { get; set; } = null;

        public bool IsWeapon() => Type == RaidsItemType.Weapon;
        public bool IsMisc() => Type == RaidsItemType.Misc;
        public bool IsSameItem(RaidsInventoryItem other)
        {
            if (other == null)
                return false;

            if (this.IsMisc() && other.IsMisc())
                return this.MiscData != null && other.MiscData != null && this.MiscData.Type == other.MiscData.Type;

            if (this.IsWeapon() && other.IsWeapon())
                return this.WeaponData.GadgetId == other.WeaponData.GadgetId;

            return false;
        }

        public bool IsValid()
        {
            if (IsMisc()) return MiscData != null && MiscData.Type != RaidsMiscItemType.None
                && (MiscData.Count > 0 || RaidsConsumables.IsConsumable(this));
            if (IsWeapon()) return WeaponData != null && IsGadgetValid(WeaponData.GadgetId) && WeaponData.Damage > 0;

            return false;
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

        public static RaidsInventoryItem GenerateWeapon(RaidsGenerateLootDropRequest request, RaidsAccount account, bool fortunesFavor = false)
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
            int accountProf = account?.GetWeaponProficiency() ?? RaidsCustomMode.MAX_WEAPON_LEVEL;
            int minProficiency = account == null ? 0 : Math.Max(0, accountProf - 5);
            int maxProficiency = account == null ? RaidsCustomMode.MAX_WEAPON_LEVEL : Math.Min(RaidsCustomMode.MAX_WEAPON_LEVEL, accountProf + 5);
            int difficultyMinProficiency = _gadgetMinProficiencyForDifficulty[request.DifficultyStars];
            int difficultyMaxProficiency = Math.Min(RaidsCustomMode.MAX_WEAPON_LEVEL, _gadgetMaxProficiencyForDifficulty[request.DifficultyStars] - 1);
            var difficultyMaxQuality = _gadgetMaxQualityForDifficulty[request.DifficultyStars] - 0.00001;
            var proficiencyCurve = 2;

            // determine quality
            var quality = _rng.NextDouble() * _rng.NextDouble() * difficultyMaxQuality;
            if (fortunesFavor && (request.Type == RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath
                || request.Type == RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent))
                quality = Math.Max(quality, _rng.NextDouble() * _rng.NextDouble() * difficultyMaxQuality);

            switch (request.Type)
            {
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath:
                    {
                        int proficiencyCenter = Math.Clamp(accountProf, difficultyMinProficiency, difficultyMaxProficiency);
                        minProficiency = Math.Max(difficultyMinProficiency, proficiencyCenter - 5);
                        maxProficiency = Math.Min(difficultyMaxProficiency, proficiencyCenter + 5);
                        proficiencyCurve = 1;
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent:
                    {
                        var minQuality = _gadgetMissionCompleteMinQualityForDifficulty[request.DifficultyStars];
                        quality = Math.Pow(((1 - minQuality) * quality + minQuality), 1 - minQuality) + 0.01;

                        int proficiencyCenter = Math.Clamp(accountProf, difficultyMinProficiency, difficultyMaxProficiency);
                        minProficiency = Math.Max(difficultyMinProficiency, proficiencyCenter - 5);
                        maxProficiency = Math.Min(difficultyMaxProficiency, proficiencyCenter + 5);
                        proficiencyCurve = 1;
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Store:
                    {
                        // reduce quality to max legendary
                        quality = _rng.NextDouble() * (200.0 / 256.0);
                        minProficiency = (int)(quality * RaidsCustomMode.MAX_WEAPON_LEVEL);
                        maxProficiency = RaidsCustomMode.MAX_WEAPON_LEVEL;
                        break;
                    }
            }

            drop.Quality = (byte)Math.Clamp(quality * 256, 0, 255);
            // Include both endpoints for every drop type. Stored proficiency 98 displays as V99.
            maxProficiency = Math.Min(maxProficiency, RaidsCustomMode.MAX_WEAPON_LEVEL);
            int proficiencyRange = (maxProficiency - minProficiency) + 1;
            double proficiencyRoll = Math.Pow(_rng.NextDouble() * _rng.NextDouble(), proficiencyCurve / 2f);
            drop.WeaponData.Proficiency = (byte)Math.Min(maxProficiency, minProficiency + (int)(proficiencyRoll * proficiencyRange));
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

        public static RaidsInventoryItem Generate(RaidsGenerateLootDropRequest request, RaidsAccount account, bool fortunesFavor = false)
        {
            return GenerateWeapon(request, account, fortunesFavor);
        }

        public static uint ComputeBuyPrice(RaidsInventoryItem item)
        {
            if (item.IsMisc()) return item.Price;

            int rarity = (int)GetRarityFromQuality(item.Quality);
            float quality = item.Quality / 255.0f;
            int roundTo = 100000;

            if (item.IsWeapon())
            {
                double bolts = (item.WeaponData.Proficiency * 100000) + Math.Pow(500000, 1 + Math.Pow((rarity / 6.0), 2)) + (10000000 * quality);
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }

            throw new NotImplementedException();
        }

        public static uint ComputeSellPrice(RaidsInventoryItem item)
        {
            if (item.IsMisc()) return item.Price;

            int rarity = (int)GetRarityFromQuality(item.Quality);
            float quality = item.Quality / 255.0f;
            int roundTo = 100;

            if (item.IsWeapon())
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
            if (filter == 0) return IsMisc() && IsValid();

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
                        writer.Write((ushort)Math.Clamp(WeaponData.Upgrades, 0, ushort.MaxValue));
                        break;
                    }
                case RaidsItemType.Misc:
                    {
                        var data = MiscData ?? new ItemMiscData();
                        writer.Write((uint)data.Type);
                        writer.Write(data.Count);
                        writer.Write(new byte[12]); // union payload is 20 bytes
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
                        WeaponData.Upgrades = reader.ReadUInt16();

                        WeaponData.Paint = (RaidsWeaponPaints)(paintCombined & 0xf);
                        WeaponData.PaintSpecialMask = (RaidsPaintSpecialFlags)(paintCombined >> 4);
                        break;
                    }
                case RaidsItemType.Misc:
                    {
                        MiscData = new ItemMiscData
                        {
                            Type = (RaidsMiscItemType)reader.ReadUInt32(),
                            Count = reader.ReadUInt32(),
                        };
                        reader.ReadBytes(12);
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
}
