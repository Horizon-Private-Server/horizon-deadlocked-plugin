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

    public static void Write(this BinaryWriter writer, string str, int? length)
    {
        if (length == null)
        {
            if (str != null)
                writer.Write(Encoding.UTF8.GetBytes(str));

            writer.Write(new byte[1]);
        }
        else
        {
            if (str == null)
                writer.Write(new byte[length.Value]);
            else if (str.Length >= length)
                writer.Write(Encoding.UTF8.GetBytes(str.Substring(0, length.Value - 1) + "\0"));
            else
                writer.Write(Encoding.UTF8.GetBytes(str.PadRight(length.Value, '\0')));
        }
    }


    public static string ReadCString(this BinaryReader reader)
    {
        var pos = reader.BaseStream.Position;
        while (reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadByte() != 0)
            ;

        var len = (int)(reader.BaseStream.Position - pos);
        reader.BaseStream.Position = pos;
        var str = Encoding.UTF8.GetString(reader.ReadBytes(len-1));
        reader.ReadByte(); // skip null byte
        return str;
    }

    public static string ReadString(this BinaryReader reader, int fixedLength)
    {
        var bytes = reader.ReadBytes(fixedLength);
        var str = Encoding.UTF8.GetString(bytes);
        var endCharIdx = str.IndexOf('\0');
        if (endCharIdx >= 0)
            return str.Substring(0, endCharIdx);

        return str;
    }
}
