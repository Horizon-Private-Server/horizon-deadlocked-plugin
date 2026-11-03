using Horizon.Plugin.Deadlocked.CustomModes;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.IO;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsUpdateMapContractRulesRequest : BasePluginMessage
    {
        public const int MAX_CONTRACTS = 16;
        public const int MAX_RULES_PER_PAGE = 5;

        public override byte CustomMsgId => 68;
        public override bool SkipEncryption { get => true; set { } }

        public string MapFilename { get; set; }
        public string MapName { get; set; }
        public ushort Offset { get; set; }
        public ushort TotalCount { get; set; }
        public ushort Count { get; set; }
        public ushort MissionType { get; set; }
        public List<RaidsContractRule> ContractRules { get; set; } = new List<RaidsContractRule>();

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            var start = reader.BaseStream.Position;
            MapFilename = reader.ReadString(64);
            MapName = reader.ReadString(32);
            Offset = reader.ReadUInt16();
            TotalCount = reader.ReadUInt16();
            Count = reader.ReadUInt16();
            MissionType = reader.ReadUInt16();
            if (TotalCount > MAX_CONTRACTS || Count > MAX_RULES_PER_PAGE || Offset > TotalCount || Count > TotalCount - Offset)
                throw new InvalidDataException("Invalid contract rule page.");

            for (int i = 0; i < Count; ++i)
            {
                var rule = new RaidsContractRule();
                rule.Deserialize(reader);
                ContractRules.Add(rule);
            }

            if (reader.BaseStream.Position - start > 504)
                throw new InvalidDataException("Contract rule page exceeds its payload budget.");
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
