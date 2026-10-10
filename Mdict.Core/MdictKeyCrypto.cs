using System;

namespace Mdict.Core
{
// MDict v2 "Encrypted=2" affects the Key Block Info payload, not all MDX bytes.
internal static class MdictKeyCrypto
{
    public static byte[] Decrypt(byte[] encrypted, byte[] checksumBytes)
    {
        if (checksumBytes.Length != 4)
            throw new ArgumentException("Expected the four checksum bytes.");
        byte[] seed = new byte[8];
        Buffer.BlockCopy(checksumBytes, 0, seed, 0, 4);
        seed[4] = 0x95;
        seed[5] = 0x36;
        // seed[6..7] are zero (little-endian 0x3695).
        byte[] key = Ripemd128.Hash(seed);

        byte[] output = new byte[encrypted.Length];
        int previous = 0x36;
        for (int i = 0; i < encrypted.Length; i++)
        {
            int current = encrypted[i];
            int rotated = ((current >> 4) | (current << 4)) & 0xff;
            output[i] = (byte)(rotated ^ previous ^ (i & 0xff) ^ key[i % key.Length]);
            previous = current;
        }
        return output;
    }
}
}
