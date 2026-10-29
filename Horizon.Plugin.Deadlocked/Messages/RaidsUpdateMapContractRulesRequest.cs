using Horizon.Plugin.Deadlocked.CustomModes;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsUpdateMapContractRulesRequest : BasePluginMessage
    {
        public const int MAX_CONTRACTS = 16;
        public const int RULES_PER_PAGE = 8;

        public override byte CustomMsgId => 68;
        public override bool SkipEncryption { get => true; set { } }

        public string MapFilename { get; set; }
        public int Offset { get; set; }
        public int Count { get; set; }
        public int TotalCount { get; set; }
        public List<RaidsContractRule> ContractRules { get; set; } = new List<RaidsContractRule>();

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            MapFilename = reader.ReadString(64);
            Offset = reader.ReadInt32();
            Count = reader.ReadInt32();
            TotalCount = reader.ReadInt32();
            for (int i = 0; i < RULES_PER_PAGE; ++i)
            {
                var contractRule = new RaidsContractRule();
                contractRule.Deserialize(reader);
                if (i < Count)
                    ContractRules.Add(contractRule);
            }
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
