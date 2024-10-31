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
    public class RaidsUpdateBankInventoryRequest : BasePluginMessage
    {
        public override byte CustomMsgId => 52;
        public override bool SkipEncryption { get => true; set { } }

        public int Index { get; set; }
        public int Count { get; set; }
        public RaidsInventoryWeapon[] Weapons { get; set; }
        public sbyte[] EquippedWeaponIdxs { get; set; }

        public override void Deserialize(MessageReader reader)
        {
            Weapons = new RaidsInventoryWeapon[16];
            EquippedWeaponIdxs = new sbyte[8];

            base.Deserialize(reader);

            Index = reader.ReadInt32();
            Count = reader.ReadInt32();
            for (int i = 0; i < Weapons.Length; i++)
            {
                Weapons[i] = new RaidsInventoryWeapon();
                Weapons[i].Deserialize(reader);
            }
            for (int i = 0; i < EquippedWeaponIdxs.Length; i++)
            {
                EquippedWeaponIdxs[i] = reader.ReadSByte();
            }
        }

        public override void Serialize(MessageWriter writer)
        {
            base.Serialize(writer);

            throw new NotImplementedException();
        }
    }
}
