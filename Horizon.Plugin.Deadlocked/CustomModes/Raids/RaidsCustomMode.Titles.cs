using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Server.Medius.Models;
using System;
using System.IO;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode
    {
        public void OnClientTitleAction(RaidsTitleActionRequest request, ClientObject client, GameMetadata metadata)
        {
            if (request.AccountSize != RaidsAccount.SERIALIZED_SIZE || request.RequestId == 0 || request.ReplyAddress == 0) return;
            var bank = GetBank(client);
            if (bank == null) return;

            lock (_contractLock)
            {
                if (bank.TitleReplies.TryGetValue(request.RequestId, out var previous))
                {
                    client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.ReplyAddress, previous));
                    return;
                }

                uint result = metadata?.CustomMapConfig?.Filename == HUB_MAP_FILENAME
                    ? request.Action switch
                    {
                        0 => HandleTitlePurchase(bank, request.Argument),
                        1 => HandleTitleEquip(bank.Account, request.Argument),
                        2 => HandleTitleStatus(request.Argument),
                        _ => 0u
                    }
                    : 0u;

                SendTitleActionReply(request, client, bank, result);
            }
        }

        private static uint HandleTitlePurchase(RaidsBank bank, uint title)
        {
            if (title == 0 || title >= (uint)RaidsPlayerTitle.Count
                || !RaidsTitles.TitlePrices.TryGetValue((RaidsPlayerTitle)title, out var price)) return 0;

            var account = bank.Account;
            if (account.HasTitle(title) || account.Bolts < price
                || !RaidsTitles.MeetsTitleRequirements(bank, title)) return 0;

            account.Bolts -= price;
            account.OwnedTitles |= (UInt128)1 << (int)title;
            account.EquippedTitle = (RaidsPlayerTitle)title;
            return 1;
        }

        private static uint HandleTitleEquip(RaidsAccount account, uint title)
        {
            if (title >= (uint)RaidsPlayerTitle.Count || !account.HasTitle(title)) return 0;

            account.EquippedTitle = (RaidsPlayerTitle)title;
            return 1;
        }

        // The shared reply includes eligibility and every requirement for the selected title.
        private static uint HandleTitleStatus(uint title) => title < (uint)RaidsPlayerTitle.Count ? 1u : 0u;

        private static void SendTitleActionReply(RaidsTitleActionRequest request, ClientObject client, RaidsBank bank, uint result)
        {
            var account = bank.Account;
            uint title = request.Argument < (uint)RaidsPlayerTitle.Count ? request.Argument : 0;
            RaidsTitles.TitleRequirements.TryGetValue((RaidsPlayerTitle)title, out var requirements);
            requirements ??= [];
            int requirementCount = Math.Min(requirements.Length, RaidsTitles.MAX_REQUIREMENTS);
            UInt128 eligibleTitles = 0;
            for (uint i = 0; i < (uint)RaidsPlayerTitle.Count; ++i)
                if (RaidsTitles.MeetsTitleRequirements(bank, i)) eligibleTitles |= (UInt128)1 << (int)i;

            using var stream = new MemoryStream(480);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.WriteUInt128(account.OwnedTitles);
                writer.Write(request.RequestId);
                writer.Write(result);
                writer.Write(account.Bolts);
                writer.Write((uint)account.EquippedTitle);
                writer.WriteUInt128(eligibleTitles);
                writer.Write(title);
                writer.Write((uint)requirementCount);
                for (int i = 0; i < RaidsTitles.MAX_REQUIREMENTS; ++i)
                {
                    var rule = i < requirementCount ? requirements[i] : null;
                    writer.Write(rule?.GetProgress(bank) ?? 0);
                    writer.Write(rule?.Target ?? 0);
                    // Fixed ASCII text keeps new rule types out of the client protocol.
                    var description = new byte[RaidsTitles.REQUIREMENT_DESCRIPTION_LENGTH];
                    var text = System.Text.Encoding.ASCII.GetBytes(rule?.GetDescription(bank) ?? "");
                    Array.Copy(text, description, Math.Min(text.Length, description.Length - 1));
                    writer.Write(description);
                }
                writer.Write(new byte[8]); // 16-byte status alignment
            }

            var reply = stream.ToArray();
            if (bank.TitleReplies.Count >= 128) bank.TitleReplies.Clear();
            bank.TitleReplies[request.RequestId] = reply;
            if (result != 0 && request.Action != 2) Player.SavePlayerMetadata(client);
            client.Queue(RT.Models.RT_MSG_SERVER_MEMORY_POKE.FromPayload(request.ReplyAddress, reply));
        }
    }
}
