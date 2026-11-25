using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages;
using Horizon.Plugin.Deadlocked.Messages;
using Server.Common.Stream;
using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids
{
    public partial class RaidsCustomMode
    {
        private readonly Dictionary<(int Owner, string Map, int Sender), (ushort Expected, List<RaidsContractRule> Rules)> _contractUploads = new();

        public void OnClientRequestContracts(RaidsGetContractsRequest request, ClientObject client, GameMetadata metadata)
        {
            if (client.CurrentGame?.Host == null
                || request.Difficulty >= 5
                || request.PartySize < 1 || request.PartySize > 5
                || !Enum.IsDefined(request.Flags))
                return;

            lock (_contractLock)
            {
                var host = client.CurrentGame.Host;
                var bank = GetBank(host);
                if (bank == null) return;

                // fill empty board slots when a map page is requested
                if (!request.Flags.HasFlag(RaidsContractRequestFlags.Pending)
                    && !string.IsNullOrEmpty(request.MapFilename)
                    && bank.MapStats.TryGetValue(request.MapFilename, out var stats)
                    && stats.ContractRules.Count > 0)
                {
                    var previousUid = bank.ContractStore.NextUid;
                    var previousCount = bank.ContractStore.Boards.GetValueOrDefault(request.MapFilename)?.Contracts.Count ?? 0;
                    var board = bank.ContractStore.GetBoard(request.MapFilename, stats.Name ?? request.MapFilename);
                    bank.ContractStore.Refresh(board, (uint)host.AccountId, stats.ContractRules, ContractLevels(host), DateTime.UtcNow,
                        RaidsAccount.GetAccountLevelFromXp(bank.Account.Experience) + 1);

                    // save if the board changed
                    var boardChanged = previousUid != bank.ContractStore.NextUid || previousCount != board.Contracts.Count;
                    if (boardChanged)
                        Player.SavePlayerMetadata(host);
                }

                // resume party claims that were interrupted by a disconnect or map change
                if (GetBank(client)?.ContractStore.Receipts.Values.Any(r => r.PartyClaimPending && !r.Claimed) == true)
                    NotifyPartyContractClaim(client, new RaidsActionContractRequest());

                // send requested page
                SendContractPage(client, request);
            }
        }

        public void OnClientUpdateContractStats(RaidsUpdateContractStatsRequest request, ClientObject client, GameMetadata metadata)
        {
            // only the host can report progress for its boards
            if (client.CurrentGame?.Host != client || request.OwnerAccountId != client.AccountId || request.Count > 3) return;

            lock (_contractLock)
            {
                var bank = GetBank(client);
                var board = bank?.ContractStore.Boards.Values.FirstOrDefault(b => b.BoardId == request.BoardId);
                if (board == null) return;

                // acknowledge stale updates without applying progress twice
                if (request.Sequence <= board.Sequence)
                {
                    SendContractProgress(client, request, board);
                    return;
                }

                // apply progress to active contracts
                var now = DateTime.UtcNow;
                foreach (var stat in request.Contracts)
                {
                    var contract = board.Contracts.FirstOrDefault(c => c.Uid == stat.ContractUid);
                    if (contract == null || contract.Completed || contract.IsExpired(now)) continue;

                    if (contract.Type == RaidsContractType.Kill || contract.Type == RaidsContractType.Task)
                    {
                        contract.UpdateCount(stat.Progress, (stat.Flags & (byte)RaidsContractFlags.TaskStarted) != 0);
                    }
                    else if (contract.Type == RaidsContractType.Clear
                        && stat.Progress > 0 && stat.Difficulty >= 0 && stat.Difficulty < 5
                        && stat.PartySize >= 1 && stat.PartySize <= 5)
                    {
                        contract.CompletedTimeMs = stat.Progress;
                        contract.CompletedDifficulty = stat.Difficulty;
                        contract.CompletedPartySize = stat.PartySize;
                    }

                    if (!contract.Completed) continue;

                    // record rewards for all members present at completion
                    foreach (var member in GetParty(client))
                    {
                        var memberBank = GetBank(member);
                        memberBank.ContractStore.AddReceipt(contract, (int)memberBank.Account.EquippedClass);
                        if (member != client)
                            Player.SavePlayerMetadata(member);
                    }
                }

                // save host progress
                board.Sequence = request.Sequence;
                Player.SavePlayerMetadata(client);

                // send progress to the party
                foreach (var member in GetParty(client))
                    SendContractProgress(member, request, board);
            }
        }

        public void OnClientActionContract(RaidsActionContractRequest request, ClientObject client, GameMetadata metadata)
        {
            if (client.CurrentGame?.Host == null
                || (request.Action != RaidsActionContractRequest.ContractActions.Complete
                    && request.Action != RaidsActionContractRequest.ContractActions.DismissExpired
                    && request.Action != RaidsActionContractRequest.ContractActions.ClaimPartyRewards
                    && request.Action != RaidsActionContractRequest.ContractActions.ProcessAll
                    && request.Action != RaidsActionContractRequest.ContractActions.GetStatus))
                return;

            lock (_contractLock)
            {
                var bank = GetBank(client);
                var host = client.CurrentGame.Host;
                var hostBank = GetBank(host);
                if (bank == null || hostBank == null) return;

                if (request.Action == RaidsActionContractRequest.ContractActions.GetStatus)
                {
                    // status covers all saved rewards and the host's boards without changing selection
                    var inHub = metadata?.CustomMapConfig?.Filename == HUB_MAP_FILENAME;
                    var now = DateTime.UtcNow;
                    ContractReply(client, request, writer =>
                    {
                        writer.Write(request.RequestId);
                        writer.Write(inHub ? (uint)bank.ContractStore.Receipts.Values.Count(r => !r.Claimed) : 0u);
                        writer.Write(3u); // status response, followed by the expired offer count
                        writer.Write(inHub ? (uint)hostBank.ContractStore.Boards.Values
                            .Where(b => hostBank.MapStats.ContainsKey(b.MapFilename))
                            .Sum(b => b.Contracts.Count(c => !c.Completed && c.ExpiresUtc <= now)) : 0u);
                    });
                }
                else if (request.Action == RaidsActionContractRequest.ContractActions.ProcessAll)
                {
                    // clank collects saved rewards across every map and previous host
                    uint result = 0;
                    var inHub = metadata?.CustomMapConfig?.Filename == HUB_MAP_FILENAME;
                    if (inHub)
                    {
                        result = 1;
                        foreach (var receipt in bank.ContractStore.Receipts.Values.Where(r => !r.Claimed).ToList())
                        {
                            ClaimContractForParty(client, host, bank, hostBank, receipt);
                            result |= 4; // claimed rewards, including rewards capped by class/hero level
                        }

                        // refresh only unfinished expired offers on the shared host boards
                        var now = DateTime.UtcNow;
                        var levels = ContractLevels(host);
                        var hostLevel = RaidsAccount.GetAccountLevelFromXp(hostBank.Account.Experience) + 1;
                        foreach (var board in hostBank.ContractStore.Boards.Values)
                        {
                            if (!hostBank.MapStats.TryGetValue(board.MapFilename, out var map)) continue;
                            foreach (var contract in board.Contracts.Where(c => !c.Completed && c.ExpiresUtc <= now).ToList())
                            {
                                hostBank.ContractStore.Replace(board, contract.Uid, (uint)host.AccountId, map.ContractRules,
                                    levels, now, hostLevel);
                                if (!board.Contracts.Any(c => c.Uid == contract.Uid)) result |= 8;
                            }
                        }

                        Player.SavePlayerMetadata(client);
                        if (host != client) Player.SavePlayerMetadata(host);
                    }

                    // send result and updated account to client
                    ContractReply(client, request, writer =>
                    {
                        writer.Write(request.RequestId);
                        writer.Write(request.ContractUid);
                        writer.Write(result);
                        bank.Account.Serialize(writer);
                    });
                }
                else if (request.Action == RaidsActionContractRequest.ContractActions.DismissExpired)
                {
                    // only unfinished offers past their server deadline can be dismissed
                    var now = DateTime.UtcNow;
                    var dismissed = false;
                    var board = hostBank.ContractStore.Boards.Values.FirstOrDefault(b => b.Contracts.Any(c => c.Uid == request.ContractUid));
                    var contract = board?.Contracts.FirstOrDefault(c => c.Uid == request.ContractUid);
                    if (contract?.IsExpired(now) == true && hostBank.MapStats.TryGetValue(board.MapFilename, out var map))
                    {
                        dismissed = hostBank.ContractStore.Replace(board, contract.Uid, (uint)host.AccountId, map.ContractRules,
                            ContractLevels(host), now, RaidsAccount.GetAccountLevelFromXp(hostBank.Account.Experience) + 1);

                        // save
                        if (dismissed) Player.SavePlayerMetadata(host);
                    }

                    // send result and account to client
                    ContractReply(client, request, writer =>
                    {
                        writer.Write(request.RequestId);
                        writer.Write(request.ContractUid);
                        writer.Write(dismissed ? 1u : 0u);
                        bank.Account.Serialize(writer);
                    });
                }
                else if (request.Action == RaidsActionContractRequest.ContractActions.ClaimPartyRewards)
                {
                    // each member saves its local account before collecting the marked receipts
                    // a retry cannot collect rewards from a later batch
                    if (request.ContractUid != 0 && request.ContractUid == bank.ContractStore.PartyClaimUid)
                    {
                        foreach (var receipt in bank.ContractStore.Receipts.Values.Where(r => r.PartyClaimPending && !r.Claimed).ToList())
                            ClaimContractReceipt(bank, receipt);
                        bank.ContractStore.PartyClaimUid = 0;
                    }

                    Player.SavePlayerMetadata(client);
                    ContractReply(client, request, writer =>
                    {
                        writer.Write(request.RequestId);
                        writer.Write(request.ContractUid);
                        writer.Write(1u);
                        bank.Account.Serialize(writer);
                    });
                }
                else if (request.Action == RaidsActionContractRequest.ContractActions.Complete)
                {
                    // claim only rewards recorded for this client
                    var receipt = bank.ContractStore.FindReceipt(request.OwnerAccountId, request.ContractUid);
                    if (receipt != null && !receipt.Claimed)
                    {
                        ClaimContractForParty(client, host, bank, hostBank, receipt);

                        // save
                        Player.SavePlayerMetadata(client);
                    }

                    // send result and updated account to client
                    // a repeated claim returns success without awarding the reward again
                    ContractReply(client, request, writer =>
                    {
                        writer.Write(request.RequestId);
                        writer.Write(request.ContractUid);
                        writer.Write(receipt?.Claimed == true
                            || bank.ContractStore.WasClaimed(request.OwnerAccountId, request.ContractUid) ? 1u : 0u);
                        bank.Account.Serialize(writer);
                    });
                }
            }
        }

        public void OnClientUpdateMapContractRules(RaidsUpdateMapContractRulesRequest request, ClientObject client, GameMetadata metadata)
        {
            if (client.CurrentGame?.Host == null
                || string.IsNullOrWhiteSpace(request.MapFilename)
                || request.MissionType > (ushort)RaidsMissionType.Minigame)
                return;

            lock (_contractLock)
            {
                var host = client.CurrentGame.Host;
                var bank = GetBank(host);
                var key = (host.AccountId, request.MapFilename, client.AccountId);

                // validate this upload page before changing the accumulated rules
                if (request.TotalCount > 16
                    || request.Count > 7
                    || request.Offset > request.TotalCount
                    || request.Count > request.TotalCount - request.Offset
                    || request.ContractRules.Count != request.Count
                    || (request.Count == 0 && request.TotalCount != 0)
                    || request.ContractRules.Any(r => !r.IsValid()))
                    return;

                // the first page starts a new upload for this host, map and sender
                if (request.Offset == 0)
                    _contractUploads[key] = (request.TotalCount, new());

                // accept only the next page of the same upload
                if (!_contractUploads.TryGetValue(key, out var upload)
                    || upload.Expected != request.TotalCount
                    || upload.Rules.Count != request.Offset)
                    return;

                upload.Rules.AddRange(request.ContractRules);
                if (upload.Rules.Count != upload.Expected) return;

                _contractUploads.Remove(key);

                // validate the complete table before replacing the saved rules
                // clear contracts require a raid map with at most one clear rule
                if ((request.MissionType != 2 && upload.Rules.Any(r => r.Type == RaidsContractType.Clear))
                    || upload.Rules.Count(r => r.Type == RaidsContractType.Clear) > 1)
                    return;

                // update the host's map metadata
                if (!bank.MapStats.TryGetValue(request.MapFilename, out var stats))
                    bank.MapStats[request.MapFilename] = stats = new RaidsMapStats();

                stats.Name = request.MapName;
                stats.ContractRules = upload.Rules;
                stats.MissionType = (RaidsMissionType)request.MissionType;

                // save
                Player.SavePlayerMetadata(host);
            }
        }

        private static void ClaimContractForParty(ClientObject client, ClientObject host, RaidsBank bank,
            RaidsBank hostBank, RaidsContractReceipt receipt)
        {
            // mark eligible party receipts before replacing the shared offer
            foreach (var member in GetParty(client))
            {
                if (member == client) continue;
                var memberBank = GetBank(member);
                var memberReceipt = memberBank?.ContractStore.FindReceipt(receipt.Contract.OwnerAccountId, receipt.Contract.Uid);
                if (memberReceipt == null || memberReceipt.Claimed) continue;

                memberReceipt.PartyClaimPending = true;
                NotifyPartyContractClaim(member, new RaidsActionContractRequest());
            }

            ClaimContractReceipt(bank, receipt);

            // replace the claimed offer if it belongs to the current host's board
            if (receipt.Contract.OwnerAccountId == host.AccountId
                && hostBank.ContractStore.Boards.TryGetValue(receipt.Contract.MapFilename, out var board)
                && hostBank.MapStats.TryGetValue(board.MapFilename, out var map))
            {
                hostBank.ContractStore.Replace(board, receipt.Contract.Uid, (uint)host.AccountId, map.ContractRules,
                    ContractLevels(host), DateTime.UtcNow, RaidsAccount.GetAccountLevelFromXp(hostBank.Account.Experience) + 1);
                if (host != client)
                    Player.SavePlayerMetadata(host);
            }
        }

        private static void ClaimContractReceipt(RaidsBank bank, RaidsContractReceipt receipt)
        {
            // apply account and weapon rewards once per receipt
            var payout = receipt.Contract.Payout;
            bank.Account.Bolts = RaidsContractReward.Saturate((ulong)bank.Account.Bolts + payout.Bolts);
            bank.Account.AddXp(payout.PlayerXp);
            bank.Account.AddWeaponExperience(payout.WeaponXp);

            // class xp belongs to the class equipped at completion
            if (payout.ClassXp > 0 && RaidsClasses.Definitions.Any(c => (int)c.ClassType == receipt.ClassType))
            {
                bank.Account.Dreadpoints += Math.Min((ulong)payout.ClassXp, ulong.MaxValue - bank.Account.Dreadpoints);
                var type = (RaidsClassType)receipt.ClassType;
                if (!bank.Account.ClassStats.TryGetValue(type, out var stats))
                    bank.Account.ClassStats[type] = stats = new RaidsClassStats();

                var definition = RaidsClasses.Definitions.First(c => c.ClassType == type);
                stats.Experience = Math.Min(stats.Experience + payout.ClassXp, definition.GetClassXpFromLevel(MAX_CLASS_LEVEL));
            }

            receipt.Claimed = true;
            bank.ContractStore.RecordClaim(receipt);
        }

        private static void NotifyPartyContractClaim(ClientObject client, BasePluginMessage request)
        {
            var store = GetBank(client).ContractStore;
            if (store.PartyClaimUid == 0)
                store.PartyClaimUid = store.AllocateUid();
            Player.SavePlayerMetadata(client);

            // result 2 asks the member to save its account and collect this batch
            ContractReply(client, request, writer =>
            {
                writer.Write(0u);
                writer.Write(store.PartyClaimUid);
                writer.Write(2u);
                writer.Write(new byte[RaidsAccount.SERIALIZED_SIZE]);
            });
        }

        private static int[] ContractLevels(ClientObject client) =>
            GetParty(client)
                .Select(c => RaidsAccount.GetAccountLevelFromXp(GetBank(c).Account.Experience) + 1)
                .ToArray();

        private static void ContractReply(ClientObject client, BasePluginMessage request, Action<BinaryWriter> write)
        {
            // serialize within the lobby message limit
            using var stream = new MemoryStream(504);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                write(writer);

            if (stream.Length > 504)
                throw new InvalidDataException("Contract payload exceeded 504 bytes.");

            // send to client
            client.Queue(new RaidsContractResponse { ResponseId = request.CustomMsgId, Payload = stream.ToArray() });
        }

        private void SendContractPage(ClientObject client, RaidsGetContractsRequest request)
        {
            var owner = client.CurrentGame?.Host;
            var bank = GetBank(client);
            var ownerBank = owner == null ? null : GetBank(owner);
            if (bank == null || ownerBank == null || request.Difficulty >= 5) return;

            // pending rewards belong to the client; map boards belong to the host
            var pending = request.Flags.HasFlag(RaidsContractRequestFlags.Pending);
            var now = DateTime.UtcNow;
            var receipts = bank.ContractStore.Receipts.Values
                .Where(r => !r.Claimed)
                .OrderBy(r => r.Contract.OwnerAccountId)
                .ThenBy(r => r.Contract.Uid)
                .ToList();
            var supportsContracts = ownerBank.MapStats.TryGetValue(request.MapFilename, out var stats)
                && stats.ContractRules.Count > 0;
            var board = pending || !supportsContracts ? null : ownerBank.ContractStore.Boards.GetValueOrDefault(request.MapFilename);
            var contracts = pending
                ? receipts.Skip(request.PendingOffset).Take(3).Select(r => r.Contract).ToList()
                : board?.Contracts ?? new List<RaidsContract>();

            // send to client
            ContractReply(client, request, writer =>
            {
                // page header
                writer.Write(board?.BoardId ?? 0);
                writer.Write((uint)(owner.AccountId));
                writer.Write(board?.Sequence ?? 0);
                writer.Write(request.RequestId);
                writer.Write((ushort)Math.Min(ushort.MaxValue, receipts.Count));
                writer.Write(request.Difficulty);
                writer.Write((byte)contracts.Count);
                writer.Write(pending ? "" : request.MapFilename, 64);
                writer.Write(pending ? "Unclaimed rewards" : board?.MapName ?? request.MapFilename, 32);

                // contract rewards and claim status for this client
                foreach (var contract in contracts)
                    contract.Write(writer, request.Difficulty, now,
                        bank.ContractStore.FindReceipt(contract.OwnerAccountId, contract.Uid),
                        bank.ContractStore.WasClaimed(contract.OwnerAccountId, contract.Uid),
                        pending ? $"{contract.MapName}: {contract.Name}" : null, (int)request.PartySize);

                // pad unused contract slots
                writer.Write(new byte[(3 - contracts.Count) * RaidsContract.SERIALIZED_SIZE]);
            });
        }

        private void SendContractProgress(ClientObject client, RaidsUpdateContractStatsRequest request, RaidsContractBoard board)
        {
            var bank = GetBank(client);

            // send to client
            ContractReply(client, request, writer =>
            {
                // board header
                writer.Write(board.BoardId);
                writer.Write((uint)client.CurrentGame.Host.AccountId);
                writer.Write(board.Sequence);
                writer.Write((uint)board.Contracts.Count);

                // progress is shared, but claim status belongs to each client
                foreach (var contract in board.Contracts)
                {
                    var receipt = bank.ContractStore.FindReceipt(contract.OwnerAccountId, contract.Uid);
                    writer.Write(contract.Uid);

                    uint progress = 0;
                    if (contract.Type == RaidsContractType.Kill || contract.Type == RaidsContractType.Task)
                        progress = contract.Count;
                    else if (contract.Type == RaidsContractType.Clear)
                        progress = contract.CompletedTimeMs;

                    writer.Write(progress);

                    var flags = contract.Flags(DateTime.UtcNow);
                    if (receipt != null)
                        flags |= RaidsContractFlags.Claimable;
                    else if (bank.ContractStore.WasClaimed(contract.OwnerAccountId, contract.Uid))
                        flags |= RaidsContractFlags.Claimed;

                    writer.Write((byte)flags);
                    writer.Write((sbyte)contract.CompletedDifficulty);
                    writer.Write((byte)contract.CompletedPartySize);
                    writer.Write((byte)0); // natural alignment
                }

                // pad unused progress slots
                writer.Write(new byte[(3 - board.Contracts.Count) * 12]);
            });
        }
    }
}
