using RT.Models;
using Server.Common;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public static class Patch
    {
        class PatchSetup
        {
            public enum PatchHookType
            {
                NONE,
                JUMP
            }

            public int AppId { get; set; }
            public (uint, string) UnpatchPayload { get; set; }
            public (uint, string)[] Payloads { get; set; }
            public uint HookAddress { get; set; }
            public PatchHookType HookType { get; set; }

            public uint GetHookValue(uint targetAddress)
            {
                if (HookType == PatchHookType.NONE)
                    return 0;

                uint value = targetAddress / 4;
                return value | 0x08000000;
            }
        }

        static readonly PatchSetup[] PatchSetups = new PatchSetup[]
        {
            new PatchSetup()
            {
                AppId = 11184,
                HookAddress = 0x00138DFC, 
                HookType = PatchSetup.PatchHookType.JUMP,
                UnpatchPayload = (0x000CE000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/unpatch-11184.bin")),
                Payloads = new (uint, string)[]
                {
                    (0x000E0000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/patch-11184.bin")),
                    (0x000EC000, Path.Combine(Plugin.WorkingDirectory,  "bin/patch/gamerules-11184.bin")),
                    (0x000C8000, Path.Combine(Plugin.WorkingDirectory,  "bin/exceptiondisplay.bin"))
                }
            }
        };

        public static Task SendPatch(ClientObject client)
        {
            foreach (var setup in PatchSetups)
            {
                if (setup.AppId == client.ApplicationId)
                {
                    // we want to send the patch without blocking since it will be over a period of time
                    _ = Apply(client, setup);
                }
            }

            return Task.CompletedTask;
        }

        private static async Task Apply(ClientObject client, PatchSetup setup)
        {
            try
            {
                var hasHook = setup.HookType != PatchSetup.PatchHookType.NONE && setup.HookAddress > 0;

                // reset hook first
                if (hasHook && setup.HookType == PatchSetup.PatchHookType.JUMP)
                    client.Queue(RT_MSG_SERVER_MEMORY_POKE.FromPayload(setup.HookAddress, BitConverter.GetBytes(0x03E00008)));

                // send unpatch payload
                if (setup.UnpatchPayload.Item1 > 0 && File.Exists(setup.UnpatchPayload.Item2))
                {
                    var bytes = File.ReadAllBytes(setup.UnpatchPayload.Item2);
                    var pokeMsgs = RT_MSG_SERVER_MEMORY_POKE.FromPayload(setup.UnpatchPayload.Item1, bytes);

                    foreach (var pokeMsg in pokeMsgs)
                    {
                        // send
                        client.Queue(pokeMsg);

                        // wait 25 ms after each poke
                        await Task.Delay(25);
                    }

                    // send hook
                    if (hasHook)
                    {
                        var hookMsgs = RT_MSG_SERVER_MEMORY_POKE.FromPayload(setup.HookAddress, BitConverter.GetBytes(setup.GetHookValue(setup.UnpatchPayload.Item1)));
                        client.Queue(hookMsgs);
                    }

                    // wait a bit
                    await Task.Delay(500);
                }

                // construct payloads
                var payloads = setup.Payloads.Select(x =>
                {
                    return new Payload(x.Item1, File.ReadAllBytes(x.Item2));
                }).Union(new Payload[]
                {
                    // gamerules module entry
                    new Payload(0x000E0008, (await Player.GetPatchConfig(client)).Serialize()),
                    // hook
                    new Payload(0x000E0008, (await Player.GetPatchConfig(client)).Serialize())
                });

                // send payloads as data download
                await Downloader.InitiateDataDownload(client, 101, payloads, (_client, _id) =>
                {
                    if (hasHook)
                    {
                        var hookMsgs = RT_MSG_SERVER_MEMORY_POKE.FromPayload(setup.HookAddress, BitConverter.GetBytes(setup.GetHookValue(setup.Payloads[0].Item1)));
                        _client.Queue(hookMsgs);
                    }

                    return Task.CompletedTask;
                });

            }
            catch (Exception ex)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, ex);
            }
        }

    }


    public enum PatchModuleEntryType : int
    {
        DISABLED,
        RUN_ONCE_GAME,
        RUN_ALWAYS
    }

    public class PatchModuleEntry
    {
        public PatchModuleEntryType Type { get; set; }
        public uint GameEntrypoint { get; set; }
        public uint LobbyEntrypoint { get; set; }
        public uint LoadEntrypoint { get; set; }

        public byte[] Serialize()
        {
            byte[] output = new byte[16];
            using (var ms = new MemoryStream(output, true))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write(Type);
                    writer.Write(GameEntrypoint);
                    writer.Write(LobbyEntrypoint);
                    writer.Write(LoadEntrypoint);
                }
            }

            return output;
        }

        public void Deserialize(BinaryReader reader)
        {
            Type = reader.Read<PatchModuleEntryType>();
            GameEntrypoint = reader.ReadUInt32();
            LobbyEntrypoint = reader.ReadUInt32();
            LoadEntrypoint = reader.ReadUInt32();
        }
    }
}
