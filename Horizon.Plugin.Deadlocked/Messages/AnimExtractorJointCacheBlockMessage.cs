using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    class AnimExtractorJointCacheBlockMessage : BasePluginMessage
    {
        public override byte CustomMsgId => 100;
        public override bool SkipEncryption { get => true; set { } }

        public int Offset { get; set; }
        public short OClass { get; set; }
        public short SeqId { get; set; }
        public float Time { get; set; }
        public float Scale { get; set; }
        public sbyte JointCount { get; set; }
        public bool IsEnd { get; set; }
        public float[] Data { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);

            Offset = reader.ReadInt32();
            OClass = reader.ReadInt16();
            SeqId = reader.ReadInt16();
            Time = reader.ReadSingle();
            Scale = reader.ReadSingle();
            JointCount = reader.ReadSByte();
            IsEnd = reader.ReadBoolean();
            Data = new float[16 * 7];
            for (int i = 0; i < Data.Length; ++i)
                Data[i] = reader.ReadSingle();
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

        }
    }
}
