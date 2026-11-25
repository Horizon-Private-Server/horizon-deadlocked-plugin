using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Server.Medius.Models;
using System;
using System.IO;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode
    {
        public void OnClientRequestSettings(RaidsGetSettingsRequest request, ClientObject client)
        {
            var bank = GetBank(client);
            if (bank == null) return;

            var account = bank.Account;
            if (account.Settings == null || !account.Settings.IsValid())
                account.Settings = new RaidsUserSettings();
            using (var ms = new MemoryStream(RaidsUserSettings.SERIALIZED_SIZE))
            {
                using (var writer = new BinaryWriter(ms))
                {
                    account.Settings.Serialize(writer);
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestAddress, ms.ToArray()));
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.DestHasFlagAddress, BitConverter.GetBytes(1)));
                }
            }
            Player.SavePlayerMetadata(client);
        }

        public void OnClientSaveSettings(RaidsSaveSettingsRequest request, ClientObject client)
        {
            if (request.Settings == null || !request.Settings.IsValid() || request.Revision <= 0) return;
            var bank = GetBank(client);
            if (bank == null) return;

            bank.Account.Settings = request.Settings;
            Player.SavePlayerMetadata(client);
            client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.AckAddress, BitConverter.GetBytes(request.Revision)));
        }
    }
}
