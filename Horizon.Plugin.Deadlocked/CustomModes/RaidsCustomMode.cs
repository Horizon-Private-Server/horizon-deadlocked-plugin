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
                metadata.RaidsBank.Account.UnlockWeapon(request.Item.WeaponData.GadgetId);

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
        HEATH_BUFF,
        AMMO_BUFF,
        COUNT
    };

    public enum RaidsItemType
    {
        None = 0,
        Weapon,
        Badge
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
            Account.Bolts = 300000; // start with 300k bolts

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


        private static int GetLevelFromXp(ulong xp, int maxLevel)
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
            if (level > maxLevel) return maxLevel;
            return (int)level;
        }

        private static ulong GetXpFromLevel(int level)
        {
            if (level <= 0) return 0;
            return (ulong)((double)Math.Pow(1 * level, 3) + (500 * level));
        }

        private static int GetAccountLevelFromXp(ulong xp)
        {
            int level = (int)(xp / 100);
            if (level <= 0) return 0;
            if (level >= RaidsCustomMode.MAX_ACCOUNT_LEVEL) level = RaidsCustomMode.MAX_ACCOUNT_LEVEL - 1;
            return level;
        }

        private static ulong GetAccountXpFromLevel(int level)
        {
            if (level <= 0) return 0;
            if (level >= RaidsCustomMode.MAX_ACCOUNT_LEVEL) level = RaidsCustomMode.MAX_ACCOUNT_LEVEL - 1;
            return (ulong)(level * 100);
        }

        private static int GetProficiencyFromXp(ulong xp)
        {
            return GetLevelFromXp(xp, RaidsCustomMode.MAX_WEAPON_LEVEL-1);
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
            ulong maxAccountXp = GetAccountXpFromLevel(RaidsCustomMode.MAX_ACCOUNT_LEVEL);
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
        private static readonly int[] _gadgetMinProficiencyForDifficulty = new[] { 0, 10, 30, 60, 90 };
        private static readonly int[] _gadgetMaxProficiencyForDifficulty = new[] { 15, 35, 65, 90, 99 };
        private static readonly float[] _gadgetMaxQualityForDifficulty = new[] { 0.4f, 0.6f, 0.70f, 0.85f, 0.999f };
        private static readonly float[] _gadgetMissionCompleteMinQualityForDifficulty = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        private static readonly int[] _badgeMinEffectCountForRarity = new[] { 1, 1, 2, 3, 4 };
        private static readonly int[] _badgeMaxEffectCountForRarity = new[] { 2, 3, 4, 5, 7 };
        private static readonly float[] _badgeEffectStrengthForRarity = new[] { 2f, 1.5f, 1.25f, 1f, 0.5f };

        public static readonly RaidsInventoryItem Empty = new RaidsInventoryItem();
        public static readonly RaidsInventoryItem DefaultVipers = GenerateDefaultWeapon(Gadgets.Vipers);
        public static readonly RaidsInventoryItem DefaultMagmaCannon = GenerateDefaultWeapon(Gadgets.MagmaCannon);

        public class ItemWeaponData
        {
            public int Damage;
            public Gadgets GadgetId;
            public RaidsWeaponPaints Paint;
            public RaidsPaintSpecialFlags PaintSpecialMask;
            public byte Proficiency; // gadget lvl weapon created at (v1-v99)
            public byte CritChance; // 0-255 (0-100%)
            public OmegaMods OmegaMod;
            public byte[] AlphaModCounts = new byte[8];
        }

        public class ItemBadgeData
        {
            public List<RaidsBadgeType> Effects = new List<RaidsBadgeType>();
            public List<float> EffectStrength = new List<float>();
        }

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

        public static RaidsInventoryItem GenerateDefaultWeapon(Gadgets gadget)
        {
            return new RaidsInventoryItem()
            {
                Type = RaidsItemType.Weapon,
                WeaponData = new RaidsInventoryItem.ItemWeaponData()
                {
                    GadgetId = gadget,
                    Damage = (int)RaidsInventoryItem.GetWeaponBaseDamage(gadget),
                    AlphaModCounts = new byte[8] { 0, 0, 0, 0, 0, 1, 0, 0 }
                }
            };
        }

        public static RaidsInventoryItem GenerateWeapon(RaidsGenerateLootDropRequest request, RaidsAccount account)
        {
            var drop = new RaidsInventoryItem();
            drop.Type = RaidsItemType.Weapon;
            drop.WeaponData = new ItemWeaponData();

            var availableGadgets = account == null ? _weaponGadgetIds : _weaponGadgetIds.Where(x => account.HasUnlockedWeapon(x));
            var unavailableGadgets = account == null ? new Gadgets[0] : _weaponGadgetIds.Where(x => !account.HasUnlockedWeapon(x));
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
                { AlphaMods.Xp, 15 },
                { AlphaMods.Jackpot, 15 },
                { AlphaMods.Nanoleech, 15 },
                { AlphaMods.Impact, 10 },
            };

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
            // otherwise pick a random one
            if (availableGadgets.Contains(request.MobKilledByGadget) && _rng.NextDouble() < 0.5)
                drop.WeaponData.GadgetId = request.MobKilledByGadget;
            else
                drop.WeaponData.GadgetId = availableGadgets.OrderBy(x => Guid.NewGuid()).FirstOrDefault();

            // 
            int accountProf = account?.GetProficiency(drop.WeaponData.GadgetId) ?? 98;
            int minProficiency = account == null ? 0 : Math.Max(0, accountProf - 5);
            int maxProficiency = account == null ? 99 : Math.Min(99, accountProf + 5);
            int difficultyMinProficiency = _gadgetMinProficiencyForDifficulty[request.DifficultyStars];
            int difficultyMaxProficiency = _gadgetMaxProficiencyForDifficulty[request.DifficultyStars];
            var difficultyMaxQuality = _gadgetMaxQualityForDifficulty[request.DifficultyStars] - 0.00001;
            var proficiencyCurve = 1.5;

            // determine quality
            var quality = _rng.NextDouble() * _rng.NextDouble() * difficultyMaxQuality;

            switch (request.Type)
            {
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath:
                    {
                        minProficiency = difficultyMinProficiency;
                        maxProficiency = difficultyMaxProficiency;
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent:
                    {
                        var minQuality = _gadgetMissionCompleteMinQualityForDifficulty[request.DifficultyStars];
                        quality = Math.Pow(((1 - minQuality) * quality + minQuality), 1 - minQuality) + 0.01;
                        //quality = Math.Min(((1 - minQuality) * quality * (1 + minQuality)) + minQuality, Math.Min(1, difficultyMaxQuality + 0.05));

                        minProficiency = difficultyMinProficiency;
                        maxProficiency = difficultyMaxProficiency;
                        proficiencyCurve = 1;
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
            drop.WeaponData.Proficiency = (byte)(minProficiency + (Math.Pow(_rng.NextDouble(), proficiencyCurve) * (maxProficiency - minProficiency)));
            drop.Notify = 1;
            int rarity = GetRarityFromQuality(drop.Quality);

            // paint
            if (_rng.NextDouble() < paintChances[rarity])
                drop.WeaponData.Paint = (RaidsWeaponPaints)_rng.Next(0, (int)RaidsWeaponPaints.COUNT);

            // special
            if (_rng.NextDouble() < specialChances[rarity])
                drop.WeaponData.PaintSpecialMask = (RaidsPaintSpecialFlags)_rng.Next(0, (int)RaidsPaintSpecialFlags.ALL + 1);

            // omega mod
            if (_rng.NextDouble() < omegaChances[rarity])
                drop.WeaponData.OmegaMod = GetRandomOmegaMod(drop.WeaponData.GadgetId);

            // crit
            if (_rng.NextDouble() < critChances[rarity])
                drop.WeaponData.CritChance = (byte)(_rng.NextDouble() * drop.Quality);

            // damage
            var damageScale = _damageScales[drop.WeaponData.GadgetId];
            double damage = _baseDamages[drop.WeaponData.GadgetId];
            damage += damageScale * _damageCurveMultMax * Math.Pow(drop.WeaponData.Proficiency / 98.0, 2);
            damage += damageScale * damage * 0.50 * (drop.Quality / 255.0) * _rng.NextDouble(); // up to +50% for higher rarity
            damage += damageScale * damage * 0.1 * (_rng.NextDouble() - 0.5); // +/- 5%
            damage += 10 * (_rng.NextDouble() - 0.5); // +/- 5 (good for early levels)
            if (damage < _baseDamages[drop.WeaponData.GadgetId])
                damage = _baseDamages[drop.WeaponData.GadgetId];
            drop.WeaponData.Damage = (int)damage;

            // alpha mods
            int minAmods = 2 * (int)Math.Pow(2, rarity) - 1;
            int maxAmods = (2 * (int)Math.Pow(3, rarity)) + (drop.WeaponData.Proficiency / 25) + 2;
            int amodCount = (int)(Math.Pow(_rng.NextDouble(), 1.1) * maxAmods);
            if (amodCount < minAmods) amodCount = minAmods;
            for (int i = 0; i < amodCount; ++i)
            {
                while (true)
                {
                    var amod = GetRandomAlphaMod(drop.WeaponData.GadgetId);
                    var amodIdx = (int)amod - 1;
                    if (amod != AlphaMods.None)
                    {
                        if (drop.WeaponData.AlphaModCounts[amodIdx] >= alphaModMax[amod]) continue;
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
                        quality = _rng.NextDouble();
                        break;
                    }
                case RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Prestige:
                    {
                        quality = (0.5 + (0.5 * _rng.NextDouble()));
                        break;
                    }
            }

            drop.Quality = (byte)Math.Clamp(quality * 256, 0, 255);

            int rarity = GetRarityFromQuality(drop.Quality);
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
            int rarity = GetRarityFromQuality(item.Quality);
            float quality = item.Quality / 255.0f;
            int roundTo = 1000;

            if (item.IsBadge())
            {
                double bolts = (5000 + Math.Pow(1000, 1 + Math.Pow((rarity / 4.0), 1.25)) + (10000 * quality));
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }
            else if (item.IsWeapon())
            {
                double bolts = (item.WeaponData.Proficiency * 1000) + Math.Pow(500, 1 + Math.Pow((rarity / 4.0), 2)) + (10000 * quality);
                return (uint)(Math.Round(bolts / roundTo) * roundTo);
            }

            return 0;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)Type);
            writer.Write((byte)Notify);
            writer.Write((byte)Quality);
            writer.Write((byte)0); // padding
            writer.Write(Price);

            switch (Type)
            {
                case RaidsItemType.Weapon:
                    {
                        if (WeaponData == null) WeaponData = new ItemWeaponData();
                        if (WeaponData.AlphaModCounts == null) WeaponData.AlphaModCounts = new byte[8];
                        else if (WeaponData.AlphaModCounts.Length != 8) Array.Resize(ref WeaponData.AlphaModCounts, 8);

                        writer.Write(WeaponData.Damage);
                        writer.Write((byte)WeaponData.GadgetId);
                        writer.Write((byte)WeaponData.Paint);
                        writer.Write((byte)WeaponData.PaintSpecialMask);
                        writer.Write((byte)WeaponData.Proficiency);
                        writer.Write((byte)WeaponData.CritChance);
                        writer.Write((byte)WeaponData.OmegaMod);
                        writer.Write(WeaponData.AlphaModCounts);
                        writer.Write(new byte[2]);
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

            switch (Type)
            {
                case RaidsItemType.Weapon:
                    {
                        if (WeaponData == null) WeaponData = new ItemWeaponData();

                        WeaponData.Damage = reader.ReadInt32();
                        WeaponData.GadgetId = (Gadgets)reader.ReadByte();
                        WeaponData.Paint = (RaidsWeaponPaints)reader.ReadByte();
                        WeaponData.PaintSpecialMask = (RaidsPaintSpecialFlags)reader.ReadByte();
                        WeaponData.Proficiency = reader.ReadByte();
                        WeaponData.CritChance = reader.ReadByte();
                        WeaponData.OmegaMod = (OmegaMods)reader.ReadByte();
                        WeaponData.AlphaModCounts = reader.ReadBytes(8);
                        reader.ReadBytes(2);
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

    public class RaidsStore
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
            if (storeItem.Type != item.Type) return;

            // generate new item
            Items[idx] = RaidsInventoryItem.Generate(new RaidsGenerateLootDropRequest() { Type = RaidsGenerateLootDropRequest.GenerateLootDropRequestType.Store }, account);
            Items[idx].Price = RaidsInventoryItem.ComputeBuyPrice(Items[idx]);
            Items = Items.OrderByDescending(x => x.Price).ThenBy(x => x.IsWeapon()).ToList();
        }
    }
}
