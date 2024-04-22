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

        public static Task SendMapOverride(ClientObject client, GameCustomMapConfig mapConfig)
        {
            client.Queue(new SetMapOverrideRequestMessage()
            {
                CustomMapConfig = mapConfig
            });

            return Task.CompletedTask;
        }
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
}
