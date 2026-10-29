using Horizon.Plugin.Deadlocked.CustomModes;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Common.Stream;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Plugin.Deadlocked.Messages
{
    public class RaidsResetAccountRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 63;
        public override bool SkipEncryption { get => true; set { } }

        public override void Deserialize(MessageReader reader)
        {
            base.Deserialize(reader);
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
