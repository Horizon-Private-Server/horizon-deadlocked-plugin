using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode
    {
        public void OnClientRequestEquippedClassProgress(RaidsGetEquippedClassProgressRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            // send to client
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    var raidsClass = bank.Account.GetEquippedClass();
                    var stats = bank.Account.GetEquippedClassStats();
                    RaidsClassStats.Serialize(writer, raidsClass?.ClassType ?? (RaidsClassType)(-1), stats);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientRequestAllClassProgress(RaidsGetAllClassProgressRequest request, ClientObject client)
        {
            if (request.Offset < 0 || request.Count < 0 || request.Offset > 16 || request.Count > 16 - request.Offset) return;

            var bank = GetBank(client);
            if (bank == null) return;

            // send all requested slots; unused slots have an invalid class ID
            using (var ms = new MemoryStream(2048))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    for (int i = request.Offset; i < request.Offset + request.Count; ++i)
                    {
                        var raidsClass = RaidsClasses.Definitions.ElementAtOrDefault(i);
                        var classType = raidsClass?.ClassType ?? (RaidsClassType)(-1);
                        var stats = bank.Account.ClassStats.GetValueOrDefault(classType);
                        RaidsClassStats.Serialize(writer, classType, stats);
                    }

                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestTimeFlagAddress, new byte[8]));
                }
            }

            // save
            Player.SavePlayerMetadata(client);
        }

        public void OnClientSpendClassSkillPoint(RaidsClassSkillRequest request, ClientObject client)
        {
            var bank = GetBank(client, initialize: false);
            if (bank == null) return;
            var account = bank.Account;
            if (request.ClassType != account.EquippedClass || account.GetEquippedClass() == null
                || request.SkillId <= 0 || request.SkillId > byte.MaxValue
                || !Enum.IsDefined(typeof(RaidsClassSkillId), (byte)request.SkillId)) return;

            // Grid access, class levels and rank caps are checked by the shared map code.
            var stats = account.GetEquippedClassStats();
            int rank = stats.SkillRanks.GetValueOrDefault(request.SkillId);
            if (account.GetClassSkillPoints(request.ClassType) <= 0 || rank < 0 || rank >= byte.MaxValue
                || (!stats.SkillRanks.ContainsKey(request.SkillId) && stats.SkillRanks.Count >= RaidsClassStats.MAX_SKILLS)) return;

            stats.SkillRanks[request.SkillId] = rank + 1;
            Player.SavePlayerMetadata(client);
        }

        public void OnClientResetClassSkills(RaidsResetClassSkillsRequest request, ClientObject client)
        {
            var bank = GetBank(client, initialize: false);
            if (bank == null) return;
            var account = bank.Account;
            if (request.ClassType != account.EquippedClass || request.SkillId != 0 || account.GetEquippedClass() == null) return;

            var stats = account.GetEquippedClassStats();
            stats.SkillRanks.Clear();
            Player.SavePlayerMetadata(client);
        }
    }
}
