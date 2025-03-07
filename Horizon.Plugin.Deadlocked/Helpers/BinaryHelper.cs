using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class BinaryHelper
{
    public static int CountBits(uint value)
    {
        int count = 0;
        for (int i = 0; i < 32; ++i)
            count += ((value & (1 << i)) != 0) ? 1 : 0;
        return count;
    }
    public static void Write(this BinaryWriter writer, string str, int length)
    {
        if (str == null)
            writer.Write(new byte[length]);
        else if (str.Length >= length)
            writer.Write(Encoding.UTF8.GetBytes(str.Substring(0, length - 1) + "\0"));
        else
            writer.Write(Encoding.UTF8.GetBytes(str.PadRight(length, '\0')));
    }
}
