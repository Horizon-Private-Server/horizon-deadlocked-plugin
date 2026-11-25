using Horizon.Plugin.Deadlocked.Messages;
using Horizon.Plugin.Deadlocked.CustomModes.Raids.Models;
using Horizon.Plugin.Deadlocked.CustomModes.Raids;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Messages
{
    public class RaidsResetClassSkillsRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 76;
        public override bool SkipEncryption { get => true; set { } }

        public RaidsClassType ClassType { get; set; }
        public int SkillId { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
            ClassType = (RaidsClassType)reader.ReadInt32();
            SkillId = reader.ReadInt32();
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            writer.Write((int)ClassType);
            writer.Write(SkillId);
        }
    }
}
