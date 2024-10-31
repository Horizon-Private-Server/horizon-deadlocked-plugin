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
    public class RaidsUpdateBankAccountRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 54;
        public override bool SkipEncryption { get => true; set { } }

        public RaidsAccount Account { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            Account = new RaidsAccount();

            base.Deserialize(reader);
            Account.Deserialize(reader);
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            throw new NotImplementedException();
        }
    }
}
