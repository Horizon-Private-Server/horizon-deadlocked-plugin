using Horizon.Plugin.Deadlocked.CustomModes;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;

namespace Horizon.Plugin.Deadlocked.Messages
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

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);
            writer.Write((int)ClassType);
            writer.Write(SkillId);
        }
    }
}
