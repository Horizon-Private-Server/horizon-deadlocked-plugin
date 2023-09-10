using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Server.Common;
using Server.Common.Stream;

namespace Horizon.Plugin.Deadlocked
{
    public static class Helpers
    {

        #region BinaryReader

        public static T[] ReadArray<T>(this MessageReader reader, int count)
        {
            T[] results = new T[count];
            for (int i = 0; i < count; ++i)
                results[i] = reader.Read<T>();
            return results;
        }

        #endregion

    }
}
