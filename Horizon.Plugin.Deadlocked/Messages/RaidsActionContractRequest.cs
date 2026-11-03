using Server.Common.Stream;
using System;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsActionContractRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 65;
        public override bool SkipEncryption { get => true; set { } }

        public uint ContractUid { get; set; }
        public uint OwnerAccountId { get; set; }
        public uint RequestId { get; set; }
        public ContractActions Action { get; set; }

        public enum ContractActions
        {
            Reroll = 0,
            Complete,
            Activate,
            DismissExpired,
            ClaimPartyRewards
        }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            ContractUid = reader.ReadUInt32();
            OwnerAccountId = reader.ReadUInt32();
            RequestId = reader.ReadUInt32();
            Action = (ContractActions)reader.ReadUInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
