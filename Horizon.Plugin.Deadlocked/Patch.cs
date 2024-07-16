using Horizon.Plugin.Deadlocked.Messages;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
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
        private static readonly uint PATCH_HASH_ADDRESS = 0x000CFFD0;

        public class PatchSetup
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
            public uint? ConfigAddress { get; set; }
            public PatchHookType HookType { get; set; }

            public uint GetHookValue(uint targetAddress)
            {
                if (HookType == PatchHookType.NONE)
                    return 0;

                uint value = targetAddress / 4;
                return value | 0x08000000;
            }

            public byte[] ComputeHash()
            {
                var appSettings = Plugin.GetAppSettingsOrDefault(AppId);
                var bytes = new List<byte>();

                foreach (var payload in Payloads)
                    bytes.AddRange(File.ReadAllBytes(GetPatchPath(payload.Item2, appSettings?.PatchOverrideName)));

                var hash = System.Security.Cryptography.SHA256.Create();
                return hash.ComputeHash(bytes.ToArray());
            }

            public byte[] ComputeHash(IEnumerable<byte[]> payloads)
            {
                var bytes = new List<byte>();

                foreach (var payload in payloads)
                    bytes.AddRange(payload);

                var hash = System.Security.Cryptography.SHA256.Create();
                return hash.ComputeHash(bytes.ToArray());
            }

            public bool IsMatch(ClientObject client)
            {
                return client.ApplicationId == this.AppId;
            }
        }

        public static readonly PatchSetup[] PatchSetups = new PatchSetup[]
        {
            new PatchSetup()
            {
                AppId = 11184,
                HookAddress = 0x00138DFC, 
                HookType = PatchSetup.PatchHookType.JUMP,
                UnpatchPayload = (0x000CE000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/unpatch-11184.bin")),
                Payloads = new (uint, string)[]
                {
#if COMP
                    (0x000D0000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/patch-comp-11184.bin")),
#else
                    (0x000D0000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/patch-11184.bin")),
#endif
                    (0x000C8000, Path.Combine(Plugin.WorkingDirectory,  "bin/exceptiondisplay.bin"))
                },
                ConfigAddress = 0x000D0008
            },
            new PatchSetup()
            {
                AppId = -1,
                HookAddress = 0x00138DFC,
                HookType = PatchSetup.PatchHookType.JUMP,
                UnpatchPayload = (0x000CE000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/unpatch-11184.bin")),
                Payloads = new (uint, string)[]
                {
                    (0x000FC000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/elfloader-11184.bin")),
                },
                ConfigAddress = null
            },
            new PatchSetup()
            {
                AppId = -2,
                HookAddress = 0x00138DFC,
                HookType = PatchSetup.PatchHookType.JUMP,
                UnpatchPayload = (0x000CE000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/unpatch-11184.bin")),
                Payloads = new (uint, string)[]
                {
                    (0x000D0000, Path.Combine(Plugin.WorkingDirectory, "bin/patch/mapdownloader-11184.bin")),
                },
                ConfigAddress = null
            }
        };

        public static Task QueryForPatch(ClientObject client)
        {
            var appSettings = Plugin.GetAppSettingsOrDefault(client.ApplicationId);
            if (!appSettings.EnablePatch && !appSettings.EnableUnpatch)
                return Task.CompletedTask;

            var patch = PatchSetups.FirstOrDefault(x => x.IsMatch(client));
            if (patch == null)
                return Task.CompletedTask;

            var playerInfo = Player.GetPlayerExtraInfo(client.AccountId);
            playerInfo.PatchHandled = false;

            if (appSettings.EnablePatch)
            {
                var patchHash = patch.ComputeHash();

                client.Queue(new RT_MSG_SERVER_CHEAT_QUERY()
                {
                    Address = PATCH_HASH_ADDRESS,
                    Length = 0x20,
                    QueryType = RT.Common.CheatQueryType.DME_SERVER_CHEAT_QUERY_RAW_MEMORY,
                    SequenceId = 101
                });

                // setup task that will auto send patch if the client doesn't respond in a period of time
                Task.Delay(5000).ContinueWith(r =>
                {
                    if (client.IsConnected && playerInfo != null && !playerInfo.PatchHandled)
                    {
                        if (playerInfo.PatchHash == null || !patchHash.SequenceEqual(playerInfo.PatchHash))
                        {
                            _ = Apply(client, patch);
                        }
                        else
                        {
                            // just send player patch config
                            _ = SendConfig(client, patch);
                        }
                    }
                });
            }
            else if (appSettings.EnableUnpatch)
            {
                _ = Apply(client, patch);
            }

            return Task.CompletedTask;
        }

        public static Task QueryForPatchResponse(ClientObject client, RT_MSG_SERVER_CHEAT_QUERY response)
        {
            var patch = PatchSetups.FirstOrDefault(x => x.IsMatch(client));
            if (patch == null)
                return Task.CompletedTask;

            var playerInfo = Player.GetPlayerExtraInfo(client.AccountId);
            var patchHash = patch.ComputeHash();
            if (client.IsConnected && playerInfo != null && !playerInfo.PatchHandled)
            {
                playerInfo.PatchHash = response.Data;
                if (playerInfo.PatchHash == null || !patchHash.SequenceEqual(playerInfo.PatchHash))
                {
                    _ = Apply(client, patch);
                }
                else
                {
                    playerInfo.PatchHandled = true;

                    // just send player patch config
                    _ = SendConfig(client, patch);
                }
            }

            return Task.CompletedTask;
        }

        public static Task SendPatch(ClientObject client)
        {
            var patch = PatchSetups.FirstOrDefault(x => x.IsMatch(client));
            if (patch == null)
                return Task.CompletedTask;

            _ = Apply(client, patch);

            return Task.CompletedTask;
        }

        private static string GetPatchPath(string path, string overrideName)
        {
            if (String.IsNullOrEmpty(overrideName))
                return path;

            var fi = new FileInfo(path);
            var dir = fi.Directory.FullName;
            var filename = fi.Name;

            var newPath = Path.Combine(dir, filename.Replace("patch-", "patch-" + overrideName + "-"));
            if (File.Exists(newPath))
                return newPath;

            return path;
        }

        public static async Task Apply(ClientObject client, PatchSetup setup)
        {
            try
            {
                var appSettings = Plugin.GetAppSettingsOrDefault(client.ApplicationId);
                var hasHook = setup.HookType != PatchSetup.PatchHookType.NONE && setup.HookAddress > 0;
                var playerInfo = Player.GetPlayerExtraInfo(client.AccountId);

                // indicate we've handled patch
                playerInfo.PatchHandled = true;

                // indicate to patch its unloading
                if (setup.ConfigAddress.HasValue)
                    client.Queue(RT_MSG_SERVER_MEMORY_POKE.FromPayload(setup.ConfigAddress.Value, BitConverter.GetBytes(1)));

                // wait a little to give time
                await Task.Delay(25);

                // reset hook first
                if (hasHook && setup.HookType == PatchSetup.PatchHookType.JUMP)
                    client.Queue(RT_MSG_SERVER_MEMORY_POKE.FromPayload(setup.HookAddress, BitConverter.GetBytes(0x03E00008)));

                if (appSettings.EnableUnpatch)
                {
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

                        // send self destruct if EnablePatch is false
                        if (!appSettings.EnablePatch)
                        {
                            // send
                            client.Queue(new RT_MSG_SERVER_MEMORY_POKE() { Address = setup.UnpatchPayload.Item1 + 8, Payload = BitConverter.GetBytes(1) });

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
                }

                if (appSettings.EnablePatch)
                {
                    // construct payloads
                    var payloads = setup.Payloads.Select(x =>
                    {
                        return new Payload(x.Item1, File.ReadAllBytes(GetPatchPath(x.Item2, appSettings.PatchOverrideName)));
                    });

                    // compute patch hash
                    var hash = setup.ComputeHash(payloads.Select(x => x.Data));

                    if (setup.ConfigAddress.HasValue)
                    {
                        // patch config
                        payloads = payloads.Append(new Payload(setup.ConfigAddress.Value + 8, (await Player.GetPatchConfig(client)).Serialize()));
                    }

                    // add hash
                    payloads = payloads.Append(new Payload(PATCH_HASH_ADDRESS, hash));

                    // update saved player hash
                    playerInfo.PatchHash = hash;

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
                else if (appSettings.EnableUnpatch)
                {
                    // send unhook to unpatch
                    client.Queue(RT_MSG_SERVER_MEMORY_POKE.FromPayload(setup.HookAddress, BitConverter.GetBytes(0x03E00008)));
                }
            }
            catch (Exception ex)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, ex);
            }
        }

        private static async Task SendConfig(ClientObject client, PatchSetup setup)
        {
            try
            {
                // send to config it patch has it configured
                if (setup.ConfigAddress.HasValue)
                {
                    var configMsgs = RT_MSG_SERVER_MEMORY_POKE.FromPayload(setup.ConfigAddress.Value + 8, (await Player.GetPatchConfig(client)).Serialize());
                    client.Queue(configMsgs);
                }

                // send global map version
                await Maps.SendMapVersion(client);
            }
            catch (Exception ex)
            {
                Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.ERROR, ex);
            }
        }

    }


    public enum PatchModuleEntryType : byte
    {
        DISABLED,
        RUN_ONCE_GAME,
        RUN_ALWAYS
    }

    public class PatchModuleEntry
    {
        public PatchModuleEntryType Type { get; set; }
        public sbyte ModeId { get; set; }
        public sbyte Arg2 { get; set; }
        public sbyte Arg3 { get; set; }
        public uint Entrypoint { get; set; }

        public byte[] Serialize()
        {
            byte[] output = new byte[16];
            using (var ms = new MemoryStream(output, true))
            {
                using (var writer = new MessageWriter(ms))
                {
                    writer.Write((byte)Type);
                    writer.Write(ModeId);
                    writer.Write(Arg2);
                    writer.Write(Arg3);
                    writer.Write(Entrypoint);
                }
            }

            return output;
        }

        public void Deserialize(MessageReader reader)
        {
            Type = reader.Read<PatchModuleEntryType>();
            ModeId = reader.ReadSByte();
            Arg2 = reader.ReadSByte();
            Arg3 = reader.ReadSByte();
            Entrypoint = reader.ReadUInt32();
        }
    }
}
