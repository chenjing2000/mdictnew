using System;
using System.IO;

namespace Mdict.Core
{
// Big-endian numbers used by MDict 2.0. All file positions and sizes use long.
internal static class MdictBinary
{
    public static long ReadInt64(BinaryReader reader)
    {
        byte[] bytes = ReadBytes(reader, 8);
        ulong value = 0;
        for (int i = 0; i < 8; i++)
        {
            value = (value << 8) | bytes[i];
        }
        if (value > long.MaxValue)
            throw new InvalidDataException("MDict number is too large for Int64.");
        return (long)value;
    }

    public static int ReadUInt16(BinaryReader reader)
    {
        byte[] bytes = ReadBytes(reader, 2);
        return (bytes[0] << 8) | bytes[1];
    }

    public static uint ReadUInt32(BinaryReader reader)
    {
        byte[] bytes = ReadBytes(reader, 4);
        return ToUInt32(bytes, 0);
    }

    public static uint ToUInt32(byte[] data, int offset)
    {
        if (offset < 0 || data.Length - offset < 4)
            throw new InvalidDataException("Missing 4-byte big-endian number.");
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    public static byte[] ReadBytes(BinaryReader reader, int length)
    {
        if (length < 0)
            throw new InvalidDataException("Negative byte length.");
        byte[] data = reader.ReadBytes(length);
        if (data.Length != length)
            throw new InvalidDataException("Unexpected end of MDict file.");
        return data;
    }

    public static int ToArrayLength(long length, int maximum, string description)
    {
        if (length < 0 || length > maximum || length > int.MaxValue)
            throw new InvalidDataException(description + " size is outside the safety limit: " + length);
        return (int)length;
    }

    public static void CheckRange(long offset, long length, long fileLength, string description)
    {
        if (offset < 0 || length < 0 || offset > fileLength || length > fileLength - offset)
            throw new InvalidDataException(description + " exceeds the file boundaries.");
    }
}
}
