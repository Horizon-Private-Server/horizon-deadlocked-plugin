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
            if (metadata.RaidsBank.Inventory.WeaponsBacklog.Any())
            {
                for (int i = 0; i < metadata.RaidsBank.Inventory.Weapons.Length; ++i)
                {
                    var weapon = metadata.RaidsBank.Inventory.Weapons[i];
                    if (weapon == null || weapon.GadgetId == Gadgets.None)
                    {
                        metadata.RaidsBank.Inventory.Weapons[i] = metadata.RaidsBank.Inventory.WeaponsBacklog.Dequeue();
                        update = true;
                        if (!metadata.RaidsBank.Inventory.WeaponsBacklog.Any()) break;
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
            for (int i = 0; i < request.Count; ++i)
                metadata.RaidsBank.Inventory.Weapons[i + request.Index] = request.Weapons[i];

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
            metadata.RaidsBank.Account = request.Account;

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

            // min 15s cooldown on mob death drops
            if (bank.TimeLastMobDeathLootDrop.HasValue && (DateTime.UtcNow - bank.TimeLastMobDeathLootDrop.Value).TotalSeconds < 15) return;

            var drop = RaidsInventoryWeapon.Generate(request, bank.Account);
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

    public class RaidsBank
    {
        public RaidsInventory Inventory = new RaidsInventory();
        public RaidsAccount Account = new RaidsAccount();
        public DateTime? TimeLastMobDeathLootDrop = null;
        public bool Initialized = false;

        public void Initialize()
        {
            if (Initialized) return;

            // give default weapons
            Inventory.WeaponsBacklog = new Queue<RaidsInventoryWeapon>();
            Inventory.Weapons = new RaidsInventoryWeapon[RaidsInventory.WEAPONS_COUNT];
            Inventory.EquippedWeaponIdxs = new sbyte[RaidsInventory.EQUIPPED_SIZE];

            Inventory.Weapons[0] = RaidsInventoryWeapon.DefaultVipers;
            Inventory.Weapons[1] = RaidsInventoryWeapon.DefaultMagmaCannon;
            Inventory.EquippedWeaponIdxs[(int)GadgetSlots.Vipers - 1] = 0;
            Inventory.EquippedWeaponIdxs[(int)GadgetSlots.MagmaCannon - 1] = 1;

            // give random stats/weapons
            if (false)
            {
                var rng = new Random();

                for (int i = 2; i < RaidsInventory.WEAPONS_COUNT; ++i)
                {
                    // Inventory.Weapons[i] = RaidsInventoryWeapon.Random();
                    Inventory.Weapons[i] = RaidsInventoryWeapon.Generate(new RaidsGenerateLootDropRequest()
                    {
                        Type = RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MissionCompleteEvent,
                    }, Account);
                }

                for (int i = 0; i < Account.WeaponXp.Length; ++i)
                    Account.WeaponXp[i] = (ulong)rng.Next(0, 100000);

            }

            Initialized = true;
        }

        public void Add(RaidsInventoryWeapon weapon)
        {
            // add to backlog
            // let server move into inventory on next GetBank request (if room)
            Inventory.WeaponsBacklog.Enqueue(weapon);
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
        public uint Bolts = 0;
        public uint SkillPoints = 0;
        public ushort[] Skills = new ushort[SKILLS_COUNT];


        private static readonly ulong XP_CONSTANT = 0;
        private static readonly ulong XP_LINEAR = 0;
        private static readonly ulong XP_QUADRATIC = 10;
        private static int GetLevelFromXp(ulong xp)
        {
            if (xp < XP_CONSTANT) return 0;

            var level = (-(XP_LINEAR / 2.0) + Math.Sqrt((xp - XP_CONSTANT) + Math.Pow(XP_LINEAR / 2.0, 2.0))) / (double)XP_QUADRATIC;
            if (level < 0) return 0;
            return (int)level;
        }

        private static ulong GetXpFromLevel(ulong level)
        {
            return (ulong)(Math.Pow(level * XP_QUADRATIC, 2.0) + (XP_LINEAR * level) + XP_CONSTANT);
        }

        private static int GetProficiencyFromXp(ulong xp)
        {
            if (xp < XP_CONSTANT) return 0;

            var proficiency = (-(XP_LINEAR / 2.0) + Math.Sqrt((xp - XP_CONSTANT) + Math.Pow(XP_LINEAR / 2.0, 2.0))) / (double)XP_QUADRATIC;
            if (proficiency < 0) return 0;
            if (proficiency > 98) return 98;
            return (int)proficiency;
        }

        private static ulong GetXpFromProficiency(ulong proficiency)
        {
            return (ulong)(Math.Pow(proficiency * XP_QUADRATIC, 2.0) + (XP_LINEAR * proficiency) + XP_CONSTANT);
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
        public const int WEAPONS_COUNT = 64;
        public const int EQUIPPED_SIZE = 8;

        public RaidsInventoryWeapon[] Weapons = new RaidsInventoryWeapon[WEAPONS_COUNT];
        public Queue<RaidsInventoryWeapon> WeaponsBacklog = new Queue<RaidsInventoryWeapon>();
        public sbyte[] EquippedWeaponIdxs = new sbyte[EQUIPPED_SIZE];

        public void Serialize(BinaryWriter writer)
        {
            if (Weapons == null) Weapons = new RaidsInventoryWeapon[WEAPONS_COUNT];
            else if (Weapons.Length != WEAPONS_COUNT) Array.Resize(ref Weapons, WEAPONS_COUNT);
            if (EquippedWeaponIdxs == null) EquippedWeaponIdxs = new sbyte[EQUIPPED_SIZE];
            else if (EquippedWeaponIdxs.Length != EQUIPPED_SIZE) Array.Resize(ref EquippedWeaponIdxs, EQUIPPED_SIZE);

            int weaponsCount = WeaponsBacklog.Count;
            for (int i = 0; i < WEAPONS_COUNT; ++i)
            {
                weaponsCount += (Weapons[i] == null) ? 0 : 1;
                (Weapons[i] ?? RaidsInventoryWeapon.Empty).Serialize(writer);
            }

            writer.Write(weaponsCount);
            writer.Write(1); // tells the client the server just sent a payload
            for (int i = 0; i < EQUIPPED_SIZE; ++i)
                writer.Write(EquippedWeaponIdxs[i]);
        }

        public void Deserialize(BinaryReader reader)
        {
            if (Weapons == null) Weapons = new RaidsInventoryWeapon[WEAPONS_COUNT];
            else if (Weapons.Length != WEAPONS_COUNT) Array.Resize(ref Weapons, WEAPONS_COUNT);
            if (EquippedWeaponIdxs == null) EquippedWeaponIdxs = new sbyte[EQUIPPED_SIZE];
            else if (EquippedWeaponIdxs.Length != EQUIPPED_SIZE) Array.Resize(ref EquippedWeaponIdxs, EQUIPPED_SIZE);

            for (int i = 0; i < WEAPONS_COUNT; ++i)
                Weapons[i].Deserialize(reader);
            reader.ReadInt32(); // total weapons count
            reader.ReadInt32(); // refresh flag
            for (int i = 0; i < EQUIPPED_SIZE; ++i)
                EquippedWeaponIdxs[i] = reader.ReadSByte();
        }
    }

    public class RaidsInventoryWeapon
    {
        private static readonly Random _rng = new Random();
        private static readonly float _damageCurveMultMax = 10000;
        private static readonly Dictionary<Gadgets, float> _baseDamages = new Dictionary<Gadgets, float>()
        {
            { Gadgets.Vipers, 10 },
            { Gadgets.MagmaCannon, 25 },
            { Gadgets.Arbiter, 50 },
            { Gadgets.Fusion, 75 },
            { Gadgets.MineLauncher, 25 },
            { Gadgets.B6, 50 },
            { Gadgets.Flail, 30 },
            { Gadgets.Holoshields, 15 },
        };
        private static readonly Dictionary<Gadgets, AlphaMods[]> _gadgetAlphamods = new Dictionary<Gadgets, AlphaMods[]>()
        {
            { Gadgets.Vipers, new AlphaMods[] { AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.MagmaCannon, new AlphaMods[] { AlphaMods.Speed, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.Arbiter, new AlphaMods[] { AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.Fusion, new AlphaMods[] { AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.MineLauncher, new AlphaMods[] { AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.B6, new AlphaMods[] { AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.Flail, new AlphaMods[] { AlphaMods.Speed, AlphaMods.Area, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
            { Gadgets.Holoshields, new AlphaMods[] { AlphaMods.Speed, AlphaMods.Impact, AlphaMods.Jackpot, AlphaMods.Nanoleech, AlphaMods.Xp } },
        };

        public static readonly RaidsInventoryWeapon Empty = new RaidsInventoryWeapon();
        public static readonly RaidsInventoryWeapon DefaultVipers = new RaidsInventoryWeapon() { Damage = 5, GadgetId = Gadgets.Vipers, Speed = 1.0f };
        public static readonly RaidsInventoryWeapon DefaultMagmaCannon = new RaidsInventoryWeapon() { Damage = 15, GadgetId = Gadgets.MagmaCannon };

        public int Damage;
        public float Speed;
        public Gadgets GadgetId;
        public RaidsWeaponPaints Paint;
        public RaidsPaintSpecialFlags PaintSpecialMask;
        public byte Proficiency; // gadget lvl weapon created at (v1-v99)
        public byte Quality; // rarity index (0-255)
        public byte CritChance; // 0-255 (0-100%)
        public OmegaMods OmegaMod;
        public byte Notify;
        public byte[] AlphaModCounts = new byte[8];

        public bool IsValid() => IsGadgetValid(GadgetId) && Damage > 0;

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

        public static int Compare(RaidsInventoryWeapon a, RaidsInventoryWeapon b)
        {
            if (a.GadgetId == Gadgets.None && b.GadgetId == Gadgets.None) return 0;
            if (a.GadgetId == Gadgets.None) return 1;
            if (b.GadgetId == Gadgets.None) return -1;

            if (a.GadgetId != b.GadgetId) return a.GadgetId.CompareTo(b.GadgetId);

            return a.Quality.CompareTo(b.Quality);
        }


        public static Gadgets GetRandomGadget()
        {
            switch (_rng.Next(0, 8))
            {
                case 0: return Gadgets.Vipers;
                case 1: return Gadgets.MagmaCannon;
                case 2: return Gadgets.Arbiter;
                case 3: return Gadgets.Fusion;
                case 4: return Gadgets.MineLauncher;
                case 5: return Gadgets.B6;
                case 6: return Gadgets.Flail;
                case 7: return Gadgets.Holoshields;
            }

            return 0;
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
            if (quality < 196) return 2;

            return 3;
        }

        public static RaidsInventoryWeapon Random()
        {
            var r = new Random();
            var gadget = Gadgets.Vipers + r.Next(0, 8);
            if (gadget == Gadgets.Miniturret) gadget = Gadgets.Flail;

            return new RaidsInventoryWeapon()
            {
                Damage = r.Next(0, 1000),
                Speed = (float)r.NextDouble() * 10,
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

        public static RaidsInventoryWeapon Generate(RaidsGenerateLootDropRequest request, RaidsAccount account)
        {
            var drop = new RaidsInventoryWeapon();

            var paintChances = new[] { 0.05, 0.125, 0.175, 0.5 };
            var specialChances = new[] { 0, 0.01, 0.02, 0.08 };
            var omegaChances = new[] { 0, 0.1, 0.2, 0.5 };
            var critChances = new[] { 0.25, 0.5, 0.75, 1 };

            drop.GadgetId = (request.Type == RaidsGenerateLootDropRequest.GenerateLootDropRequestType.MobDeath && IsGadgetValid(request.MobKilledByGadget) && _rng.NextDouble() < 0.5) ? request.MobKilledByGadget : GetRandomGadget();
            int accountProf = account.GetProficiency(drop.GadgetId);
            drop.Quality = (byte)(_rng.NextDouble() * _rng.NextDouble() * 256);
            drop.Proficiency = (byte)_rng.Next(0, Math.Clamp(accountProf + 2, 0, 99));
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
                drop.CritChance = (byte)(_rng.NextDouble() * _rng.NextDouble() * drop.Quality);

            // damage
            double damage = _baseDamages[drop.GadgetId];
            damage += _damageCurveMultMax * Math.Pow(drop.Proficiency / 98.0, 2);
            damage += damage * 0.50 * (drop.Quality / 255.0) * _rng.NextDouble(); // up to +50% for higher rarity
            damage += damage * 0.1 * (_rng.NextDouble() - 0.5); // +/- 5%
            damage += 10 * (_rng.NextDouble() - 0.5); // +/- 5 (good for early levels)
            drop.Damage = (int)damage;

            // unused
            drop.Speed = 1;

            // alpha mods
            int minAmods = 2 * (int)Math.Pow(2, rarity) - 1;
            int maxAmods = (2 * (int)Math.Pow(3, rarity)) + (drop.Proficiency / 25) + 2;
            int amodCount = (int)(Math.Pow(_rng.NextDouble(), 1.1) * maxAmods);
            if (amodCount < minAmods) amodCount = minAmods;
            for (int i = 0; i < amodCount; ++i)
            {
                var amod = GetRandomAlphaMod(drop.GadgetId);
                if (amod != AlphaMods.None)
                {
                    drop.AlphaModCounts[(int)amod - 1]++;
                }
            }

            return drop;
        }

        public void Serialize(BinaryWriter writer)
        {
            if (AlphaModCounts == null) AlphaModCounts = new byte[8];
            else if (AlphaModCounts.Length != 8) Array.Resize(ref AlphaModCounts, 8);

            writer.Write(Damage);
            writer.Write(Speed);
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
            Speed = reader.ReadSingle();
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

}
