using Server.Medius.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public class CompChannel : Channel
    {
        public CompChannel() : base()
        {

        }

        public CompChannel(int appId) : base()
        {
            ApplicationId = appId;
            Name = this.Id.ToString();
            Password = null;
            SecurityLevel = RT.Common.MediusWorldSecurityLevelType.WORLD_SECURITY_NONE;
            MaxPlayers = 10;
            GenericField1 = 8;
            GenericField2 = 0;
            GenericField3 = 0;
            GenericField4 = 0x1AF5F0;
            GenericFieldLevel = RT.Common.MediusWorldGenericFieldLevelType.MediusWorldGenericFieldLevel0;
        }
    }
}
