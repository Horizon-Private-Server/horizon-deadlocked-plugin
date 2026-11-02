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
            Account = null;
            base.Deserialize(reader);
            if (reader.BaseStream.Length - reader.BaseStream.Position != RaidsAccount.SERIALIZED_SIZE) return;

            var account = new RaidsAccount();
            account.Deserialize(reader);
            if (!double.IsFinite(account.WeaponExperience) || account.WeaponExperience < 0) return;
            Account = account;
        }

        protected override void SerializePayload(MessageWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
