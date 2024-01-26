using Horizon.Plugin.Deadlocked.Messages;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public static class Maps
    {
        static readonly string MapVersionPath = Path.Combine(Plugin.WorkingDirectory, "bin/cmaps version.txt");
        static readonly string[] MapModules = new string[]
        {
            Path.Combine(Plugin.WorkingDirectory, "bin/usbhdfsd.irx"),
            Path.Combine(Plugin.WorkingDirectory, "bin/usbserv.irx"),
        };

        static readonly CustomMap[] CustomMaps = new CustomMap[]
        {
            new CustomMap(CustomMapId.CMAP_ID_ACE_HARDLIGHT_SUITE, "Ace Hardlight's Suite", "ace suite", 51),
            new CustomMap(CustomMapId.CMAP_ID_ALPINE_JUNCTION, "Alpine Junction", "alpine junction", 46),
            new CustomMap(CustomMapId.CMAP_ID_ANNIHILATION_NATION, "Annihilation Nation", "annihilation nation", 48),
            new CustomMap(CustomMapId.CMAP_ID_BAKISI_ISLES, "Bakisi Isles", "bakisi isles", 44),
            new CustomMap(CustomMapId.CMAP_ID_BDOME_SP, "Battledome SP", "battledome sp", 51),
            new CustomMap(CustomMapId.CMAP_ID_BLACKWATER_CITY, "Blackwater City", "blackwater city", 48),
            new CustomMap(CustomMapId.CMAP_ID_BLACKWATER_DOCKS, "Blackwater Docks", "blackwater docks", 46),
            new CustomMap(CustomMapId.CMAP_ID_CANAL_CITY, "Canal City", "canal city", 46),
            new CustomMap(CustomMapId.CMAP_ID_CONTAINMENT_SUITE, "Containment Suite", "containment suite", 51),
            new CustomMap(CustomMapId.CMAP_ID_DC_INTERIOR, "Dark Cathedral Interior", "dc interior", 45),
            //new CustomMap(CustomMapId.CMAP_ID_DESERT_PRISON, "Desert Prison", "desert prison", 53),
            //new CustomMap(CustomMapId.CMAP_ID_DUCK_HUNT, "Duck Hunt", "duck hunt", 44, CustomModeId.CMODE_ID_DUCK_HUNT ),
            new CustomMap(CustomMapId.CMAP_ID_GHOST_HANGAR, "Ghost Hangar", "ghost hangar", 54),
            new CustomMap(CustomMapId.CMAP_ID_GHOST_SHIP, "Ghost Ship", "ghost ship", 54),
            new CustomMap(CustomMapId.CMAP_ID_HOVEN_GORGE, "Hoven Gorge", "hoven gorge", 46),
            //new CustomMap(CustomMapId.CMAP_ID_HOVERBIKE_RACE, "Hoverbike Race", "sarathos sp", 44, CustomModeId.CMODE_ID_HOVERBIKE_RACE ),
            new CustomMap(CustomMapId.CMAP_ID_INFINITE_CLIMBER, "Infinite Climber", "climber", 42, CustomModeId.CMODE_ID_INFINITE_CLIMBER),
            new CustomMap(CustomMapId.CMAP_ID_KORGON_OUTPOST, "Korgon Outpost", "korgon outpost", 53),
            new CustomMap(CustomMapId.CMAP_ID_LAUNCH_SITE, "Launch Site", "launch site", 44),
            new CustomMap(CustomMapId.CMAP_ID_MARCADIA_PALACE, "Marcadia Palace", "marcadia palace", 44),
            new CustomMap(CustomMapId.CMAP_ID_METROPOLIS_MP, "Metropolis MP", "metropolis mp", 51),
            new CustomMap(CustomMapId.CMAP_ID_MF_SP, "Mining Facility SP", "mining facility sp", 48),
            new CustomMap(CustomMapId.CMAP_ID_MOUNTAIN_PASS, "Mountain Pass", "mountain pass", 50),
            //new CustomMap(CustomMapId.CMAP_ID_RUST, "Rust", "rust", 53),
            //new CustomMap(CustomMapId.CMAP_ID_SARATHOS_SP, "Sarathos SP", "sarathos sp", 44),
            new CustomMap(CustomMapId.CMAP_ID_SHAAR_SP, "Shaar SP", "shaar sp", 46),
            //new CustomMap(CustomMapId.CMAP_ID_SHIPMENT, "Shipment", "shipment", 42),
            new CustomMap(CustomMapId.CMAP_ID_SNIVELAK, "Snivelak", "snivelak", 46),
            new CustomMap(CustomMapId.CMAP_ID_SPLEEF, "Spleef", "spleef", 44, CustomModeId.CMODE_ID_SPLEEF ),
            new CustomMap(CustomMapId.CMAP_ID_TORVAL_LOST_FACTORY, "Torval Lost Factory", "torval lost factory", 50),
            new CustomMap(CustomMapId.CMAP_ID_TORVAL_SP, "Torval SP", "torval sp", 50),
            new CustomMap(CustomMapId.CMAP_ID_TYHRRANOSIS, "Tyhrranosis", "tyhrranosis", 53),
            new CustomMap(CustomMapId.CMAP_ID_SURVIVAL_ORXON, "Orxon", "survival v2 mf", 48),
            new CustomMap(CustomMapId.CMAP_ID_SURVIVAL_MOUNTAIN_PASS, "Mountain Pass", "survival mpass", 50),
            new CustomMap(CustomMapId.CMAP_ID_SURVIVAL_VELDIN, "Veldin", "survival veldin", 48),
        };

        public static CustomMap FindCustomMapById(CustomMapId id)
        {
            return CustomMaps.FirstOrDefault(x => x.MapId == id);
        }

        public static Task SendMapModules(ClientObject client, uint module1Addr, uint module2Addr)
        {
            var payloads = new Payload[]
            {
                new Payload(module1Addr, File.ReadAllBytes(MapModules[0])),
                new Payload(module2Addr, File.ReadAllBytes(MapModules[1])),
            };
            
            return Downloader.InitiateDataDownload(client, 102, payloads, (_client, _id) =>
            {
                client.Queue(new MapModulesResponseMessage()
                {
                    CustomMapsVersion = int.Parse(File.ReadAllText(MapVersionPath)),
                    Module1Size = payloads[0].Data.Length,
                    Module2Size = payloads[1].Data.Length,
                });

                return Task.CompletedTask;
            });
        }

        public static Task SendMapVersion(ClientObject client)
        {
            client.Queue(new MapGlobalVersionMessage()
            {
                CustomMapsVersion = int.Parse(File.ReadAllText(MapVersionPath))
            });

            return Task.CompletedTask;
        }

        public static Task SendMapOverride(ClientObject client, CustomMap map)
        {
            client.Queue(new SetMapOverrideRequestMessage()
            {
                MapId = (byte)(map?.MapId ?? 0),
                LoadingMapId = (byte)(map?.LoadingMapId ?? 0),
                MapFilename = map?.MapFilename ?? "",
                MapName = map?.MapName ?? ""
            });

            return Task.CompletedTask;
        }

        public static int ToUniqueId(this MapId id) => (int)id;
        public static int ToUniqueId(this CustomMapId id) => (int)id + 100;
    }


    public enum MapId : byte
    {
        BATTLEDOME = 41,
        CATACROM = 42,
        SARATHOS = 44,
        DARK_CATHEDRAL = 45,
        SHAAR = 46,
        VALIX = 47,
        MINING_FACILITY = 48,
        TORVAL = 50,
        TEMPUS = 51,
        MARAXUS = 53,
        GHOST_STATION = 54
    }

    public enum CustomMapId : byte
    {
        // custom map ids
        CMAP_ID_ACE_HARDLIGHT_SUITE = 1,
        CMAP_ID_ALPINE_JUNCTION = CMAP_ID_ACE_HARDLIGHT_SUITE + 1,
        CMAP_ID_ANNIHILATION_NATION = CMAP_ID_ALPINE_JUNCTION + 1,
        CMAP_ID_BAKISI_ISLES = CMAP_ID_ANNIHILATION_NATION + 1,
        CMAP_ID_BDOME_SP = CMAP_ID_BAKISI_ISLES + 1,
        CMAP_ID_BLACKWATER_CITY = CMAP_ID_BDOME_SP + 1,
        CMAP_ID_BLACKWATER_DOCKS = CMAP_ID_BLACKWATER_CITY + 1,
        CMAP_ID_CANAL_CITY = CMAP_ID_BLACKWATER_DOCKS + 1,
        CMAP_ID_CONTAINMENT_SUITE = CMAP_ID_CANAL_CITY + 1,
        CMAP_ID_DC_INTERIOR = CMAP_ID_CONTAINMENT_SUITE + 1,
        CMAP_ID_GHOST_HANGAR = CMAP_ID_DC_INTERIOR + 1,
        CMAP_ID_GHOST_SHIP = CMAP_ID_GHOST_HANGAR + 1,
        CMAP_ID_HOVEN_GORGE = CMAP_ID_GHOST_SHIP + 1,
        CMAP_ID_INFINITE_CLIMBER = CMAP_ID_HOVEN_GORGE + 1,
        CMAP_ID_KORGON_OUTPOST = CMAP_ID_INFINITE_CLIMBER + 1,
        CMAP_ID_LAUNCH_SITE = CMAP_ID_KORGON_OUTPOST + 1,
        CMAP_ID_MARCADIA_PALACE = CMAP_ID_LAUNCH_SITE + 1,
        CMAP_ID_METROPOLIS_MP = CMAP_ID_MARCADIA_PALACE + 1,
        CMAP_ID_MF_SP = CMAP_ID_METROPOLIS_MP + 1,
        CMAP_ID_MOUNTAIN_PASS = CMAP_ID_MF_SP + 1,
        //CMAP_ID_RUST = CMAP_ID_MOUNTAIN_PASS + 1,
        CMAP_ID_SHAAR_SP = CMAP_ID_MOUNTAIN_PASS + 1,
        CMAP_ID_SNIVELAK = CMAP_ID_SHAAR_SP + 1,
        CMAP_ID_SPLEEF = CMAP_ID_SNIVELAK + 1,
        CMAP_ID_TORVAL_LOST_FACTORY = CMAP_ID_SPLEEF + 1,
        CMAP_ID_TORVAL_SP = CMAP_ID_TORVAL_LOST_FACTORY + 1,
        CMAP_ID_TYHRRANOSIS = CMAP_ID_TORVAL_SP + 1,

        // survival custom map ids
        CMAP_ID_SURVIVAL_ORXON = CMAP_ID_TYHRRANOSIS + 1,
        CMAP_ID_SURVIVAL_MOUNTAIN_PASS = CMAP_ID_SURVIVAL_ORXON + 1,
        CMAP_ID_SURVIVAL_VELDIN = CMAP_ID_SURVIVAL_MOUNTAIN_PASS + 1,
    }

    public class CustomMap
    {
        public CustomMapId MapId { get; private set; }
        public string MapName { get; private set; }
        public string MapFilename { get; private set; }
        public int LoadingMapId { get; private set; }
        public virtual CustomModeId? ModeId { get; private set; }

        public CustomMap(CustomMapId id, string name, string filename, int loadingMapId, CustomModeId? modeId = null)
        {
            MapId = id;
            MapName = name;
            MapFilename = filename;
            LoadingMapId = loadingMapId;
            ModeId = modeId;
        }
    }
}
