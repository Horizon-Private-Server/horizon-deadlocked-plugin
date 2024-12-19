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
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.CustomModes
{
    public class RaidsCustomMode : BaseCustomMode
    {
        public static readonly int MAX_ACCOUNT_LEVEL = 99;
        public static readonly int MAX_WEAPON_LEVEL = 99;

        public override CustomModeId Id => CustomModeId.CMODE_ID_RAIDS;
        public override string Name => "DreadZone Raids";

        private int GetRatingFromXp(long xp)
        {
            //return (int)Math.Max(100, Math.Min(10000, 100 + Math.Sqrt(xp * 4)));
            return (int)Math.Max(100, Math.Min(10000, 100 + (xp / 5000f)));
        }

        public override async Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            // invalid
            if (metadata == null || !metadata.CustomMapConfig.HasMap())
                return 0;

            return GetRatingFromXp(0);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            args.Reject = true; // reject all
            return Task.CompletedTask;
        }

        public void OnClientRequestBankInventory(RaidsGetBankInventoryRequest request, ClientObject client)
        {
            var update = false;
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            metadata.RaidsBank.Initialize();

            // move weapons backlog into main bank if free slots
            if (metadata.RaidsBank.Inventory.ItemsBacklog.Any())
            {
                for (int i = 0; i < metadata.RaidsBank.Inventory.Items.Length; ++i)
                {
                    var weapon = metadata.RaidsBank.Inventory.Items[i];
                    if (weapon == null || !weapon.IsValid())
                    {
                        metadata.RaidsBank.Inventory.Items[i] = metadata.RaidsBank.Inventory.ItemsBacklog.Dequeue();
                        update = true;
                        if (!metadata.RaidsBank.Inventory.ItemsBacklog.Any()) break;
                    }
                }
            }

            // sort inventory
            // breaks equipped indices
            //Array.Sort(metadata.RaidsBank.Inventory.Weapons, RaidsInventoryWeapon.Compare);

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    metadata.RaidsBank.Inventory.Serialize(writer);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                }
            }
    
            // save
            client.Metadata = JsonConvert.SerializeObject(metadata);
            if (update)
                _ = Server.Medius.Program.Database.PostAccountMetadata(client.AccountId, client.Metadata);
        }

        public void OnClientRequestBankInventoryUpdate(RaidsUpdateBankInventoryRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // update
            metadata.RaidsBank.Inventory.EquippedWeaponIdxs = request.EquippedWeaponIdxs;
            metadata.RaidsBank.Inventory.EquippedBadgeIdx = request.EquippedBadgeIdx;
            for (int i = 0; i < request.Count; ++i)
                metadata.RaidsBank.Inventory.Items[i + request.Index] = request.Weapons[i];

            // save
            client.Metadata = JsonConvert.SerializeObject(metadata);
            _ = Server.Medius.Program.Database.PostAccountMetadata(client.AccountId, client.Metadata);
        }

        public void OnClientRequestBankAccount(RaidsGetBankAccountRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            metadata.RaidsBank.Initialize();

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
        }

        public void OnClientRequestBankAccountUpdate(RaidsUpdateBankAccountRequest request, ClientObject client)
        {
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // update
            metadata.RaidsBank.Account.Update(request.Account);

            // save
            client.Metadata = JsonConvert.SerializeObject(metadata);
            _ = Server.Medius.Program.Database.PostAccountMetadata(client.AccountId, client.Metadata);
        }

        public void OnClientRequestStoreItems(RaidsGetStoreItemsRequest request, ClientObject client)
        {
            var update = false;
            var metadata = Player.GetPlayerMetadata(client);
            if (metadata == null) return;

            // ensure bank is initialized
            metadata.RaidsBank.Initialize();

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    // refresh items
                    update = metadata.RaidsBank.Store.Refresh(metadata.RaidsBank.Account, false);

                    // items
                    var items = metadata.RaidsBank.Store.GetPaginated(request.Page, request.PageSize, out int itemsCount);
                    foreach (var item in items)
                    {
                        item.Price = RaidsInventoryItem.ComputeBuyPrice(item);
                        item.Serialize(writer);
                    }

                    int rotateInSeconds = (int)((metadata.RaidsBank.Store.NextRefresh - DateTime.UtcNow)?.TotalSeconds ?? 0);

                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTotalItemsAddress, BitConverter.GetBytes(itemsCount)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestRotateInSecondsAddress, BitConverter.GetBytes(rotateInSeconds)));
                }
            }

            // save
            client.Metadata = JsonConvert.SerializeObject(metadata);
            if (update)
                _ = Server.Medius.Program.Database.PostAccountMetadata(client.AccountId, client.Metadata);
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
                metadata.RaidsBank.Account.UnlockWeapon(request.Item.GadgetId);

            // pass to store
            metadata.RaidsBank.Store.OnBuy(request.Page, request.PageSize, request.ItemIdx, request.Item, metadata.RaidsBank.Account);

            // check for refresh items
            metadata.RaidsBank.Store.Refresh(metadata.RaidsBank.Account, false);

            // save
            client.Metadata = JsonConvert.SerializeObject(metadata);
            _ = Server.Medius.Program.Database.PostAccountMetadata(client.AccountId, client.Metadata);
        }

        public void OnClientRequestGenerateLootDrop(RaidsGenerateLootDropRequest request, ClientObject client)
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
            client.Metadata = JsonConvert.SerializeObject(metadata);
            _ = Server.Medius.Program.Database.PostAccountMetadata(client.AccountId, client.Metadata);

            // send back to client
            client.Queue(new RaidsGenerateLootDropResponse() { Position = request.Position, Drop = drop });
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            string info = "";
            return Task.FromResult(info);
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
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
        EXPLOSIVE_WRENCH,
        COUNT
    };

    public class RaidsBank
    {
        public RaidsInventory Inventory = new RaidsInventory();
        public RaidsAccount Account = new RaidsAccount();
        public RaidsStore Store = new RaidsStore();
        public DateTime? TimeLastMobDeathLootDrop = null;
        public bool Initialized = false;

        public void Initialize()
        {
            // make sure vipers & mag are always unlocked
            Account.UnlockWeapon(Gadgets.Vipers);
            Account.UnlockWeapon(Gadgets.MagmaCannon);
            Account.ClampLevels();
            
            if (Initialized) return;

            // give default weapons
            Inventory = new RaidsInventory();
            Inventory.ItemsBacklog = new Queue<RaidsInventoryItem>();
            Inventory.Items = new RaidsInventoryItem[RaidsInventory.ITEMS_COUNT];
            Inventory.EquippedWeaponIdxs = new sbyte[RaidsInventory.EQUIPPED_SIZE];

            Inventory.Items[0] = RaidsInventoryItem.DefaultVipers;
            Inventory.Items[1] = RaidsInventoryItem.DefaultMagmaCannon;
            Inventory.EquippedBadgeIdx = -1;
            Inventory.EquippedWeaponIdxs[(int)GadgetSlots.Vipers - 1] = 0;
            Inventory.EquippedWeaponIdxs[(int)GadgetSlots.MagmaCannon - 1] = 1;

            Account = new RaidsAccount();
            Account.UnlockWeapon(Gadgets.Vipers);
            Account.UnlockWeapon(Gadgets.MagmaCannon);

            // give random stats/weapons
            if (false)
            {
                var rng = new Random();

                for (int i = 2; i < RaidsInventory.ITEMS_COUNT; ++i)
                {
                    // Inventory.Weapons[i] = RaidsInventoryWeapon.Random();
                    Inventory.Items[i] = RaidsInventoryItem.Generate(new RaidsGenerateLootDropRequest()
                    {
                        Type = RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent,
                        MobKilledByGadget = Gadgets.Vipers
                    }, Account);
                }

                //for (int i = 0; i < Account.WeaponXp.Length; ++i)
                //    Account.WeaponXp[i] = (ulong)rng.Next(0, 100000);
            }

            Initialized = true;
        }

        public void Add(RaidsInventoryItem weapon)
        {
            // add to backlog
            // let server move into inventory on next GetBank request (if room)
            Inventory.ItemsBacklog.Enqueue(weapon);
        }

        public void Serialize(BinaryWriter writer)
        {
            Initialize();

            Inventory.Serialize(writer);
            Account.Serialize(writer);
        }

        public void Deserialize(BinaryReader reader)
        {
            Inventory.Deserialize(reader);
            Account.Deserialize(reader);
            reader.ReadInt32();
        }
    }

    public class RaidsAccount
    {
        public const int SKILLS_COUNT = 4;
        public const int PROFICIENCY_COUNT = 8;

        public ulong Experience = 0;
        public ulong[] WeaponXp = new ulong[PROFICIENCY_COUNT];
        public ulong[] WeaponPrestigeCount = new ulong[PROFICIENCY_COUNT];
        public ulong PlayerPrestigeCount = 0;
        public uint Bolts = 0;
        public uint SkillPoints = 0;
        public ushort[] Skills = new ushort[SKILLS_COUNT];
        public Dictionary<Gadgets, bool> HasWeapon = new Dictionary<Gadgets, bool>();


        private static readonly int LEVELUP_MAX_LEVEL = 98;
        private static int GetLevelFromXp(ulong xp)
        {
            if (xp < 0) return 0;

            // (500 (2/3)^(1/3))/(sqrt(3) sqrt(27 x^2 + 500000000) - 9 x)^(1/3) - (sqrt(3) sqrt(27 x^2 + 500000000) - 9 x)^(1/3)/(2^(1/3) 3^(2/3))
            // Constants
            const double c1 = 0.87358046;                 // (2/3)^(1/3)
            const double c2 = 1.25992104;                 // 2^(1/3)
            const double c3 = 2.08008382;                 // 3^(2/3)
            const double sqrt3 = 1.73205080;              // sqrt(3)

            // Calculate the inner term
            double inner = sqrt3 * Math.Sqrt((double)27.0 * xp * xp + 500000000.0) - (double)9.0 * xp;

            // Compute the two terms
            double term1 = (double)500.0 * c1 / Math.Pow(inner, (double)1.0 / (double)3.0);
            double term2 = Math.Pow(inner, (double)1.0 / (double)3.0) / (c2 * c3);

            // Final result
            double level = term1 - term2;

            if (level < 0) return 0;
            if (level > LEVELUP_MAX_LEVEL) return LEVELUP_MAX_LEVEL;
            return (int)level;
        }

        private static ulong GetXpFromLevel(int level)
        {
            if (level > LEVELUP_MAX_LEVEL) level = LEVELUP_MAX_LEVEL;
            if (level <= 0) return 0;
            return (ulong)((double)Math.Pow(1 * level, 3) + (500 * level));
        }

        private static int GetProficiencyFromXp(ulong xp)
        {
            return GetLevelFromXp(xp);
        }

        private static ulong GetXpFromProficiency(int proficiency)
        {
            return GetXpFromLevel(proficiency);
        }

        public ulong GetWeaponXp(Gadgets gadget)
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
            ulong maxAccountXp = GetXpFromLevel(RaidsCustomMode.MAX_ACCOUNT_LEVEL);
            if (Experience > maxAccountXp)
                Experience = maxAccountXp;

            ulong maxWeaponXp = GetXpFromProficiency(RaidsCustomMode.MAX_WEAPON_LEVEL);
            for (int i = 0; i < WeaponXp.Length; ++i)
                if (WeaponXp[i] > maxWeaponXp)
                    WeaponXp[i] = maxWeaponXp;
        }

        public void Update(RaidsAccount account)
        {
            this.Experience = account.Experience;
            this.Bolts = account.Bolts;
            this.WeaponXp = account.WeaponXp;
            this.SkillPoints = account.SkillPoints;
            this.Skills = account.Skills;
            ClampLevels();
        }

        public void Serialize(BinaryWriter writer)
        {
            if (Skills == null) Skills = new ushort[SKILLS_COUNT];
            else if (Skills.Length != SKILLS_COUNT) Array.Resize(ref Skills, SKILLS_COUNT);
            if (WeaponXp == null) WeaponXp = new ulong[PROFICIENCY_COUNT];
            else if (WeaponXp.Length != PROFICIENCY_COUNT) Array.Resize(ref WeaponXp, PROFICIENCY_COUNT);

            writer.Write(Experience);
            for (int i = 0; i < PROFICIENCY_COUNT; ++i)
                writer.Write(WeaponXp[i]);
            writer.Write(Bolts);
            writer.Write(SkillPoints);
            for (int i = 0; i < SKILLS_COUNT; ++i)
                writer.Write(Skills[i]);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (Skills == null) Skills = new ushort[SKILLS_COUNT];
            else if (Skills.Length != SKILLS_COUNT) Array.Resize(ref Skills, SKILLS_COUNT);
            if (WeaponXp == null) WeaponXp = new ulong[PROFICIENCY_COUNT];
            else if (WeaponXp.Length != PROFICIENCY_COUNT) Array.Resize(ref WeaponXp, PROFICIENCY_COUNT);

            Experience = reader.ReadUInt64();
            for (int i = 0; i < PROFICIENCY_COUNT; ++i)
                WeaponXp[i] = reader.ReadUInt64();
            Bolts = reader.ReadUInt32();
            SkillPoints = reader.ReadUInt32();
            for (int i = 0; i < SKILLS_COUNT; ++i)
                Skills[i] = reader.ReadUInt16();
        }
    }

    public class RaidsInventory
    {
        public const int ITEMS_COUNT = 64;
        public const int EQUIPPED_SIZE = 8;

        public RaidsInventoryItem[] Items = new RaidsInventoryItem[ITEMS_COUNT];
        public Queue<RaidsInventoryItem> ItemsBacklog = new Queue<RaidsInventoryItem>();
        public sbyte[] EquippedWeaponIdxs = new sbyte[EQUIPPED_SIZE];
        public sbyte EquippedBadgeIdx = -1;

        public void Serialize(BinaryWriter writer)
        {
            if (Items == null) Items = new RaidsInventoryItem[ITEMS_COUNT];
            else if (Items.Length != ITEMS_COUNT) Array.Resize(ref Items, ITEMS_COUNT);
            if (EquippedWeaponIdxs == null) EquippedWeaponIdxs = new sbyte[EQUIPPED_SIZE];
            else if (EquippedWeaponIdxs.Length != EQUIPPED_SIZE) Array.Resize(ref EquippedWeaponIdxs, EQUIPPED_SIZE);

            int weaponsCount = ItemsBacklog.Count;
            for (int i = 0; i < ITEMS_COUNT; ++i)
            {
                if (Items[i] != null)
                {
                    Items[i].Price = RaidsInventoryItem.ComputeSellPrice(Items[i]);
                    weaponsCount++;
                }

                (Items[i] ?? RaidsInventoryItem.Empty).Serialize(writer);
            }

            writer.Write(weaponsCount);
            writer.Write(1); // tells the client the server just sent a payload
            writer.Write(EquippedBadgeIdx);
            for (int i = 0; i < EQUIPPED_SIZE; ++i)
                writer.Write(EquippedWeaponIdxs[i]);
            writer.Write(new byte[3]); // padding
        }

        public void Deserialize(BinaryReader reader)
        {
            if (Items == null) Items = new RaidsInventoryItem[ITEMS_COUNT];
            else if (Items.Length != ITEMS_COUNT) Array.Resize(ref Items, ITEMS_COUNT);
            if (EquippedWeaponIdxs == null) EquippedWeaponIdxs = new sbyte[EQUIPPED_SIZE];
            else if (EquippedWeaponIdxs.Length != EQUIPPED_SIZE) Array.Resize(ref EquippedWeaponIdxs, EQUIPPED_SIZE);

            for (int i = 0; i < ITEMS_COUNT; ++i)
                Items[i].Deserialize(reader);
            reader.ReadInt32(); // total weapons count
            reader.ReadInt32(); // refresh flag
            EquippedBadgeIdx = reader.ReadSByte();
            for (int i = 0; i < EQUIPPED_SIZE; ++i)
                EquippedWeaponIdxs[i] = reader.ReadSByte();
            reader.ReadBytes(3); // padding
        }
    }

    public class RaidsInventoryItem
    {
        private static readonly int _badgeGadgetId = 0x41;
        private static readonly Random _rng = new Random();
        private static readonly float _damageCurveMultMax = 10000;
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
            { Gadgets.Vipers, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.MagmaCannon, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.Arbiter, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.Fusion, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.MineLauncher, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.B6, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.Flail, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.Holoshields, new AlphaMods[] { AlphaMods.Ammo, AlphaMods.Speed, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
        };
        private static readonly int[] _gadgetMaxProficiencyForDifficulty = new[] { 10, 25, 50, 80, 99 };
        private static readonly float[] _gadgetMaxQualityForDifficulty = new[] { 0.4f, 0.6f, 0.725f, 0.90f, 0.999f };
        private static readonly float[] _gadgetMissionCompleteMinQualityForDifficulty = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };

        public static readonly RaidsInventoryItem Empty = new RaidsInventoryItem();
        public static readonly RaidsInventoryItem DefaultVipers = new RaidsInventoryItem() { Damage = 5, GadgetId = Gadgets.Vipers, AlphaModCounts = new byte[8] { 0, 0, 0, 0, 0, 1, 0, 0 } };
        public static readonly RaidsInventoryItem DefaultMagmaCannon = new RaidsInventoryItem() { Damage = 15, GadgetId = Gadgets.MagmaCannon, AlphaModCounts = new byte[8] { 0, 0, 0, 0, 0, 1, 0, 0 } };

        public int Damage;
        public uint Price;
        public Gadgets GadgetId;
        public RaidsWeaponPaints Paint;
        public RaidsPaintSpecialFlags PaintSpecialMask;
        public byte Proficiency; // gadget lvl weapon created at (v1-v99)
        public byte Quality; // rarity index (0-255)
        public byte CritChance; // 0-255 (0-100%)
        public OmegaMods OmegaMod;
        public byte Notify;
        public byte[] AlphaModCounts = new byte[8];

        public bool IsBadge() => (int)GadgetId == _badgeGadgetId;
        public bool IsWeapon() => GadgetId != Gadgets.None && !IsBadge();
        public bool IsSameItem(RaidsInventoryItem other) => (this.IsBadge() && other.IsBadge() && this.Proficiency == other.Proficiency) || (this.IsWeapon() && other.IsWeapon() && this.GadgetId == other.GadgetId);

        public bool IsValid()
        {
            if (IsBadge()) return Proficiency > 0;
            if (IsWeapon()) return IsGadgetValid(GadgetId) && Damage > 0;

            return false;
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

        public static float GetWeaponBaseDamage(Gadgets gadget)
        {
            return _baseDamages.GetValueOrDefault(gadget);
        }

        public static int Compare(RaidsInventoryItem a, RaidsInventoryItem b)
        {
            if (a.GadgetId == Gadgets.None && b.GadgetId == Gadgets.None) return 0;
            if (a.GadgetId == Gadgets.None) return 1;
            if (b.GadgetId == Gadgets.None) return -1;

            if (a.GadgetId != b.GadgetId) return a.GadgetId.CompareTo(b.GadgetId);

            return a.Quality.CompareTo(b.Quality);
        }


        public static OmegaMods GetRandomOmegaMod(Gadgets gadget)
        {
            if (gadget == Gadgets.Arbiter || gadget == Gadgets.B6 || gadget == Gadgets.MineLauncher)
            {
                switch (_rng.Next(0, 4))
                {
                    case 0: return OmegaMods.Acid;
                    case 1: return OmegaMods.Freeze;
                    case 2: return OmegaMods.Napalm;
                    case 3: return OmegaMods.MiniBomb;
                }
            }
            else
            {
                switch (_rng.Next(0, 2))
                {
                    case 0: return OmegaMods.Acid;
                    case 1: return OmegaMods.Freeze;
                }
            }

            return 0;
        }

        public static AlphaMods GetRandomAlphaMod(Gadgets gadget)
        {
            AlphaMods[] options = _gadgetAlphamods[gadget];
            var r = _rng.Next(0, options.Length + 1);
            if (r == 0) return 0;

            return options[r - 1];
        }

        public static int GetRarityFromQuality(int quality)
        {
            if (quality < 64) return 0;
            if (quality < 128) return 1;
            if (quality < 192) return 2;
            if (quality < 255) return 3;

            return 4;
        }

        public static RaidsInventoryItem Random()
        {
            var r = new Random();
            var gadget = Gadgets.Vipers + r.Next(0, 8);
            if (gadget == Gadgets.Miniturret) gadget = Gadgets.Flail;

            return new RaidsInventoryItem()
            {
                Damage = r.Next(0, 1000),
                GadgetId = gadget,
                Paint = (RaidsWeaponPaints)r.Next(0, (int)RaidsWeaponPaints.COUNT),
                PaintSpecialMask = (RaidsPaintSpecialFlags)r.Next(0, 4),
                Proficiency = (byte)r.Next(0, 99),
                Quality = (byte)r.Next(0, 256),
                CritChance = (byte)r.Next(0, 256),
                OmegaMod = (OmegaMods)r.Next(0, (int)OmegaMods.Shock + 1),
                Notify = 1,
                AlphaModCounts = new byte[8]
                {
                    (byte)r.Next(0, 10),
                    (byte)r.Next(0, 10),
                    (byte)r.Next(0, 10),
                    (byte)r.Next(0, 10),
                    (byte)r.Next(0, 10),
                    (byte)r.Next(0, 10),
                    (byte)r.Next(0, 10),
                    (byte)r.Next(0, 10)
                }
            };
        }

        public static RaidsInventoryItem GenerateWeapon(RaidsGenerateLootDropRequest request, RaidsAccount account)
        {
            var drop = new RaidsInventoryItem();

            var availableGadgets = account == null ? _weaponGadgetIds : _weaponGadgetIds.Where(x => account.HasUnlockedWeapon(x));
            var paintChances = new[] { 0.05, 0.125, 0.175, 0.5, 1.0 };
            var specialChances = new[] { 0, 0.01, 0.02, 0.08, 0.25 };
            var omegaChances = new[] { 0, 0.1, 0.2, 0.5, 1.0 };
            var critChances = new[] { 0, 0.25, 0.5, 0.75, 1 };
            var alphaModMax = new Dictionary<AlphaMods, int>()
            {
                { AlphaMods.Speed, 15 },
                { AlphaMods.Area, 10 },
                { AlphaMods.Aiming, 99 },
                { AlphaMods.Ammo, 99 },
                { AlphaMods.Xp, 99 },
                { AlphaMods.Jackpot, 99 },
                { AlphaMods.Nanoleech, 99 },
                { AlphaMods.Impact, 10 },
            };

            // can't generate for player
            if (!availableGadgets.Any()) return drop;

            // 50% chance to drop requested gadget
            // otherwise pick a random one
            if (availableGadgets.Contains(request.MobKilledByGadget) && _rng.NextDouble() < 0.5)
                drop.GadgetId = request.MobKilledByGadget;
            else
                drop.GadgetId = availableGadgets.OrderBy(x => Guid.NewGuid()).FirstOrDefault();

            // 
            int accountProf = account?.GetProficiency(drop.GadgetId) ?? 98;
            int minProficiency = account == null ? 0 : Math.Max(0, accountProf - 5);
            int maxProficiency = account == null ? 99 : Math.Min(99, accountProf + 5);
            int difficultyMaxProficiency = _gadgetMaxProficiencyForDifficulty[request.DifficultyStars];
            var difficultyMaxQuality = _gadgetMaxQualityForDifficulty[request.DifficultyStars] - 0.00001;

            // determine quality
            var quality = _rng.NextDouble() * _rng.NextDouble() * difficultyMaxQuality;

            switch (request.Type)
            {
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath:
                    {
                        // clamp to max proficiency per difficulty
                        if (maxProficiency > difficultyMaxProficiency)
                            maxProficiency = difficultyMaxProficiency;
                        if (minProficiency >= maxProficiency)
                            minProficiency = Math.Max(maxProficiency - 5, 0);
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent:
                    {
                        var minQuality = _gadgetMissionCompleteMinQualityForDifficulty[request.DifficultyStars];
                        quality = Math.Pow(((1 - minQuality) * quality + minQuality), 1 - minQuality) + 0.01;
                        //quality = Math.Min(((1 - minQuality) * quality * (1 + minQuality)) + minQuality, Math.Min(1, difficultyMaxQuality + 0.05));
                        minProficiency = accountProf;
                        maxProficiency = Math.Min(99, accountProf + 10);

                        // clamp to max proficiency per difficulty
                        if (maxProficiency > difficultyMaxProficiency)
                            maxProficiency = difficultyMaxProficiency;
                        if (minProficiency >= maxProficiency)
                            minProficiency = Math.Max(maxProficiency - 5, 0);
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Store:
                    {
                        quality = _rng.NextDouble();
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
            drop.Proficiency = (byte)_rng.Next(minProficiency, maxProficiency);
            drop.Notify = 1;
            int rarity = GetRarityFromQuality(drop.Quality);

            // paint
            if (_rng.NextDouble() < paintChances[rarity])
                drop.Paint = (RaidsWeaponPaints)_rng.Next(0, (int)RaidsWeaponPaints.COUNT);

            // special
            if (_rng.NextDouble() < specialChances[rarity])
                drop.PaintSpecialMask = (RaidsPaintSpecialFlags)_rng.Next(0, (int)RaidsPaintSpecialFlags.ALL + 1);

            // omega mod
            if (_rng.NextDouble() < omegaChances[rarity])
                drop.OmegaMod = GetRandomOmegaMod(drop.GadgetId);

            // crit
            if (_rng.NextDouble() < critChances[rarity])
                drop.CritChance = (byte)(_rng.NextDouble() * drop.Quality);

            // damage
            var damageScale = _damageScales[drop.GadgetId];
            double damage = _baseDamages[drop.GadgetId];
            damage += damageScale * _damageCurveMultMax * Math.Pow(drop.Proficiency / 98.0, 2);
            damage += damageScale * damage * 0.50 * (drop.Quality / 255.0) * _rng.NextDouble(); // up to +50% for higher rarity
            damage += damageScale * damage * 0.1 * (_rng.NextDouble() - 0.5); // +/- 5%
            damage += 10 * (_rng.NextDouble() - 0.5); // +/- 5 (good for early levels)
            if (damage < _baseDamages[drop.GadgetId])
                damage = _baseDamages[drop.GadgetId];
            drop.Damage = (int)damage;

            // alpha mods
            int minAmods = 2 * (int)Math.Pow(2, rarity) - 1;
            int maxAmods = (2 * (int)Math.Pow(3, rarity)) + (drop.Proficiency / 25) + 2;
            int amodCount = (int)(Math.Pow(_rng.NextDouble(), 1.1) * maxAmods);
            if (amodCount < minAmods) amodCount = minAmods;
            for (int i = 0; i < amodCount; ++i)
            {
                while (true)
                {
                    var amod = GetRandomAlphaMod(drop.GadgetId);
                    var amodIdx = (int)amod - 1;
                    if (amod != AlphaMods.None)
                    {
                        if (drop.AlphaModCounts[amodIdx] >= alphaModMax[amod]) continue;
                        drop.AlphaModCounts[amodIdx]++;
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
            var badgeType = (RaidsBadgeType)_rng.Next((int)RaidsBadgeType.NONE + 1, (int)RaidsBadgeType.COUNT);
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
                        quality = _rng.NextDouble();
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Prestige:
                    {
                        quality = (0.5 + (0.5 * _rng.NextDouble()));
                        break;
                    }
            }

            // reroll if mythic-only badge
            //var isMythic = GetRarityFromQuality((int)(quality * 256)) == 4;
            //while (badgeType == RaidsBadgeType.EXTRALIFE && !isMythic)
            //    badgeType = (RaidsBadgeType)_rng.Next((int)RaidsBadgeType.NONE + 1, (int)RaidsBadgeType.COUNT);

            drop.GadgetId = (Gadgets)_badgeGadgetId;
            drop.Proficiency = (byte)badgeType;
            drop.Quality = (byte)Math.Clamp(quality * 256, 0, 255);
            drop.Notify = 1;

            drop.Price = ComputeSellPrice(drop);
            return drop;
        }

        public static RaidsInventoryItem Generate(RaidsGenerateLootDropRequest request, RaidsAccount account)
        {
            // 15% chance to generate badge
            if ((!request.IsPrestige() && _rng.NextDouble() < 0.15) || request.IsAccountPrestige())
                return GenerateBadge(request, account);

            return GenerateWeapon(request, account);
        }

        public static uint ComputeBuyPrice(RaidsInventoryItem item)
        {
            int rarity = GetRarityFromQuality(item.Quality);
            float quality = item.Quality / 255.0f;
            int roundTo = 100000;

            if (item.IsBadge())
            {
                double bolts = (item.Proficiency * 100000) + Math.Pow(1000000, 1 + Math.Pow((rarity / 6.0), 2)) + (10000000 * quality);
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }
            else
            {
                double bolts = (item.Proficiency * 100000) + Math.Pow(1000000, 1 + Math.Pow((rarity / 6.0), 2)) + (10000000 * quality);
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }
        }

        public static uint ComputeSellPrice(RaidsInventoryItem item)
        {
            int rarity = GetRarityFromQuality(item.Quality);
            float quality = item.Quality / 255.0f;
            int roundTo = 1000;

            if (item.IsBadge())
            {
                double bolts = (5000 + Math.Pow(1000, 1 + Math.Pow((rarity / 4.0), 1.25)) + (10000 * quality));
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }
            else
            {
                double bolts = (item.Proficiency * 1000) + Math.Pow(500, 1 + Math.Pow((rarity / 4.0), 2)) + (10000 * quality);
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }
        }

        public void Serialize(BinaryWriter writer)
        {
            if (AlphaModCounts == null) AlphaModCounts = new byte[8];
            else if (AlphaModCounts.Length != 8) Array.Resize(ref AlphaModCounts, 8);

            writer.Write(Damage);
            writer.Write(Price);
            writer.Write((byte)GadgetId);
            writer.Write((byte)Paint);
            writer.Write((byte)PaintSpecialMask);
            writer.Write((byte)Proficiency);
            writer.Write((byte)Quality);
            writer.Write((byte)CritChance);
            writer.Write((byte)OmegaMod);
            writer.Write(Notify);
            writer.Write(AlphaModCounts);
        }

        public void Deserialize(BinaryReader reader)
        {
            Damage = reader.ReadInt32();
            Price = reader.ReadUInt32();
            GadgetId = (Gadgets)reader.ReadByte();
            Paint = (RaidsWeaponPaints)reader.ReadByte();
            PaintSpecialMask = (RaidsPaintSpecialFlags)reader.ReadByte();
            Proficiency = reader.ReadByte();
            Quality = reader.ReadByte();
            CritChance = reader.ReadByte();
            OmegaMod = (OmegaMods)reader.ReadByte();
            Notify = reader.ReadByte();
            AlphaModCounts = reader.ReadBytes(8);
        }
    }

    public class RaidsStore
    {
        // one xp mod
        public static readonly byte[] STORE_FIXED_ITEM_ALPHAMODS = new byte[8] { 0, 0, 0, 0, 0, 1, 0, 0 };
        public static readonly List<RaidsInventoryItem> FIXED_ITEMS = new List<RaidsInventoryItem>()
        {
            new RaidsInventoryItem() { GadgetId = Gadgets.Vipers, Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(Gadgets.Vipers), AlphaModCounts = STORE_FIXED_ITEM_ALPHAMODS },
            new RaidsInventoryItem() { GadgetId = Gadgets.MagmaCannon, Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(Gadgets.MagmaCannon), AlphaModCounts = STORE_FIXED_ITEM_ALPHAMODS },
            new RaidsInventoryItem() { GadgetId = Gadgets.Arbiter, Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(Gadgets.Arbiter), AlphaModCounts = STORE_FIXED_ITEM_ALPHAMODS },
            new RaidsInventoryItem() { GadgetId = Gadgets.Fusion, Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(Gadgets.Fusion), AlphaModCounts = STORE_FIXED_ITEM_ALPHAMODS },
            new RaidsInventoryItem() { GadgetId = Gadgets.MineLauncher, Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(Gadgets.MineLauncher), AlphaModCounts = STORE_FIXED_ITEM_ALPHAMODS },
            new RaidsInventoryItem() { GadgetId = Gadgets.B6, Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(Gadgets.B6), AlphaModCounts = STORE_FIXED_ITEM_ALPHAMODS },
            new RaidsInventoryItem() { GadgetId = Gadgets.Holoshields, Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(Gadgets.Holoshields), AlphaModCounts = STORE_FIXED_ITEM_ALPHAMODS },
            new RaidsInventoryItem() { GadgetId = Gadgets.Flail, Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(Gadgets.Flail), AlphaModCounts = STORE_FIXED_ITEM_ALPHAMODS },
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
            // second page is the base weapons
            int itemsPages = (int)Math.Ceiling(Items.Count / (float)pageSize);
            totalCount = (itemsPages * pageSize) + FIXED_ITEMS.Count;

            int idx = page * pageSize;
            if (idx < Items.Count)
            {
                return Items.Skip(idx).Take(pageSize);
            }
            else
            {
                return FIXED_ITEMS.Skip((page - itemsPages) * pageSize).Take(pageSize);
            }
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
            if (storeItem.GadgetId != item.GadgetId || storeItem.Quality != item.Quality || storeItem.Proficiency != item.Proficiency) return;

            // generate new item
            Items[idx] = RaidsInventoryItem.Generate(new RaidsGenerateLootDropRequest() { Type = RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Store }, account);
            Items[idx].Price = RaidsInventoryItem.ComputeBuyPrice(Items[idx]);
            Items = Items.OrderByDescending(x => x.Price).ThenBy(x => x.IsWeapon()).ToList();
        }
    }
}
