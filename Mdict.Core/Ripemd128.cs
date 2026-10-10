using System;

namespace Mdict.Core
{
// A small standalone RIPEMD-128 implementation for MDict 2.0 Key Info.
// The constants, rounds and public test vectors follow the RIPEMD-128 specification.
// This is a fresh C# implementation, not a translation of mdict-utils/readmdict.py.
internal static class Ripemd128
{
    private static readonly int[] LeftWords = {
        0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,
        7,4,13,1,10,6,15,3,12,0,9,5,2,14,11,8,
        3,10,14,4,9,15,8,1,2,7,0,6,13,11,5,12,
        1,9,11,10,0,8,12,4,13,3,7,15,14,5,6,2
    };
    private static readonly int[] RightWords = {
        5,14,7,0,9,2,11,4,13,6,15,8,1,10,3,12,
        6,11,3,7,0,13,5,10,14,15,8,12,4,9,1,2,
        15,5,1,3,7,14,6,9,11,8,12,2,10,0,4,13,
        8,6,4,1,3,11,15,0,5,12,2,13,9,7,10,14
    };
    private static readonly int[] LeftRotations = {
        11,14,15,12,5,8,7,9,11,13,14,15,6,7,9,8,
        7,6,8,13,11,9,7,15,7,12,15,9,11,7,13,12,
        11,13,6,7,14,9,13,15,14,8,13,6,5,12,7,5,
        11,12,14,15,14,15,9,8,9,14,5,6,8,6,5,12
    };
    private static readonly int[] RightRotations = {
        8,9,9,11,13,15,15,5,7,7,8,11,14,14,12,6,
        9,13,15,7,12,8,9,11,7,7,12,7,6,15,13,11,
        9,7,15,11,8,6,6,14,12,13,5,14,13,13,7,5,
        15,5,8,11,14,14,6,14,6,9,12,9,12,5,15,8
    };
    private static readonly uint[] LeftConstants = {
        0x00000000u, 0x5A827999u, 0x6ED9EBA1u, 0x8F1BBCDCu
    };
    private static readonly uint[] RightConstants = {
        0x50A28BE6u, 0x5C4DD124u, 0x6D703EF3u, 0x00000000u
    };

    public static byte[] Hash(byte[] input)
    {
        // Padding: 0x80, zeros, 64-bit little-endian original bit length.
        long paddedLength = ((long)input.Length + 8) / 64 + 1;
        paddedLength *= 64;
        if (paddedLength > int.MaxValue)
            throw new ArgumentException("RIPEMD-128 input too large.");
        byte[] data = new byte[(int)paddedLength];
        Buffer.BlockCopy(input, 0, data, 0, input.Length);
        data[input.Length] = 0x80;
        ulong bitLength = (ulong)input.Length * 8;
        for (int i = 0; i < 8; i++)
            data[data.Length - 8 + i] = (byte)(bitLength >> (i * 8));

        uint h0 = 0x67452301u;
        uint h1 = 0xEFCDAB89u;
        uint h2 = 0x98BADCFEu;
        uint h3 = 0x10325476u;

        unchecked
        {
            for (int offset = 0; offset < data.Length; offset += 64)
            {
                uint[] words = new uint[16];
                for (int i = 0; i < 16; i++)
                {
                    int p = offset + i * 4;
                    words[i] = (uint)data[p] | ((uint)data[p + 1] << 8) |
                               ((uint)data[p + 2] << 16) | ((uint)data[p + 3] << 24);
                }

                uint a = h0, b = h1, c = h2, d = h3;
                uint aa = h0, bb = h1, cc = h2, dd = h3;
                for (int i = 0; i < 64; i++)
                {
                    int round = i / 16;
                    uint value = Rotate(a + Function(round, b, c, d) +
                                        words[LeftWords[i]] + LeftConstants[round], LeftRotations[i]);
                    a = d; d = c; c = b; b = value;

                    uint rightValue = Rotate(aa + Function(3 - round, bb, cc, dd) +
                                             words[RightWords[i]] + RightConstants[round], RightRotations[i]);
                    aa = dd; dd = cc; cc = bb; bb = rightValue;
                }

                uint combined = h1 + c + dd;
                h1 = h2 + d + aa;
                h2 = h3 + a + bb;
                h3 = h0 + b + cc;
                h0 = combined;
            }
        }
        byte[] result = new byte[16];
        WriteUInt32LE(result, 0, h0);
        WriteUInt32LE(result, 4, h1);
        WriteUInt32LE(result, 8, h2);
        WriteUInt32LE(result, 12, h3);
        return result;
    }

    private static uint Function(int round, uint x, uint y, uint z)
    {
        if (round == 0) return x ^ y ^ z;
        if (round == 1) return (x & y) | (~x & z);
        if (round == 2) return (x | ~y) ^ z;
        return (x & z) | (y & ~z);
    }

    private static uint Rotate(uint value, int bits)
    {
        return (value << bits) | (value >> (32 - bits));
    }

    private static void WriteUInt32LE(byte[] output, int start, uint value)
    {
        for (int i = 0; i < 4; i++)
            output[start + i] = (byte)(value >> (i * 8));
    }
}
}
