using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Mdict.Core
{
// v0.5: known Oxford MDX UTF-8 and MDD UTF-16LE key index formats.
// This is not a universal MDict reader.
public class OxfordKeyReader
{
    private const int MaxKeyInfoCompressed = 64 * 1024 * 1024;
    private const int MaxKeyInfoDecompressed = 64 * 1024 * 1024;
    private const int MaxKeyBlockCompressed = 64 * 1024 * 1024;
    private const int MaxKeyBlockDecompressed = 64 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly UnicodeEncoding StrictUtf16 = new UnicodeEncoding(false, false, true);

    public KeySectionInfo Read(MdictHeader header, bool readAllKeys = false)
    {
        bool isMdd = Path.GetExtension(header.FilePath).Equals(".mdd", StringComparison.OrdinalIgnoreCase);
        if (!isMdd && !Path.GetExtension(header.FilePath).Equals(".mdx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Key Reader requires MDX or MDD.");
        if (header.GetAttribute("GeneratedByEngineVersion") != "2.0")
            throw new NotSupportedException("Only EngineVersion 2.0 is supported.");
        if (isMdd && header.RootName != "Library_Data")
            throw new InvalidDataException("MDD root must be Library_Data.");
        if (!isMdd && (header.RootName != "Dictionary" ||
            !header.GetAttribute("Encoding").Equals("UTF-8", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("This Oxford MDX requires UTF-8 keys.");
        // MDD keys are UTF-16LE, even if the header says otherwise.

        string encryption = header.GetAttribute("Encrypted");
        int encryptionFlag;
        if (encryption.Equals("No", StringComparison.OrdinalIgnoreCase) ||
            encryption == "(not specified)")
            encryptionFlag = 0;
        else if (encryption.Equals("Yes", StringComparison.OrdinalIgnoreCase))
            encryptionFlag = 1;
        else if (!int.TryParse(encryption, out encryptionFlag))
            throw new NotSupportedException("Unknown encryption flags: " + encryption);
        if (encryptionFlag != 0 && encryptionFlag != 2)
            throw new NotSupportedException("Only Encrypted=0/2 is supported; found " + encryptionFlag);

        using (FileStream stream = new FileStream(header.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            // v2.0 preamble = 5 big-endian uint64 values + 4-byte big-endian Adler32.
            MdictBinary.CheckRange(header.KeySectionOffset, 44, stream.Length, "Key preamble");
            stream.Position = header.KeySectionOffset;
            byte[] preamble = MdictBinary.ReadBytes(reader, 40);
            uint storedPreambleChecksum = MdictBinary.ReadUInt32(reader);
            uint computedPreambleChecksum = OxfordHeaderReader.CalculateAdler32(preamble);
            if (storedPreambleChecksum != computedPreambleChecksum)
                throw new InvalidDataException("Key preamble Adler-32 mismatch.");

            using (BinaryReader numbers = new BinaryReader(new MemoryStream(preamble)))
            {
                KeySectionInfo section = new KeySectionInfo();
                section.BlockCount = MdictBinary.ReadInt64(numbers);
                section.EntryCount = MdictBinary.ReadInt64(numbers);
                section.KeyInfoDecompressedSize = MdictBinary.ReadInt64(numbers);
                section.KeyInfoCompressedSize = MdictBinary.ReadInt64(numbers);
                section.KeyBlocksTotalSize = MdictBinary.ReadInt64(numbers);
                section.StoredPreambleChecksum = storedPreambleChecksum;
                section.ComputedPreambleChecksum = computedPreambleChecksum;
                section.KeyInfoEncrypted = encryptionFlag == 2;
                section.KeyInfoFileOffset = stream.Position;

                if (section.BlockCount <= 0 || section.BlockCount > 1_000_000 ||
                    section.EntryCount <= 0 || section.EntryCount > 20_000_000)
                    throw new InvalidDataException("Unreasonable Key Section block/entry count.");
                int packedLength = MdictBinary.ToArrayLength(section.KeyInfoCompressedSize,
                    MaxKeyInfoCompressed, "Key Block Info (compressed)");
                int unpackedLength = MdictBinary.ToArrayLength(section.KeyInfoDecompressedSize,
                    MaxKeyInfoDecompressed, "Key Block Info (decompressed)");
                if (packedLength < 8 || unpackedLength < 1)
                    throw new InvalidDataException("Invalid Key Block Info lengths.");

                MdictBinary.CheckRange(section.KeyInfoFileOffset,
                    section.KeyInfoCompressedSize, stream.Length, "Key Block Info");
                section.KeyBlocksFileOffset = checked(section.KeyInfoFileOffset + section.KeyInfoCompressedSize);
                MdictBinary.CheckRange(section.KeyBlocksFileOffset,
                    section.KeyBlocksTotalSize, stream.Length, "Key Blocks");
                section.RecordSectionOffset = checked(section.KeyBlocksFileOffset + section.KeyBlocksTotalSize);

                byte[] packed = MdictBinary.ReadBytes(reader, packedLength);
                if (packed[0] != 2 || packed[1] != 0 || packed[2] != 0 || packed[3] != 0)
                    throw new InvalidDataException("Expected zlib Key Block Info (compression marker 02 00 00 00).");
                section.StoredKeyInfoChecksum = MdictBinary.ToUInt32(packed, 4);

                byte[] payload = new byte[packed.Length - 8];
                Buffer.BlockCopy(packed, 8, payload, 0, payload.Length);
                if (section.KeyInfoEncrypted)
                {
                    byte[] check = new byte[4];
                    Buffer.BlockCopy(packed, 4, check, 0, 4);
                    payload = MdictKeyCrypto.Decrypt(payload, check);
                }
                byte[] decoded = DecompressZlib(payload, unpackedLength);
                section.ComputedKeyInfoChecksum = OxfordHeaderReader.CalculateAdler32(decoded);
                if (section.StoredKeyInfoChecksum != section.ComputedKeyInfoChecksum)
                    throw new InvalidDataException("Key Block Info Adler-32 mismatch after decryption/decompression.");

                ParseBlockInfo(decoded, section, isMdd);
                if (readAllKeys)
                    ReadAllKeyBlocks(reader, section, isMdd);
                else
                    ReadFirstKeyBlock(reader, section, isMdd);
                return section;
            }
        }
    }

    private static void ParseBlockInfo(byte[] decoded, KeySectionInfo section, bool isMdd)
    {
        long totalEntries = 0;
        long totalCompressedBytes = 0;
        long offset = section.KeyBlocksFileOffset;
        using (BinaryReader reader = new BinaryReader(new MemoryStream(decoded)))
        {
            for (long block = 0; block < section.BlockCount; block++)
            {
                KeyBlockInfo info = new KeyBlockInfo();
                info.EntryCount = MdictBinary.ReadInt64(reader);
                info.FirstKey = ReadIndexWord(reader, isMdd);
                info.LastKey = ReadIndexWord(reader, isMdd);
                info.CompressedSize = MdictBinary.ReadInt64(reader);
                info.DecompressedSize = MdictBinary.ReadInt64(reader);
                info.FileOffset = offset;

                if (info.EntryCount <= 0 || info.CompressedSize < 8 || info.DecompressedSize <= 0 ||
                    info.CompressedSize > MaxKeyBlockCompressed ||
                    info.DecompressedSize > MaxKeyBlockDecompressed)
                    throw new InvalidDataException("Invalid Key Block metadata at index " + block);
                totalEntries = checked(totalEntries + info.EntryCount);
                totalCompressedBytes = checked(totalCompressedBytes + info.CompressedSize);
                offset = checked(offset + info.CompressedSize);
                section.Blocks.Add(info);
            }
            if (reader.BaseStream.Position != decoded.Length)
                throw new InvalidDataException("Unconsumed bytes remain in Key Block Info: " +
                    (decoded.Length - reader.BaseStream.Position));
        }
        if (totalEntries != section.EntryCount)
            throw new InvalidDataException("Key entry count does not match preamble: " + totalEntries);
        if (totalCompressedBytes != section.KeyBlocksTotalSize)
            throw new InvalidDataException("Key Block compressed lengths do not match preamble.");
    }

    private static string ReadIndexWord(BinaryReader reader, bool isMdd)
    {
        int length = MdictBinary.ReadUInt16(reader);
        if (length > 32767)
            throw new InvalidDataException("Key index word is unexpectedly long.");
        int byteCount = checked((length + 1) * (isMdd ? 2 : 1));
        byte[] word = MdictBinary.ReadBytes(reader, byteCount);
        if (isMdd)
        {
            if (word[byteCount - 2] != 0 || word[byteCount - 1] != 0)
                throw new InvalidDataException("Missing UTF-16LE index terminator.");
            return StrictUtf16.GetString(word, 0, byteCount - 2);
        }
        if (word[byteCount - 1] != 0)
            throw new InvalidDataException("Missing UTF-8 index terminator.");
        return StrictUtf8.GetString(word, 0, byteCount - 1);
    }

    private static void ReadFirstKeyBlock(BinaryReader reader, KeySectionInfo section, bool isMdd)
    {
        KeyBlockInfo first = section.Blocks[0];
        int compression;
        uint storedChecksum;
        uint computedChecksum;
        section.FirstBlockEntries = ReadKeyBlock(reader, first, 0,
            out compression, out storedChecksum, out computedChecksum, isMdd);
        section.FirstBlockCompression = compression;
        section.FirstBlockStoredChecksum = storedChecksum;
        section.FirstBlockComputedChecksum = computedChecksum;
    }

    private static void ReadAllKeyBlocks(BinaryReader reader, KeySectionInfo section, bool isMdd)
    {
        long previousRecordOffset = -1;
        for (int blockNumber = 0; blockNumber < section.Blocks.Count; blockNumber++)
        {
            int compression;
            uint storedChecksum;
            uint computedChecksum;
            List<KeyEntry> entries = ReadKeyBlock(reader, section.Blocks[blockNumber],
                blockNumber, out compression, out storedChecksum, out computedChecksum, isMdd);
            if (compression == 2)
                section.ZlibBlockCount++;
            else
                section.UncompressedBlockCount++;

            if (blockNumber == 0)
            {
                section.FirstBlockEntries = entries;
                section.FirstBlockCompression = compression;
                section.FirstBlockStoredChecksum = storedChecksum;
                section.FirstBlockComputedChecksum = computedChecksum;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                KeyEntry entry = entries[i];
                // Equal offsets are legitimate: two keys can share a record.
                if (entry.RecordOffset < previousRecordOffset)
                    throw new InvalidDataException("Key record offsets decrease in block " +
                        blockNumber + ", entry " + i + ": " + entry.RecordOffset +
                        " < " + previousRecordOffset);
                previousRecordOffset = entry.RecordOffset;
                section.AddEntry(entry);
            }
        }

        if (section.AllEntries.Count != section.EntryCount)
            throw new InvalidDataException("Decoded Key count differs from Key Section preamble: " +
                section.AllEntries.Count + " vs " + section.EntryCount);
        section.AllKeysLoaded = true;
    }

    private static List<KeyEntry> ReadKeyBlock(BinaryReader reader, KeyBlockInfo info,
        int blockNumber, out int compression, out uint storedChecksum, out uint computedChecksum, bool isMdd)
    {
        reader.BaseStream.Position = info.FileOffset;
        int packedLength = MdictBinary.ToArrayLength(info.CompressedSize,
            MaxKeyBlockCompressed, "Key Block " + blockNumber + " (compressed)");
        int unpackedLength = MdictBinary.ToArrayLength(info.DecompressedSize,
            MaxKeyBlockDecompressed, "Key Block " + blockNumber + " (decompressed)");
        byte[] packed = MdictBinary.ReadBytes(reader, packedLength);
        compression = packed[0];
        if (packed[1] != 0 || packed[2] != 0 || packed[3] != 0)
            throw new InvalidDataException("Invalid compression marker in Key Block " + blockNumber);
        storedChecksum = MdictBinary.ToUInt32(packed, 4);
        byte[] payload = new byte[packed.Length - 8];
        Buffer.BlockCopy(packed, 8, payload, 0, payload.Length);
        byte[] plain;
        if (compression == 0)
        {
            if (payload.Length != unpackedLength)
                throw new InvalidDataException("Uncompressed Key Block length mismatch in block " + blockNumber);
            plain = payload;
        }
        else if (compression == 2)
            plain = DecompressZlib(payload, unpackedLength);
        else if (compression == 1)
            throw new NotSupportedException("Key Block " + blockNumber +
                " uses LZO, which is not implemented in v0.3.");
        else
            throw new NotSupportedException("Unknown compression type in Key Block " +
                blockNumber + ": " + compression);

        computedChecksum = OxfordHeaderReader.CalculateAdler32(plain);
        if (storedChecksum != computedChecksum)
            throw new InvalidDataException("Adler-32 mismatch in Key Block " + blockNumber);

        List<KeyEntry> entries = new List<KeyEntry>((int)info.EntryCount);
        using (BinaryReader keys = new BinaryReader(new MemoryStream(plain)))
        {
            for (long i = 0; i < info.EntryCount; i++)
            {
                long recordOffset = MdictBinary.ReadInt64(keys);
                int start = checked((int)keys.BaseStream.Position);
                int end = start;
                // MDD uses UTF-16LE and a two-byte terminator; MDX is UTF-8.
                if (isMdd)
                {
                    while (end + 1 < plain.Length &&
                        !(plain[end] == 0 && plain[end + 1] == 0) && end - start < 131072)
                        end += 2;
                    if (end + 1 >= plain.Length || end - start >= 131072)
                        throw new InvalidDataException("Unterminated UTF-16LE resource key in block " + blockNumber);
                }
                else
                {
                    while (end < plain.Length && plain[end] != 0 && end - start < 65536)
                        end++;
                    if (end >= plain.Length || end - start >= 65536)
                        throw new InvalidDataException("Unterminated UTF-8 key in block " + blockNumber);
                }
                KeyEntry entry = new KeyEntry();
                entry.Key = isMdd ? StrictUtf16.GetString(plain, start, end - start) :
                    StrictUtf8.GetString(plain, start, end - start);
                entry.RecordOffset = recordOffset;
                keys.BaseStream.Position = end + (isMdd ? 2 : 1);

                if (entries.Count > 0 &&
                    entry.RecordOffset < entries[entries.Count - 1].RecordOffset)
                    throw new InvalidDataException("Decreasing record offsets in Key Block " + blockNumber);
                entries.Add(entry);
            }
            if (keys.BaseStream.Position != plain.Length)
                throw new InvalidDataException("Unconsumed bytes in Key Block " + blockNumber);
        }
        return entries;
    }

    private static byte[] DecompressZlib(byte[] data, int expectedLength)
    {
        byte[] result = new byte[expectedLength];
        using (MemoryStream input = new MemoryStream(data))
        using (ZLibStream decoder = new ZLibStream(input, CompressionMode.Decompress))
        {
            int position = 0;
            while (position < result.Length)
            {
                int n = decoder.Read(result, position, result.Length - position);
                if (n == 0)
                    throw new InvalidDataException("Zlib block ended before declared decompressed length.");
                position += n;
            }
            if (decoder.ReadByte() != -1)
                throw new InvalidDataException("Zlib output exceeded declared decompressed length.");
        }
        return result;
    }
}
}
