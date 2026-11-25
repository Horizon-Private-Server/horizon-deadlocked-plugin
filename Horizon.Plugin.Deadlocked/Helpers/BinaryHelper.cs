using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class BinaryHelper
{
    // PS2 masks are little-endian, with the low 64 bits first.
    public static void WriteUInt128(this BinaryWriter writer, UInt128 value)
    {
        writer.Write((ulong)value);
        writer.Write((ulong)(value >> 64));
    }

    public static UInt128 ReadUInt128(this BinaryReader reader)
    {
        var low = reader.ReadUInt64();
        var high = reader.ReadUInt64();
        return ((UInt128)high << 64) | low;
    }

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
            var bytes = Encoding.UTF8.GetBytes(str ?? string.Empty);
            var count = Math.Max(0, Math.Min(bytes.Length, length.Value - 1));
            writer.Write(bytes, 0, count);
            writer.Write(new byte[length.Value - count]);
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
