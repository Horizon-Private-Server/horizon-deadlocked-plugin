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
        const int MAX_CONTRACTS = 16;

        public override byte CustomMsgId => 68;
        public override bool SkipEncryption { get => true; set { } }

        public string MapFilename { get; set; }
        public List<RaidsContractRule> ContractRules { get; set; } = new List<RaidsContractRule>();

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            MapFilename = reader.ReadString(64);
            for (int i = 0; i < MAX_CONTRACTS; ++i)
            {
                var contractRule = new RaidsContractRule();
                contractRule.Deserialize(reader);
                if (contractRule.OClass > 0)
                    ContractRules.Add(contractRule);
            }
        }

        public override void Serialize(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
